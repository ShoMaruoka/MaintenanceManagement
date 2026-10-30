# Tasks: STG 適用時の CREATE OR ALTER 変換の適用範囲拡大

SPEC: [SPEC.md](SPEC.md) / PLAN: [PLAN.md](PLAN.md)

- [x] Task 1: 変換ロジック `SqlDefinitionConverter` を TDD で実装
  - Acceptance:
    - `ALTER PROCEDURE` / `ALTER PROC` / `ALTER FUNCTION` / `ALTER VIEW` が `CREATE OR ALTER ...` になる
    - 小文字・複数空白・タブ・改行・先頭インデントを許容する
    - 既に `CREATE OR ALTER` / `CREATE PROCEDURE` の入力は不変
    - 行コメント `-- ALTER PROCEDURE` は対象外
    - 2 つ目以降の行頭 `ALTER ...` は変換しない、`ALTER TABLE` は対象外
  - Verify: `dotnet test backend/Tests/MaintenanceManagement.Api.Tests.csproj --filter "FullyQualifiedName~SqlDefinitionConverter"`
  - Files: `backend/Services/SqlDefinitionConverter.cs`（新規）, `backend/Tests/Services/SqlDefinitionConverterTests.cs`（新規）

- [x] Task 2: Step 4 で削除以外をすべて変換するよう配線
  - Acceptance:
    - SQL Server「更新」の出力が `CREATE OR ALTER` になる（本件の主目的）
    - 「新規」VIEW の出力が `CREATE OR ALTER VIEW` になる
    - 「削除」は従来どおり `DROP ... IF EXISTS`
    - Shift_JIS の日本語が文字化けしない
    - ログが `[新規] / [更新] CREATE OR ALTER 変換` 表記になる
  - Verify: `dotnet test backend/Tests/MaintenanceManagement.Api.Tests.csproj`（全件）
  - Files: `backend/Services/DeployService.cs`, `backend/Tests/Services/DeployServiceStep4SqlServerTests.cs`（新規）

- [x] Task 3: 仕上げ
  - Acceptance: SPEC の Success Criteria を全てチェック。バージョンを 1.6.4 に更新（2026-10-30 ユーザー指示）
  - Verify: SPEC.md 目視、`dotnet test` 全件成功
  - Files: `docs/create-or-alter/SPEC.md`, `backend/MaintenanceManagement.Api.csproj` / `frontend/package.json` / `frontend/package-lock.json`（1.6.3 → 1.6.4。Git タグ v1.6.4 は未作成）
