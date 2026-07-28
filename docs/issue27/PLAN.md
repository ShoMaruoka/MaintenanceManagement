# Implementation Plan: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)

対応する仕様: [`SPEC.md`](./SPEC.md)
関連: [`../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md) / [`../PLAN_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](../PLAN_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md)

## Overview

Issue #25 で実装済みの「Pilot環境適用」（`WebSourceDeployService` / `WebSourcePrepareController` / `WebSourcePrepare.tsx`）に、
①共通画像フォルダ → pilot の `Images\products` への robocopy コピー、②pilot 向け SQL 適用時の View ソースの DB 名置換
（`KaiosDB` → `KaiosDB_pilot`）の2機能を追加する。あわせて robocopy のスレッド数を `/MT:8` → `/MT:32` へ引き上げる。

新規サービス・新規画面・新規テーブルは作らない。既存の実行フロー（ターゲットループ、SSEログ、`WebSourceDeployLog`）に
ステップを差し込む形の拡張とする。

## Architecture Decisions

- **画像コピーは既存ターゲットループ内のステップとして追加**: `ExecuteAsync` の `foreach (var target in config.PilotTargets)` 内、
  Files コピーの後・パイロット用 web.config 適用の前に挿入する。専用のステップ種別（`WebSourceDeployStep`）は増やさない。
  （注: Issue #25 当時は「web.config 置換」。現行はファイル差し替え。`docs/pilot-webconfig-file-swap`）
  結果も専用レコードを作らず、既存の `WebSourceDeployTargetResult`（pilot1/pilot2）に内包する。
- **コピー処理は既存 `RunRobocopyAsync` をそのまま再利用**: 除外設定・DryRun・キャンセル時 Kill・終了コード判定・
  パス検証（`ValidateDeployPaths`）をすべて共有できるため、画像専用のコピー実装は作らない。
- **`/MT` は固定値の変更のみ**: `BuildArguments` の `/MT:8` を `/MT:32` に変更する。設定項目化はしない
  （運用担当が手動実施していた値と一致させる。値を戻す場合も1行変更で済む）。
- **DB 名置換ルールはシステム名ではなく「置換元 → 置換先」のリスト**: `DbConfig.PilotSqlDbNameReplacements` として持つ。
  kaios / gos とも `KaiosDB` → `KaiosDB_pilot` の1件を設定する（gos の View も `KaiosDB` を参照するため）。
  コード側にシステム別分岐は入れない。設定が空なら置換ステップ自体をスキップする。
- **置換は「コピー先のみ」**: 書き換え対象は `PilotSqlDeployPath\Source` 配下だけ。`Deploy2PrdPath` と Git リポジトリは触らない。
  `RunSqlDeployAsync` の robocopy コピー完了後・`deploy.bat` 実行前に実行する。
- **View 判定はファイル内容ベース**: 適用フォルダの SQL は `dbo.{名前}.sql` のフラット構成でオブジェクト種別が分からないため、
  ファイル内容に `CREATE VIEW` / `ALTER VIEW` / `CREATE OR ALTER VIEW` を含むものを View とみなす。
- **文字コードは元ファイル維持**: BOM（UTF-8 / UTF-16）を検出して維持。BOM なしは Shift-JIS → UTF-8 の
  ラウンドトリップ検証で判定し、再現できないファイルは置換せず警告スキップする（「書式を壊さない」方針。旧 Issue #25 の web.config 置換と同趣旨）。

## Task List

### Phase 1: Foundation（設定・データモデル）

- [x] **Task 1: `DbConfig` に画像コピー・DB名置換の設定プロパティを追加**
  - **Description**: `DbConfig` に `CommonImagePath`（STG 共通画像フォルダ）と `PilotSqlDbNameReplacements`
    （`List<PilotDbNameReplacement>`）を追加し、`PilotTarget` に `DestImagePath` を追加する。
    `PilotDbNameReplacement`（`From`, `To`）のモデルクラスを新設する。
  - **Acceptance criteria**:
    - [x] `appsettings.json` の `DbConfigs` から3つの新規設定がバインドできる
    - [x] 既存プロパティ・既存機能に影響しない（すべて省略可能で、既定値は空）
  - **Verification**:
    - [x] `cd backend && dotnet build` が成功する（バインド煙テストも実施）
  - **Dependencies**: None
  - **Files likely touched**: `backend/Models/DbConfig.cs`
  - **Estimated scope**: XS（1ファイル）

- [x] **Task 2: `appsettings_sample.json` にサンプル設定を追加**
  - **Description**: kaios / gos の `DbConfigs` 要素に `CommonImagePath`、`PilotTargets[].DestImagePath`、
    `PilotSqlDbNameReplacements` を追記する。paf・duskin には追加しない。
    `DestImagePath` は Issue #27 記載のパス（kaios: `10.194.5.64` / `10.194.5.65`、gos: `10.194.5.67` / `10.194.5.68` の
    `\WWW_XXX_pilot\Images\products`）をサンプルとして記載する。
  - **Acceptance criteria**:
    - [x] kaios / gos のみに新規設定が入り、JSON として valid
    - [x] `PilotSqlDbNameReplacements` は kaios / gos とも `KaiosDB` → `KaiosDB_pilot` の1件
  - **Verification**:
    - [x] アプリ起動時（`dotnet run`）に設定読み込みエラーが出ない
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/appsettings_sample.json`
  - **Estimated scope**: XS（1ファイル）

### Checkpoint: Foundation

- [x] `dotnet build` が通る
- [ ] 設定項目名（`CommonImagePath` / `DestImagePath` / `PilotSqlDbNameReplacements`）を人がレビュー済み
- [x] ローカル `appsettings.Development.json` に検証用のダミーパスを投入できる状態

---

### Phase 2: 画像コピー（機能スライス1）

- [x] **Task 3: robocopy のスレッド数を `/MT:32` へ変更**
  - **Description**: `WebSourceDeployService.BuildArguments` の `/MT:8` を `/MT:32` に変更する。
    コメントに「画像を含む大量ファイルコピーのため 32（robocopy の上限は 128）」の趣旨を残す。
  - **Acceptance criteria**:
    - [x] 生成される引数に `/MT:32` が含まれる（Webソース・Files・SQL・画像すべて共通）
  - **Verification**:
    - [x] `dotnet build` 成功
    - [x] DryRun 実行時のログ（`[DRY-RUN] robocopy ...`）に `/MT:32` が出力されることを確認
  - **Dependencies**: None（Task 1 と並行可）
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: XS（1ファイル）

- [x] **Task 4: `ExecuteAsync` のターゲットループに共通画像コピーを追加**
  - **Description**: Files コピー（`FilesDeploy2PrdPath`）の後・パイロット用 web.config 適用の前に、
    `CommonImagePath` → `target.DestImagePath` の robocopy を実行するステップを追加する。
    どちらかが未設定ならスキップし、その旨をログ出力する。robocopy がエラー終了（exit code 8 以上）した場合は
    そのターゲットを失敗として `break` し、以降のターゲット・SQL適用を行わない。
    失敗時のエラーメッセージは「画像コピー」と判別できる文言にする（例: `画像コピー robocopy exit code 8`）。
  - **Acceptance criteria**:
    - [x] pilot1 → pilot2 の順に、Files コピーの後で画像コピーが実行される
    - [x] `CommonImagePath` または `DestImagePath` 未設定時はスキップし、ログに理由が出る
    - [x] robocopy エラー時にそのターゲットが失敗となり、以降のターゲット・SQL適用がスキップされる
    - [x] 履歴（`WebSourceDeployLog`）の該当ターゲットのレコードに、画像コピー失敗と分かるメッセージが残る
    - [x] `DryRun=true` で実ファイルが書き換わらない
  - **Verification**:
    - [x] `dotnet build` 成功
    - [x] ローカルにダミーのコピー元画像フォルダとコピー先フォルダを用意し、`DryRun=false` で実行してコピーされることを確認
    - [x] コピー元を存在しないパスにして実行し、ターゲット失敗・以降スキップになることを確認
  - **Dependencies**: Task 1, Task 3
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: S（1ファイル）

- [x] **Task 5: `info` API に画像コピー元・コピー先を追加**
  - **Description**: `WebSourceInfoResponse` に `commonImagePath` を、`WebSourcePilotTargetInfo` に `destImagePath` を追加し、
    `WebSourcePrepareController.GetInfo` で設定値を返す。
  - **Acceptance criteria**:
    - [x] `GET /api/web-source-prepare/{dbName}/info` のレスポンスに画像パス2種が含まれる
    - [x] 未設定時は空文字が返り、エラーにならない
  - **Verification**:
    - [x] `dotnet run` して `/api/web-source-prepare/kaios/info` を呼び、期待した JSON が返ることを確認
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Models/WebSourcePrepareModels.cs`, `backend/Controllers/WebSourcePrepareController.cs`
  - **Estimated scope**: S（2ファイル）

- [x] **Task 6: フロントエンドに画像パス表示を追加**
  - **Description**: `webSourcePrepare.ts` の `ApiWebSourceInfo` / `ApiWebSourcePilotTargetInfo` に画像パスの型を追加し、
    `WebSourcePrepare.tsx` の「コピー元・コピー先」表示に画像パスを追記する。冒頭の説明文にも画像コピーを行う旨を加える。
  - **Acceptance criteria**:
    - [x] 画面に共通画像フォルダのパスと、pilot ごとの画像コピー先が表示される
    - [x] 画像パス未設定時は「未設定（スキップ）」等が分かる表示になる
  - **Verification**:
    - [x] `cd frontend && npm run build` が成功する
    - [x] `npm run dev` で画面を開き、kaios / gos を切り替えて表示を確認
  - **Dependencies**: Task 5
  - **Files likely touched**: `frontend/src/api/webSourcePrepare.ts`, `frontend/src/pages/WebSourcePrepare.tsx`
  - **Estimated scope**: S（2ファイル）

### Checkpoint: 画像コピー

- [x] `dotnet build` / `npm run build` が通る
- [x] ローカルのダミーフォルダで、DryRun / 実コピーの双方の挙動を確認済み
- [x] 画面に画像コピー元・コピー先が表示され、実行ログで画像コピーのステップが判別できる
- [ ] 人によるレビュー: 実行順序（Files コピー → 画像コピー → パイロット用 web.config 適用）と失敗時の中断挙動

---

### Phase 3: Viewソース更新（機能スライス2）

- [x] **Task 7: View 判定と DB 名置換のロジックを実装**
  - **Description**: `WebSourceDeployService` に、指定ディレクトリ配下の `*.sql` を走査して View 定義ファイルのみ
    DB 名を置換するメソッド（例: `ReplaceViewDbNames(string sourceDir, List<PilotDbNameReplacement> rules, bool dryRun, Action<string> onOutputLine)`）を実装する。
    - View 判定: 内容に `CREATE VIEW` / `ALTER VIEW` / `CREATE OR ALTER VIEW` を含む（大文字小文字・空白の揺れを許容する正規表現）
    - 置換: `(?<![A-Za-z0-9_])<From>(?![A-Za-z0-9_])` を `RegexOptions.IgnoreCase` でマッチさせ `To` に置換
    - エンコーディング: 既定 Shift-JIS、BOM（UTF-8 / UTF-16）検出時はそれを維持して読み書き
    - 戻り値: 置換したファイル数・箇所数（ログ出力用）
  - **Acceptance criteria**:
    - [x] `KaiosDB.dbo.X` / `[KaiosDB].[dbo].[X]` / `USE KaiosDB` が置換される
    - [x] `KaiosDB_pilot` / `KaiosDB2` / `MyKaiosDB` は置換されない
    - [x] 表記ゆれ（`kaiosdb` 等）もヒットし、置換後は設定値の表記に統一される
    - [x] View 以外の SQL ファイルは一切変更されない
    - [x] 置換対象 0 件でも例外にならず、件数 0 として返る
    - [x] 元ファイルの文字コード・BOM・改行・置換対象外の行が変化しない
    - [x] `dryRun=true` のときファイルへ書き込まない
  - **Verification**:
    - [x] ローカルに検証用フォルダを作り、以下のダミー SQL を置いて実行し結果を diff で確認する
      - View（`KaiosDB.dbo.X` を含む）／View（`[KaiosDB]` を含む）／View（`KaiosDB` を含まない）
      - StoredProcedure（`KaiosDB` を含むが View ではない）／`KaiosDB_pilot` を既に含む View
      - Shift-JIS（日本語コメント入り）と UTF-8 BOM 付きの両方
    - [x] 置換後にファイル全体の diff を取り、対象箇所以外に差分が出ていないこと
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: S（1ファイル）

- [x] **Task 8: `RunSqlDeployAsync` に置換ステップを組み込む**
  - **Description**: `Deploy2PrdPath` → `PilotSqlDeploySourcePath` の robocopy 成功後、`deploy.bat` 実行前に Task 7 の
    メソッドを呼び出す。`PilotSqlDbNameReplacements` が空ならスキップする。置換ファイル数・箇所数を SSE ログへ出力し、
    DryRun 時は「置換予定」として出力する。置換中に例外が発生した場合は SQL 適用を失敗として扱い、`deploy.bat` を実行しない。
  - **Acceptance criteria**:
    - [x] コピー後・`deploy.bat` 実行前に置換が行われる
    - [x] `PilotSqlDbNameReplacements` 未設定時はスキップされ、従来どおり `deploy.bat` が実行される
    - [x] 置換件数が SSE ログに出力される
    - [x] 置換で例外が発生した場合、`WebSourceSqlDeployResult` が失敗として返り `deploy.bat` は実行されない
  - **Verification**:
    - [x] `dotnet build` 成功
    - [x] ダミーの `PilotSqlDeployPath`（`Source` と何もしない `deploy.bat`）を用意し、`step=sql` で実行して
          コピー → 置換 → bat 実行の順にログが出ることを確認
    - [x] コピー元 `Deploy2PrdPath` 側のファイルが変更されていないことを確認
  - **Dependencies**: Task 7
  - **Files likely touched**: `backend/Services/WebSourceDeployService.cs`
  - **Estimated scope**: XS（1ファイル、Task 7 と同一ファイル）

### Checkpoint: Viewソース更新

- [x] `dotnet build` が通る
- [x] ダミー SQL 群で置換結果を diff 確認済み（誤変換・二重変換・文字化けなし）
- [x] `Deploy2PrdPath` 側が無変更であることを確認済み
- [ ] 人によるレビュー: View 判定条件と置換正規表現の妥当性

---

### Phase 4: 通し確認・ドキュメント

- [x] **Task 9: DryRun / 実行の通し確認**
  - **Description**: ローカルにダミー環境（STG 相当のソース・画像フォルダ、pilot1/pilot2 相当のコピー先、
    `PilotSqlDeployPath`）を用意し、実行内容の3択（`両方` / `Webソースコピーのみ` / `SQL適用のみ`）× DryRun 有無で
    一通り実行し、SPEC の受け入れ条件を確認する。
  - **Acceptance criteria**:
    - [x] SPEC「受け入れ条件」の各項目を確認し、チェックを付けられる
    - [x] 失敗系（画像コピー元が存在しない、`deploy.bat` が無い）で期待どおり中断・記録される
  - **Verification**:
    - [x] 実行ログ・履歴テーブル（`WebSourceDeployLog`）の内容を目視確認
  - **Dependencies**: Task 4, Task 6, Task 8
  - **Files likely touched**: なし（検証のみ）
  - **Estimated scope**: S

- [x] **Task 10: ドキュメント更新**
  - **Description**: SPEC の受け入れ条件のチェックを実績に合わせて更新し、実装中に判明した仕様差分があれば SPEC へ反映する。
    本 PLAN のタスクチェックも更新する。
  - **Acceptance criteria**:
    - [x] SPEC と実装の内容が一致している
    - [x] 未確認事項（gos の実パス、実サーバー確認）が Open Questions に残っている
  - **Verification**:
    - [x] 人によるレビュー
  - **Dependencies**: Task 9
  - **Files likely touched**: `docs/issue27/SPEC.md`, `docs/issue27/PLAN.md`
  - **Estimated scope**: XS

### Checkpoint: Complete

- [x] SPEC の受け入れ条件をすべて満たしている
- [x] `dotnet build` / `npm run build` が通る
- [x] 実サーバー確認が必要な項目が Open Questions として明示されている
- [x] レビュー可能な状態

---

## 依存関係

```
Task 1 (DbConfig)
   ├── Task 2 (appsettings_sample)
   ├── Task 4 (画像コピー) ←── Task 3 (/MT:32)
   ├── Task 5 (info API) ── Task 6 (フロント)
   └── Task 7 (置換ロジック) ── Task 8 (SQL適用へ組み込み)

Task 4 + Task 6 + Task 8 ── Task 9 (通し確認) ── Task 10 (ドキュメント)
```

### 並行可能な作業

- Task 3（`/MT:32`）は Task 1 と独立に着手できる
- Task 5・6（API/フロント）と Task 7・8（置換ロジック）は互いに独立（Task 1 完了後は並行可）
- Task 4 と Task 7 は同一ファイル（`WebSourceDeployService.cs`）を触るため、同時並行ではなく順に行う

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 画像件数が多く、初回コピーが長時間化して SSE 接続が切れる | Med | `/MT:32` と `/NP`（進捗行抑制）で短縮。切断時は既存のフロント側フォールバック（完了通知未受信のエラー表示）で状態が残らないようにしてある。長時間化が問題になる場合は別途タイムアウト設計を検討 |
| `/MT:32` によりネットワーク・pilot サーバー負荷が上がる | Low | 手動運用の実績値と同じ。問題があれば `BuildArguments` の1行変更で戻せる |
| View 判定の誤検出（コメント内の `CREATE VIEW` 等） | Low | 誤検出しても置換されるのは `KaiosDB` 参照のみで、pilot 向けコピー先に限定されるため実害は小さい。ダミー SQL での検証で確認する |
| 文字列リテラルや動的 SQL 内の `KaiosDB` も置換される | Low | pilot 適用では `KaiosDB_pilot` を参照するのが正しいため、原則として意図どおり。検証時に想定外の置換が出ないか diff で確認する |
| SQL ファイルのエンコーディングが Shift-JIS 以外だった場合の文字化け | Med | BOM 検出＋ BOM なし時は SJIS→UTF-8 のラウンドトリップ検証。判定不可は置換スキップ＋WARN。検証で SJIS / UTF-8 BOM / UTF-8 BOMなしを確認する |
| gos の `DestImagePath` 実値（共有ルート）が未確定 | Low | サンプル値のみコミットし、実値は運用担当確認後に実 `appsettings.json` へ設定する |
| 画像コピー先が Files コピーと重複して意図しない上書きになる | Low | SPEC で「共通画像側が後勝ち」と定義済み。実行順序を固定し、ログで判別できるようにする |

## Open Questions

1. gos の `WWW_GOS_pilot` 共有ルートの実パス（Issue #25 から継続の未確認事項）
2. pilot1/pilot2 への UNC 到達性・書き込み権限（同上）
3. 実サーバーでの画像コピー所要時間（初回の全量コピーがどの程度かかるか）
