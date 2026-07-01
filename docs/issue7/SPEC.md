# SPEC: Issue #7 — STG適用の複数DB横断実行

## 1. Objective

STG適用画面において、複数DBにまたがって選択されたモジュールをまとめて実行できるようにする。

**対象ユーザー:** メンテナンス担当者

**解決する問題:**
- Issue #5 でDB選択を保持する機能が追加されたが、実行は「現在選択中のDB」の1件のみが対象
- 複数DBのモジュールを選択しても、DBを切り替えながら1件ずつ実行する必要があり手間がかかる

---

## 2. 現状の問題

`DeployStg.tsx` の `confirmModules` は現在選択中のDBのみを対象にしている:

```tsx
// 現在: selectedDb のモジュールのみ
const confirmModules = useMemo((): SelectedModule[] =>
  Array.from(selectedModules.entries()).map(([name, opType]) => { ... }),
  [selectedModules, modulesByDb, selectedDb],
)
```

また `LogViewer` は単一DBの `dbName: DbName` と `modules: SelectedModule[]` を受け取る設計:

```tsx
// 現在: 単一DB・単一実行
<LogViewer dbName={selectedDb} modules={confirmModules} onDone={handleDone} />
```

バックエンドの `/api/deploy/stream` は `DeployRequest`（1 DBのみ）を受け取る設計になっているが、
複数DBの順次実行はフロントエンド側で API を複数回呼び出す方式で対応する（バックエンド変更不要）。

---

## 3. To Be（受け入れ条件）

### 3-1. 確認ダイアログで全DB・全選択モジュールを表示する

- 「実行内容を確認する」ボタン押下時、**全DBの選択モジュール**を一覧表示する
- 表示はDBごとにグループ化し、各DBのモジュールをまとめて確認できる
- 選択モジュールが0件のDBは表示しない

```
┌─ 実行内容の確認 ─────────────────────────────────┐
│ 対象 DB: kaios, gos  実行者: ...                   │
│ 適用モジュール 合計 5件（2 DB）                      │
│                                                    │
│  ▼ kaios（3件）                                   │
│    [更新] dbo.SK0300アカウントSEL  StoredProcedure  │
│    [新規] dbo.SK0410注文SEL        StoredProcedure  │
│    [削除] dbo.FN_CalcPrice         Function         │
│                                                    │
│  ▼ gos（2件）                                     │
│    [更新] dbo.GS0100受注SEL        StoredProcedure  │
│    [更新] dbo.FN_GetStatus         Function         │
│                                                    │
│  ⚠ git Live Updates → merge → SQL変換 → deploy.bat │
│    の順で実行されます。複数DBは順次実行されます。      │
│                                                    │
│  ☐ 適用内容を確認しました                          │
│                     [キャンセル] [適用を実行する]    │
└────────────────────────────────────────────────────┘
```

### 3-2. 実行は全DBのモジュールを順次処理する

- 実行順序: `dbConfigs` のリスト順（kaios → gos → paf → duskin）
- DBごとに `/api/deploy/stream` を1回ずつ順次呼び出す（並列実行しない）
- 1件のDBが完了してから次のDBの実行に移る
- いずれかのDBでエラーが発生しても、後続DBの実行を継続する（エラーをログに記録し次へ）
  - ※ 中断ボタンで全体を停止できる

### 3-3. ログビューアで複数DBの実行進捗を表示する

- 現在の6ステップ（生成・git更新・merge・SQL変換・deploy・記録）を **DBごとに繰り返し表示**する
- 現在実行中のDB名をログ上部に表示する
- 全DB完了後に「完了」状態に移行する

### 3-4. 実行完了後の選択クリア

- 実行後に「適用画面に戻る」を押した際、**実行したすべてのDB**の選択をクリアする（現在は `selectedDb` のみ）

---

## 4. 技術仕様

### 4-1. 型定義の追加

`types.ts` に追加:

```ts
// 複数DBの選択モジュールまとめ
export type MultiDbModules = { db: DbName; modules: SelectedModule[] }[]
```

### 4-2. DeployStg.tsx の変更

**`confirmModules` の変更:**
```ts
// Before: 現在のDBのみ
const confirmModules = useMemo((): SelectedModule[] => ...)

// After: 全DBの選択をまとめた配列
const allConfirmModules = useMemo((): MultiDbModules =>
  dbConfigs
    .filter(db => (selectedModulesByDb.get(db.name)?.size ?? 0) > 0)
    .map(db => {
      const selMap = selectedModulesByDb.get(db.name) ?? new Map()
      const allModules = Object.values(modulesByDb[db.name] ?? {}).flat()
      return {
        db: db.name,
        modules: Array.from(selMap.entries()).map(([name, opType]) => {
          const found = allModules.find(m => m.name === name)
          return { name, opType, type: found?.type ?? 'StoredProcedure' }
        })
      }
    }),
  [dbConfigs, selectedModulesByDb, modulesByDb],
)
```

**`totalSelected` の変更（フッターの件数表示）:**
```ts
// Before: 現在DBのみ
const totalSelected = selectedModules.size

// After: 全DBの合計
const totalSelected = useMemo(
  () => Array.from(selectedModulesByDb.values()).reduce((sum, m) => sum + m.size, 0),
  [selectedModulesByDb],
)
```

**「適用画面に戻る」の選択クリア:**
```tsx
// Before: selectedDb のみクリア
onClick={() => { updateDbSelection(selectedDb, () => new Map()); setPageState('select') }}

// After: 実行済みの全DBをクリア
onClick={() => {
  allConfirmModules.forEach(({ db }) => updateDbSelection(db, () => new Map()))
  setPageState('select')
}}
```

**フッターの「実行内容を確認する」ボタン:**
- `disabled` 条件を `totalSelected === 0` に変更（現行と同じだが全DB合計に）

### 4-3. ConfirmDialog.tsx の変更

Props を変更して複数DBに対応:

```ts
// Before
interface Props {
  dbName: DbName
  modules: SelectedModule[]
  onConfirm: () => void
  onCancel: () => void
}

// After
interface Props {
  allModules: MultiDbModules   // 全DBの選択モジュール
  onConfirm: () => void
  onCancel: () => void
}
```

表示内容の変更:
- 「対象 DB」欄: 実行対象DBをカンマ区切りで表示（例: `kaios, gos`）
- 「適用モジュール」欄: 合計件数＋DB数（例: `5件（2 DB）`）
- モジュール一覧: DBごとにセクション分け（`▼ kaios（3件）` ヘッダー付き）
- 警告文: 「複数DBは順次実行されます」を追記

### 4-4. LogViewer.tsx の変更

複数DBを順次実行するよう変更:

```ts
// Before
interface Props {
  dbName: DbName
  modules: SelectedModule[]
  onDone: () => void
}

// After
interface Props {
  allModules: MultiDbModules   // 全DBの選択モジュール（実行順）
  onDone: () => void
}
```

実行ロジックの変更:
- 各DBに対して `startDeploy()` を順次呼び出す（`await` で直列化）
- 各DB開始時にステップをリセットし、DBヘッダーをログに追加表示
- ヘッダー部分に「現在実行中のDB名」を動的表示

```ts
// 概念コード（実装時はSSEコールバックで処理）
for (const { db, modules } of allModules) {
  // DBヘッダーログを追加
  setLines(prev => [...prev, { level: 'STEP', message: `=== ${db} 適用開始 ===`, ... }])
  // ステップをリセット
  setStepStates(new Map(STEPS.map(s => [s.key, 'pending'])))
  // 実行（完了まで待機）
  await new Promise<void>(resolve => {
    startDeploy(db, modules, user, handleLog, resolve, undefined, signal)
  })
}
```

---

## 5. 実行順序の設計

### 順序の根拠

DB実行順序は `dbConfigs`（APIから取得したリスト順）に従う: **kaios → gos → paf → duskin**

この順序を採用する理由:
- UI のDB選択パネルの表示順と一致し、ユーザーが直感的に把握しやすい
- 既存の設定ファイル（`appsettings.json` の `DbConfigs` 配列順）で制御可能

### エラー時の挙動

| 状況 | 動作 |
|------|------|
| あるDBのデプロイ中にエラー | ERRORログを出力し、次のDBの実行に進む |
| 中断ボタン押下 | AbortController で現在実行中のDBを停止し、後続DBも実行しない |
| ネットワーク切断 | 現在のDBの処理が失敗扱いとなり、次のDBに進む |

---

## 6. 変更ファイル一覧

| ファイル | 変更内容 |
|---|---|
| `frontend/src/types.ts` | `MultiDbModules` 型を追加 |
| `frontend/src/pages/DeployStg.tsx` | `allConfirmModules`・`totalSelected`・クリア処理の変更 |
| `frontend/src/components/ConfirmDialog.tsx` | Props 変更・複数DB表示対応 |
| `frontend/src/components/LogViewer.tsx` | Props 変更・複数DB順次実行対応 |

バックエンドの変更は不要（`/api/deploy/stream` を複数回呼び出す方式）。

---

## 7. 境界条件

| 区分 | 内容 |
|---|---|
| Always | 実行順序は `dbConfigs` リスト順（kaios → gos → paf → duskin）を維持する |
| Always | 複数DBは並列実行しない。1DB完了後に次のDBを実行する |
| Always | いずれかのDBでエラーが出ても後続DBの実行は継続する |
| Always | 実行完了後の「戻る」で実行済み全DBの選択をクリアする |
| Never | バックエンドに複数DB一括実行エンドポイントを追加しない（フロントの順次呼び出しで対応） |
| Never | 実行中に別DBの選択変更を受け付けない（実行中はUIをロック） |

---

## 8. テスト方針

- 複数DBにモジュールを選択した状態で「実行内容を確認する」を押し、全DBのモジュールが確認ダイアログに表示されること
- 確認後の実行で、DBが `dbConfigs` 順に順次処理されること（ログで確認）
- 1件目のDBでエラーが出ても2件目のDBが実行されること
- 中断ボタンで後続DBが実行されないこと
- 「戻る」押下後、実行した全DBの選択がクリアされていること
- 選択0件のDBが確認ダイアログ・実行対象から除外されること
