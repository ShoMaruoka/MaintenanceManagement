# Spec: STG 適用時の CREATE OR ALTER 変換の適用範囲拡大（SQL Server）

## Objective

STG 適用（`DeployService`）の Step 4 では、SQL Server モジュールのうち操作区分が「新規」のものだけ
`ALTER PROCEDURE/FUNCTION` → `CREATE OR ALTER PROCEDURE/FUNCTION` に置換し、「更新」は Git の
ファイル（`ALTER ...`）をそのままコピーしている（`DeployService.cs` `Step4_SqlConvert` / `ConvertAlterToCreate`）。

Pilot 環境への適用は約 1 か月周期で、その間に STG 適用は複数回行われる。このため次の現象が起こる。

1. 1 回目（新規）: `CREATE OR ALTER` に変換されて `deployed/dbo.X.sql` へ移動
2. 2 回目（更新）: `ALTER` のままコピーされ、`File.Move(..., overwrite: true)` で `deployed/dbo.X.sql` を上書き
3. Pilot 適用時: Pilot には X が存在しないため `ALTER` がエラーになる

加えて本番前準備画面の操作区分は後勝ち（`DatabaseService.GetLatestOpTypes`）のため、バッジも「更新」となり事前に気付けない。

本件では、**削除以外の SQL Server 自動デプロイ対象をすべて `CREATE OR ALTER` に変換する**ことで、
適用先にオブジェクトが存在するかどうかに依存しない SQL を `deployed/` に残す。あわせて変換処理を
正規表現ベースに置き換え、VIEW 対応・二重変換防止・表記揺れ対応を行う。

### ユーザー
STG 適用・本番前準備・Pilot 適用を担当する運用担当者。

### 成功の姿
- 新規 → 更新の順で STG 適用されたモジュールでも、`deployed/` のファイルは `CREATE OR ALTER` になっており Pilot 適用でエラーにならない。
- 新規の VIEW が `CREATE OR ALTER VIEW` に変換される。
- Git 側が既に `CREATE OR ALTER` のファイルでも壊れた SQL（`CREATE OR CREATE OR ALTER`）にならない。

### 採用方針と却下案

| 案 | 内容 | 判断 |
|----|------|------|
| **A（採用）** | 削除以外は新規・更新とも `CREATE OR ALTER` に変換 | 適用先の状態に依存せず冪等。変更は Step 4 に閉じる |
| B | deployed/ に新規分が残っていれば新規扱いを引き継ぐ | 却下。「Pilot へ適用済みか」をアプリが把握する必要があり、状態管理が複雑化する |
| C | 本番前準備画面の操作区分を先勝ちにする | 却下。表示が変わるだけで SQL は `ALTER` のまま、エラーは防げない |

### スコープ外
- MariaDB（Git 側が既に `DROP IF EXISTS + CREATE` の完成形のため変換不要。Issue #22 SPEC 参照）
- 削除（`GenerateDropSql`）の挙動
- Table / UserDefinedTableType（Git マージのみ・手動適用待ち。Step 4 を通らない）
- 本番前準備画面の操作区分表示ロジック（後勝ちのまま）
- 本件リリース前に既に `deployed/` / `deployed_hold/` に `ALTER` のまま残っているファイルの自動修正（運用での確認とする。Open Questions 参照）

## ASSUMPTIONS I'M MAKING

1. **Pilot / 本番の SQL Server は 2016 SP1 以降**であり `CREATE OR ALTER` が使える。新規モジュールでは既に `CREATE OR ALTER` を生成して Pilot へ適用しているため、運用実績上も問題ない。
2. 既存オブジェクトに対する `CREATE OR ALTER` は `ALTER` と同等に振る舞い、付与済み権限は保持される。STG 上での挙動は従来の「更新」と変わらない。
3. 1 ファイル = 1 オブジェクト（`dbo.{Name}.sql`）である。変換対象は**ファイル中で最初に現れる定義文 1 か所のみ**とし、本体中の動的 SQL 等を誤って書き換えない。
4. 定義文は**行頭**（先頭の空白は許容）に書かれている。`-- ALTER PROCEDURE ...` のような行コメントは行頭が `--` なので対象外になる。
5. 文字コードは従来どおり Shift_JIS で読み書きする（`ConvertAlterToCreate` の既存挙動を踏襲）。
6. `DeployModule.OpType` は「新規」「更新」「削除」のいずれか。「削除」以外（想定外の値を含む）は変換対象とする（従来「更新」側に倒れていた分岐と同じく、削除以外を一律に扱う）。

## 変換仕様

対象パターン（大文字小文字・空白/タブ/改行の揺れを許容、行頭のみ、最初の 1 件のみ）:

| 入力 | 出力 |
|------|------|
| `ALTER PROCEDURE` | `CREATE OR ALTER PROCEDURE` |
| `ALTER PROC` | `CREATE OR ALTER PROC` |
| `ALTER FUNCTION` | `CREATE OR ALTER FUNCTION` |
| `ALTER VIEW` | `CREATE OR ALTER VIEW` |
| `CREATE OR ALTER ...`（既に変換済み） | 変更しない |

- 置換は `ALTER` キーワードの直前に `CREATE OR ` を挿入する形で行い、キーワード以降の元の表記（大文字小文字・空白）は保持する。
- 該当パターンが無いファイル（例: `CREATE PROCEDURE` で書かれている）はそのままコピーする（Open Questions 1 参照）。

正規表現（案）:

```csharp
// 行頭の ALTER PROC[EDURE] / FUNCTION / VIEW。直前が "CREATE OR " のものは行頭 ALTER ではないので一致しない
private static readonly Regex AlterDefinitionRegex = new(
    @"^(?<indent>[ \t]*)(?<alter>ALTER\s+(?:PROC(?:EDURE)?|FUNCTION|VIEW)\b)",
    RegexOptions.IgnoreCase | RegexOptions.Multiline);
```

## Commands

```
Build: dotnet build backend/MaintenanceManagement.Api.csproj
Test:  dotnet test backend/Tests/MaintenanceManagement.Api.Tests.csproj
Test (本件のみ): dotnet test backend/Tests/MaintenanceManagement.Api.Tests.csproj --filter "FullyQualifiedName~DeployServiceStep4SqlServer|FullyQualifiedName~SqlDefinitionConverter"
```

## Project Structure

```
backend/Services/DeployService.cs                          → Step4_SqlConvert の分岐変更、ConvertAlterToCreate を置換
backend/Services/SqlDefinitionConverter.cs (新規)           → 変換ロジック（純粋関数, internal static）
backend/Tests/Services/SqlDefinitionConverterTests.cs (新規) → 変換の単体テスト
backend/Tests/Services/DeployServiceStep4SqlServerTests.cs (新規) → Step 4 の結合テスト（新規/更新/削除の出力ファイル）
docs/create-or-alter/                                      → SPEC / PLAN / TASKS
```

変換ロジックを別クラスに切り出すのは、`DeployService` の private メソッドのままでは単体テストできないため。
置き場所・粒度は `OpTypeResolver`（static クラス・純粋関数）に倣う。

## Code Style

既存の `OpTypeResolver` / `DeployService` に合わせる。

- XML ドキュメントコメントは日本語で「なぜ」を書く
- `internal static` で公開し、`InternalsVisibleTo` 経由でテストする
- テストは xUnit、`DeployServiceStep4MariaDbTests` と同じく一時ディレクトリ + `DryRun=false` で `ExecuteAsync` を回す

## Testing Strategy

**単体（SqlDefinitionConverterTests）**
- `ALTER PROCEDURE` / `ALTER PROC` / `ALTER FUNCTION` / `ALTER VIEW` → `CREATE OR ALTER ...`
- 小文字・複数空白・タブ・`ALTER` と種別の間の改行
- 先頭インデント付き
- 既に `CREATE OR ALTER` のファイルは不変（二重変換しない）
- `CREATE PROCEDURE` のファイルは不変
- 行コメント `-- ALTER PROCEDURE` は対象外、その後の実定義が変換される
- 本体中に行頭 `ALTER VIEW` 等が 2 つ目として現れても、変換は最初の 1 件のみ
- `ALTER TABLE` 等は対象外

**結合（DeployServiceStep4SqlServerTests）**
- 新規 StoredProcedure → 出力が `CREATE OR ALTER PROCEDURE`
- **更新 StoredProcedure → 出力が `CREATE OR ALTER PROCEDURE`**（本件の主目的）
- 新規 VIEW → 出力が `CREATE OR ALTER VIEW`
- 削除 → 従来どおり `DROP ... IF EXISTS`
- Shift_JIS の日本語（モジュール名・コメント）が文字化けしない

## Boundaries

- Always: 変更前後で既存テストを全件通す。Shift_JIS の読み書きを維持する。
- Ask first: 変換対象の追加（`CREATE PROCEDURE` → `CREATE OR ALTER` 等）、ログ文言以外の UI 変更、OpType の判定ロジック変更。
- Never: MariaDB 側の変換処理に手を入れる。`deployed/` 既存ファイルを自動で書き換える。勝手に commit / push する。

## Success Criteria

**Status**: 実装完了（バージョン 1.6.4）。git commit / push・タグ作成は未実施。

- [x] SQL Server の「更新」モジュールの Step 4 出力が `CREATE OR ALTER` になっている（`DeployServiceStep4SqlServerTests`）
- [x] 新規 VIEW の Step 4 出力が `CREATE OR ALTER VIEW` になっている（同上）
- [x] 既に `CREATE OR ALTER` の入力が二重変換されない（`SqlDefinitionConverterTests`）
- [x] 削除・MariaDB・Git マージのみ種別の挙動が変わらない（削除は新規テスト、MariaDB / Git マージのみは既存テストが無変更で成功）
- [x] Step 4 のログで、新規・更新とも変換したことが分かる（`[新規] / [更新] CREATE OR ALTER 変換`）
- [x] `dotnet test` が全件成功する（228 件）

## Decisions（旧 Open Questions）

1. **Git 側の `CREATE PROCEDURE`（OR ALTER なし）は変換しない。**（2026-10-30 決定）
   テストデータ（`deployed_hold/` 等）には同形式のファイルが存在するが、本件の対象は `ALTER` 起点の現象に限定する。
2. **既に `deployed/` / `deployed_hold/` に残っているファイルは対象外。本件以降の STG 適用分から効けばよい。**（2026-10-30 決定）
