# SPEC: Issue #5 — 選択機能の強化

## 1. Objective

STG適用画面（DeployStg）のモジュール選択機能を強化する。

**対象ユーザー:** メンテナンス担当者（管理者・一般ユーザー）

**解決する問題:**
1. DB切替時に別DBで選択していたモジュールが消えてしまう
2. 全DBにわたって今どのモジュールが選ばれているか確認する手段がない

---

## 2. 現状の問題

`DeployStg.tsx` の DB クリック時の処理（line 136）:

```tsx
onClick={() => { setSelectedDb(db.name); setSelectedModules(new Map()); setSearch('') }}
//                                        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//                                        ここで選択がリセットされる（問題）
```

また `selectedModules` が `Map<string, OpType>`（モジュール名→操作区分）のフラット構造のため、DB をまたいだ選択管理ができない。

---

## 3. To Be（受け入れ条件）

### 3-1. DB切替時に選択を保持する

- DB を切り替えても、切替前のDBで選択していたモジュールはクリアされない
- 各DBの選択状態は独立して保持される
- 確認・実行画面では「現在選択中のDB」の選択モジュールのみを対象にする

### 3-2. 全DBの選択状況を確認できる

- どのDBのどの種別のどのモジュールが選択されているかを一覧で確認できるUIを追加する
- 選択中のモジュールが0件のDBは表示しない（またはグレーアウト）
- 一覧からモジュールを個別に選択解除できる（オプション）

---

## 4. 技術仕様

### 4-1. 状態設計の変更

**Before:**
```ts
const [selectedModules, setSelectedModules] = useState<Map<string, OpType>>(new Map())
// selectedModules: Map<moduleName, OpType>
```

**After:**
```ts
const [selectedModulesByDb, setSelectedModulesByDb] = useState<Map<DbName, Map<string, OpType>>>(new Map())
// selectedModulesByDb: Map<DbName, Map<moduleName, OpType>>
```

現在のDB用のマップは `selectedModulesByDb.get(selectedDb) ?? new Map()` で取得する。

### 4-2. DB切替時の変更

DB切替時に `selectedModules` をリセットするコードを削除し、`selectedDb` のみ更新する。

```tsx
// Before
onClick={() => { setSelectedDb(db.name); setSelectedModules(new Map()); setSearch('') }}

// After
onClick={() => { setSelectedDb(db.name); setSearch('') }}
```

### 4-3. 選択状況サマリーパネル（新規コンポーネント）

**コンポーネント名:** `SelectionSummary`（または DB セレクタ下部への組み込み）

**表示内容:**
- 全DBの選択モジュール数サマリー（例: `kaios: 3件, gos: 1件`）
- 展開すると DB → 種別 → モジュール名の一覧を表示
- 各モジュールに「解除」ボタン（オプション）

**表示場所案:** DB選択パネルの下部、または右側のパネルとして追加

### 4-4. confirmModules の対応

現在 `confirmModules` は `selectedDb` を参照して `SelectedModule[]` を生成している。
DB単位で実行するため、`selectedDb` に対応するマップのみを変換する実装を維持する（複数DB同時実行は行わない）。

```ts
const currentSelected = selectedModulesByDb.get(selectedDb) ?? new Map()
const confirmModules = useMemo((): SelectedModule[] =>
  Array.from(currentSelected.entries()).map(([name, opType]) => {
    const allModules = Object.values(modulesByDb[selectedDb] ?? {}).flat()
    const found = allModules.find(m => m.name === name)
    return { name, opType, type: found?.type ?? 'StoredProcedure' }
  }),
  [currentSelected, modulesByDb, selectedDb],
)
```

---

## 5. 実装範囲

| ファイル | 変更内容 |
|---|---|
| `frontend/src/pages/DeployStg.tsx` | 状態設計の変更・DB切替処理の修正・サマリー表示の追加 |
| `frontend/src/components/SelectionSummary.tsx`（新規・任意） | 全DB選択状況サマリーコンポーネント |

---

## 6. 境界条件

| 区分 | 内容 |
|---|---|
| Always | DB切替時に他DBの選択は保持する |
| Always | 「選択をクリア」ボタンは現在のDBの選択のみクリアする |
| Always | 「実行内容を確認する」は現在選択中のDBの選択モジュールのみ対象 |
| Ask First | 複数DBにまたがった一括実行（今回スコープ外） |
| Never | 実行ログ画面に遷移後に別DBの選択を引き継いで実行する |

---

## 7. テスト方針

- DB を切り替えて別DBでモジュールを選択後、最初のDBに戻したとき選択が残っていること
- 「選択をクリア」が現在のDBのみをクリアすること
- 全DB選択状況サマリーが正しく表示されること
- 確認・実行は選択中DBのモジュールのみ対象になること
