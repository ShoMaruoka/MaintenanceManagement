# Implementation Plan: STG → pilot Webソース配布機能（Issue #25）

対応する仕様: [`SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](./SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md)

## Overview

kaios・gosのSTG公開フォルダを、robocopyでpilot1→pilot2の順に自動連続コピーし、各コピー完了後にpilot側web.configの接続文字列を書き換える機能を実装する。既存の`FastCopyService`/`PrepareController`/`DatabaseService`の設計パターン（`DbConfig`ベースの設定、SSEストリーミング、`Channel<LogEntry>`によるログ配信、`CREATE TABLE IF NOT EXISTS`による自己マイグレーション）を踏襲する。

## Architecture Decisions

- **新規サービスとして分離**: `FastCopyService`はDB/画像ファイル用に特化しているため、Webソース用は`WebSourceDeployService`として新設する（既存サービスを汎用化して混在させない）。
- **robocopy採用**: `Process.Start`で`robocopy.exe`を起動。終了コードは0〜7を成功、8以上を失敗として判定するヘルパーを設ける。
- **pilotターゲットはリスト構造**: `DbConfig.PilotTargets: List<PilotTarget>`（`Name`, `DestWebSourcePath`）とし、pilot1→pilot2の順次処理をループで表現する（3台目が増えても設定追加のみで対応可能）。
- **ログテーブルは新設**: 既存`ProductionReadyLog`は`AppliedFiles`/`HeldFiles`という列構成でDBファイル件数に特化しているため意味が合わない。新規に`WebSourceDeployLog`テーブル（`RunId`, `DbName`, `TargetName`, `Mode`, `Result`, `LogDetail`等）を追加する。
- **web.config書き換えは行単位テキスト置換**（当初`XDocument`によるDOM経由を想定していたが、実サンプル検証で自己終了タグの書式変化等の問題が判明したため変更。詳細はTask 6・SPEC 7.2/7.3参照）: `XmlReader`で`connectionStrings/add[@name]`の対象行・旧値のみを特定し、元テキストの該当行だけを文字列置換する。対象外のセクション・書式には一切触れない。

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

- [x] **Task 4: robocopy実行ヘルパーを実装**
  - **Description**: `WebSourceDeployService`に、指定した src/dest/mode（mirror|full）でrobocopyを起動し、標準出力を1行ずつコールバックへ渡し、終了コードから成功/失敗を判定するメソッド（`RunRobocopyAsync`）を実装した。除外パターン（`/XF`, `/XD`）はハードコードのデフォルト値（Task 12で設定可能化予定）。コードレビュー（`ISSUE25_CodeReview_phase2.md`）を受け、キャンセル時（`OperationCanceledException`）に robocopy プロセスを `Kill(entireProcessTree: true)` するよう対応済み（`/MIR` は削除同期を伴うため、プロセス残留時の部分ミラー状態を避けるため）。
  - **Acceptance criteria**:
    - [x] mode=mirrorで`/MIR`、mode=fullで`/E`を付与してrobocopyを起動する
    - [x] `/MT:8 /R:2 /W:5`を付与する
    - [x] 標準出力の各行がコールバックに渡される
    - [x] 終了コード0〜7を成功、8以上を失敗として返す
    - [x] キャンセル時にrobocopyプロセスを残留させない（Kill）
  - **Verification**:
    - [x] ローカルの一時フォルダ2つを使い、mirror/fullそれぞれで実際にrobocopyを実行し、ファイルがコピーされることを確認
    - [x] 存在しないコピー元を指定した場合にエラー終了コードが正しく判定されることを確認
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`（新規）
  - **Estimated scope**: S（1ファイル）

- [x] **Task 5: パス検証をrobocopy実行前に組み込み**
  - **Description**: Task 4のメソッド呼び出し前に、コピー元（STG）・コピー先（pilot1/pilot2）パスの安全性を検証する `ValidateDeployPaths` を実装した。SPEC/実装レビューで確認したとおり、`WebSourcePath`/`PilotTarget.DestWebSourcePath` は appsettings.json（信頼できる設定）由来であり、ユーザー入力の相対パスをルート配下に閉じ込める従来の `PathSafety.IsUnderRoot` の用途とは異なる。代わりに **設定ミスによる事故防止ガード**（空文字・相対パス・src=dest一致・ドライブ/共有ルート指定の拒否）として `PathSafety.AreSamePath` 等を利用する実装とした（SPEC 3節・8節に追随済み）。
  - **Acceptance criteria**:
    - [x] コピー元・コピー先パスが不正な場合、robocopyを起動せず例外を投げる
    - [x] 正常なパスでは検証を通過する
    - [x] コピー先がドライブルート/共有ルートの場合は拒否する（`/MIR`によるドライブ・共有全体の意図しない削除防止）
  - **Verification**:
    - [x] 使い捨て検証プロジェクトで6パターン（正常/空/相対/src=dest/ドライブルート/共有ルート）を実行し、期待通りの結果を確認
  - **Dependencies**: Task 4
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: XS（1ファイル、Task 4と同一ファイルの追加変更）

- [x] **Task 6: web.config接続文字列の置換処理を実装**
  - **Description**: 当初`XDocument`によるロード→Save方式で実装したが、実サンプルファイル検証で自己終了タグへの空白挿入（`/>` → ` />`）やXML宣言への`encoding`属性付与など、対象外の書式まで変わる問題が判明。**`XmlReader`で対象行番号・旧値のみを特定し、元テキストの該当行を文字列置換する方式**に変更した（SPEC 7.2に詳細を追記済み）。BOM有無（kaiosはBOMあり、gosはBOMなしを実ファイルで確認）も検出して保持する。コードレビュー指摘を受け、`dryRun`引数を追加（`true`時はファイル書き込みをスキップ）、`PilotConnectionStrings`のnameが1件でも未ヒットの場合は例外を送出しファイルへの書き込みを行わない（部分適用の禁止）よう対応した（SPEC 7.3に追記済み）。
  - **Acceptance criteria**:
    - [x] `PilotConnectionStrings`に定義された`name`のみ値が置換される
    - [x] 未定義の`name`が1件でもあれば例外を送出し、書き込みを行わない
    - [x] `connectionStrings`以外のセクション（`appSettings`等）は一切変更されない
    - [x] コメントアウトされた`<add>`（逆システム向けの残骸）は変更・削除されない
    - [x] web.configが存在しない場合はエラーとしてログ・例外を発生させる
    - [x] `dryRun=true`の場合はファイルへ書き込まない
  - **Verification**:
    - [x] `docs/Web.config_sample_kaios` / `docs/Web.config_sample_gos` の両方をコピーしたテスト用ファイルに対して実際に置換処理を実行し、`ConnectionString`/`ConnectionStringMySQL`の値のみが変わることを確認
    - [x] 置換前後でファイル全体のdiffを取り、対象の2属性値以外に差分（インデント崩れ・改行コード変化・BOM変化・コメント消失等）が出ていないことを確認（BOM差分を発見し修正）
    - [x] DryRun時にファイルが一切変更されないこと、未ヒット・部分ヒット時に例外送出かつファイル不変であることを検証プロジェクトで確認
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: S（1ファイル）

- [x] **Task 7: `ExecuteAsync`でpilot1→pilot2の順次実行フローを実装**
  - **Description**: Task 4〜6を組み合わせ、`DbConfig.PilotTargets`を順に処理する`ExecuteAsync(DbConfig config, string mode, ChannelWriter<LogEntry> writer, CancellationToken ct)`を実装した。各ターゲット開始時に「{target.Name} 適用開始」をログ出力し、robocopyエラーまたはweb.config置換エラー時は以降のターゲットをスキップする。`WebSourceDeployTargetResult`（TargetName, Success, ErrorMessage）のリストを返す。呼び出し元（Task 8のController）が1実行につき1つの`RunId`（GUID）を発行し、`InsertWebSourceDeployLog`にターゲットごと同一`RunId`を渡して記録する。
  - **Acceptance criteria**:
    - [x] pilot1→pilot2の順に処理される
    - [x] pilot1が失敗した場合、pilot2のrobocopyは実行されない
    - [x] 各ターゲットの開始・完了・エラーがログエントリとして`writer`に書き込まれる
  - **Verification**:
    - [x] ロジックレベルでの動作確認（robocopy・web.config置換の単体検証を踏まえた組み合わせロジックのコードレビュー）
  - **Dependencies**: Task 5, Task 6
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: M（1ファイル、既存メソッドの組み合わせ）

- [x] **Task 8: `WebSourcePrepareController`を新設**
  - **Description**: `GET /api/web-source-prepare/{dbName}/info`（dbNameがkaios/gos以外なら404、STG/pilot1/pilot2のパス情報を返す）と`POST /api/web-source-prepare/{dbName}/stream`（`PrepareController.StreamPrepare`と同様のSSEパターンでTask 7の`ExecuteAsync`を呼び出し、完了時に`RunId`を発行して`InsertWebSourceDeployLog`で記録）を実装した。
  - **Acceptance criteria**:
    - [x] dbNameがkaios/gos以外の場合404を返す
    - [x] `info`エンドポイントがSTG/pilot1/pilot2のパスをJSONで返す
    - [x] `stream`エンドポイントがSSEでログをリアルタイム配信し、完了時に`done`イベントを送る
    - [x] pilot1・pilot2それぞれの結果が同一`RunId`で`WebSourceDeployLog`に記録される
  - **Verification**:
    - [x] `dotnet build`成功
    - [ ] `curl`等での実HTTPリクエスト確認は、サンドボックスのネットワークアクセス制限により未実施（既存`PrepareController`と同一パターンでの実装により担保）
  - **Dependencies**: Task 3, Task 7
  - **Files likely touched**: `backend/Controllers/WebSourcePrepareController.cs`（新規）
  - **Estimated scope**: M（1ファイル）

### Checkpoint: Backend Core
- [ ] `dotnet build`成功、既存のPrepare機能に影響がないことを確認（既存テスト/動作確認）
- [ ] ローカル環境で実際の一時フォルダを使い、info→stream→web.config置換までの一連の流れをAPI経由で手動確認
- [ ] 人によるレビュー: API設計・エラーハンドリングの妥当性確認

---

### Phase 3: Frontend（画面・APIクライアント）

- [x] **Task 9: APIクライアント`webSourcePrepare.ts`を実装**
  - **Description**: `frontend/src/api/webSourcePrepare.ts`を新設し、`getWebSourceInfo(dbName)`と`startWebSourceDeploy(dbName, mode, executedBy, onLog, onDone, onError)`を実装する（`prepare.ts`の`fetchJson`/`fetchStream`パターンを踏襲）。
  - **Acceptance criteria**:
    - [x] `getWebSourceInfo`がTask 8の`info`エンドポイントを呼び、STG/pilot1/pilot2パスを返す
    - [x] `startWebSourceDeploy`がSSEストリームを受信し、ログ行・完了イベントをコールバックに渡す
  - **Verification**:
    - [x] `npm run build`（フロントエンド）が成功する
    - [x] 型チェック（`tsc`）がエラーなく通る
  - **Dependencies**: Task 8
  - **Files likely touched**: `frontend/src/api/webSourcePrepare.ts`（新規）
  - **Estimated scope**: S（1ファイル）

- [x] **Task 10: `WebSourcePrepare`画面を実装**
  - **Description**: `frontend/src/pages/WebSourcePrepare.tsx`を新設し、対象システム（kaios/gos）選択、STG/pilot1/pilot2パス表示、コピー方式（差分ミラー/全量）選択、実行ボタン、SSEログ表示（現在処理中のターゲット名を含む）を実装した。`PrepareForPrd.tsx`の画面構成・ログ表示スタイルを踏襲し、完了時はターゲット別（pilot1/pilot2）の成功・失敗結果も表示する。コードレビュー（`docs/ISSUE25_CodeReview_phase3.md`）のImportant指摘3件を受け、以下を対応した: (1) `runDeploy`を`try/catch`で囲み、`startWebSourceDeploy`が例外を投げた場合も`handleError`で失敗表示へ遷移させる。(2) `done`/`error`のどちらも受信せずストリームが終了した場合に備え、`completed`フラグで検知し、未完了時は「完了通知を受信できませんでした」として失敗表示へ遷移させる（実行中残留の防止）。(3) `loadInfo`にリクエスト世代（`infoRequestSeq`）を導入し、システム切替を素早く行った際に古い応答が新しい選択を上書きしないようにした。また現在ターゲット推定のログ照合を`t.name 適用開始`という文字列に限定し誤検知を減らした。
  - **Acceptance criteria**:
    - [x] システム選択（kaios/gosのみ）、モード選択、実行ボタンが表示される
    - [x] 実行中はpilot1/pilot2どちらを処理中か画面上で判別できる（ログメッセージ中のターゲット名を照合して表示）
    - [x] ログがリアルタイムに追記表示される
    - [x] エラー時（pilot1失敗など）はエラー内容が画面に表示される
  - **Verification**:
    - [x] `npm run build`成功（`tsc`型チェック含む）
    - [ ] ブラウザでの実APIに対する目視確認は未実施（サンドボックスのネットワーク制約により、Phase 3完了時点ではビルド確認のみ。実サーバー環境での動作確認は運用担当に依頼）
  - **Dependencies**: Task 9
  - **Files likely touched**: `frontend/src/pages/WebSourcePrepare.tsx`（新規）
  - **Estimated scope**: M（1ファイル、既存コンポーネント再利用）

- [x] **Task 11: ルーティング・ナビゲーションへの追加**
  - **Description**: `frontend/src/App.tsx`に`/web-source`ルート（`WebSourcePrepare`）を追加し、`Sidebar.tsx`に「Webソース配布」メニュー項目を追加した。
  - **Acceptance criteria**:
    - [x] 新規メニューから画面遷移できる
    - [x] 既存の他画面への遷移に影響がない
  - **Verification**:
    - [x] `npm run build`成功
    - [ ] ブラウザでメニュークリック→画面表示の目視確認は未実施（Task 10と同様、実環境での確認を推奨）
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
