# Spec: モジュールの適用区分の一括変更機能 (issue #10)

## Objective

STG適用画面(`DeployStg.tsx`)で、選択済みモジュールの操作区分(新規/更新/削除)を1件ずつしか変更できず、選択モジュールが多い場合の変更作業が煩雑という課題を解決する。
現在表示中(検索フィルタ後)の選択済みモジュールに対して、操作区分を一括で変更できるUIを追加する。

- 対象ユーザー: STG適用を行う開発者・運用担当者
- 成功条件: 複数モジュールを選択した状態で、一括変更UIから操作区分を選ぶと、対象モジュール全ての操作区分が一斉に変わる

## Tech Stack

- React 18 + TypeScript + Vite（frontend/ 配下）
- 状態管理: useState/useMemo によるローカルステート（Redux等は未導入）
- 既存コンポーネント: `frontend/src/pages/DeployStg.tsx` のみを変更対象とする

## Commands

```
Dev:     cd frontend && npm run dev
Build:   cd frontend && npm run build   (tsc の型チェック含む)
Preview: cd frontend && npm run preview
```

自動テストフレームワークは未導入のため、`npm run build` の型チェック通過 + `npm run dev` での手動確認を検証手段とする。

## Project Structure

```
frontend/src/pages/DeployStg.tsx   → 変更対象（STG適用メイン画面）
frontend/src/types/                → OpType 等の型定義（変更不要見込み）
```

## Code Style

既存コードのスタイルに合わせる（関数コンポーネント、更新関数は `update〜`/`set〜` 命名、Map ベースの選択状態管理）。

```tsx
function setOpTypeBulk(names: string[], op: OpType) {
  updateDbSelection(selectedDb, m => {
    names.forEach(name => m.set(name, op))
    return m
  })
}
```

## Testing Strategy

- 自動テストなし（プロジェクトに既存のテスト基盤なし）
- 手動確認: `npm run dev` で STG適用画面を開き、以下を確認
  - 複数モジュールを選択 → 一括変更UIで「新規」を選択 → 選択中の全モジュールの操作区分が「新規」になる
  - 検索フィルタで絞り込んだ状態で一括変更 → フィルタ後に表示されているモジュールのみ変更され、フィルタ外（非表示）の選択済みモジュールは変更されない
  - 未選択時は一括変更UIが非表示 or 無効化される

## Boundaries

- Always: 既存の個別変更（行ごとの `<select>`）は維持し、一括変更は追加機能とする。型チェック(`npm run build`)を通す。
- Ask first: バックエンドAPI・型定義（`types/`）に変更が必要になった場合
- Never: 選択状態のデータ構造（`Map<DbName, Map<string, OpType>>`）を破壊的に変更しない。他画面（本番前準備画面等）には手を入れない。

## Success Criteria

1. STG適用画面で、フィルタ後の選択済みモジュールが1件以上あるとき、一括変更UI（検索バー付近）が表示される
2. 一括変更UIで操作区分（新規/更新/削除）を選ぶと、フィルタ後に表示されている選択済みモジュール全ての操作区分が一斉に変わる
3. フィルタで非表示になっている選択済みモジュール（他タブ・他DBの選択含む）は一括変更の対象外
4. `npm run build` がエラーなく通る

## Open Questions

- なし（不明点はユーザーとの質疑で解消済み）
