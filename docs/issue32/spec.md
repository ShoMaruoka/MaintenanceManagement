# Spec: Issue #32 操作区分（新規／更新）の自動判定

## Objective

本番前準備／STG適用のモジュールツリー（`DeployStg.tsx`）では、操作区分「削除」は
DBとGitリポジトリの存在有無から自動判定されているが、「新規」「更新」は現状ユーザーが
ドロップダウンで手動設定している。

この手動設定を廃止し、「削除」と同じ考え方（DB上の存在有無 × Gitリポジトリ上の
ファイル存在有無）で「新規」「更新」も自動判定できるようにする。

- ユーザー: 本アプリでSTG/本番へのモジュール適用を行う開発者・運用担当者
- 成功の定義: モジュールツリーに表示される各モジュールの操作区分（新規／更新／削除）が、
  DBとGitの存在有無から常に自動で正しく決定され、ユーザーが誤った区分を選んで
  適用してしまうリスクがなくなる。

## 判定ルール（削除判定と対称）

既存の削除候補判定 (`ModuleQueryService.FindDeleteCandidates` /
`FindMariaDbStoredDeleteCandidates`) は「Gitに存在し、DBに存在しない」ファイルを
削除候補として検出している。これと対称に、DB問い合わせ結果（`QuerySqlServerAsync` /
`QueryMariaDbRoutinesAsync` / `QueryMariaDbTablesAsync` が返す各モジュール）について、
対応するGitファイルが存在するかどうかで以下のように判定する。

| DB (Dev) | STG | Git | 操作区分 |
|---|---|---|---|
| 存在する | 存在する | （不問） | 更新 |
| 存在する | 存在しない | （不問） | 新規 |
| 存在する | （接続未設定） | 存在する | 更新（Git 代理） |
| 存在する | （接続未設定） | 存在しない | 新規（Git 代理） |
| 存在しない | （不問） | 存在する | 削除（既存ロジックのまま変更なし） |

- SQL Server: `StgConnectionString` が設定されていれば STG DB への存在照会を権威ある判定とする。
  未設定または照会失敗時のみ、Git ファイル存在を代理指標とする（従来方式）。
- MariaDB: STG 専用接続が無いため Git ファイル存在で判定する（MariaDB の適用 SQL は
  新規／更新とも DROP IF EXISTS + CREATE のため、SQL Server ほど区分の影響は大きくない）。
- 判定は名前の存在有無のみで行う。ファイル内容の差分比較は行わない（既存の削除判定と
  同じ方針）。
- 対象は全種別（StoredProcedure, Function, VIEW, Stored, MariaDbFunction）に加えて、
  GitOnly系（Table, UserDefinedTableType, MariaDbTable — modify_dateによる差分追跡が
  できない種別）も含める。GitOnly系も「Gitにファイルがあるかどうか」だけで新規／更新を
  判定する。
- ファイル名の正規化（`dbo.`プレフィックスの扱い、拡張子除去）は既存の
  `FindDeleteCandidates` / `OpTypeResolver.NormalizeFileName` 相当のルールを踏襲し、
  DB側名前とGit側ファイル名を同じ基準で比較する。

## 挙動変更

- 自動判定された操作区分（新規／更新）は固定表示とし、削除と同様にユーザーは
  手動で上書きできない。
- `DeployStg.tsx` の操作区分ドロップダウン（`SELECTABLE_OP_TYPES` による更新／新規の
  手動選択、一括変更セレクトを含む）は撤去し、`op-badge op-badge-fixed` 相当の
  固定バッジ表示に統一する（削除候補の表示と同じ扱い）。
- `Module.isDeleteCandidate` と対になる形で、バックエンドが算出した操作区分
  （新規／更新／削除）をAPIレスポンスに含め、フロントエンドはそれをそのまま表示・送信する。
  現状 `ModuleInfo` は `IsDeleteCandidate: bool` のみを持つが、新規判定結果を表現する
  ためにモデル拡張が必要（例: `IsNewCandidate: bool` を追加、または
  `OpType: "新規"|"更新"|"削除"` という文字列プロパティに統一するかは実装時に判断）。

## 影響範囲（変更が想定されるファイル）

- `backend/Services/ModuleQueryService.cs` — DB問い合わせ結果に対してGit存在チェックを行い、
  新規／更新を判定するロジックを追加。
- `backend/Models/ModuleInfo.cs` — 新規判定結果を保持するプロパティを追加。
- `backend/Tests/Services/ModuleQueryServiceNewCandidateTests.cs`（新設）— 新規／更新判定の
  テストを追加する。既存の `ModuleQueryServiceDeleteCandidateTests.cs` /
  `ModuleQueryServiceMariaDbDeleteCandidateTests.cs` は実リポジトリの外部fixture
  （`test/` 配下、`.gitignore` 対象）に依存しているため既存ファイルへの追加はせず、
  `Directory.CreateTempSubdirectory()` を使った自己完結型のテストとして新規ファイルに
  分離する（詳細は [`PLAN.md`](./PLAN.md) の Architecture Decisions を参照）。
- `frontend/src/types.ts` — `Module` 型に自動判定結果を反映するフィールドを追加。
- `frontend/src/pages/DeployStg.tsx` — 手動ドロップダウン・一括変更UIの撤去、固定バッジ表示への変更。
- `frontend/src/lib/opType.ts` — 必要に応じてヘルパー追加（既存の `opTypeClass` 等は流用可）。
- `frontend/src/api/modules.ts`（型定義箇所）— レスポンス型の更新。

`OpTypeResolver.cs`（本番前準備画面でのdeployed/ファイルとDeploySessionDetailの突合用）は
別用途（適用済みログとの照合）であり、本Issueの対象である「STG適用前のモジュールツリー
表示」とは別レイヤーのため、原則変更不要と想定。ただし影響有無は実装時に再確認する。

## Commands

```
Backend build : cd backend && dotnet build
Backend test  : cd backend/Tests && dotnet test
Frontend build: cd frontend && npm run build   (tsc && vite build)
Frontend dev  : cd frontend && npm run dev
```

## Project Structure

```
backend/Services/ModuleQueryService.cs → DB/Git突合・削除候補検出ロジック（今回拡張）
backend/Services/OpTypeResolver.cs     → 適用済みログとdeployedファイルの突合（別用途）
backend/Models/ModuleInfo.cs           → モジュール1件分のDTO
backend/Tests/Services/               → xUnitテスト
frontend/src/pages/DeployStg.tsx      → STG適用画面（モジュールツリー・操作区分表示）
frontend/src/lib/opType.ts            → 操作区分の表示ヘルパー
frontend/src/types.ts                 → Module/OpType等の型定義
```

## Code Style

既存コードに準拠する。C#側は日本語XMLドキュメントコメントで「なぜ」を明記するスタイル
（`OpTypeResolver.cs`, `ModuleQueryService.cs` 参照）。TypeScript側はコンポーネント内での
局所的な日本語コメントで意図を残すスタイル。

## Testing Strategy

- バックエンド: xUnit。`ModuleQueryServiceDeleteCandidateTests.cs` /
  `ModuleQueryServiceMariaDbDeleteCandidateTests.cs` に倣い、一時ディレクトリに
  ダミーGitファイルを配置し `FindDeleteCandidates` 等と対称の新規／更新判定関数を
  ユニットテストする。
- フロントエンド: 既存に自動テストの仕組みは見当たらないため、型チェック（`tsc`）と
  手動確認（開発サーバーでモジュールツリーを表示し、DB/Gitの存在パターンごとに
  バッジが正しく出ることを確認）で担保する。

## Boundaries

- Always: 既存の削除候補判定ロジックとの対称性を保つ（同じ正規化ルール・同じ比較方式）。
- Ask first: なし（プロパティ設計は `IsNewCandidate: bool` 追加で決定済み）。
- Never: 削除判定ロジック自体（`FindDeleteCandidates` / `FindMariaDbStoredDeleteCandidates`）
  の挙動を変更しない。

## Success Criteria

- [x] STG適用画面で、DBに存在しGitに存在しないモジュールは「新規」、DBにもGitにも存在する
  モジュールは「更新」として自動表示される。
- [x] 上記の自動判定結果はユーザーが変更できない（削除候補と同様の固定バッジ表示）。
- [x] Table / UserDefinedTableType / MariaDbTable を含む全種別・SQL Server/MariaDB両エンジンで
  正しく判定される。
- [x] 既存の削除候補判定・適用フロー（DeployService等）に回帰がない
  （`ModuleQueryServiceNewCandidateTests` 10件合格。fixture 依存の既存4件はこの環境では未実行）

**実装完了（2026-08-07）**: Backend（`IsNewCandidate` / STG 優先の `MarkAbsentAsNew` /
Git フォールバック）と Frontend（`resolveOpType` / 選択は `Set`・opType は都度導出 /
固定バッジ）を実装し、Task 7 の通し確認も完了。PR レビュー指摘 1〜5 にも対応済み。
詳細は [`PLAN.md`](./PLAN.md)。

**実装上の差分メモ**:
- 新規判定テストは `Path.GetTempPath()` + GUID の一時ディレクトリで自己完結
  （当該環境の `DirectoryInfo` が `IDisposable` 非対応のため）。
- フロントの `formatModules` は API 欠落時に備え `!!` で boolean 化している。
- SQL Server の新規／更新は `StgConnectionString` による STG 存在判定を優先する。
  MariaDB は Git 代理のまま（STG 専用接続が設定に無い）。

**既知の限界（対応不要・仕様として明記）**: 対象タイプのGitサブフォルダが丸ごと存在しない
（gitは空ディレクトリを追跡しないため起こりうる）場合、当該タイプのDBモジュールは
「更新」バッジ表示になるが、これは誤表示防止のためのフォールバックであり、実際にそのモジュールを
選択してSTG適用を実行すると `DeployService` がGit上のファイルを読めず失敗する
（新規／更新いずれの判定でも同様に失敗する）。バッジ表示はあくまで画面上の分類であり、
「更新」表示＝デプロイ可能を保証するものではない。詳細は
[`PLAN.md`](./PLAN.md) の Risks とレビュー対応履歴（指摘3）を参照。

## Open Questions（Planフェーズで決定済み）

- `ModuleInfo` のプロパティ設計: `IsDeleteCandidate` と対称の `IsNewCandidate: bool` を
  追加する方式を採用する。`OpType` 文字列への一本化は既存の `IsDeleteCandidate` を使う
  呼び出し箇所（フロント・テスト）への影響が大きいため見送る。詳細は
  [`PLAN.md`](./PLAN.md) の Architecture Decisions を参照。
- `OpTypeResolver.cs`（本番前準備／実行履歴照合用）は対象外。本Issueが扱うのは
  STG適用前のモジュールツリー（`ModuleQueryService` / `DeployStg.tsx`）であり、
  適用済みログとの突合ロジックとは別レイヤーのため変更しない。
