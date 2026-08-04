# Implementation Plan: MariaDB 適用自動化（Issue #22）

対応する仕様書: [docs/issue22/SPEC.md](./SPEC.md)

## Overview

MariaDB のストアドプロシージャと Table を、既存の SQL Server 用 STG 適用パイプライン（`DeployService` Step1〜6）と同じ流れ・同じ画面で扱えるようにする。ストアドプロシージャは mysql CLI による自動デプロイまで、Table は SQL Server の Table/UserDefinedTableType と同様に Git 管理・手動適用待ち登録までを対象とする。1回の実行操作で SQL Server と MariaDB のモジュールが混在していても、両方が独立して完了する。

## Architecture Decisions

- **DbConfig への新規保存プロパティは `MariaDbGitRepoPath` の1つのみ**。`MariaDbMergePath`/`MariaDbForNewCreationPath`/`MariaDbDeploySourcePath`/`MariaDbDeployBatPath` はすべて既存プロパティからの計算式（`=>`）で導出し、Step6 の移動先は既存の `MariaDbDeployedPath` を再利用する（本番前準備フェーズと自動連携）。
- **`ModuleType` を `"MariaDB"` → `"Stored"` に変更し、`"MariaDbTable"` を新設**。MariaDB の Table は SQL Server の `"Table"` とは別の型として扱う（Git リポジトリ・適用先 DB が異なるものを同じツリー枠に混在させない）。
- **`DeployService.RunPipelineAsync` は種別（SQL Server系 / MariaDB系）ごとに独立したサブパイプラインとして実行**。Step1（ファイル生成）のみ出力先を分けて共通実行し、Step3〜6 は種別ごとに完全に独立させる。一方が失敗しても他方は最後まで実行する。
- **MariaDB Table の手動適用待ちファイルは、既存の `deployed_manual` フォルダに SQL Server の Table/UDTT と混在させる**（`ManualApplyItem.ModuleType` で区別済みのため、フォルダを分離する必要はないと判断）。
- **MariaDB 用 `deploy.bat`（mysql CLI 呼び出し）は運用担当者が事前配置**し、アプリ側は生成しない。既存の `RunBatAsync`（cmd.exe 経由）でそのまま実行できる構造にする。

## Task List

### Phase 1: Foundation（バックエンド設定・クエリ層）

- [x] **Task 1: DbConfig 拡張 + ローカル設定**
  - **Description:** `DbConfig` に `MariaDbGitRepoPath` プロパティと、MariaDB 用の計算プロパティ（`MariaDbMergePath`, `MariaDbForNewCreationPath`, `MariaDbDeploySourcePath`, `MariaDbDeployBatPath`）を追加する。ローカル検証用に `appsettings.Development.json` の kaios 設定へ `MariaDbGitRepoPath: test/Kaios_MariaDB_rep` を追加する。
  - **Acceptance criteria:**
    - [ ] `DbConfig.MariaDbGitRepoPath` が追加され、4つの計算プロパティが仕様通りのパスを返す
    - [ ] `appsettings.Development.json`（kaios）に `MariaDbGitRepoPath` が設定されている
    - [ ] `appsettings_sample.json` にも同様のサンプル値を追記（ドキュメント整合性のため）
  - **Verification:**
    - [ ] `dotnet build` 成功
    - [ ] 手動確認: アプリ起動後 `GET /api/modules` が既存通り200を返す（デシリアライズ失敗しない）
  - **Dependencies:** None
  - **Files likely touched:** `backend/Models/DbConfig.cs`, `backend/appsettings.Development.json`, `backend/appsettings_sample.json`
  - **Estimated scope:** S

- [x] **Task 2: ModuleQueryService の Type 変更 + MariaDB Table クエリ追加**
  - **Description:** `QueryMariaDbAsync` が返す `Type` を `"MariaDB"` から `"Stored"` に変更する。新規に MariaDB Table 取得クエリ（`information_schema.TABLES`、`Type="MariaDbTable"`、`GitOnly=true`）を追加し、`ModuleListResponse` に `MariaDbTables` フィールドを追加する。
  - **Acceptance criteria:**
    - [ ] `QueryMariaDbAsync` が `Type="Stored"` で返す
    - [ ] 新規メソッドが `TABLE_TYPE='BASE TABLE'` の一覧を `Type="MariaDbTable"`, `GitOnly=true` で返す
    - [ ] `ModuleListResponse.MariaDbTables` が追加され `GetModulesAsync` で設定される
  - **Verification:**
    - [ ] `dotnet build` 成功
    - [ ] ローカル MariaDB 接続時、`GET /api/modules/kaios` のレスポンスに `mariaDbTables` が含まれ、`test_dev` スキーマの実テーブル一覧と一致する
  - **Dependencies:** None（Task 1 と並行可）
  - **Files likely touched:** `backend/Services/ModuleQueryService.cs`, `backend/Models/ModuleInfo.cs`
  - **Estimated scope:** S

- [ ] **Task 3: ManualApplyService / FindDeleteCandidates の DB種別対応**
  - **Description:** `ManualApplyService.Register`/`List` と `ModuleQueryService.FindDeleteCandidates` にハードコードされている `config.GitRepoPath` と `"dbo."` プレフィックスを、モジュール種別（SQL Server系 / `MariaDbTable`）に応じて切り替えられるようにする。共通の解決ロジック（GitRepoPath・フォルダ名・ファイル名プレフィックスのマッピング）を導入し、`ManualApplyService.ManualApplyTypes` に `"MariaDbTable"` を追加する。
  - **Acceptance criteria:**
    - [ ] `MariaDbTable` 種別のモジュールに対し `config.MariaDbGitRepoPath\Table\{Name}.sql`（プレフィックスなし）を正しく解決する
    - [ ] 既存の SQL Server 系（`Table`/`UserDefinedTableType`）の挙動は変更なし（回帰なし）
    - [ ] `ManualApplyService.ManualApplyTypes` に `"MariaDbTable"` が含まれる
  - **Verification:**
    - [ ] xUnit スモークテスト: `test/Kaios_MariaDB_rep/Table/tm0010catalogno.sql` を対象に `Register` がファイルを正しくコピーすることを確認
    - [ ] 既存 SQL Server 向けの手動確認（Table 選択→実行→本番前準備画面に表示）で回帰がないことを確認
  - **Dependencies:** Task 1（`MariaDbGitRepoPath`）, Task 2（`MariaDbTable` 種別の定義）
  - **Files likely touched:** `backend/Services/ManualApplyService.cs`, `backend/Services/ModuleQueryService.cs`
  - **Estimated scope:** M

### Checkpoint: Phase 1 完了
- [ ] `dotnet build` が成功する
- [ ] `GET /api/modules/kaios` が SQL Server 全種別 + MariaDB（Stored / MariaDbTable）を返す
- [ ] 既存 SQL Server の Table/UDTT 手動適用フローに回帰がない
- [ ] 人間によるレビュー後、Phase 2 へ進む

---

### Phase 2: DeployService パイプライン拡張（MariaDB ストアド自動デプロイ）

- [ ] **Task 4: RunPipelineAsync を種別ごとのサブパイプラインに再編**
  - **Description:** `request.Modules` を SQL Server 系 / MariaDB 系（Stored/MariaDbTable）に分割する。Step1（`UpdateModule.txt`/`DeleteModule.txt` 生成）を種別ごとに出力先（`MergePath` / `MariaDbMergePath`）を分けて実行するよう改修する。SQL Server のみのリクエストでは従来と同一の挙動を維持する。
  - **Acceptance criteria:**
    - [ ] MariaDB モジュールが選択されている場合、`MariaDbMergePath\UpdateModule.txt`/`DeleteModule.txt` が SJIS で生成される
    - [ ] SQL Server モジュールのみのリクエストでは既存の `MergePath` 出力のみが行われ、ファイル内容・ログ順序が変わらない
  - **Verification:**
    - [ ] 新規 xUnit テスト: 混在リクエストで2つの出力ファイルが正しい内容で生成されることを確認
    - [ ] 手動: DryRun モードで既存 SQL Server 単独デプロイのログが変化しないことを確認（回帰確認）
  - **Dependencies:** Task 1
  - **Files likely touched:** `backend/Services/DeployService.cs`
  - **Estimated scope:** M

- [ ] **Task 5: MariaDB 用 Step3（git_merge.bat）実行**
  - **Description:** MariaDB 系モジュールが含まれる場合に `MariaDbMergePath\git_merge.bat` を実行するサブステップを追加する。既存の `Step3_GitMerge`/`RunBatAsync` パターンをそのまま流用する。
  - **Acceptance criteria:**
    - [ ] MariaDB モジュール選択時、`test/SourceControl/merge_MariaDB/git_merge.bat` が実行される
    - [ ] SQL Server のみの場合は実行されない（不要な bat 実行をしない）
  - **Verification:**
    - [ ] DryRun モードでログにステップが表示されることを確認
    - [ ] 実行環境で `git_merge.bat` が正常終了することを確認（ローカル `test/Kaios_MariaDB_rep` を対象リポジトリとして設定）
  - **Dependencies:** Task 4
  - **Files likely touched:** `backend/Services/DeployService.cs`
  - **Estimated scope:** S

- [ ] **Task 6: MariaDB 用 SQL 変換（コピー / DROP生成）**
  - **Description:** MariaDB のストアドプロシージャについて、新規・更新は `git checkout` 済みファイルをそのまま `MariaDbDeploySourcePath` へコピーし、削除は `DROP PROCEDURE IF EXISTS` の単独SQLを生成する処理を追加する（SQL Server の `ConvertAlterToCreate` に相当する変換は不要）。
  - **Acceptance criteria:**
    - [ ] 新規/更新モジュールが `MariaDbGitRepoPath\Stored\{Name}.sql` から `MariaDbDeploySourcePath\{Name}.sql` へそのままコピーされる
    - [ ] 削除モジュールに対し `DROP PROCEDURE IF EXISTS \`{Name}\`;` が UTF-8 で生成される
  - **Verification:**
    - [ ] xUnit テスト: `test/Kaios_MariaDB_rep/Stored/*.sql` を入力にコピー結果・DROP文生成結果を検証
  - **Dependencies:** Task 4
  - **Files likely touched:** `backend/Services/DeployService.cs`
  - **Estimated scope:** S

- [ ] **Task 7: MariaDB 用 deploy.bat 作成 + mysql CLI 実行・ロールバック**
  - **Description:** mysql CLI（`mysql.exe`）を1ファイルずつ実行し、`START TRANSACTION`/`COMMIT` でラップするか、非ゼロ終了コード時に `git checkout` で該当ファイルのみ巻き戻す新規 `deploy.bat` をテストフィクスチャとして作成する。`DeployService` の Step5 相当を `RunBatAsync` で実行できるようにする。
  - **Acceptance criteria:**
    - [ ] `test/SourceControl/Deploy_DEV2STG/MariaDB/ForNewCreation/deploy.bat` が新規作成され、`MariaDbDeploySourcePath` 配下のSQLを1ファイルずつ適用する
    - [ ] 適用失敗時、失敗したファイルの変更のみロールバックされ、他の成功済みファイルは維持される
    - [ ] mysql コマンドの終了コード・stderr がSSEログに転送される
  - **Verification:**
    - [ ] ローカル MariaDB（`test_dev`）に対し1件の正常な適用が成功することを実機確認
    - [ ] 故意に SQL 構文エラーを混入させ、そのファイルのみロールバックされ他は成功することを実機確認
  - **Dependencies:** Task 6
  - **Files likely touched:** `test/SourceControl/Deploy_DEV2STG/MariaDB/ForNewCreation/deploy.bat`（新規）, `backend/Services/DeployService.cs`
  - **Estimated scope:** M

- [ ] **Task 8: Step6（deployedへの移動）を MariaDbDeployedPath に対応**
  - **Description:** 適用成功した MariaDB ファイルを、既存の `config.MariaDbDeployedPath` へ移動する処理を追加する。
  - **Acceptance criteria:**
    - [ ] 成功したファイルが `MariaDbDeploySourcePath` から `MariaDbDeployedPath` へ移動される
    - [ ] 既存の「本番前準備」画面（`FastCopyService`/`PrepareController`）が変更なしでこのファイルを認識できる
  - **Verification:**
    - [ ] 手動確認: 適用後に本番前準備画面へ遷移し、MariaDB ファイルが一覧表示されることを確認
    - [ ] xUnit テスト: 移動処理の単体確認
  - **Dependencies:** Task 7
  - **Files likely touched:** `backend/Services/DeployService.cs`
  - **Estimated scope:** XS

### Checkpoint: Phase 2 完了
- [ ] MariaDB のストアドプロシージャを DryRun で選択→実行し、6ステップすべてがエラーなく完了する
- [ ] ローカル MariaDB 実環境で1件の新規/更新/削除が実際に適用できる
- [ ] 既存の SQL Server 単独デプロイフローに回帰がない
- [ ] 人間によるレビュー後、Phase 3 へ進む

---

### Phase 3: MariaDB Table の Git 管理対応

- [ ] **Task 9: Step3b（手動適用待ち登録）を MariaDbTable 対応に拡張**
  - **Description:** `Step3b_RegisterManualApply`（および `GitOnlyTypes` 判定）に `"MariaDbTable"` を含める。`ManualApplyService.Register`（Task 3 で汎用化済み）を通じて、MariaDB Table の Git マージ済みファイルが手動適用待ちとして登録されるようにする。
  - **Acceptance criteria:**
    - [ ] `MariaDbTable` モジュールを選択して実行すると、Step3（MariaDB用 git_merge）のみ実行され、Step4以降（mysql CLI 適用）はスキップされる
    - [ ] 手動適用待りリストに `ModuleType="MariaDbTable"` として登録される
  - **Verification:**
    - [ ] 手動確認: MariaDB Table を選択→実行→本番前準備画面の一覧に SQL Server の Table/UDTT と並んで表示されることを確認
  - **Dependencies:** Task 3, Task 5
  - **Files likely touched:** `backend/Services/DeployService.cs`
  - **Estimated scope:** S

- [ ] **Task 10: 削除候補検出への MariaDB 対応呼び出し追加**
  - **Description:** `ModuleQueryService.GetModulesAsync` に、MariaDB Stored / MariaDbTable 用の `FindDeleteCandidates` 呼び出しを追加する（`MariaDbGitRepoPath` 基準・プレフィックスなし）。
  - **Acceptance criteria:**
    - [ ] DB上に存在せず `MariaDbGitRepoPath` に残っている Stored/Table ファイルが `IsDeleteCandidate=true` として一覧に含まれる
  - **Verification:**
    - [ ] xUnit テスト: `test/Kaios_MariaDB_rep` 内にダミーファイルを配置し検出されることを確認
    - [ ] 手動確認: `GET /api/modules/kaios` のレスポンスで削除候補バッジが正しく付く
  - **Dependencies:** Task 3
  - **Files likely touched:** `backend/Services/ModuleQueryService.cs`
  - **Estimated scope:** XS

### Checkpoint: Phase 3 完了
- [ ] MariaDB Table の選択→Gitマージのみ→手動適用待ち登録の一連の流れがエラーなく完了する
- [ ] MariaDB の削除候補が一覧に正しく表示される
- [ ] 人間によるレビュー後、Phase 4 へ進む

---

### Phase 4: フロントエンド統合

- [ ] **Task 11: 型定義・APIクライアント更新**
  - **Description:** `ModuleType` の `'MariaDB'` を `'Stored'` に変更し `'MariaDbTable'` を追加する。`api/modules.ts` の `ApiModuleInfo`/`ApiModuleResponse`/`getModules()` を新しいレスポンス形状（`mariaDb`→`Stored`, `mariaDbTables`→`MariaDbTable`）に追随させる。
  - **Acceptance criteria:**
    - [ ] `types.ts` の `ModuleType` に `'Stored'`/`'MariaDbTable'` が定義され `'MariaDB'` が削除されている
    - [ ] `getModules()` が `MariaDbTable` キーを含む `Record<ModuleType, Module[]>` を返す
  - **Verification:**
    - [ ] `npm run build`（tsc型チェック含む）成功
  - **Dependencies:** Task 2
  - **Files likely touched:** `frontend/src/types.ts`, `frontend/src/api/modules.ts`
  - **Estimated scope:** XS

- [ ] **Task 12: DeployStg.tsx 表示・選択対応**
  - **Description:** `MODULE_TYPES` 配列に `'Stored'`/`'MariaDbTable'` を復活・追加し、非表示コメントを削除する。Git マージのみバッジの判定条件（`module.type === 'Table' || module.type === 'UserDefinedTableType'`）に `'MariaDbTable'` を追加する。種別タブの表示名マッピング（`'Stored'`→「MariaDBストアド」等）を追加する。
  - **Acceptance criteria:**
    - [ ] STG適用画面のツリーに MariaDB のストアド・Table が種別タブとして表示される
    - [ ] MariaDB Table 選択時は「Git マージのみ」バッジが表示され操作区分が固定される（SQL Server の Table と同じ挙動）
    - [ ] SQL Server と MariaDB モジュールを同時に選択でき、確認ダイアログに両方表示される
  - **Verification:**
    - [ ] `npm run dev` でブラウザ手動確認: ツリー表示・選択・確認ダイアログ・実行→SSEログ→完了までの一連の操作
  - **Dependencies:** Task 11
  - **Files likely touched:** `frontend/src/pages/DeployStg.tsx`
  - **Estimated scope:** S

### Checkpoint: Phase 4 完了
- [ ] STG適用画面で SQL Server / MariaDB（Stored・Table）混在選択→実行→両方完了の一連の流れをブラウザで確認できる
- [ ] 人間によるレビュー後、Phase 5 へ進む

---

### Phase 5: 堅牢化・テスト整備

- [ ] **Task 13: 部分失敗時のハンドリング検証**
  - **Description:** SQL Server / MariaDB 混在実行で、一方が失敗しても他方は独立して最後まで実行されることを確認し、必要に応じてログ・セッション状態の記録を調整する。
  - **Acceptance criteria:**
    - [ ] MariaDB側 mysql CLI を意図的に失敗させても、SQL Server側の適用は最後まで完了する
    - [ ] セッションステータスはいずれかの種別が失敗していれば `failed`、ログには種別ごとの成否が判別できる形で記録される
  - **Verification:**
    - [ ] 手動確認: 上記シナリオを実行しログ・実行履歴を確認
  - **Dependencies:** Task 8, Task 9
  - **Files likely touched:** `backend/Services/DeployService.cs`, `backend/Controllers/DeployController.cs`
  - **Estimated scope:** S

- [ ] **Task 14: xUnit 単体テスト整備**
  - **Description:** Success Criteria のうち自動化可能な項目（SQL変換・DROP文生成・ロールバック挙動・手動適用パス解決・削除候補検出）を xUnit でカバーする。バックエンドのテストプロジェクトが未整備の場合は新規作成する。
  - **Acceptance criteria:**
    - [ ] Task 3, 6, 7, 10 で個別に書いたテストが1つのテストプロジェクトにまとまっている
    - [ ] `dotnet test` が全てパスする
  - **Verification:**
    - [ ] `dotnet test` 実行結果
  - **Dependencies:** Task 3, 6, 7, 10
  - **Files likely touched:** `backend/Tests/*.cs`（新規プロジェクトの場合 `backend/Tests/Tests.csproj` 含む）
  - **Estimated scope:** M

### Checkpoint: Complete
- [ ] `docs/issue22/SPEC.md` の Success Criteria が全て満たされている
- [ ] `dotnet build` / `dotnet test` / `npm run build` すべて成功
- [ ] 既存 SQL Server 適用フロー・本番前準備フローに回帰がない
- [ ] レビュー・マージ準備完了

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| mysql CLI のロールバック方式（トランザクションラップ or ファイル単位checkout）が実機で想定通り動かない | Medium | Task 7 を早期（Phase 2 冒頭寄り）に配置し、実機検証を必須の Verification とする |
| `ManualApplyService`/`FindDeleteCandidates` の汎用化が既存 SQL Server 動作に回帰を起こす | High | Task 3 で既存 SQL Server 向けの手動確認を必須項目として明記。可能ならリファクタ前後で同一入力の出力比較を行う |
| MariaDB 用 `deploy.bat` の運用環境への事前配置が遅れ、本番相当環境でのE2E検証ができない | Medium | Ask first 事項として早期に運用担当者と配置手順・タイミングを確認（Open Question 3） |
| `DeployService.RunPipelineAsync` の種別分岐リファクタが大きく、既存SQL Serverフローに予期せぬ影響を与える | High | Task 4 で「SQL Serverのみのリクエストでは挙動が変わらない」ことを明示的な回帰確認項目とする |

## Open Questions（SPEC.md から引き継ぎ・実装時に確定させる）

- MariaDB用 `deploy.bat` の具体的なコマンド構成（1ファイルずつ実行 or 一括実行、トランザクションのラップ方法）→ Task 7 着手時に確定
- `ModuleQueryService.QueryMariaDbAsync` のファンクション（FUNCTION）対応要否 → 本Planでは対象外のまま。将来Issue化を検討
- MariaDB用 `deploy.bat` の実行環境への事前配置手順・タイミング → 運用担当者と要調整（Task 7 着手前に確認）
