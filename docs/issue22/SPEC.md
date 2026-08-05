# Spec: MariaDB 適用自動化（Issue #22）

## Objective

現在 MariaDB のストアドプロシージャ適用は、`UpdateModule.txt`/`DeleteModule.txt` の生成（SJIS/CP932）、`git_Live Updates.bat`、`merge_MariaDB\git_merge.bat` までは SQL Server と同じ運用に乗っているが、それ以降（SQL 変換・STG DB への実適用・適用済みファイルの記録）は自動化されておらず、`deploy.bat` での一括適用を試みても原因不明の失敗（SQL構文/文字コードエラーの疑い、詳細ログなし）により手動適用が続いている。

加えて、現行の STG 適用画面（`DeployStg.tsx`）では MariaDB は「仕様見直し中」として意図的にモジュールツリーから非表示にされている（型・API・バックエンドの土台は残置済み）。

本 Issue では、
1. 既存の `DeployService`（SQL Server 用 STG 適用パイプライン、Step1〜6）と同様の流れを MariaDB のストアドプロシージャにも適用し、STG DB への自動デプロイを実現する
2. MariaDB の Table も SQL Server の Table/UserDefinedTableType と同様に **Git 管理**（一覧表示・選択→ `git_merge.bat` によるマージ→手動適用待ち登録）の対象とする（自動デプロイはしない）
3. STG 適用画面に MariaDB モジュール一覧（ストアド・Table 双方）を表示・選択できるようにし、SQL Server と同じ画面・同じ操作感（ツリー選択→確認ダイアログ→実行→SSEログ）で扱えるようにする
4. 1回の実行操作で SQL Server と MariaDB のモジュールが混在していても、両方の適用（自動デプロイ・Git マージのみ双方含む）が完了する

ことを実現する。DryRun・確認ダイアログ・SSE ログ・実行履歴など既存の共通基盤はそのまま流用する。

### ユーザー
本アプリの運用担当者（SQL Server の STG 適用を日常的に Web UI から実行している人）。MariaDB も同じ画面・同じ操作感で適用できるようにする。

### 成功の姿
- Web UI の STG 適用画面で、SQL Server と MariaDB 両方のモジュール（MariaDB はストアドプロシージャと Table の双方）がツリーに表示され、同じ画面から選択できる。
- SQL Server / MariaDB のモジュールを同時に選択して1回の実行操作を行うと、両方の DB への適用が（それぞれ独立して）完了する。MariaDB の Table は Git マージのみ行われ、SQL Server の Table/UserDefinedTableType と同様に本番前準備画面での手動適用待ちとして登録される。
- 適用失敗時にエラー内容（mysql コマンドの終了コード・標準エラー出力）がログに残り、原因調査ができる。
- 失敗したファイルはファイル単位で「未適用」として扱われ、`deployed` へは移動されない（成功したファイルのみ移動される）。MariaDBのDDLはトランザクション非対応のため、DBレベルの自動ロールバックは行わない（Assumption 5参照）。

### 対象範囲の整理（SQL Server との対比）

| 種別 | SQL Server | MariaDB |
|------|-----------|---------|
| ストアドプロシージャ | Git管理 + STG自動デプロイ | Git管理 + STG自動デプロイ（★本Issueの主対象） |
| ファンクション | Git管理 + STG自動デプロイ | 対象外（Open Question、下記参照） |
| VIEW | Git管理 + STG自動デプロイ | 対象外（MariaDB側にVIEW運用なし） |
| Table | Git管理のみ・手動適用待ち登録 | **Git管理のみ・手動適用待ち登録（★本Issueで新規対応）** |
| UserDefinedTableType | Git管理のみ・手動適用待ち登録 | 対象外（MariaDBに相当機能なし） |

### スコープ外（今回やらないこと）
- MariaDB Table の **自動デプロイ**（SQL Server の Table/UserDefinedTableType と同じ「テーブルは危険なので自動適用しない」方針を踏襲。Git 管理・手動適用待ち登録までが対象）
- MariaDB ファンクション（FUNCTION）の一覧表示・デプロイ（`Export.py`/Git リポジトリ側は対応済みだが、`QueryMariaDbAsync` の対象拡張は Open Question とし、今回はストアドプロシージャのみを対象とする）
- 本番（PRD）への MariaDB 適用（フェーズ2 相当、対象外。既存の `MariaDbDeployedPath`/`MariaDbDeployedHoldPath` を使う「本番前準備」フェーズ＝`FastCopyService`/`PrepareController` は変更しない）
- `backup_MariaDB.bat` / `Export.py` による DEV ブランチへの mysqldump 取り込み処理（既存の手動運用のまま。本 Issue は `git_merge.bat` 実行以降＝Step3以降の自動化が対象）
- `git_Live Updates.bat` の変更（SQL Server と共用のバッチであり、`SourceControlPath` 配下を一括更新済みのため、MariaDB 用に再実行する必要はない）

## ASSUMPTIONS I'M MAKING

1. **DbConfig への追加は最小限にする。** 新規に保持するプロパティは `MariaDbGitRepoPath`（MariaDB 用 Git リポジトリのパス。例: `test/Kaios_MariaDB_rep`。SQL Server の `GitRepoPath` とは命名規則が異なり単純な文字列置換では導出できないため、明示的な設定が必要）の1つのみとする。それ以外はすべて既存プロパティから計算式（`=>` プロパティ）で導出する:
   - `MariaDbMergePath => Path.Combine(SourceControlPath, "merge_MariaDB")`（`merge` と同階層、新規ストレージ不要）
   - `MariaDbForNewCreationPath => Path.Combine(MariaDbSourcePath, "ForNewCreation")`
   - `MariaDbDeploySourcePath => Path.Combine(MariaDbForNewCreationPath, "Source")`
   - `MariaDbDeployBatPath => Path.Combine(MariaDbForNewCreationPath, "deploy.bat")`
   - Step6（適用済み移動）の移動先は、**既存の** `MariaDbDeployedPath`（`MariaDbSourcePath\deployed`）をそのまま使う。これにより新規パスを追加せずに、既存の「本番前準備」フェーズ（`FastCopyService`/`PrepareController`）とそのまま連携できる。
2. `ModuleQueryService.QueryMariaDbAsync` が返す `ModuleInfo.Type` を現行の `"MariaDB"` から `"Stored"` に変更する（`git_merge.bat` が `UpdateModule.txt` の Type 列をフォルダ名として使うため、`Kaios_MariaDB_rep\Stored\{name}.sql` という実際のフォルダ構成と一致させる必要がある）。フロントエンドの `ModuleType` 型・`DeployStg.tsx` の `MODULE_TYPES` 配列・`api/modules.ts` も `'Stored'` に追随させる。画面上の種別タブ表示名は `Type` 値と切り離し、表示名マッピングで `"MariaDB"` と表示する。
3. `Export.py` が生成する SQL ファイルは既に `DROP ... IF EXISTS` + `CREATE DEFINER=... PROCEDURE/FUNCTION ...` の完全な定義（1ファイル=1オブジェクトの完成形、`test/Kaios_MariaDB_rep/Stored/*.sql` で確認済み）になっているため、SQL Server の `ConvertAlterToCreate`（ALTER→CREATE 変換）に相当する変換処理は不要で、`git checkout` 済みファイルをそのまま `MariaDbDeploySourcePath` へコピーするだけでよい（新規・更新とも同じ扱い）。削除操作（OpType="削除"）の場合のみ `DROP PROCEDURE/FUNCTION IF EXISTS \`{Name}\`;` の単独 SQL を生成する。
4. MariaDB 用 `deploy.bat` は mysql CLI（`mysql.exe`）を呼び出す新規バッチとして作成し、既存の `RunBatAsync`（cmd.exe 経由・SJIS chcp・標準出力/エラーをログ転送）でそのまま実行できる構造にする。接続情報（ホスト・ユーザー・パスワード）は bat 側に事前設定してもらう＝SQL Server の `deploy.bat`（sqlcmd）と同じ運用ポリシーを踏襲し、`DbConfig` に STG 用の新規接続文字列は追加しない。
5. **【変更】ロールバックはDBトランザクションではなく「ファイル単位の成否管理」で実現する。** MariaDB(MySQL系)の `CREATE PROCEDURE`/`DROP PROCEDURE` 等のDDLはトランザクション非対応（実行時に暗黙コミットされる）ため、`START TRANSACTION`でラップしても実効的なロールバックにはならない。かつ Export.py が生成するファイルは `DROP IF EXISTS` の直後に `CREATE` を行う構成のため、**DROPが成功した直後にCREATEが失敗すると、そのプロシージャがDBから一時的に欠落する**リスクは技術的に排除できない（MySQL/MariaDBの仕様上の制約）。
   このリスクを踏まえ、deploy.bat は1ファイルずつ mysql CLI を実行し、ファイルごとの成否（`RESULT:OK:{file}` / `RESULT:FAIL:{file}`）を標準出力に明示する。アプリ側はこの出力を解析し、成功したファイルのみ `deployed` へ移動、失敗したファイルは移動せず次回再適用の対象として残す。1件の失敗が他のファイルの適用を止めない（全体を中断しない）。
6. **SQL Server と MariaDB の混在選択・実行**: 1回の `DeployRequest` に SQL Server 系モジュール（StoredProcedure/Function/VIEW/Table/UserDefinedTableType）と MariaDB 系モジュール（Stored）が混在することを許可する。`DeployService` は Step1（モジュール一覧ファイル生成）で種別ごとに出力先を分け（SQL Server → `MergePath`、MariaDB → `MariaDbMergePath`）、Step3〜6 は種別ごとに独立したサブパイプラインとして順次実行する。SQL Server 側が失敗しても MariaDB 側は独立して最後まで実行し、逆も同様（どちらか一方の失敗が他方をブロックしない）。セッション全体のステータスは、いずれかの種別が失敗した場合は `failed` とし、ログ・実行履歴には種別ごとの成否を区別して記録する。
7. `MariaDbGitRepoPath` のローカル開発・検証用の値には `test/Kaios_MariaDB_rep`（実データを含む既存フィクスチャ）を使用し、mysql CLI がここから checkout・適用されたファイルを実際に読み込んで実行できることを確認する。本番相当環境では `D:\STGENV\Kaios_MariaDB_rep` 等の実クローンパスを設定する。
8. ファイル読み書きの文字コードは MariaDB 側は UTF-8 とする（`Export.py` 出力に合わせる）。`UpdateModule.txt`/`DeleteModule.txt` 生成のみ既存仕様通り SJIS(CP932) を維持する（Step1 は SQL Server と共通処理のため）。
9. 現状 `QueryMariaDbAsync` はストアドプロシージャ（`ROUTINE_TYPE = 'PROCEDURE'`）のみ取得しファンクションを含まないが、`Export.py`/Git リポジトリ側は FUNCTION にも対応している。今回のスコープでは既存のクエリ範囲（PROCEDURE のみ）を維持し、FUNCTION 対応は Open Question とする。
10. SQL Server / MariaDB 混在実行時も、実行履歴（`DeploySession`）は既存どおり1リクエスト=1セッションとして記録する（`DeploySessionDetail.ModuleType` は自由入力のため `"Stored"` も既存カラムでそのまま記録可能。新規テーブル・スキーマ変更は不要）。
11. **MariaDB Table の型は `"MariaDbTable"` という新しい `ModuleType` として扱う**（SQL Server の `"Table"` とは別の値にする）。同じ `"Table"` を使うと、フロントエンドの種別タブ（`Record<ModuleType, Module[]>`）で SQL Server の Table と MariaDB の Table が同一バケットに混在してしまい、Git リポジトリも適用先 DB も異なる2つのものが1つのツリー項目として扱われてしまうため。
12. `ModuleQueryService` に MariaDB Table 用のクエリを追加する（`information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_TYPE='BASE TABLE'`、`GitOnly=true`）。`ModuleListResponse` に `MariaDbTables` フィールドを追加し、フロントエンドの `ApiModuleResponse`/`getModules()` にも `mariaDbTables` → `'MariaDbTable'` のマッピングを追加する。
13. `ManualApplyService`（手動適用待ち登録）と `ModuleQueryService.FindDeleteCandidates`（削除候補検出）は現在 `config.GitRepoPath` と `"dbo."` プレフィックスを **ハードコード**しており、そのままでは MariaDB Table に対応できない（Gitリポジトリが `config.MariaDbGitRepoPath` であり、ファイル名にも `dbo.` プレフィックスが付かないため）。両サービスを DB 種別（SQL Server / MariaDB）に応じて Git リポジトリパス・ファイル名プレフィックスを切り替えられるように改修する。`ManualApplyService.ManualApplyTypes` にも `"MariaDbTable"` を追加する。
14. MariaDB Table の手動適用待ちファイルは、既存の `DeployedManualPath`（SQL Server用 `deployed_manual` フォルダ）に `ModuleType` で区別しつつ格納する（SQL Server の Table/UDTT と同じ manifest・同じ本番前準備画面の一覧に混在表示される）。フォルダを分離する必要はないと判断するが、Plan フェーズで確認する。

→ 誤りがあれば訂正してください。特に 1, 2, 4, 6, 11, 13 は実装方式に直結するため確認をお願いします。

## Core Features（追加・変更）

| ID | 機能名 | 説明 |
|----|--------|------|
| F1' | モジュール一覧表示（拡張） | `DeployStg.tsx` の `MODULE_TYPES` に `'Stored'`（MariaDBストアド）と `'MariaDbTable'`（MariaDB Table）を復活・追加し、SQL Server 系種別と並べてツリー表示する |
| F2' | モジュール選択（拡張） | MariaDB モジュールも SQL Server と同じチェックボックス選択・操作区分（新規/更新/削除）指定に対応。`MariaDbTable` は SQL Server の Table/UserDefinedTableType と同様「Git マージのみ」バッジを表示（既存の `module.type === 'Table' \|\| module.type === 'UserDefinedTableType'` 判定に `'MariaDbTable'` を追加するだけで対応可能） |
| F4' | STG 適用実行（拡張） | SQL Server・MariaDB混在の選択でも1回の実行操作で両方に適用。`DeployService` 内で種別ごとにサブパイプラインを順次実行。MariaDB の `'MariaDbTable'` は既存の `Step3b_RegisterManualApply` 相当の処理（MariaDB用GitRepoPath・ファイル名プレフィックスなしに対応させたもの）で手動適用待ちに登録 |
| F5 | リアルタイムログ | 既存のまま。MariaDB側のステップ・mysqlコマンド出力もSSEで流す |
| F6' | 本番前準備（拡張） | 本番前準備画面の手動適用待ち一覧に MariaDB Table（`'MariaDbTable'`）も SQL Server の Table/UDTT と同じ扱いで表示・確認・消化できる |

## Tech Stack

- Backend: ASP.NET Core (.NET) / C#、既存 `DeployService` パイプラインを拡張
- DB接続: `MySqlConnector`（`ModuleQueryService.QueryMariaDbAsync` で使用中のライブラリを踏襲）
- Frontend: React + TypeScript（`DeployStg.tsx`/`types.ts`/`api/modules.ts` を最小限修正、`ConfirmDialog`/`LogViewer`/`SelectionSummary` は型変更のみで追随可能）
- バッチ: cmd.exe 経由の `.bat` 実行（`RunBatAsync` を流用）

## Commands

既存プロジェクトのコマンド体系を踏襲（新規コマンド追加なし）。
```
Backend build/test: dotnet build / dotnet test（backend配下）
Frontend build/test: npm run build / npm test（frontend配下）
```

## Project Structure（変更・追加箇所）

```
backend/Models/DbConfig.cs             → MariaDbGitRepoPath（新規1プロパティ）+ 計算プロパティ群を追加
backend/Models/ModuleInfo.cs           → ModuleListResponse に MariaDbTables フィールドを追加
backend/Services/DeployService.cs      → Step1/3〜6 を種別（SQLServer/MariaDB）ごとのサブパイプラインに再編
backend/Services/ModuleQueryService.cs → QueryMariaDbAsync の Type を "Stored" に変更、MariaDB Table 用クエリを追加、FindDeleteCandidates をDB種別対応に拡張
backend/Services/ManualApplyService.cs → GitRepoPath・ファイル名プレフィックスをDB種別で切り替えられるよう拡張、ManualApplyTypes に "MariaDbTable" を追加
frontend/src/types.ts                  → ModuleType の 'MariaDB' を 'Stored' に変更 + 'MariaDbTable' を追加（表示名は別マッピング）
frontend/src/pages/DeployStg.tsx       → MODULE_TYPES に 'Stored'/'MariaDbTable' を追加。関連コメント削除。Gitマージのみ判定に 'MariaDbTable' を追加
frontend/src/api/modules.ts            → ApiModuleInfo/ApiModuleResponse の型を追随（mariaDb→Stored, mariaDbTables→MariaDbTable）
docs/issue22/SPEC.md                   → 本仕様書
docs/issue22/PLAN.md                   → 実装計画（次フェーズで作成）
test/SourceControl*/merge_MariaDB/     → 既存テストフィクスチャ（git_merge.bat 等）を活用
test/Kaios_MariaDB_rep/                → 既存フィクスチャ（Stored/Table 双方）。MariaDbGitRepoPath のローカル検証用リポジトリとして使用
test/SourceControl/Deploy_DEV2STG/MariaDB/ForNewCreation/deploy.bat → 新規: MariaDB用 deploy.bat のテストフィクスチャ（要追加）
backend/appsettings.Development.json   → kaios の DbConfig に MariaDbGitRepoPath を追加
```

## Code Style

既存 `DeployService.cs` のパターンを踏襲する。Step関数は `StepN_処理名` 命名、`ChannelWriter<LogEntry>` へのログ出力、`_dryRun` によるシミュレーション分岐、`RunBatAsync` での bat 実行を再利用する。

```csharp
// 既存パターン例（DeployService.cs Step5_Deploy）
private async Task Step5_Deploy(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> deployModules, string tag, CancellationToken ct)
{
    var batPath = Path.Combine(config.ForNewCreationPath, "deploy.bat");
    ...
    await RunBatAsync(w, batPath, config.ForNewCreationPath, ct);
}
```
MariaDB版もこの構造に揃え、`config.MariaDbDeployBatPath` 等の計算プロパティを使う想定。

## Testing Strategy

- バックエンド単体テスト（xUnit）: SQL変換不要の確認（コピーのみ）、削除時のDROP文生成、UTF-8エンコーディング処理、mysql CLI異常終了時のロールバック挙動、SQLServer/MariaDB混在リクエストでの独立実行・部分失敗ハンドリング、MariaDbTableの手動適用待ち登録・削除候補検出（`dbo.`プレフィックスなし・`MariaDbGitRepoPath`基準での動作確認）
- 統合テスト: 手動（IIS環境での実DB適用確認）。`test/Kaios_MariaDB_rep` を使ったローカル検証を含む。既存の `docs/LOCAL_TEST_GUIDE.md` のMariaDB接続設定手順を踏襲
- フロントエンド: `DeployStg.tsx` でMariaDBモジュールの表示・選択・確認ダイアログ・実行完了までの手動E2E確認
- DryRunモードでのパイプライン全体シミュレーション確認

## Boundaries

- **Always:**
  - Table・UserDefinedTableType・MariaDbTable は Git マージのみ（自動適用しない）。危険なため自動デプロイ対象に含めない方針は SQL Server／MariaDB 共通
  - ファイル書き込みは既存仕様に合わせる（UpdateModule.txt等はSJIS、Export.py生成物はUTF-8）
  - 実行前は必ず確認ダイアログを表示
  - MariaDBはファイル単位の成否管理（DBトランザクションでの自動ロールバックではない。Assumption 5参照）
  - 複数DBの実行は順次実行（並列実行しない）。同一実行内のSQLServer/MariaDBサブパイプラインも順次実行とする
- **Ask first:**
  - MariaDB用 `deploy.bat` の配置場所・実行環境（運用担当者側での事前配置が必要なため、配置手順の確定は要相談）
  - `ModuleQueryService`/`ModuleType` の Type 値変更（`"MariaDB"` → `"Stored"`）によるフロントエンド影響範囲の最終確認
- **Never:**
  - 本番 MariaDB への直接デプロイ（フェーズ2対象外）
  - 確認なしでの自動実行

## Success Criteria

- [ ] STG適用画面のモジュールツリーに MariaDB のストアドプロシージャと Table が表示され、SQL Serverと同じ操作感で選択できる
- [ ] SQL Server と MariaDB のモジュールを同時選択して実行すると、両方への適用が完了する（一方が失敗しても他方は最後まで実行される）
- [ ] MariaDB の Table を選択して実行すると、Git マージのみが行われ、本番前準備画面の手動適用待ち一覧に SQL Server の Table/UDTT と同様に表示される
- [ ] 適用失敗時、エラーメッセージ（mysqlコマンドの終了コード・stderr）がSSEログに表示される
- [ ] 適用失敗したファイルは `deployed` へ移動されず未適用のまま残り、他の成功済みファイルは `deployed` へ移動される（DBレベルの自動ロールバックではなくファイル単位の成否管理）
- [ ] 適用成功したストアドプロシージャは既存の `MariaDbDeployedPath` に移動され、実行履歴に記録される
- [ ] mysql CLI が `test/Kaios_MariaDB_rep` 配下の実ファイルに対して実際に適用処理を実行できる（ローカル検証）
- [ ] DryRunモードで一連の流れをエラーなくシミュレーションできる
- [ ] 既存のSQL Server適用フローに影響を与えない（回帰なし）

## Open Questions

1. MariaDB用 `deploy.bat`（mysql CLI呼び出し）は具体的にどのようなコマンド構成にするか（1ファイルずつ実行 or 全ファイル一括実行、トランザクションのラップ方法）→ Plan フェーズで詳細設計
2. 【解決済み】`ModuleQueryService` はファンクション（FUNCTION）にも対応した。Git上は PROCEDURE と FUNCTION が同じ `Stored` フォルダに混在する（`Export.py` の出力仕様）ため、DBクエリでは `ROUTINE_TYPE` で判別して `Type="Stored"`（PROCEDURE）/`Type="MariaDbFunction"`（FUNCTION）に振り分け、削除候補検出はファイル内容（`CREATE DEFINER=... FUNCTION/PROCEDURE`）から種別判定する。フロントエンドは SQL Server と同様に別タブ（MariaDB エンジン内で「Stored」「Function」に分離）で表示する。
3. MariaDB用 `deploy.bat` の実行環境への事前配置手順・タイミング（誰がいつ配置するか）
4. MariaDB Table の手動適用待ちファイルを、SQL Server用 `deployed_manual` フォルダに混在させてよいか、それとも MariaDB 専用フォルダに分離すべきか（Assumption 14 参照）
