# Implementation Plan: モジュールの適用区分の一括変更機能 (issue #10)

## Overview

`frontend/src/pages/DeployStg.tsx` に、現在表示中（検索フィルタ後）の選択済みモジュールの操作区分（新規/更新/削除）を一括変更できるUIを追加する。対象は単一ファイルのフロントエンド変更で、バックエンド・型定義の変更は不要。

## Architecture Decisions

- 一括変更は新規の状態変数を追加せず、既存の `selectedModulesByDb` / `updateDbSelection` を再利用する `setOpTypeBulk` 関数で実現する。
- 対象範囲は「フィルタ後に表示中（`filteredModules`）かつ選択済み（`selectedInCurrentType`）」のモジュールのみとし、他DB・他種別・フィルタ外の選択には影響しない。
- UIは検索バー横（`module-list-search-bar` 内、既存の「すべて選択」リンクの近く）に配置する。

## Task List

### Phase 1: 一括変更ロジック

- [x] Task 1: `setOpTypeBulk` 関数の追加
  - **Description:** `DeployStg.tsx` に、指定した名前配列に対して一括で `OpType` をセットする関数を追加する。既存の `setOpType` と同じ `updateDbSelection` パターンを使う。
  - **Acceptance criteria:**
    - `setOpTypeBulk(names: string[], op: OpType)` が `selectedDb` の選択Mapに対して、渡された全ての `name` の操作区分を `op` に更新する
    - 選択されていない（Mapに存在しない）モジュール名を渡しても新規追加はしない（既存選択のみ変更対象）
  - **Verification:**
    - 型チェック: `cd frontend && npm run build`
    - 手動確認: ブラウザのdevtoolsやconsole.logで一時的に呼び出し、Mapの中身が変わることを確認（Task 2で結線後にUI経由で確認できるため簡易確認でよい）
  - **Dependencies:** None
  - **Files likely touched:**
    - `frontend/src/pages/DeployStg.tsx`
  - **Estimated scope:** XS（1ファイル・1関数追加）

### Phase 2: 一括変更UI

- [x] Task 2: 一括変更UIの追加と結線
  - **Description:** 検索バー付近（`module-list-search-bar` 内）に、選択中（`selectedInCurrentType.length > 0`）のときのみ表示される一括変更コントロールを追加する。操作区分（新規/更新/削除）を選ぶと `setOpTypeBulk(selectedInCurrentType.map(m => m.name), op)` を呼び出す。
  - **Acceptance criteria:**
    - `selectedInCurrentType.length === 0` のとき一括変更UIは表示されない（または無効化される）
    - `selectedInCurrentType.length > 0` のとき一括変更UIが表示され、操作区分を選択すると、フィルタ後に表示されている選択済みモジュール全ての操作区分（各行の `<select>` 表示）が一斉に変わる
    - フィルタで非表示になっている選択済みモジュール（他タブ・他DB含む）の操作区分は変更されない
  - **Verification:**
    - 型チェック: `cd frontend && npm run build`
    - 手動確認: `cd frontend && npm run dev` でSTG適用画面を開き、
      1. 複数モジュールを選択 → 一括変更UIで「新規」を選択 → 全選択行が「新規」になることを確認
      2. 検索で絞り込んだ状態で一括変更 → 絞り込み外の選択済みモジュールが変更されないことを確認（別タブに切り替えて確認）
      3. 未選択時は一括変更UIが表示されない/操作できないことを確認
  - **Dependencies:** Task 1
  - **Files likely touched:**
    - `frontend/src/pages/DeployStg.tsx`
  - **Estimated scope:** S（1ファイル・UI追加＋結線）

### Checkpoint: Complete

- [ ] `npm run build` がエラーなく通る
- [ ] SPEC.md の Success Criteria 1〜4 を全て満たす
- [ ] ユーザーによる最終レビュー・承認

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 一括変更UIが既存レイアウト（検索バー・「すべて選択」リンク）と視覚的に競合する | Low | 既存の `select-all-btn` 相当のスタイルに合わせて配置し、必要ならスタイル微調整 |
| 「すべて選択」ボタンと一括変更ボタンの意味が混同される（選択 vs 操作区分変更） | Low | ラベル文言を明確にする（例:「操作区分を一括変更」） |

## Open Questions

- なし
