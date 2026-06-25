# ローカルテスト手順書

## 前提条件

| 項目 | 要件 |
|------|------|
| .NET SDK | 8.0 以上 |
| Node.js | 18 以上 |
| SQL Server | ローカルインスタンス（Windows 認証）または不要（DryRun モード） |
| MariaDB | ローカルインスタンスまたは不要（DryRun モード） |
| SQLite | 自動作成（インストール不要） |

---

## 1. 接続設定

設定ファイル: `backend/appsettings.Development.json`

### 1-1. SQL Server 接続設定

```json
{
  "DbConfigs": [
    {
      "Name": "kaios",
      "DevDb": "kaios_dev",
      "PrdDb": "kaios",
      "SqlServerConnectionString": "Server=.;Database=kaios_dev;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;",
      ...
    }
  ]
}
```

**`SqlServerConnectionString` の書き方パターン**

| 接続先 | 接続文字列 |
|--------|-----------|
| ローカル既定インスタンス（Windows 認証） | `Server=.;Database=<DB名>;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;` |
| 名前付きインスタンス（例: SQLEXPRESS） | `Server=.\SQLEXPRESS;Database=<DB名>;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;` |
| 別ホスト（Windows 認証） | `Server=192.168.1.10;Database=<DB名>;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;` |
| SQL Server 認証 | `Server=.;Database=<DB名>;User Id=sa;Password=xxxxx;TrustServerCertificate=true;Connect Timeout=3;` |
| 未使用（空欄） | `""` （空文字列にすると接続試行しない） |

> **`Connect Timeout=3` を必ず付けること。** SQL Server が起動していない場合、デフォルト 30 秒待つため UI がフリーズする。

### 1-2. MariaDB 接続設定

```json
{
  "DbConfigs": [
    {
      "Name": "kaios",
      "MariaDbConnectionString": "Server=localhost;Port=3306;Database=kaios_dev;User Id=root;Password=secret;"
    }
  ]
}
```

**`MariaDbConnectionString` の書き方パターン**

| 接続先 | 接続文字列 |
|--------|-----------|
| ローカル MariaDB | `Server=localhost;Port=3306;Database=<DB名>;User Id=<ユーザー>;Password=<パスワード>;` |
| 別ホスト | `Server=192.168.1.20;Port=3306;Database=<DB名>;User Id=<ユーザー>;Password=<パスワード>;` |
| 未使用 | `""` （空文字列にすると MariaDB タブが非表示になる） |

### 1-3. DryRun モード（実ファイル・バッチを実行しない）

```json
{
  "DryRun": true
}
```

- `true`: ファイル生成・bat 実行・FastCopy をシミュレートのみ（ログに `[DRY-RUN]` 表示）
- `false`: 実際にファイル書き込み・バッチ実行を行う（本番相当）

**SQL Server・MariaDB への接続は DryRun に関わらず行われる**（モジュール一覧取得のため）。

### 1-4. パス設定

各 DB の以下のパスが存在しない場合、対応する機能がスキップされる（エラーにはならない）。

```json
{
  "SourceControlPath": "D:\\Tools\\SourceControl",
  "GitRepoPath": "D:\\STGENV\\KaiosDB_rep",
  "DeployDev2StgPath": "D:\\Tools\\SourceControl\\Deploy_DEV2STG",
  "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate\\2_Deploy_STG2PRD"
}
```

| パス | 用途 | パスが存在しない場合 |
|------|------|---------------------|
| `SourceControlPath` | `UpdateModule.txt` 生成先・bat ファイル格納先 | DryRun なら問題なし。本番モードはエラー |
| `GitRepoPath` | git 操作対象リポジトリ | bat 実行時エラー |
| `DeployDev2StgPath/ForNewCreation/Source/deployed/` | 本番前準備のコピー元 | ファイル 0 件として表示される |
| `DeployDev2StgPath/ForNewCreation/Source/deployed_hold/` | 保留ファイル一覧 | ファイル 0 件として表示される |

### 1-5. 設定ファイル全体サンプル（DryRun + ローカル SQL Server）

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "DatabasePath": "D:\\Git\\MaintenanceManagement\\data\\maintenance.db",
  "DryRun": true,
  "AllowedOrigins": [ "http://localhost:5173" ],
  "DbConfigs": [
    {
      "Name": "kaios",
      "DevDb": "kaios_dev",
      "PrdDb": "kaios",
      "SqlServerConnectionString": "Server=.;Database=kaios_dev;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;",
      "MariaDbConnectionString": "",
      "SourceControlPath": "D:\\Tools\\SourceControl",
      "GitRepoPath": "D:\\STGENV\\KaiosDB_rep",
      "DeployDev2StgPath": "D:\\Tools\\SourceControl\\Deploy_DEV2STG",
      "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate\\2_Deploy_STG2PRD"
    },
    {
      "Name": "gos",
      "DevDb": "gos_dev",
      "PrdDb": "gos",
      "SqlServerConnectionString": "Server=.;Database=gos_dev;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;",
      "MariaDbConnectionString": "",
      "SourceControlPath": "D:\\Tools\\SourceControl_Gos",
      "GitRepoPath": "D:\\STGENV\\GosDB_rep",
      "DeployDev2StgPath": "D:\\Tools\\SourceControl_Gos\\Deploy_DEV2STG",
      "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate_Gos\\2_Deploy_STG2PRD"
    },
    {
      "Name": "paf",
      "DevDb": "paf_dev",
      "PrdDb": "paf",
      "SqlServerConnectionString": "Server=.;Database=paf_dev;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;",
      "MariaDbConnectionString": "",
      "SourceControlPath": "D:\\Tools\\SourceControl_Paf",
      "GitRepoPath": "D:\\STGENV\\PafDB_rep",
      "DeployDev2StgPath": "D:\\Tools\\SourceControl_Paf\\Deploy_DEV2STG",
      "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate_Paf\\2_Deploy_STG2PRD"
    },
    {
      "Name": "duskin",
      "DevDb": "duskin_dev",
      "PrdDb": "duskin",
      "SqlServerConnectionString": "Server=.;Database=duskin_dev;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3;",
      "MariaDbConnectionString": "",
      "SourceControlPath": "D:\\Tools\\SourceControl_DuskinRN",
      "GitRepoPath": "D:\\STGENV\\DuskinRNDB_rep",
      "DeployDev2StgPath": "D:\\Tools\\SourceControl_DuskinRN\\Deploy_DEV2STG",
      "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate_DuskinRN\\2_Deploy_STG2PRD"
    }
  ]
}
```

---

## 2. サーバー起動手順

### ターミナル A（バックエンド）

```powershell
cd D:\Git\MaintenanceManagement\backend
dotnet run --launch-profile http
```

起動確認: `http://localhost:5254/api/modules` にブラウザでアクセスして JSON が返ればOK。

### ターミナル B（フロントエンド）

```powershell
cd D:\Git\MaintenanceManagement\frontend
npm run dev
```

起動確認: `http://localhost:5173` をブラウザで開く。

> **注意**: フロントエンドはバックエンドへの API 呼び出しを Vite のプロキシ経由（`/api` → `http://localhost:5254/api`）で行っている。両方のサーバーが起動していることを確認すること。

---

## 3. 画面別テスト手順

### 3-1. ダッシュボード

**アクセス**: `http://localhost:5173`（トップページ）

| 手順 | 確認ポイント |
|------|------------|
| 1. ページを開く | 「最近の実行履歴」セクションが表示される |
| 2. 初回（履歴なし） | 「実行履歴はありません」などの空状態が表示される |
| 3. STG 適用を 1 回実行後に戻る | 実行したセッションが履歴として表示される |
| 4. 各行に日時・DB名・モジュール数・ステータスが表示されることを確認 | |

---

### 3-2. STG 適用画面（メインの機能）

**アクセス**: `http://localhost:5173` → 左メニュー「STG 適用」

#### パターン A: SQL Server に接続できる環境

| 手順 | 操作 | 確認ポイント |
|------|------|-------------|
| 1 | DB 選択（例: kaios） | モジュール一覧がロードされる |
| 2 | ロード中 | 「読み込み中...」などのローディング表示が出る |
| 3 | ロード完了 | SP・Function・VIEW・Table の各セクションに DB のモジュール一覧が表示される |
| 4 | Table/UserDefinedTableType | 「Git マージのみ」バッジが表示される（デプロイ実行ボタンが押せない） |
| 5 | SP を 1 件チェック、操作区分「更新」を選択 | |
| 6 | 「実行内容を確認する」ボタンを押す | 確認ダイアログが表示される |
| 7 | ダイアログで「実行」を押す | ログ画面に切り替わる |
| 8 | ログがリアルタイムに流れる | INFO/STEP/DETAIL/OK の各レベルが色付きで表示される |
| 9 | ステップバー | 生成→git更新→merge→SQL変換→deploy→記録 が順番に完了状態になる |
| 10 | 完了 | 「✅ STG 適用が完了しました」が表示される |

#### パターン B: SQL Server が未起動（DryRun テスト）

| 手順 | 操作 | 確認ポイント |
|------|------|-------------|
| 1 | DB 選択 | 3 秒以内にタイムアウト → モジュール 0 件で表示（エラーではなく空一覧） |
| 2 | 「実行内容を確認する」ボタン | 何も選択していないので押せない（または警告が出る） |

> SQL Server が起動していなくても画面がクラッシュしないことを確認する。

---

### 3-3. 本番前準備画面

**アクセス**: `http://localhost:5173` → 左メニュー「本番前準備」

| 手順 | 確認ポイント |
|------|------------|
| 1. ページを開く | 4 つの DB セクション（kaios, gos, paf, duskin）が表示される |
| 2. deployed/ フォルダにファイルなし | 各セクションの「今回適用する」が「(0 件)」表示 |
| 3. deployed/ にテスト用 .sql ファイルを配置した場合 | バックエンドを再起動後、ファイルが一覧に表示され、デフォルトで全選択状態になる |
| 4. deployed_hold/ にファイルを配置した場合 | 「保留中」セクションに表示され、デフォルト未選択状態 |
| 5. 「実行」ボタンを押す | SSE ログが流れ、DryRun モードなら `[DRY-RUN]` 付きでファイルコピーがシミュレートされる |

**テスト用ファイル配置例（kaios の場合）**

```
D:\Tools\SourceControl\Deploy_DEV2STG\ForNewCreation\Source\deployed\
  └── dbo.TestSP.sql
  └── dbo.TestFunc.sql

D:\Tools\SourceControl\Deploy_DEV2STG\ForNewCreation\Source\deployed_hold\
  └── dbo.HoldSP.sql
```

---

### 3-4. 履歴画面

**アクセス**: `http://localhost:5173` → 左メニュー「履歴」

| 手順 | 確認ポイント |
|------|------------|
| 1. ページを開く | 実行済みセッションの一覧が表示される |
| 2. 各行に日時・DB名・実行者・ステータスが表示される | |
| 3. 行をクリック（展開） | 対象モジュール・操作区分の詳細が展開表示される |
| 4. DryRun での実行者 | 「unknown」と表示される（Windows 認証が有効でないため） |

---

## 4. トラブルシューティング

### バックエンドが起動しない「port already in use」

```powershell
# ポート 5254 を使用しているプロセスを確認・終了
netstat -ano | findstr :5254
taskkill /PID <PID番号> /F
```

### モジュール一覧が空になる

1. SQL Server の接続文字列を確認（`Server=.` → インスタンス名が合っているか）
2. SQL Server Management Studio で手動接続できるか確認
3. Windows 認証の場合、バックエンドを実行しているユーザーに DB アクセス権があるか確認
4. `Connect Timeout=3` が設定されているか確認（長いと UI がフリーズ）

### MariaDB の手続きが表示されない

1. `MariaDbConnectionString` が空でないか確認
2. MariaDB のサービスが起動しているか: `services.msc` → MySQL / MariaDB
3. バックエンドのログ（ターミナル A）に `MariaDB query failed` が出ていないか確認

### フロントエンドが API に繋がらない（CORS エラー）

`appsettings.Development.json` の `AllowedOrigins` に `http://localhost:5173` が含まれているか確認:

```json
"AllowedOrigins": [ "http://localhost:5173" ]
```

### SQLite のデータベースが見つからない

`appsettings.Development.json` の `DatabasePath` のフォルダが存在するか確認:

```powershell
# data フォルダを作成
mkdir D:\Git\MaintenanceManagement\data
```

バックエンド起動時にテーブルが自動作成される。

---

## 5. DryRun → 本番モードへの切り替え

実際のファイル書き込み・バッチ実行をテストする場合は `DryRun` を `false` に変更する。

```json
{
  "DryRun": false
}
```

**本番モードで必要なもの**

| 必要なもの | 用途 |
|-----------|------|
| `SourceControlPath\git_Live Updates.bat` | Step 2: git pull |
| `SourceControlPath\git_merge.bat` | Step 3: git merge |
| `DeployDev2StgPath\deploy.bat` | Step 5: SQL Server への適用 |
| `FastCopyPath`（デフォルト: `C:\Program Files\FastCopy\FastCopy.exe`） | 本番前準備のファイルコピー |

FastCopy のパスを変更する場合は `appsettings.Development.json` に追記:

```json
{
  "FastCopyPath": "C:\\Program Files\\FastCopy\\FastCopy.exe"
}
```
