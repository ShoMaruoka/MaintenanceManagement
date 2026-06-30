# Implementation Plan: Issue #5 — 選択機能の強化

## Overview

`DeployStg.tsx` のモジュール選択状態をDB別に保持するよう状態設計を変更し、全DBの選択状況を確認できるサマリーUIを追加する。変更の核心は `Map<string, OpType>` → `Map<DbName, Map<string, OpType>>` への状態構造の変更と、それに伴う参照箇所の更新。

## Architecture Decisions

- サマリーパネルは DBセレクタパネル内（`.db-selector`）の下部に組み込む。独立コンポーネントに切り出してもよいが、状態を props で渡すだけで済むためシンプルを優先する
- `confirmModules` は「現在選択中のDB」のみを対象とする設計を維持し、複数DB同時実行は行わない

---

## Task List

### Phase 1: 状態設計の変更と DB切替修正

#### Task 1: `selectedModules` をDB別管理に変更する

**Description:**  
`DeployStg.tsx` の `selectedModules` 状態を `Map<DbName, Map<string, OpType>>` に変更し、それを参照・更新しているすべての箇所を更新する。DB切替時のリセット処理も削除する。

**Acceptance criteria:**
- [ ] `selectedModulesByDb` 状態が `Map<DbName, Map<string, OpType>>` 型で定義されている
- [ ] DB切替クリック時に `setSelectedModules(new Map())` が呼ばれない
- [ ] `toggleModule`・`setOpType`・`selectAll`・`clearAll` が現在のDBのマップに対して動作する
- [ ] `totalSelected`・`selectedInCurrentType`・`opsCount` が現在のDBの選択のみを参照する
- [ ] `confirmModules` が `selectedModulesByDb.get(selectedDb)` を参照する

**Verification:**
- [ ] TypeScript コンパイルエラーなし: `cd frontend && npx tsc --noEmit`
- [ ] DB切替後、前DBの選択が消えないこと（手動確認）

**Dependencies:** なし

**Files likely touched:**
- `frontend/src/pages/DeployStg.tsx`

**Estimated scope:** Small（1ファイル、状態の型と参照箇所の変更）

---

### Checkpoint: Phase 1

- [ ] `npx tsc --noEmit` がエラーなし
- [ ] 開発サーバーが起動し、DB切替で選択が保持されることを手動確認

---

### Phase 2: 全DB選択状況サマリーUIの追加

#### Task 2: DB セレクタに全DB選択サマリーを追加する

**Description:**  
DBセレクタパネル（`.db-selector`）内に、全DBにわたる選択状況を表示するサマリーセクションを追加する。選択件数が1件以上のDBについて「DB名: N件」を表示し、展開すると種別ごとのモジュール一覧を確認できる。

**Acceptance criteria:**
- [ ] 選択中モジュールが存在するDBのみサマリーに表示される
- [ ] DB名と選択件数（例: `kaios: 3件`）が表示される
- [ ] 展開/折りたたみでモジュール一覧（種別・名前）が確認できる
- [ ] 全DBで選択が0件のときはサマリーセクション自体が表示されない
- [ ] 各モジュール行に「×」ボタンがあり、クリックで該当DBの該当モジュールを選択解除できる

**Verification:**
- [ ] TypeScript コンパイルエラーなし
- [ ] 複数DBでモジュールを選択したときサマリーに両方表示されること（手動確認）
- [ ] 選択をクリアしたDB分がサマリーから消えること（手動確認）

**Dependencies:** Task 1

**Files likely touched:**
- `frontend/src/pages/DeployStg.tsx`
- `frontend/src/components/SelectionSummary.tsx`（新規、任意）

**Estimated scope:** Small-Medium（1〜2ファイル）

---

### Checkpoint: Phase 2（完了）

- [ ] `npx tsc --noEmit` がエラーなし
- [ ] 開発サーバーで以下を手動確認:
  - [ ] DB A でモジュールを選択 → DB B に切替 → DB A の選択が残っている
  - [ ] DB A・B それぞれ選択後、サマリーに両DBが表示される
  - [ ] 「選択をクリア」で現在のDBの選択のみが消える
  - [ ] 確認画面が現在のDBの選択のみを対象にしている

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `clearAll` の仕様変更（全クリアか現DB限定か） | Med | SPEC 境界条件に従い「現在のDBのみ」とする |
| サマリーUIがDBセレクタパネルのレイアウトを崩す | Low | 選択0件時は非表示にして影響を最小化 |

## Open Questions

- サマリーから個別モジュールを「解除」できるボタンを付けるか？（SPEC ではオプション扱い。実装コストが低ければ追加）
