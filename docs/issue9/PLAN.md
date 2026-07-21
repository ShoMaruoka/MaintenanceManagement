# Implementation Plan: STG適用画面「削除」モジュールの検出方式見直し (issue #9)

## Overview

STG適用画面のモジュールツリーは開発環境(DevDB)への直接クエリのみで構築されており、開発環境から既に削除済みのモジュールは一覧に出てこず「削除」として選択できない。
`ModuleQueryService` にて DevDB クエリ結果と `GitRepoPath\{Type}\*.sql`（STG側Gitミラー）のファイル一覧を突き合わせ、差分を「削除候補」として検出・API応答に含め、フロントエンドではその削除候補をツリーに混在表示（操作区分は「削除」固定）できるようにする。

バックエンド（検出ロジック・モデル拡張）→ フロントエンド型・APIクライアント → フロントエンドUI・状態管理、の順に垂直に積み上げる。

## Architecture Decisions

- 削除候補の判定は `ModuleInfo.IsDeleteCandidate`（bool）という新規フラグで表現し、既存の `GitOnly` と同様に型・API・フロントエンドまで素通しする。
- 検出ロジックは `ModuleQueryService` 内に閉じる（`GetModulesAsync` が返す各リストに対して、DevDBクエリ結果とは別に `GitRepoPath\{Type}\*.sql` を列挙し差分を追加する）。既存のDevDBクエリ・MariaDBクエリのコードパスには手を入れない。
- フロントエンドは、既存の `module.type === 'Table' || 'UserDefinedTableType'` のような「型に応じた表示分岐」と同じパターンで `module.isDeleteCandidate` の分岐を追加する。新しい状態変数やコンポーネントは作らず、既存の `Map<string, OpType>` 選択状態・`toggleModule`/`selectAll`/`setOpTypeBulk` を拡張する形で対応する。
- 削除候補モジュールの操作区分は、選択状態Map上も常に `'削除'` を維持する（Mapの型自体は変更しない。ロジック側でガードする）。

## Task List

### Phase 1: バックエンド — 検出ロジック

- [ ] Task 1: `ModuleInfo` に `IsDeleteCandidate` フラグを追加
  - **Description:** `backend/Models/ModuleInfo.cs` の `ModuleInfo` クラスに `bool IsDeleteCandidate` プロパティを追加する（デフォルト `false`）。
  - **Acceptance criteria:**
    - `ModuleInfo` に `IsDeleteCandidate` プロパティが追加されている
    - 既存の呼び出し箇所（`ModuleQueryService` 内の `new ModuleInfo { ... }`）はデフォルト値 `false` のままコンパイルが通る
  - **Verification:**
    - `cd backend && dotnet build` がエラーなく通る
  - **Dependencies:** None
  - **Files likely touched:**
    - `backend/Models/ModuleInfo.cs`
  - **Estimated scope:** XS（1ファイル・1プロパティ追加）

- [ ] Task 2: `ModuleQueryService` に削除候補検出ロジックを実装
  - **Description:** `GitRepoPath\{Type}\dbo.*.sql` のファイル一覧を列挙し、DevDBクエリ結果に存在しない名前を `IsDeleteCandidate = true` の `ModuleInfo` として各リストに追加するメソッドを実装し、`GetModulesAsync` から呼び出す。対象タイプは `StoredProcedure` / `Function` / `VIEW` / `Table` / `UserDefinedTableType`（`MariaDB` は対象外）。
  - **Acceptance criteria:**
    - `GitRepoPath\{Type}\` 配下に存在し、DevDBクエリ結果に同名のモジュールが存在しない場合、そのモジュールが対応するリスト（`StoredProcedures`/`Functions`/`Views`/`Tables`/`UserDefinedTableTypes`）に `IsDeleteCandidate = true` で追加される
    - DevDBクエリ結果に既に存在する名前は重複追加されない
    - `GitRepoPath` や対象タイプのフォルダが存在しない場合は例外を投げず、空リストとして扱われる（既存の「パスが存在しない場合はスキップ」という規約に合わせる）
    - `MariaDb` リストには削除候補検出を適用しない
    - 名前の比較は大文字小文字を区別しない（`StringComparer.OrdinalIgnoreCase`）
  - **Verification:**
    - `cd backend && dotnet build` がエラーなく通る
    - 手動確認: ローカル環境で `appsettings.Development.json` の `GitRepoPath`（例: `D:\STGENV\KaiosDB_rep\StoredProcedure\`）にDevDBには存在しないテスト用ファイル（例: `dbo.TestDeleteCandidate.sql`）を配置し、バックエンドを起動して `http://localhost:5254/api/modules/kaios` を叩き、レスポンスの `storedProcedures` に `isDeleteCandidate: true` の当該モジュールが含まれることを確認
  - **Dependencies:** Task 1
  - **Files likely touched:**
    - `backend/Services/ModuleQueryService.cs`
  - **Estimated scope:** M（1ファイル・新規メソッド＋既存メソッドへの組み込み）

### Checkpoint: バックエンド完了

- [ ] `dotnet build` がエラーなく通る
- [ ] ローカル環境（DryRun可）で `/api/modules/{db}` のレスポンスに削除候補が正しく含まれることを確認
- [ ] 通常モジュール（DevDBに存在するもの）のレスポンス内容に変化がないことを確認

### Phase 2: フロントエンド — 型・APIクライアント

- [ ] Task 3: `Module` / `ApiModuleInfo` に `isDeleteCandidate` を追加
  - **Description:** `frontend/src/types.ts` の `Module` インターフェースと `frontend/src/api/modules.ts` の `ApiModuleInfo` に `isDeleteCandidate: boolean` を追加し、`formatModules` でバックエンドのレスポンスから素通しする。
  - **Acceptance criteria:**
    - `Module` 型に `isDeleteCandidate: boolean` が追加されている
    - `formatModules` が `isDeleteCandidate` をマッピングして返す
  - **Verification:**
    - `cd frontend && npm run build`（型チェック）がエラーなく通る
  - **Dependencies:** Task 2
  - **Files likely touched:**
    - `frontend/src/types.ts`
    - `frontend/src/api/modules.ts`
  - **Estimated scope:** XS（2ファイル・フィールド追加）

### Phase 3: フロントエンド — 状態管理・表示

- [ ] Task 4: 削除候補モジュールの選択・一括操作ロジックのガード
  - **Description:** `DeployStg.tsx` の `toggleModule` / `selectAll` / `setOpTypeBulk` を修正し、削除候補モジュール（`module.isDeleteCandidate === true`）をチェックした際は操作区分を常に `'削除'` にする／維持するようにする。`moduleTypeOf` 等で対象モジュールを引けるようにし、一括変更・すべて選択の対象からは除外するか、操作区分を上書きしないようにガードする。
  - **Acceptance criteria:**
    - 削除候補モジュールをチェックすると、選択状態Mapの値が `'削除'` になる
    - `selectAll()` 実行時、削除候補モジュールも選択されるが操作区分は `'更新'` ではなく `'削除'` になる
    - `setOpTypeBulk()`（一括変更）を実行しても、削除候補モジュールの操作区分は `'削除'` のまま変わらない
    - 通常モジュール（`isDeleteCandidate` が `false`）の既存の挙動（初期値`'更新'`、一括変更で自由に変更可能）に変化がない
  - **Verification:**
    - `cd frontend && npm run build` がエラーなく通る
    - 手動確認: `npm run dev` でSTG適用画面を開き、Task 2で用意した削除候補モジュールをチェック → 「すべて選択」→ 一括変更で「新規」を選んでも、削除候補モジュールの区分だけ「削除」のままであることを確認
  - **Dependencies:** Task 3
  - **Files likely touched:**
    - `frontend/src/pages/DeployStg.tsx`
  - **Estimated scope:** S（1ファイル・既存関数の修正）

- [ ] Task 5: ツリー行の表示（削除候補バッジ・操作区分の固定表示）
  - **Description:** `DeployStg.tsx` のモジュール一覧行のレンダリングに、`module.isDeleteCandidate` が `true` の場合の分岐を追加する。「削除候補」バッジを表示し、選択済み時は既存の操作区分 `<select>` の代わりに「削除」固定のバッジ（変更不可）を表示する。
  - **Acceptance criteria:**
    - 削除候補モジュールの行に「削除候補」バッジが表示される
    - 削除候補モジュールをチェックした状態で、操作区分は `<select>` ではなく「削除」固定のバッジとして表示され、クリックしても他の区分に変更できない
    - 通常モジュールの行の表示（既存の `<select>` によるプルダウン）に変化がない
  - **Verification:**
    - `cd frontend && npm run build` がエラーなく通る
    - 手動確認: `npm run dev` でSTG適用画面を開き、削除候補モジュールの行に「削除候補」バッジが出ること、チェック時に操作区分が「削除」固定表示（プルダウンでない）になることを確認
  - **Dependencies:** Task 4
  - **Files likely touched:**
    - `frontend/src/pages/DeployStg.tsx`
    - （必要であれば）`frontend/src/index.css`（`module-delete-candidate-badge` 等のスタイル追加）
  - **Estimated scope:** S（1ファイル・表示分岐追加）

### Checkpoint: フロントエンド完了・全体E2E確認

- [ ] `npm run build` / `dotnet build` がエラーなく通る
- [ ] ローカル環境で以下のE2Eシナリオを確認
  - `GitRepoPath` にはあるがDevDBにないモジュールが「削除候補」バッジ付きでツリーに表示される
  - チェックすると操作区分「削除」固定になる（変更不可）
  - 「すべて選択」「一括変更」を行っても削除候補の区分は「削除」のまま
  - 確認ダイアログ・実行ログ・実行履歴が通常の削除と同様に動作する（既存の `DeployService` 側は無変更のため回帰がないことの確認）
  - 通常モジュール（新規/更新/削除を自由に選べる）の挙動に影響がない
- [ ] SPEC.md の Success Criteria 1〜7 を全て満たす
- [ ] ユーザーによる最終レビュー・承認

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `GitRepoPath` 配下のディレクトリ列挙をAPIリクエスト毎に行うため、ファイル数が多い環境でレスポンスが遅くなる | Low | 現状のモジュール数規模（数百件程度）では実用上問題ない想定。将来的にキャッシュが必要になれば別issueで対応 |
| ファイル名パース（`dbo.{Name}.sql`）が命名規則の例外（`dbo` 以外のスキーマ等）に対応できない | Low | SPEC.md Open Questions に記載済み。既存の `Step4_SqlConvert` と同じ前提を踏襲するため、既存フローと矛盾は生じない |
| ローカル開発環境でSQL Server/GitRepoPathの両方を用意できず手動確認がしづらい | Medium | `LOCAL_TEST_GUIDE.md` の手順（テスト用 `.sql` ファイルの配置）を踏襲し、`GitRepoPath` フォルダにダミーファイルを置くだけで検証可能な設計にする（Task 2で確認済み） |
| 削除候補の選択状態Mapが通常モジュールと同じ `OpType` 型のため、誤って他の操作区分に変更できてしまう実装ミス | Medium | Task 4 で `toggleModule`/`selectAll`/`setOpTypeBulk` 全ての変更経路にガードを入れ、Task 5 でUI上も `<select>` を出さないことで二重に防止する |

## Open Questions

- なし（SPEC.md記載のOpen Questions [GitRepoPathの更新タイミング／命名規則の例外] は実装をブロックしないため、実装を進めながら必要に応じて確認する）
