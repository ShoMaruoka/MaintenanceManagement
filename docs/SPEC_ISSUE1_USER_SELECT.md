# Issue #1 実装仕様: ユーザー選択機能

## 1. Objective（目的）

アプリ起動時にユーザーを選択できるようにし、その選択を localStorage に記憶させる。
初回または記憶なし → ユーザー選択画面 → メインアプリ。
記憶あり → 直接トップメニュー（ダッシュボード）。

ユーザーリストは既存の SQLite DB で管理し、実行履歴の `ExecutedBy` は選択したユーザー名を使用する。

---

## 2. 機能詳細

### 2-1. 起動フロー

```
アプリ起動
  ↓
localStorage に 'currentUser' キーが存在するか？
  ├─ YES → ダッシュボードへ（/）
  └─ NO  → ユーザー選択画面へ（/select-user）
              ↓
           API からユーザーリスト取得
              ↓
           ユーザーを選択して「決定」
              ↓
           localStorage.setItem('currentUser', userName)
              ↓
           ダッシュボードへ（/）
```

### 2-2. ユーザー切り替え

- Sidebar 下部のユーザー表示エリアに「ユーザー切り替え」ボタンを追加
- クリック → `localStorage.removeItem('currentUser')` → ユーザー選択画面にリダイレクト

### 2-3. 実行履歴への反映

- `DeployController`・`PrepareController` が `executedBy` を受け取る方法を変更
- **現状**: Windows 認証ユーザー（`User.Identity?.Name`）
- **変更後**: リクエストボディまたはヘッダーで送られる選択ユーザー名を使用

---

## 3. データベース変更（SQLite）

### 新規テーブル: AppUser

```sql
CREATE TABLE IF NOT EXISTS AppUser (
    UserId      INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName    TEXT NOT NULL UNIQUE,   -- ログイン識別子（例: yamada）
    DisplayName TEXT NOT NULL,          -- 表示名（例: 山田 太郎）
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);
```

### 初期シードデータ

`DatabaseService.EnsureCreated()` の中でシードを投入する。
すでに存在する場合はスキップ（`INSERT OR IGNORE`）。

```sql
INSERT OR IGNORE INTO AppUser (UserName, DisplayName) VALUES
  ('user1', 'ユーザー1'),
  ('user2', 'ユーザー2');
```

> **Note**: 初回セットアップ時は `appsettings.json` または DB 直接操作で実際のユーザーを登録する。

---

## 4. バックエンド変更

### 4-1. 新規ファイル

| ファイル | 内容 |
|---------|------|
| `Controllers/UsersController.cs` | GET /api/users, POST /api/users, DELETE /api/users/{userName} |

#### GET /api/users レスポンス

```json
[
  { "userName": "yamada", "displayName": "山田 太郎" },
  { "userName": "tanaka", "displayName": "田中 花子" }
]
```

#### POST /api/users リクエスト

```json
{ "userName": "suzuki", "displayName": "鈴木 次郎" }
```

### 4-2. DatabaseService の変更

- `EnsureCreated()` に `AppUser` テーブル作成 + シード追加
- `GetAllUsers()` メソッド追加
- `AddUser(string userName, string displayName)` メソッド追加
- `DeleteUser(string userName)` メソッド追加

### 4-3. 既存コントローラーの変更

`executedBy` の取得元を変更する。

#### DeployController

```csharp
// Before
var executedBy = User.Identity?.Name ?? "unknown";

// After
var executedBy = request.ExecutedBy;  // リクエストボディから取得
```

#### PrepareController

同様に `ExecutedBy` をリクエストボディから受け取るよう変更。

---

## 5. フロントエンド変更

### 5-1. 新規ファイル

| ファイル | 内容 |
|---------|------|
| `src/context/UserContext.tsx` | React Context でアプリ全体にユーザー情報を提供 |
| `src/pages/UserSelectPage.tsx` | ユーザー選択画面 |
| `src/api/users.ts` | GET /api/users, POST /api/users, DELETE /api/users/:userName |

### 5-2. UserContext

```tsx
interface UserContextValue {
  currentUser: string | null       // UserName (例: 'yamada')
  selectUser: (userName: string) => void
  clearUser: () => void
}
```

- `currentUser` は localStorage の `'currentUser'` キーから初期化
- `selectUser` → localStorage に保存 + state 更新
- `clearUser` → localStorage クリア + state を null に

### 5-3. UserSelectPage

- API から `/api/users` を取得してカード形式で表示
- カードクリック → `selectUser()` → `navigate('/')`
- ローディング・エラー状態のハンドリング

### 5-4. App.tsx の変更

```tsx
// ルーティングに ProtectedRoute ラッパーを追加
// currentUser が null なら /select-user にリダイレクト
```

- `/select-user` ルートを追加
- 既存ルート（`/`, `/deploy`, `/prepare`, `/history`）は `currentUser` がないと `/select-user` へリダイレクト

### 5-5. Sidebar.tsx の変更

- ユーザー表示エリアを `UserContext` から取得した情報に更新
- 「ユーザー切り替え」ボタン追加（`clearUser()` → `/select-user`）

### 5-6. Header.tsx の変更

- `TANAKA\yamada` ハードコードを `UserContext.currentUser` に置き換え

### 5-7. API クライアント変更（executedBy の送信）

`deploy.ts` と `prepare.ts` で、API リクエストボディに `executedBy: currentUser` を含める。

### 5-8. types.ts の変更

```ts
export interface AppUser {
  userName: string
  displayName: string
}
```

---

## 6. 画面設計: ユーザー管理画面（新規）

Sidebar に「管理」セクションを追加し、「ユーザー管理」メニューを設ける。

### 画面構成

```
┌─ ユーザー管理 ───────────────────────────────────────────────────┐
│                                              [+ ユーザーを追加]  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  山  yamada   山田 太郎                        [削除]   │   │
│  │  田  tanaka   田中 花子                        [削除]   │   │
│  │  鈴  suzuki   鈴木 次郎                        [削除]   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### ユーザー追加フォーム（モーダルまたはインライン）

```
  ユーザー名（ID）: [         ]  例: yamada
  表示名:          [         ]  例: 山田 太郎
                               [キャンセル] [追加]
```

- 削除ボタン → 確認ダイアログなしで即削除（社内ツールのため）
- 重複 userName は `400 Bad Request` で弾く（バックエンドで制御）

---

## 7. 画面設計: ユーザー選択画面

```
┌─────────────────────────────────────────────────────────────────┐
│                    Maintenance Manager                          │
│                                                                 │
│              ご利用になるユーザーを選択してください               │
│                                                                 │
│   ┌──────────────┐  ┌──────────────┐  ┌──────────────┐        │
│   │     山        │  │     田        │  │     鈴        │        │
│   │   山田 太郎   │  │   田中 花子   │  │   鈴木 次郎   │        │
│   │   yamada     │  │   tanaka     │  │   suzuki     │        │
│   └──────────────┘  └──────────────┘  └──────────────┘        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- アバター: DisplayName の最初の1文字（例: 山）
- カードホバー → ハイライト
- カードクリック → 即座にダッシュボードへ遷移（確認なし）

---

## 7. 変更ファイル一覧

### バックエンド（追加・変更）

| ファイル | 種別 |
|---------|------|
| `backend/Controllers/UsersController.cs` | 新規 |
| `backend/Services/DatabaseService.cs` | 変更（AppUser テーブル追加、CRUD メソッド追加） |
| `backend/Controllers/DeployController.cs` | 変更（executedBy をリクエストから取得） |
| `backend/Controllers/PrepareController.cs` | 変更（executedBy をリクエストから取得） |
| `backend/Models/UserModels.cs` | 新規（AppUser, AddUserRequest） |

### フロントエンド（追加・変更）

| ファイル | 種別 |
|---------|------|
| `frontend/src/context/UserContext.tsx` | 新規 |
| `frontend/src/pages/UserSelectPage.tsx` | 新規 |
| `frontend/src/pages/UserManagePage.tsx` | 新規（ユーザー管理画面） |
| `frontend/src/api/users.ts` | 新規 |
| `frontend/src/App.tsx` | 変更（ルーティング・ProtectedRoute） |
| `frontend/src/components/Sidebar.tsx` | 変更（UserContext 対応、切り替えボタン、管理メニュー追加） |
| `frontend/src/components/Header.tsx` | 変更（UserContext 対応） |
| `frontend/src/api/deploy.ts` | 変更（executedBy 追加） |
| `frontend/src/api/prepare.ts` | 変更（executedBy 追加） |
| `frontend/src/types.ts` | 変更（AppUser 型追加） |

---

## 8. Boundaries（制約）

### Always（必ず守る）

- ユーザー選択は localStorage のみで管理（セッション跨ぎで記憶）
- 選択画面はシンプルに保つ（パスワード等は不要）
- `executedBy` は必ず選択したユーザー名を使う（Windows 認証ユーザー名は使わない）

### Ask first（要確認）

- 初期シードユーザーの名前・表示名は別途確認する（デフォルトは `user1/ユーザー1` でプレースホルダー）

### Never（やらない）

- パスワード認証・セッション管理は実装しない（社内ツールのため不要）
- ユーザーの「編集」（userName の変更）は実装しない（削除→再追加で対応）

---

## 9. テスト観点

| 観点 | 確認内容 |
|-----|---------|
| 初回起動 | localStorage 空 → ユーザー選択画面が表示される |
| 選択後 | localStorage に userName が保存される |
| 再読み込み | localStorage あり → 直接ダッシュボード |
| 切り替え | Sidebar のボタン押下 → 選択画面に戻る |
| 実行履歴 | デプロイ・本番前準備の `ExecutedBy` が選択ユーザー名になる |
| Header/Sidebar | 選択ユーザー名が正しく表示される |
| ユーザー追加 | 管理画面からユーザーを追加 → 選択画面に反映される |
| ユーザー削除 | 管理画面からユーザーを削除 → 選択画面から消える |
| 重複追加 | 同じ userName を追加するとエラーが表示される |
