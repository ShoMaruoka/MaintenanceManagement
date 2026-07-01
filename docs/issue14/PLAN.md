# 実装計画: Issue #14 — 本番前準備(Prepare)画面の横並び比較機能

## Overview

`PrepareForPrd.tsx` に「比較」ボタンを追加し、既存のDBごとカード表示 ⇔ 表形式の横並び比較ビューを画面内で切り替えられるようにする。比較ビューは新規コンポーネント `PrepareCompareView.tsx` として切り出し、比較用データ生成・TSV変換ロジックはユーティリティ関数として分離する。バックエンド変更は不要。

## アーキテクチャ決定事項

- **フロントのみ変更**: 既存API `GET /api/prepare/files` のレスポンス（`dbEntries`, `checked`）をそのままフロントで加工する。
- **コンポーネント分離**: 比較表は `PrepareForPrd.tsx` に直書きせず `PrepareCompareView.tsx` に切り出す（既存357行の肥大化を避け、責務を分離）。
- **ロジック分離**: 比較行データの構築（DB横断でのファイル存在マップ作成）とTSV文字列化は `frontend/src/lib/prepareCompare.ts`（新規）に純粋関数として実装し、単体で確認しやすくする。
- **表示状態**: `PrepareForPrd.tsx` に `viewMode: 'cards' | 'compare'` の state を追加し、切り替えボタンで制御する。
- **表構成**: 「今回適用する」「保留中」でセクション（表）を分ける。行キーは `fileName + dbType`。
- **保留中の状態表現**: 比較ビューは `checked: Set<string>`（既存のチェック状態）を props で受け取り、保留ファイルが適用予定に変わっているかをセルの見た目に反映する（比較ビュー内でのトグル操作はしない＝読み取り専用）。
- **エクスポート形式**: TSV。セクション見出し行＋ヘッダー行＋DB列。クリップボードコピーとダウンロードの生成元テキストは共通化する（同じ関数の出力を使う）。

## 依存関係グラフ

```
frontend/src/lib/prepareCompare.ts（新規: 比較データ生成 + TSV変換の純粋関数）
    │
    ├── frontend/src/components/PrepareCompareView.tsx（新規: 比較表UI・コピー/ダウンロードボタン）
    │       │
    │       └── frontend/src/pages/PrepareForPrd.tsx（既存: 比較ボタン追加・viewMode切替・PrepareCompareView呼び出し）
    │
    └── frontend/src/index.css（新規: prep-compare-* クラス追記）
```

## タスク一覧

---

### Phase 1: 比較データ生成ロジック

---

## Task 1: 比較データ生成・TSV変換ユーティリティを作成する

**Description:**
`dbEntries`（DB×ファイル一覧）と `checked`（選択状態）を受け取り、セクション（今回適用する/保留中）ごとに「ファイル行 × DB列」の比較データ構造を組み立てる純粋関数、およびそれをTSV文字列に変換する関数を実装する。UIから独立させ、他タスクの土台とする。

想定インターフェース（実装時に微調整可）:
```ts
type CompareCell = { exists: boolean; checked: boolean }
type CompareRow = { fileName: string; dbType: 'sqlserver' | 'mariadb'; cells: Record<DbName, CompareCell>; isCommon: boolean }
type CompareSection = { label: '今回適用する' | '保留中'; rows: CompareRow[] }

function buildCompareSections(dbEntries: DbEntry[], checked: Set<string>): CompareSection[]
function toTsv(sections: CompareSection[], dbOrder: DbName[]): string
```

- `isCommon`: 全DB（4件）に存在する行かどうか（色分け強調の判定に使う）。
- `toTsv` は各セクションごとに「セクション見出し行」→「ヘッダー行（ファイル名, kaios, gos, paf, duskin）」→ 各行を出力。値は存在すれば `○`、保留中セクションで適用予定へ変更済みなら `○(適用予定)`、なければ空欄。

**Acceptance criteria:**
- [ ] `buildCompareSections` が「今回適用する」「保留中」の2セクションを返す
- [ ] 全DBに存在するファイル行で `isCommon: true` になる
- [ ] 一部DBにしか存在しないファイル行で `isCommon: false` になる
- [ ] 同名だが `dbType` が異なるファイルは別行として扱われる
- [ ] `toTsv` の出力がタブ区切り・セクション見出し・ヘッダー行を含む
- [ ] 保留中で `checked` に含まれるファイルは `○(適用予定)` 表記になる
- [ ] TypeScript のビルドエラーが出ない

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] 自動テストは未導入のため、簡単な手動スクリプト（ブラウザconsoleまたは一時的なconsole.log）で入出力を確認してよい

**Dependencies:** なし

**Files likely touched:**
- `frontend/src/lib/prepareCompare.ts`（新規）

**Estimated scope:** S

---

### Phase 2: 比較ビューUIコンポーネント

---

## Task 2: `PrepareCompareView.tsx` を作成する

**Description:**
Task 1のユーティリティを使い、比較表（セクションごとの表、DB列、有無マーク、色分け）とツールバー（コピー/ダウンロードボタン）を持つ表示専用コンポーネントを実装する。

Props（想定）:
```ts
interface PrepareCompareViewProps {
  dbEntries: DbEntry[]
  checked: Set<string>
  dbOrder: DbName[]
}
```

- 各セクションを `<table>` として描画（見出し行 + `ファイル名` 列 + DB列4つ）。
- `isCommon: false` の行に背景色（例: 淡いオレンジ）を付与するクラスを適用。
- セル: 存在すれば ✓ アイコン。保留中セクションで適用予定に変更済みなら別色/補助アイコンを付与。
- ツールバー: 「コピー」ボタン（`navigator.clipboard.writeText(toTsv(...))`、コピー後2秒程度「コピーしました」表示に切り替え）、「ダウンロード」ボタン（Blob生成→`<a download="prepare-compare_YYYYMMDD_HHmmss.txt">`をクリックしてダウンロード）。

**Acceptance criteria:**
- [ ] セクション（今回適用する/保留中）ごとに表が分かれて表示される
- [ ] DB列が `dbOrder`（kaios, gos, paf, duskin）の順で表示される
- [ ] 全DB共通行と一部DB固有行が視覚的に区別できる（色分け）
- [ ] 保留中の「適用予定に変更済み」セルが通常の保留セルと区別できる
- [ ] 「コピー」ボタンでクリップボードにTSVがコピーされ、フィードバック表示がある
- [ ] 「ダウンロード」ボタンで `.txt` ファイルがダウンロードされる
- [ ] ファイルが0件のDB/セクションでも表が崩れない
- [ ] TypeScript のビルドエラーが出ない

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `npm run dev` で単体マウントして見た目を目視確認（Task 3完了後に画面から確認でも可）

**Dependencies:** Task 1

**Files likely touched:**
- `frontend/src/components/PrepareCompareView.tsx`（新規）

**Estimated scope:** M

---

## Task 3: 比較表・関連要素のスタイルを追加する

**Description:**
`index.css` に `prep-compare-*` プレフィックスのクラスを追加し、Task 2のコンポーネントの見た目（表の罫線、色分け背景、ツールバーのボタン、コピー完了フィードバック）を整える。既存の `prep-*` クラスのトーン（フォントサイズ・カラーパレット）に合わせる。

**Acceptance criteria:**
- [ ] 比較表がカード表示と統一感のあるデザインになっている
- [ ] 固有ファイル行の色分けが視認できる
- [ ] ツールバーのボタンが既存の `btn-secondary`/`btn-primary` 等と統一感がある、または明示的に `prep-compare-*` クラスで整えられている
- [ ] レスポンシブ崩れがない（既存ページ幅内で表示可能）

**Verification:**
- [ ] `npm run dev` で目視確認

**Dependencies:** Task 2

**Files likely touched:**
- `frontend/src/index.css`

**Estimated scope:** S

---

### Checkpoint: Phase 1-2完了後

- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `PrepareCompareView` を一時的に `PrepareForPrd.tsx` からダミーpropsでマウントし、表・色分け・コピー・ダウンロードが動作することを確認
- [ ] 人間によるレビュー（表のデザイン・エクスポート内容が期待通りか）

---

### Phase 3: 既存画面への統合

---

## Task 4: `PrepareForPrd.tsx` に比較ボタンと表示切り替えを組み込む

**Description:**
既存カード一覧の上部に「比較」ボタンを追加し、`viewMode` state で `PrepareCompareView` とカード一覧（`prep-grid`）を切り替える。既存のファイル選択・実行フローには影響を与えない。

変更内容:
1. `viewMode: 'cards' | 'compare'` state を追加（初期値 `'cards'`）
2. 説明文の直下、`prep-grid` の直上に比較ボタンを配置（`viewMode === 'compare'` の時はラベルを「一覧に戻る」に変更）
3. `viewMode === 'compare'` のとき `prep-grid` の代わりに `<PrepareCompareView dbEntries={dbEntries} checked={checked} dbOrder={[...]} />` を描画
4. `pageState`（select/confirm/running/done）とは独立した状態として扱う（`running`/`done` 中は比較ボタンを表示しない、または非活性にする）

**Acceptance criteria:**
- [ ] 「比較」ボタンでカード表示 ⇔ 比較表表示が切り替わる
- [ ] 比較ビュー表示中も「本番前準備を実行する」フッター等、既存の実行フローに影響がない（比較ビュー表示中は実行ボタンを隠すか、カード表示同様に出すかは実装時にどちらか選択。デフォルトは比較ビュー中も実行フッターは表示したままにする）
- [ ] `running`/`done` 状態からは比較ビューに入れない、または入っても矛盾がない
- [ ] 既存のファイル選択（チェックボックスのトグル）・全選択/全解除ボタンの動作に後退がない
- [ ] TypeScript のビルドエラーが出ない
- [ ] `cd frontend && npm run build` がエラーなく通る

**Verification:**
- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `cd frontend && npm run build` がエラーなく通る
- [ ] `npm run dev` で本番前準備画面を開き、比較ボタンの切り替え・データ表示・コピー/ダウンロードをEnd-to-Endで確認

**Dependencies:** Task 1, Task 2, Task 3

**Files likely touched:**
- `frontend/src/pages/PrepareForPrd.tsx`

**Estimated scope:** S

---

### Checkpoint: 全タスク完了後

- [ ] `cd frontend && npx tsc --noEmit` がエラーなく通る
- [ ] `cd frontend && npm run build` が成功する
- [ ] 本番前準備画面で比較ボタン → 比較表表示 → 一覧に戻る、が一通り動作する
- [ ] 比較表でDB固有ファイルの色分けが確認できる
- [ ] コピー・ダウンロードでTSV内容が正しいことを確認（実際にWinMerge等に貼り付けて確認できるとなお良い）
- [ ] 既存の本番前準備実行フロー（選択→確認→実行→完了）が壊れていない
- [ ] 人間によるレビュー・SPEC.md の Success Criteria 全項目の確認

---

## タスクサマリー

| # | タスク | Scope | Dependencies |
|---|--------|-------|---|
| 1 | 比較データ生成・TSV変換ユーティリティを作成 | S | なし |
| 2 | `PrepareCompareView.tsx` を作成 | M | Task 1 |
| 3 | 比較表・関連要素のスタイルを追加 | S | Task 2 |
| 4 | `PrepareForPrd.tsx` に比較ボタンと表示切り替えを統合 | S | Task 1,2,3 |

**実装順序:** Task 1 → Task 2 → Task 3 → Task 4（基本的に直列。Task 3はTask 2のマークアップに依存するため並行不可）

---

## リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| `fileKey` の形式（`dbName::dbType::source::fileName`）と比較用の行キー（`fileName+dbType` のみ、DB非依存）が異なるため変換ミスが起きやすい | 比較表の有無判定がずれる | Task 1で `fileKey` を参考にしつつ、比較専用のキー生成関数を明示的に用意しテストする |
| 保留中セクションの「適用予定への変更」表現がUI上わかりにくい | ユーザーが誤解する | Task 2で凡例（色の意味）を表内またはツールバー付近に小さく表示する |
| `dbEntries` が0件・一部DB欠落の場合の表描画 | レイアウト崩れ | Task 2で空データ・欠落DBのハンドリングをacceptance criteriaに明記済み。実装時に空配列を渡して確認 |
| クリップボードAPI (`navigator.clipboard`) が使えない環境（非HTTPS等） | コピー機能が失敗 | 失敗時はエラーを握りつぶさずアラート表示 or ダウンロードを代替手段として案内 |

## 未解決の質問

なし（下記の通り確定済み）:
- 比較ビュー表示中も「本番前準備を実行する」フッターは表示したままにする。
