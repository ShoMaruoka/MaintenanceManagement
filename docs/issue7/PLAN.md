# 実装計画: Issue #7 — STG適用の複数DB横断実行

## Overview

複数DBにまたがって選択されたモジュールを一括実行できるよう、フロントエンドの4ファイルを変更する。
バックエンドは変更不要（`/api/deploy/stream` をDB数分だけ順次呼び出す）。

## アーキテクチャ決定事項

- **フロントのみ変更**: バックエンドAPIは変更せず、既存の単一DB用エンドポイントをDB数分呼び出す
- **実行順序**: `dbConfigs` リスト順（kaios→gos→paf→duskin）で直列実行
- **エラー時継続**: あるDBがエラーになっても後続DBは実行する
- **型の追加**: `MultiDbModules = { db: DbName; modules: SelectedModule[] }[]` を `types.ts` に追加
- **`LogViewer` の責務**: 複数DBを順次実行し、各DBの実行フェーズをまとめて表示する

## 依存関係グラフ

```
types.ts（MultiDbModules型追加）
    │
    ├── DeployStg.tsx（allConfirmModules / totalSelected の変更）
    │       │
    │       ├── ConfirmDialog.tsx（Props変更・複数DB表示）
    │       │
    │       └── LogViewer.tsx（Props変更・順次実行）
    │
    └── （バックエンド変更なし）
```

## タスク一覧

---

### Phase 1: 型定義の追加

---

## Task 1: `types.ts` に `MultiDbModules` 型を追加

**Description:**
複数DBのモジュール選択をまとめて渡すための型を追加する。
`ConfirmDialog` と `LogViewer` の Props 変更の基盤となる。

**Acceptance criteria:**
- [ ] `MultiDbModules` 型が `types.ts` にエクスポートされている
- [ ] 型定義は `{ db: DbName; modules: SelectedModule[] }[]` である
- [ ] TypeScript のビルドエラーが出ない

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る

**Dependencies:** なし

**Files likely touched:**
- `frontend/src/types.ts`

**Estimated scope:** XS

---

### Phase 2: コンポーネントの Props 変更（下位から上位へ）

---

## Task 2: `ConfirmDialog.tsx` を複数DB表示に対応する

**Description:**
Props を `dbName + modules` から `allModules: MultiDbModules` に変更し、
DBごとにグループ化したモジュール一覧を表示するよう変更する。

変更内容:
- Props: `dbName: DbName`, `modules: SelectedModule[]` → `allModules: MultiDbModules`
- 「対象 DB」欄: 実行対象DBをカンマ区切りで表示（例: `kaios, gos`）
- 「適用モジュール」欄: 合計件数＋DB数（例: `適用モジュール 5件（2 DB）`）
- モジュール一覧: DBごとにヘッダー（`▼ kaios（3件）`）を付けてグループ表示
- 警告文: 「複数DBは順次実行されます」を追記

**Acceptance criteria:**
- [ ] `allModules` が空のDBはリストに表示されない（呼び出し側でフィルタ済みが前提だが防御）
- [ ] DBが1件の場合は「1 DB」と表示される（複数形に変える必要なし）
- [ ] DBが複数の場合は各DBセクションにモジュール件数が表示される
- [ ] TypeScript のビルドエラーが出ない

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る

**Dependencies:** Task 1

**Files likely touched:**
- `frontend/src/components/ConfirmDialog.tsx`

**Estimated scope:** S

---

## Task 3: `LogViewer.tsx` を複数DB順次実行に対応する

**Description:**
Props を `dbName + modules` から `allModules: MultiDbModules` に変更し、
DBごとに `startDeploy()` を順次呼び出す実行ロジックに変更する。

変更内容:
- Props: `dbName: DbName`, `modules: SelectedModule[]` → `allModules: MultiDbModules`
- 実行ロジック: `for...of` ループで各DBを順次 `await` 実行
- 各DB開始時にステップインジケーターをリセット
- 各DB開始時にDBヘッダーログを追加（例: `=== kaios 適用開始（1/2） ===`）
- ヘッダーの「STG 適用」横に現在実行中のDB名を表示
- 全DB完了後に `onDone()` を呼び出す
- 中断時は AbortController で現在DBを停止し後続DBも実行しない

**Acceptance criteria:**
- [ ] 複数DBを `allModules` の順番通りに順次実行する
- [ ] DB間でステップインジケーターがリセットされる
- [ ] ログに各DBの開始・終了を区切るヘッダー行が出力される
- [ ] 中断ボタンで後続DBの実行が停止する
- [ ] 1件DBのみの場合も正常に動作する（既存動作の後退がない）
- [ ] TypeScript のビルドエラーが出ない

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る

**Dependencies:** Task 1

**Files likely touched:**
- `frontend/src/components/LogViewer.tsx`

**Estimated scope:** M

---

### Phase 3: DeployStg.tsx の変更（呼び出し側の更新）

---

## Task 4: `DeployStg.tsx` を全DB選択対応に変更する

**Description:**
`confirmModules`（単一DB）を `allConfirmModules`（全DB）に切り替え、
`totalSelected`（フッター件数表示）・「戻る」のクリア処理・`ConfirmDialog`・`LogViewer` への Props 渡しを更新する。

変更内容:
1. `allConfirmModules: MultiDbModules` の追加（全DBの選択をまとめる `useMemo`）
2. `totalSelected` を全DBの合計値に変更（`selectedModulesByDb` を集計）
3. `ConfirmDialog` の Props を `allModules={allConfirmModules}` に変更
4. `LogViewer` の Props を `allModules={allConfirmModules}` に変更
5. 「適用画面に戻る」の選択クリアを `allConfirmModules` の全DBに適用
6. フッターの操作区分カウント（新規/更新/削除）を全DBの合計に変更

**Acceptance criteria:**
- [ ] 複数DBにモジュールを選択した状態でフッターの件数が全DB合計を表示する
- [ ] 「実行内容を確認する」で全DBのモジュールが確認ダイアログに渡される
- [ ] 実行後「戻る」で、実行した全DBの選択がクリアされる
- [ ] 選択が0件の場合「実行内容を確認する」がdisabledのままである
- [ ] TypeScript のビルドエラーが出ない
- [ ] `cd frontend && npm run build` がエラーなく通る

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `cd frontend && npm run build` がエラーなく通る

**Dependencies:** Task 1, Task 2, Task 3

**Files likely touched:**
- `frontend/src/pages/DeployStg.tsx`

**Estimated scope:** M

---

### Checkpoint: 全タスク完了後

- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `cd frontend && npm run build` が成功する
- [ ] 単一DB選択時の動作が既存と変わらない（後退なし）
- [ ] 複数DBにモジュールを選択して確認ダイアログにDB別一覧が表示される
- [ ] 実行ログで各DB開始ヘッダーが出力される

---

## タスクサマリー

| # | タスク | Scope | Dependencies |
|---|--------|-------|---|
| 1 | `types.ts` に `MultiDbModules` 型を追加 | XS | なし |
| 2 | `ConfirmDialog.tsx` を複数DB表示に対応 | S | Task 1 |
| 3 | `LogViewer.tsx` を複数DB順次実行に対応 | M | Task 1 |
| 4 | `DeployStg.tsx` を全DB選択対応に変更 | M | Task 1,2,3 |

**実装順序:** Task 1 → Task 2 & Task 3（並行可能）→ Task 4

---

## リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| `LogViewer` の `useEffect` 依存関係で StrictMode 2重実行 | デプロイが2回走る | 既存の `deps: []` パターンを維持し `allModules` を ref で保持 |
| DB間ステップリセットでアニメーションが不自然 | UX 劣化 | ステップリセット前に短い区切りログを挿入して視覚的に区別 |
| 中断後に後続DBのPromiseが解決されない | メモリリーク | AbortController の signal を各 `startDeploy` に渡し、abort 時はループを break |

## 未解決の質問

- エラーが発生したDBを後続も継続するか、止めるかはSPECでは「継続」だが、UX上「止める」が安全かもしれない。実装前に確認推奨。
