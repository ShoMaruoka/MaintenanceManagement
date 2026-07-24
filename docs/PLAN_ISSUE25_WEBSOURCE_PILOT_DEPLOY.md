# Implementation Plan: STG → pilot Webソース配布機能（Issue #25）

対応する仕様: [`SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](./SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md)

## Overview

kaios・gosのSTG公開フォルダを、robocopyでpilot1→pilot2の順に自動連続コピーし、各コピー完了後にpilot側web.configの接続文字列を書き換える機能を実装する。既存の`FastCopyService`/`PrepareController`/`DatabaseService`の設計パターン（`DbConfig`ベースの設定、SSEストリーミング、`Channel<LogEntry>`によるログ配信、`CREATE TABLE IF NOT EXISTS`による自己マイグレーション）を踏襲する。

## Architecture Decisions

- **新規サービスとして分離**: `FastCopyService`はDB/画像ファイル用に特化しているため、Webソース用は`WebSourceDeployService`として新設する（既存サービスを汎用化して混在させない）。
- **robocopy採用**: `Process.Start`で`robocopy.exe`を起動。終了コードは0〜7を成功、8以上を失敗として判定するヘルパーを設ける。
- **pilotターゲットはリスト構造**: `DbConfig.PilotTargets: List<PilotTarget>`（`Name`, `DestWebSourcePath`）とし、pilot1→pilot2の順次処理をループで表現する（3台目が増えても設定追加のみで対応可能）。
- **ログテーブルは新設**: 既存`ProductionReadyLog`は`AppliedFiles`/`HeldFiles`という列構成でDBファイル件数に特化しているため意味が合わない。新規に`WebSourceDeployLog`テーブル（`DbName`, `TargetName`, `Mode`, `Result`, `LogDetail`等）を追加する。
- **web.config書き換えはXML DOM経由**: `System.Xml.Linq`（`XDocument`）で`connectionStrings/add[@name]/@connectionString`のみを対象にする。文字列置換は行わない（誤置換防止）。

## Task List

### Phase 1: Foundation（設定・データモデル）

- [ ] **Task 1: `DbConfig`にpilot関連プロパティを追加**
  - **Description**: `DbConfig.cs`に`WebSourcePath`（STG側公開フォルダ）、`PilotTargets`（`List<PilotTarget>`）、`PilotConnectionStrings`（`List<PilotConnectionString>`）を追加する。`PilotTarget`（`Name`, `DestWebSourcePath`）と`PilotConnectionString`（`Name`, `ConnectionString`）のモデルクラスも新設する。
  - **Acceptance criteria**:
    - [ ] `DbConfig`に3つの新規プロパティが追加され、既存プロパティ・ビルドに影響しない
    - [ ] `appsettings.json`の`DbConfigs`セクションから`WebSourcePath`/`PilotTargets`/`PilotConnectionStrings`がバインドできる
  - **Verification**:
    - [ ] `dotnet build` が成功する
    - [ ] 単体テスト（該当があれば）または簡易コンソール確認でバインド結果を確認
  - **Dependencies**: None
  - **Files likely touched**: `backend/Models/DbConfig.cs`
  - **Estimated scope**: XS（1ファイル）

- [ ] **Task 2: `appsettings_sample.json`にkaios・gos用のpilot設定サンプルを追加**
  - **Description**: kaios・gosの`DbConfigs`要素に、Task 1で追加したプロパティのサンプル値（ダミーパス・ダミー接続文字列）を追加する。paf・duskinには追加しない。
  - **Acceptance criteria**:
    - [ ] kaios・gosのみ`WebSourcePath`, `PilotTargets`（2件: pilot1, pilot2）, `PilotConnectionStrings`が設定される
    - [ ] JSONとして valid（パース可能）
  - **Verification**:
    - [ ] `dotnet build`後、アプリ起動時に設定読み込みエラーが出ない
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/appsettings_sample.json`
  - **Estimated scope**: XS（1ファイル）

- [x] **Task 3: `WebSourceDeployLog`テーブルとログ記録メソッドを追加**
  - **Description**: `DatabaseService`の初期化SQLに`WebSourceDeployLog`テーブル（`LogId`, `RunId`, `DbName`, `TargetName`, `Mode`, `ExecutedBy`, `ExecutedAt`, `Result`, `LogDetail`）を追加し、`InsertWebSourceDeployLog(...)`メソッドを実装する（既存`InsertProductionReadyLog`と同様のパターン）。コードレビュー指摘（`docs/ISSUE25_CodeReview_phase1.md`）を受け、pilot1/pilot2を同一実行として束ねる`RunId`列と、`(DbName, ExecutedAt DESC)`インデックスを追加した。
  - **Acceptance criteria**:
    - [x] アプリ起動時に`WebSourceDeployLog`テーブルが自動作成される
    - [x] `InsertWebSourceDeployLog`でレコードが1件挿入され、`LogId`が返る
    - [x] 同一`RunId`でpilot1/pilot2の2レコードを挿入でき、後で紐付け集計できる
  - **Verification**:
    - [ ] `dotnet build`成功
    - [ ] 手動確認: SQLiteファイルを開き、テーブル定義とレコード挿入を確認
  - **Dependencies**: None（Task 1と並行可）
  - **Files likely touched**: `backend/Services/DatabaseService.cs`
  - **Estimated scope**: S（1ファイル）

### Checkpoint: Foundation
- [ ] `dotnet build`が通る
- [ ] `appsettings.json`（実ファイル、gitignore対象）にkaios・gos用のpilot設定を投入すれば読み込める状態
- [ ] 人によるレビュー: 設定項目名・テーブル設計の妥当性確認

---

### Phase 2: Backend Core（robocopy実行・web.config書き換え・API）

- [ ] **Task 4: robocopy実行ヘルパーを実装**
  - **Description**: `WebSourceDeployService`に、指定した src/dest/mode（mirror|full）でrobocopyを起動し、標準出力を1行ずつコールバックへ渡し、終了コードから成功/失敗を判定するメソッド（例: `RunRobocopyAsync`）を実装する。除外パターン（`/XF`, `/XD`）は設定またはハードコードのデフォルト値を使う。
  - **Acceptance criteria**:
    - [ ] mode=mirrorで`/MIR`、mode=fullで`/E`を付与してrobocopyを起動する
    - [ ] `/MT:8 /R:2 /W:5`を付与する
    - [ ] 標準出力の各行がコールバックに渡される
    - [ ] 終了コード0〜7を成功、8以上を失敗として返す
  - **Verification**:
    - [ ] ローカルの一時フォルダ2つを使い、mirror/fullそれぞれで実際にrobocopyを実行し、ファイルがコピーされることを確認
    - [ ] 存在しないコピー元を指定した場合にエラー終了コードが正しく判定されることを確認
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`（新規）
  - **Estimated scope**: S（1ファイル）

- [ ] **Task 5: `PathSafety`を用いたパス検証をrobocopy実行前に組み込み**
  - **Description**: Task 4のメソッド呼び出し前に、コピー元（STG）・コピー先（pilot1/pilot2）パスが設定されたルート配下であることを`PathSafety`で検証する処理を`WebSourceDeployService`に追加する。
  - **Acceptance criteria**:
    - [ ] コピー元・コピー先パスが不正な場合、robocopyを起動せず例外を投げる
    - [ ] 正常なパスでは検証を通過する
  - **Verification**:
    - [ ] 単体テストまたは手動確認: 不正パス（ルート外への相対パスなど）でエラーになることを確認
  - **Dependencies**: Task 4
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: XS（1ファイル、Task 4と同一ファイルの追加変更）

- [ ] **Task 6: web.config接続文字列の置換処理を実装**
  - **Description**: pilot側`web.config`を`XDocument`で読み込み、`connectionStrings/add`要素を`name`属性で照合し、`PilotConnectionStrings`に定義がある`name`のみ`connectionString`属性を置換して保存するメソッドを`WebSourceDeployService`に実装する。`docs/Web.config_sample_kaios` / `docs/Web.config_sample_gos` で確認済みのとおり、コメントアウトされた`<add>`（逆システム向けの値）は`XDocument`パース時に`XComment`として扱われ要素とみなされないため、name属性照合だけで有効な要素のみに自動的にヒットする。kaios/gosでコメント位置が入れ替わっていても分岐は不要。
  - **Acceptance criteria**:
    - [ ] `PilotConnectionStrings`に定義された`name`のみ値が置換される
    - [ ] 未定義の`name`はSTGの値のまま変更されない
    - [ ] `connectionStrings`以外のセクション（`appSettings`等）は一切変更されない
    - [ ] コメントアウトされた`<add>`（逆システム向けの残骸）は変更・削除されない
    - [ ] web.configが存在しない場合はエラーとしてログ・例外を発生させる
  - **Verification**:
    - [ ] `docs/Web.config_sample_kaios` / `docs/Web.config_sample_gos` の両方をコピーしたテスト用ファイルに対して実際に置換処理を実行し、`ConnectionString`/`ConnectionStringMySQL`の値のみが変わることを確認
    - [ ] 置換前後でファイル全体のdiffを取り、対象の2属性値以外に差分（インデント崩れ・改行コード変化・コメント消失等）が出ていないことを確認する
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: S（1ファイル）

- [ ] **Task 7: `ExecuteAsync`でpilot1→pilot2の順次実行フローを実装**
  - **Description**: Task 4〜6を組み合わせ、`DbConfig.PilotTargets`を順に処理する`ExecuteAsync(DbConfig config, string mode, ChannelWriter<LogEntry> writer, CancellationToken ct)`を実装する。各ターゲット開始時に「{target.Name} 適用開始」をログ出力し、robocopyエラー時は以降のターゲットをスキップして例外を投げる。各ターゲットの結果を呼び出し元へ返せるようにする（成功/失敗のリストなど）。呼び出し元（Task 8のController）が1実行につき1つの`RunId`（GUID等）を発行し、`InsertWebSourceDeployLog`にターゲットごと同一`RunId`を渡して記録する。
  - **Acceptance criteria**:
    - [ ] pilot1→pilot2の順に処理される
    - [ ] pilot1が失敗した場合、pilot2のrobocopyは実行されない
    - [ ] 各ターゲットの開始・完了・エラーがログエントリとして`writer`に書き込まれる
  - **Verification**:
    - [ ] ローカルの疑似pilot1/pilot2フォルダ（2つの一時ディレクトリ）を使い、正常系・pilot1エラー時の中断を確認
  - **Dependencies**: Task 5, Task 6
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: M（1ファイル、既存メソッドの組み合わせ）

- [ ] **Task 8: `WebSourcePrepareController`を新設**
  - **Description**: `GET /api/web-source-prepare/{dbName}/info`（dbNameがkaios/gos以外なら404、STG/pilot1/pilot2のパス情報を返す）と`POST /api/web-source-prepare/{dbName}/stream`（`PrepareController.StreamPrepare`と同様のSSEパターンでTask 7の`ExecuteAsync`を呼び出し、完了時に`InsertWebSourceDeployLog`で記録）を実装する。
  - **Acceptance criteria**:
    - [ ] dbNameがkaios/gos以外の場合400または404を返す
    - [ ] `info`エンドポイントがSTG/pilot1/pilot2のパスをJSONで返す
    - [ ] `stream`エンドポイントがSSEでログをリアルタイム配信し、完了時に`done`イベントを送る
    - [ ] pilot1・pilot2それぞれの結果が`WebSourceDeployLog`に記録される
  - **Verification**:
    - [ ] `curl`または`Invoke-WebRequest`で`info`エンドポイントを叩き、期待どおりのJSONが返ることを確認
    - [ ] `stream`エンドポイントをブラウザ/curlで叩き、SSEイベントが順次流れることを確認
  - **Dependencies**: Task 3, Task 7
  - **Files likely touched**: `backend/Controllers/WebSourcePrepareController.cs`（新規）
  - **Estimated scope**: M（1ファイル）

### Checkpoint: Backend Core
- [ ] `dotnet build`成功、既存のPrepare機能に影響がないことを確認（既存テスト/動作確認）
- [ ] ローカル環境で実際の一時フォルダを使い、info→stream→web.config置換までの一連の流れをAPI経由で手動確認
- [ ] 人によるレビュー: API設計・エラーハンドリングの妥当性確認

---

### Phase 3: Frontend（画面・APIクライアント）

- [ ] **Task 9: APIクライアント`webSourcePrepare.ts`を実装**
  - **Description**: `frontend/src/api/webSourcePrepare.ts`を新設し、`getWebSourceInfo(dbName)`と`startWebSourceDeploy(dbName, mode, executedBy, onLog, onDone, onError)`を実装する（`prepare.ts`の`fetchJson`/`fetchStream`パターンを踏襲）。
  - **Acceptance criteria**:
    - [ ] `getWebSourceInfo`がTask 8の`info`エンドポイントを呼び、STG/pilot1/pilot2パスを返す
    - [ ] `startWebSourceDeploy`がSSEストリームを受信し、ログ行・完了イベントをコールバックに渡す
  - **Verification**:
    - [ ] `npm run build`（フロントエンド）が成功する
    - [ ] 型チェック（`tsc`）がエラーなく通る
  - **Dependencies**: Task 8
  - **Files likely touched**: `frontend/src/api/webSourcePrepare.ts`（新規）
  - **Estimated scope**: S（1ファイル）

- [ ] **Task 10: `WebSourcePrepare`画面を実装**
  - **Description**: `frontend/src/pages/WebSourcePrepare.tsx`を新設し、対象システム（kaios/gos）選択、STG/pilot1/pilot2パス表示、コピー方式（差分ミラー/全量）選択、実行ボタン、SSEログ表示（現在処理中のターゲット名を含む）を実装する。`PrepareForPrd.tsx`の画面構成・ログ表示コンポーネント（`PrepareLogViewer`等）を参考に再利用する。
  - **Acceptance criteria**:
    - [ ] システム選択（kaios/gosのみ）、モード選択、実行ボタンが表示される
    - [ ] 実行中はpilot1/pilot2どちらを処理中か画面上で判別できる
    - [ ] ログがリアルタイムに追記表示される
    - [ ] エラー時（pilot1失敗など）はエラー内容が画面に表示される
  - **Verification**:
    - [ ] `npm run build`成功
    - [ ] ブラウザで実際に開き、モックまたはローカルAPIに対して一連の操作フロー（選択→実行→ログ表示→完了）を目視確認
  - **Dependencies**: Task 9
  - **Files likely touched**: `frontend/src/pages/WebSourcePrepare.tsx`（新規）
  - **Estimated scope**: M（1ファイル、既存コンポーネント再利用）

- [ ] **Task 11: ルーティング・ナビゲーションへの追加**
  - **Description**: `frontend/src/App.tsx`に`WebSourcePrepare`ページへのルートを追加し、既存ナビゲーションメニューにリンクを追加する。
  - **Acceptance criteria**:
    - [ ] 新規メニューから画面遷移できる
    - [ ] 既存の他画面への遷移に影響がない
  - **Verification**:
    - [ ] `npm run build`成功
    - [ ] ブラウザでメニュークリック→画面表示を確認
  - **Dependencies**: Task 10
  - **Files likely touched**: `frontend/src/App.tsx`
  - **Estimated scope**: XS（1ファイル）

### Checkpoint: Frontend
- [ ] フロントエンド・バックエンドともにビルド成功
- [ ] ローカル環境（実サーバーの代わりに一時フォルダをSTG/pilot1/pilot2に見立てる等）でend-to-endの動作確認
- [ ] 人によるレビュー: UI/UXの確認

---

### Phase 4: Polish／運用確認

- [ ] **Task 12: 除外パターンの設定可能化**
  - **Description**: robocopyの`/XF`, `/XD`除外パターンを`DbConfig`またはグローバル設定（`appsettings.json`）から読み込めるようにする（Open Question 2の回答に基づきデフォルト値を決定）。
  - **Acceptance criteria**:
    - [ ] 設定ファイルで除外パターンを変更できる
    - [ ] 未設定時は妥当なデフォルト（`.vs`, `obj`等）が適用される
  - **Verification**:
    - [ ] 除外パターンに該当するファイルがコピーされないことを確認
  - **Dependencies**: Task 4
  - **Files likely touched**: `backend/Models/DbConfig.cs`, `backend/Services/WebSourceDeployService.cs`, `backend/appsettings_sample.json`
  - **Estimated scope**: S

- [ ] **Task 13: エラー時の履歴・UI表示の最終確認とドキュメント更新**
  - **Description**: pilot1失敗時にpilot2がスキップされる挙動、履歴テーブルへの記録内容を実データで確認し、必要であれば`docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`のOpen Questionsを更新する。README等に新機能の説明を追記する（必要な場合のみ）。
  - **Acceptance criteria**:
    - [ ] pilot1失敗シナリオで、pilot2が実行されないこと・履歴にfailedとして記録されることを確認
    - [ ] Open Questions（除外パターン、IIS再起動要否、UNCパス到達性、機密情報の扱い、ロールバック要否）が人によって解消されている
  - **Verification**:
    - [ ] 手動シナリオテスト実施
  - **Dependencies**: Task 8, Task 12
  - **Files likely touched**: ドキュメントのみ
  - **Estimated scope**: XS

### Checkpoint: Complete
- [ ] 全受け入れ条件（spec 3節）を満たしている
- [ ] `dotnet build` / `npm run build` ともに成功
- [ ] 人によるレビュー・承認

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| pilot1/pilot2サーバーへUNCパスで到達できない | High | Task 4着手前に実際のパス到達性を運用担当に確認（Open Question 1） |
| robocopyの終了コード判定を誤り、警告(1-3)を失敗扱いしてしまう | Medium | Task 4で終了コード判定ロジックを明示的にテストする |
| web.config書き換えでXML構造を壊す | High | `XDocument`のみを使用し文字列置換を禁止、Task 6で置換前後の差分を必ず目視確認 |
| pilot1成功・pilot2失敗時の状態（部分適用）が運用上問題にならないか未確定 | Medium | Task 13でOpen Question 5を運用担当に確認し、必要ならロールバック処理を追加タスク化 |
| 機密情報（pilot接続文字列）を`appsettings.json`に平文で持つことの是非 | Medium | 既存`DbConfigs`の扱いに準拠する前提で進めるが、Task 2着手前に運用担当へ確認（Open Question 4） |

## Open Questions

- spec記載のOpen Questions（1〜5）はいずれもPhase 1着手前、または該当タスク着手前に運用担当への確認が必要
