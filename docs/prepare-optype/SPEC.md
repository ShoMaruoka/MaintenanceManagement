# Spec: 本番前準備画面での操作区分（新規／更新／削除）表示

## Objective

本番前準備画面（`PrepareForPrd.tsx`）は `ForNewCreation/Source/deployed`（MariaDB は `MariaDB/deployed`）配下の `.sql` ファイルを一覧するだけで、**そのファイルが「新規」「更新」「削除」のどの操作で作られたものかが画面上で区別できない**。

特に「削除」のファイルは中身が `DROP PROCEDURE/FUNCTION/VIEW` であり、本番へコピーされると本番 DB からオブジェクトが消える。にもかかわらず、更新ファイルと見た目が完全に同じため、確認作業が名前とファイル内容の目視に依存している。

本件では、STG 適用時に既に記録済みの操作区分（`OpType`）を本番前準備画面の各ファイル行に表示し、削除を視覚的に強調することで、本番適用前の確認精度を上げる。

### ユーザー
本番前準備を実行する運用担当者。deployed/ の一覧を見てチェックを付け／外し、本番適用フォルダへ送る作業を行う。

### 成功の姿
- deployed/ および deployed_hold/ の各 SQL ファイル行に `[新規]` `[更新]` `[削除]` バッジが表示される。
- 削除区分の行は赤系で強調され、セクションヘッダに「うち削除 N 件」が出るため、削除の存在を見落とさない。
- 比較ビュー（`PrepareCompareView`）でも同じ区分が確認でき、コピー／ダウンロードした TSV にも区分が載る。
- 既存の選択・実行・保留の挙動は一切変わらない（表示のみの機能追加）。

### 採用方針（案A: SQLite からの逆引き）

操作区分は STG 適用時に **SQLite の `DeploySessionDetail`（SessionId, OpType, ModuleType, ModuleName, Result）** に全件記録済み（`DeployController.cs:83`）。これを正とし、`deployed/` のファイル名から逆引きして API に載せる。

検討した他案と却下理由:

| 案 | 内容 | 判断 |
|----|------|------|
| **A（採用）** | SQLite `DeploySessionDetail` から逆引き | 書き込み処理の追加がゼロ。`FastCopyService` の deployed↔hold 移動処理に手を入れる必要がなく、状態の二重管理が発生しない。過去に溜まっているファイルも遡って区分が出る |
| B | `deployed/manifest.jsonl` サイドカー（`deployed_manual` と同方式） | 却下。deployed → hold → 本番の移動のたびに manifest を同期する処理が必要になり、状態が「ファイル」と「manifest」の2箇所に分かれてズレる。`deployed_manual` が manifest 方式なのは「STG に自動適用されないので DB のセッション記録に残らない」という事情によるもので、deployed/ には当てはまらない |
| C | SQL 内容から判定 | 却下。削除は DROP 単独 SQL なので判別できるが、新規と更新は両方 `CREATE OR ALTER` のため区別不能。単独では成立しない |

### スコープ外（今回やらないこと）
- 手動適用待ち一覧（Table / UDTT / MariaDbTable）の表示変更。既に `ManualApplyItem.OpType` でバッジ表示済みのため対象外。
- 画像・静的ファイル（Files）セクション。操作区分の概念がない。
- 選択の初期状態・実行時の挙動の変更（削除も現状どおり既定でチェック済みのまま）。
- `DeploySessionDetail` のスキーマ変更・記録内容の変更。
- 区分によるフィルタ／ソート機能（今回は表示と強調まで）。

## ASSUMPTIONS I'M MAKING

1. **本件は表示専用の機能追加であり、ファイル移動・選択・実行のロジックは一切変更しない。** `FastCopyService` は変更対象外。`PrepareSelection`（リクエスト側の DTO）にも `OpType` は追加しない（サーバー側で判断に使わないため）。
2. **照合用の名前は「末尾 `.sql` の除去」と「`dbo.` プレフィックスの除去」で正規化する。**
   - `sqlserver`: `dbo.{ModuleName}.sql` → `{ModuleName}`（`DeployService.Step6_MoveToDeployed` が必ずこの形式で置く）
   - `mariadb`: `{ModuleName}.sql` → `{ModuleName}`（`Step6_MoveToDeployedMariaDb` が必ずこの形式で置く）
   - `dbo.` プレフィックスが無い SQL Server 側ファイルは、拡張子除去のみで照合する（想定外だが安全側に倒す）。
   - **【実データ確認で追加】`DeploySessionDetail.ModuleName` 側にも `dbo.` 付きで記録された行が実在する**（`dbo.TestSP` / `dbo.TestView` / `dbo.TestTable`）。したがって正規化はファイル名側だけでなく**モジュール名側にも適用する**。
   - 拡張子の除去は `Path.GetFileNameWithoutExtension` ではなく末尾 `.sql` の除去で行う（前者だと `a.b.c.sql` が `a.b` に切り詰められる）。
   - 比較は大文字小文字を区別しない（Windows のファイル名に合わせる）。
3. **`ModuleType` から DbType を判定する。** MariaDB 系＝`Stored` / `MariaDbFunction` / `MariaDbTable`、および Issue #22 以前の旧値 `MariaDB`。それ以外（`StoredProcedure` / `Function` / `VIEW` / `Table` / `UserDefinedTableType`）は SQL Server 系。**SQL Server のストアドは `StoredProcedure`、MariaDB のストアドは `Stored`** であり別値なので衝突しない（Issue #22 Assumption 2）。この判定により、SQL Server と MariaDB に同名モジュールが存在しても取り違えない。
4. **同一モジュールが複数回デプロイされている場合は最新の 1 件（`MAX(DetailId)`）の OpType を採用する。** deployed/ にあるファイルは最後のデプロイで置かれたものだから。
5a. **【実データ確認で追加】既知の 3 区分（`新規` / `更新` / `削除`）以外の値は `不明` に寄せる。** 実データに `更新2` という不正値の行が 1 件存在する。画面に未知のラベルをそのまま出さないための防御。

5. **引き当てに失敗したファイルは `不明` として表示する。** 手動で置かれたファイル、SQLite を作り直した後、本システム導入以前のファイル等が該当する。`ManualApplyService.List()` が manifest に無いファイルを `OpType = "不明"` として拾う既存挙動と揃える。区分が引けないことをエラー扱いにはしない。
6. **セッションの成否（`DeploySession.Status`）や明細の成否（`DeploySessionDetail.Result`）では絞り込まない。** deployed/ に置かれている＝適用が成功したファイル、という事実がすでに保証されているため（`Step6_MoveToDeployed` は成功時のみ移動する）。逆に `Result` で絞ると、MariaDB のようにセッション単位で `failed` が記録されうるケース（`DeployController.cs:83` は明細に**セッション全体の**成否を書いている）で正しい区分が引けなくなる。
7. **`DeploySessionDetail` に `SessionId` のインデックスが無い**（`DatabaseService.EnsureCreated`）。逆引きクエリは `DeploySession` との JOIN を伴うため、`IX_DeploySessionDetail_SessionId` を `EnsureCreated` に追加する（既存の `IX_WebSourceDeployLog_*` と同じ `CREATE INDEX IF NOT EXISTS` パターン。既存 DB にも次回起動時に自動で作られる）。
8. **逆引きは DB（`DbConfig.Name`）ごとに 1 クエリ、ファイルごとに辞書引き**とする。`GetFiles` は最大 4 DB 分ループするので最大 4 クエリ。件数はモジュール数オーダーで小さく、キャッシュは不要。
9. **比較ビューでは操作区分をセル単位で保持する。** 同じファイル名でも DB ごとに区分が異なりうる（kaios は新規、gos は更新）ため、行レベルの代表値ではなく `CompareCell` に持たせる。
10. **バッジの見た目は既存の `prep-manual-badge`（手動適用の区分バッジ）を踏襲**し、区分ごとに配色を変える。削除のみ行全体も赤系にする。
11. **削除件数のサマリは deployed セクション（「今回適用する（SQL）」）にのみ表示する。** 保留中セクションは本番へ出ないため強調の必要度が低い（バッジ自体は表示する）。

→ 誤りがあれば訂正してください。特に 3, 5, 6, 9 は挙動に直結します。

## Core Features

| ID | 機能名 | 説明 |
|----|--------|------|
| P1 | 操作区分の逆引き | `DatabaseService` に「DB 名を指定して、モジュール（DbType + モジュール名）→ 最新 OpType の辞書」を返すメソッドを追加する |
| P2 | API への区分付与 | `PrepareFileInfo` に `OpType` を追加。`PrepareController.GetFiles` が P1 の辞書でファイル名を解決して詰める |
| P3 | ファイル行バッジ | deployed／保留中の各ファイル行に `[新規]` `[更新]` `[削除]` `[不明]` バッジを表示 |
| P4 | 削除の強調 | 削除区分の行を赤系スタイルにし、「今回適用する（SQL）」セクションヘッダに「うち削除 N 件」を表示。確認ダイアログ文言にも削除件数を明記 |
| P5 | 比較ビュー対応 | `CompareCell` に区分を持たせ、比較テーブルのセルと TSV 出力に区分を反映 |

## Tech Stack

既存構成を踏襲。新規依存の追加なし。

- Backend: ASP.NET Core / C#、`Microsoft.Data.Sqlite`（既存）
- Frontend: React + TypeScript（Vite）
- Test: xUnit（backend/Tests）

## Commands

```
Backend build : dotnet build backend/MaintenanceManagement.Api.csproj
Backend test  : dotnet test backend/Tests/Tests.csproj
Frontend build: npm run build   (frontend 配下)
Frontend dev  : npm run dev     (frontend 配下)
```

## Project Structure（変更・追加箇所）

```
backend/Models/PrepareModels.cs              → PrepareFileInfo に OpType を追加
backend/Services/OpTypeResolver.cs（新規）    → DB 種別判定・名前正規化・区分正規化の純粋関数
backend/Services/DatabaseService.cs          → 最新 OpType 逆引きメソッド + IX_DeploySessionDetail_SessionId を追加
backend/Controllers/PrepareController.cs     → GetFiles / ReadFiles で OpType を解決して詰める
backend/Tests/Services/OpTypeResolverTests.cs（新規）        → 正規化・分類の単体テスト
backend/Tests/Services/DatabaseServiceOpTypeTests.cs（新規） → 一時 SQLite 上での逆引き挙動テスト
frontend/src/api/prepare.ts                  → ApiPrepareFileInfo に opType を追加
frontend/src/pages/PrepareForPrd.tsx         → PrepareFile に opType、バッジ描画、削除件数サマリ、確認文言
frontend/src/lib/prepareCompare.ts           → PrepareCompareFile / CompareCell に opType、toTsv に反映
frontend/src/components/PrepareCompareView.tsx → セルへの区分表示
frontend/src/index.css                       → prep-optype-badge-*（区分別配色）・prep-file-item-delete
docs/prepare-optype/SPEC.md                  → 本仕様書
docs/prepare-optype/PLAN.md                  → 実装計画（次フェーズで作成）
```

## Code Style

既存の `ManualApplyService` / `PrepareController` のパターンを踏襲する。

**Backend** — 逆引きのキーは「DbType + モジュール名」の複合とし、`ManualApplyItem.Key` と同じく文字列キーで持つ。分類・正規化ルールは `OpTypeResolver`（純粋関数の static クラス）に集約し、`DatabaseService` はクエリと畳み込みのみを担う。

重複排除の粒度は `ModuleType` 単位ではなく「DB 種別＋モジュール名」単位である。SQL 側で `CASE WHEN ModuleType IN (...)` を書くと DB 種別の判定ルールが SQL と C# に二重定義されるため、**SQL は素直に古い順で返し、C# 側で後勝ちに畳み込む**（PLAN の AD1）：

```csharp
cmd.CommandText = """
    SELECT d.ModuleType, d.ModuleName, d.OpType
    FROM DeploySessionDetail d
    JOIN DeploySession s ON s.SessionId = d.SessionId
    WHERE s.DbName = $dbName
    ORDER BY d.DetailId ASC;
    """;
cmd.Parameters.AddWithValue("$dbName", dbName);

var map = new Dictionary<string, string>(OpTypeResolver.KeyComparer);
using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    var key = OpTypeResolver.ModuleKey(reader.GetString(0), reader.GetString(1));
    map[key] = OpTypeResolver.NormalizeOpType(reader.GetString(2));
}
```

**Frontend** — 既存の `prep-file-db-badge` と同じ位置・同じ構造で区分バッジを並べる。表示ロジックは純関数として切り出す：

```tsx
<span className="prep-file-name">{f.fileName}</span>
<span className={`prep-optype-badge ${opTypeClass(f.opType)}`}>{f.opType}</span>
<span className="prep-file-db-badge">{f.dbType === 'mariadb' ? 'MariaDB' : 'SS'}</span>
```

## Testing Strategy

- **バックエンド単体テスト（xUnit / backend/Tests）**
  - ファイル名 → モジュール名の正規化（`dbo.X.sql` → `X`、`X.sql` → `X`、`dbo.` 無し SQL Server ファイル）
  - `ModuleType` → DbType 判定（`Stored` / `MariaDbFunction` / `MariaDbTable` / 旧値 `MariaDB` → mariadb、`StoredProcedure` 他 → sqlserver）
  - SQL Server と MariaDB に同名モジュールがある場合に取り違えないこと
  - 同一モジュールを複数回デプロイした場合、最新の OpType が採用されること
  - 明細に存在しないファイルが `不明` になること
  - 既存の `DbConfigTests` と同様、一時ディレクトリ + 一時 SQLite ファイルで完結させる
- **フロントエンド**: 手動確認。バッジ表示・削除強調・削除件数サマリ・比較ビュー・TSV 出力を目視。
- **回帰**: `dotnet test` 全件パス。既存の選択→実行→保留の挙動が変わっていないことを DryRun で確認。

## Boundaries

- **Always:**
  - 表示専用の変更に留める。ファイル移動・選択・実行のロジックには手を入れない
  - 区分が引けない場合は `不明` にフォールバックし、例外や空表示にしない
  - SQLite のスキーマ追加は `CREATE ... IF NOT EXISTS` で既存 DB を壊さない形にする
  - 変更後は `dotnet test` と `npm run build` を通す
- **Ask first:**
  - `DeploySessionDetail` のスキーマ変更（カラム追加）が必要になった場合
  - 削除区分の既定チェック状態を変える場合（今回は現状維持で確定）
  - 区分によるフィルタ／ソートを追加する場合（今回はスコープ外）
- **Never:**
  - `FastCopyService` の移動・削除ロジックの変更
  - 未承認での commit / push
  - 区分が不明なファイルを一覧から除外すること（見えなくなる方が危険）

## Success Criteria

- [ ] `GET /api/prepare/files` のレスポンスの各 `files[]` 要素に `opType`（`新規` / `更新` / `削除` / `不明`）が含まれる
- [ ] 本番前準備画面の deployed／保留中の各ファイル行に区分バッジが表示される
- [ ] 削除区分の行が赤系で強調され、「今回適用する（SQL）」セクションヘッダに「うち削除 N 件」が表示される（0 件のときは非表示）
- [ ] 実行確認ダイアログの文言に削除件数が含まれる
- [ ] 比較ビューのセルに区分が表示され、コピー／ダウンロードした TSV にも区分が載る
- [ ] SQL Server と MariaDB に同名モジュールが存在しても、それぞれ正しい区分が表示される
- [ ] 同一モジュールを複数回デプロイした場合、最新の区分が表示される
- [ ] `DeploySessionDetail` に記録の無いファイルが `不明` と表示され、一覧から消えたりエラーになったりしない
- [ ] `dotnet test` が全件パスし、`npm run build` が通る
- [ ] 選択・実行・保留の既存挙動に変化がない（回帰なし）

## Open Questions

1. 区分バッジの配色。現行の `prep-manual-badge` は赤系一色（`#a3341f` / `#fdeae6`）。新規＝緑系、更新＝青系、削除＝赤系、不明＝グレー系を想定しているが、既存パレットとの整合は Plan フェーズで確定する。
2. 比較ビューのセル表示形式。現行は `○` / `○(適用予定)`。ここに区分を出す方法として (a) `○` を `新` / `更` / `削` の 1 文字に置き換える、(b) `○` の隣に小さくバッジを添える、の 2 案がある。TSV も同じ表現に揃える必要があり、Plan フェーズで確定する。
3. `不明` バッジを常時表示するか、`不明` のときはバッジ自体を出さないか（ノイズ削減）。運用上「区分が引けていない」ことを知りたいなら表示すべきだが、SQLite 作り直し直後は全件 `不明` になり画面がうるさくなる。
