# メンテナンス管理 Web アプリ 仕様書

## 1. Objective（目的）

Chatwork + 手動バッチによるデータベースモジュールデプロイ管理を Web UI に置き換える。
STG 適用〜本番前準備の一元管理・実行確認・実行履歴を提供し、手作業ミスと属人化を排除する。

### スコープ

| フェーズ | 対象 | 状態 |
|---------|------|------|
| フェーズ1（本仕様） | メンテナンス前日まで（STG適用・本番前準備） | 開発対象 |
| フェーズ2（将来拡張） | メンテナンス当日（本番適用・緊急適用） | 対象外 |

---

## 2. 対象システム・DB

| システム名 | SourceControl パス | Git Repo パス | DEV DB | PRD DB |
|-----------|-----------------|-------------|--------|--------|
| kaios | D:\Tools\SourceControl | D:\STGENV\KaiosDB_rep | kaios_dev | kaios |
| gos | D:\Tools\SourceControl_Gos | D:\STGENV\GosDB_rep | gos_dev | gos |
| paf | D:\Tools\SourceControl_Paf | D:\STGENV\PafDB_rep | paf_dev | paf |
| duskin | D:\Tools\SourceControl_DuskinRN | D:\STGENV\DuskinRNDB_rep | duskin_dev | duskin |

---

## 3. Core Features（機能一覧）

| ID | 機能名 | 説明 |
|----|--------|------|
| F1 | モジュール一覧表示 | xxx_dev DB をクエリし、種別ごとのツリーで表示 |
| F2 | モジュール選択 | チェックボックスで複数選択・操作区分（新規/更新/削除）を指定 |
| F3 | 実行前確認 | 実行内容の確認ダイアログ（誤実行防止） |
| F4 | STG 適用実行 | 既存 PS1 スクリプト相当の処理をバックエンドで実行 |
| F5 | リアルタイムログ | Server-Sent Events (SSE) で実行ログをリアルタイム表示 |
| F6 | 本番前準備 | FastCopy 相当の処理を Web から実行（前日バッチ代替） |
| F7 | 実行履歴 | 実行者・日時・モジュール・結果の記録と参照 |

---

## 4. 画面構成

### 4-1. ダッシュボード
- 最近の実行履歴サマリー（直近10件）
- 本番前準備の最終実行日時表示

### 4-2. STG 適用画面（メイン）

```
┌─ DB 選択 ──────────┬─ モジュールツリー ──────────────────────────┐
│  ● kaios           │ StoredProcedure/                           │
│  ○ gos             │   ☑ dbo.SK0300アカウントSEL  [操作区分: 更新 ▼] │
│  ○ paf             │   ☑ dbo.SK0410注文SEL        [操作区分: 新規 ▼] │
│  ○ duskin          │   ☐ dbo.SK1418注文キャンセル                │
│                    │ Function/                                  │
│                    │   ☐ dbo.FN_CalcPrice                       │
│                    │ VIEW/                                      │
│                    │ Table/ ※Gitマージのみ（デプロイ不可）        │
│                    │ MariaDB/                                   │
│                    │                                            │
│                    │              [実行内容を確認する]            │
└────────────────────┴────────────────────────────────────────────┘
```

確認ダイアログ → 実行 → リアルタイムログ表示

### 4-3. 本番前準備画面

各 DB につき 2 つのセクションでファイルを表示する。

| セクション | ソースフォルダ | デフォルト選択 |
|-----------|--------------|------------|
| 今回適用する | `deployed/` | 全選択（チェック入り） |
| 保留中 | `deployed_hold/` | 全未選択（チェックなし） |

```
┌─ kaios ─────────────────────────────────────────────────────────┐
│  ▼ 今回適用する (2 件)                                           │
│    ☑ dbo.SK0300アカウントSEL.sql                                 │
│    ☑ dbo.SK0410注文SEL.sql                                       │
│  ▼ 保留中 (1 件)                                                 │
│    ☐ dbo.SK1418注文キャンセル.sql  [前回保留]                     │
└─────────────────────────────────────────────────────────────────┘
```

**実行後のファイル処理:**

| ファイルの状態 | 実行後の処理 |
|-------------|------------|
| チェックあり（`deployed/` から） | FastCopy → `2_Deploy_STG2PRD/` へコピー後、`deployed/` から削除 |
| チェックなし（`deployed/` から） | `deployed_hold/` へ移動（次回保留対象として管理） |
| チェックあり（`deployed_hold/` から） | FastCopy → `2_Deploy_STG2PRD/` へコピー後、`deployed_hold/` から削除 |
| チェックなし（`deployed_hold/` から） | `deployed_hold/` に残留（次回も保留中として表示） |

[本番前準備を実行する] → 確認ダイアログ → 実行 → ログ表示

### 4-4. 実行履歴画面

- 日付・DB・モジュール数・実行者・結果（成功/失敗）の一覧
- 行クリックでログ詳細展開

---

## 5. モジュール取得クエリ

### SQL Server（xxx_dev DB に対して実行）

```sql
-- StoredProcedure
SELECT name, modify_date
FROM sys.procedures
WHERE is_ms_shipped = 0
ORDER BY name;

-- Function
SELECT name, modify_date
FROM sys.objects
WHERE type IN ('FN', 'TF', 'IF') AND is_ms_shipped = 0
ORDER BY name;

-- View
SELECT name, modify_date
FROM sys.views
WHERE is_ms_shipped = 0
ORDER BY name;

-- Table（参照のみ・デプロイ不可）
SELECT name, modify_date
FROM sys.tables
WHERE is_ms_shipped = 0
ORDER BY name;
```

### MariaDB（xxx_dev DB に対して実行）

```sql
-- ストアドプロシージャ
SELECT ROUTINE_NAME, LAST_ALTERED
FROM information_schema.ROUTINES
WHERE ROUTINE_SCHEMA = 'xxx_dev' AND ROUTINE_TYPE = 'PROCEDURE'
ORDER BY ROUTINE_NAME;

-- テーブル
SELECT TABLE_NAME, UPDATE_TIME
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = 'xxx_dev'
ORDER BY TABLE_NAME;
```

---

## 6. 処理フロー詳細

### STG 適用フロー（F4）

既存の `SQLModule_deploy.ps1` の処理を C# で再実装する。

```
1. UpdateModule.txt / DeleteModule.txt を SJIS(CP932) で生成
   → D:\Tools\SourceControl{_XX}\merge\ に書き込み
2. git_Live Updates.bat を Process.Start で実行
3. git_merge.bat を Process.Start で実行
4. SQL ファイルをコピー・変換
   - 通常: D:\STGENV\{DB}_rep\{Type}\{Name}.sql → Deploy_DEV2STG\ForNewCreation\Source\
   - 新規の場合: ALTER → CREATE に置換
   - 削除の場合: DROP {Type} [dbo].[{Name}] の SQL を生成
   - Table / UserDefinedTableType: コピー不要（Git マージのみ）
5. deploy.bat を Process.Start で実行
6. 適用済みファイルを Source\deployed\ に移動
7. 各ステップのログを SSE でストリーミング配信
8. 結果を DB（DeploySession / DeploySessionDetail）に記録
```

### 本番前準備フロー（F6）

既存の `本番前準備バッチ.bat` の FastCopy 処理を C# から実行する。
モジュール選択機能の追加により、ファイルを選択的にコピーできる。

```
[フロントエンド]
1. deployed/ と deployed_hold/ 内のファイル一覧を取得・表示
   - deployed/ → デフォルト全選択
   - deployed_hold/ → デフォルト全未選択
2. ユーザーが適用・除外を選択
3. 確認ダイアログ表示 → 実行

[バックエンド]
各 DB について:
  A. チェック済みファイルを FastCopy でコピー:
     FastCopy.exe /cmd=diff
       from: Deploy_DEV2STG\ForNewCreation\Source\deployed\{選択ファイル}
       to:   UseAtProductionUpdate\2_Deploy_STG2PRD\ForNewCreation\Source\

  B. コピー完了後にファイルを移動:
     - コピーしたファイル → deployed\ から削除
     - 未選択の deployed\ ファイル → deployed_hold\ へ移動

  C. MariaDB ファイルも同様に処理:
     FastCopy.exe /cmd=diff
       from: Deploy_DEV2STG\MariaDB\deployed\{選択ファイル}
       to:   UseAtProductionUpdate\2_Deploy_STG2PRD\MariaDB\
```

**フォルダ構成（追加）:**
```
Deploy_DEV2STG\ForNewCreation\Source\
  ├─ deployed\           # STG適用後のファイル（本番前準備の対象）
  └─ deployed_hold\      # 保留中ファイル（次回の本番前準備まで待機）
```

---

## 7. 技術スタック

| レイヤー | 技術 | 理由 |
|---------|------|------|
| フロントエンド | React 18 + TypeScript + Vite | 指定 |
| UI ライブラリ | Ant Design | Tree コンポーネントがファイルツリー向き |
| バックエンド | ASP.NET Core 8 Web API | 指定（C#）|
| ホスティング | IIS 10 (In-Process) | 指定 |
| 認証 | Windows 認証（Negotiate/NTLM） | 社内システムのため |
| SQL Server 接続 | Microsoft.Data.SqlClient | 標準ライブラリ |
| MariaDB 接続 | MySqlConnector | .NET 向け高性能ドライバー |
| リアルタイム通信 | Server-Sent Events (SSE) | 実行ログのリアルタイム配信 |
| 管理 DB | SQLite（ファイル: MaintenanceManagement.db） | セットアップ不要・バックアップ容易 |

---

## 8. データベース設計（管理用）

**使用 DB: SQLite**（ファイルパス例: `D:\Apps\MaintenanceManagement\data\maintenance.db`）

アプリ自身の実行記録のみを保存する。デプロイ対象の SQL Server（kaios / gos 等）とは完全に別。
NuGet パッケージ `Microsoft.Data.Sqlite`（または EF Core + SQLite）で接続する。

```sql
-- デプロイ実行セッション（1回の実行 = 1レコード）
CREATE TABLE DeploySession (
    SessionId    INTEGER PRIMARY KEY AUTOINCREMENT,
    DbName       TEXT NOT NULL,   -- kaios/gos/paf/duskin
    ExecutedBy   TEXT NOT NULL,   -- Windows認証ユーザー名
    ExecutedAt   TEXT NOT NULL,   -- ISO 8601形式
    Status       TEXT NOT NULL,   -- running/success/failed
    ErrorMessage TEXT
);

-- セッション内のモジュール明細
CREATE TABLE DeploySessionDetail (
    DetailId     INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId    INTEGER NOT NULL REFERENCES DeploySession(SessionId),
    OpType       TEXT    NOT NULL,  -- 新規/更新/削除
    ModuleType   TEXT    NOT NULL,  -- StoredProcedure/Function/View/etc
    ModuleName   TEXT    NOT NULL,
    Result       TEXT    NOT NULL   -- success/failed/skipped
);

-- 本番前準備実行記録
CREATE TABLE ProductionReadyLog (
    LogId        INTEGER PRIMARY KEY AUTOINCREMENT,
    ExecutedBy   TEXT NOT NULL,
    ExecutedAt   TEXT NOT NULL,
    AppliedFiles INTEGER NOT NULL,  -- 適用ファイル数
    HeldFiles    INTEGER NOT NULL,  -- 保留ファイル数
    Result       TEXT NOT NULL,
    LogDetail    TEXT
);
```

> **バックアップ**: `maintenance.db` ファイルをコピーするだけでよい。

---

## 9. プロジェクト構成

```
MaintenanceManagement/
├─ frontend/                    # React アプリ
│   ├─ src/
│   │   ├─ pages/
│   │   │   ├─ Dashboard.tsx
│   │   │   ├─ DeployStg.tsx    # STG適用メイン画面
│   │   │   ├─ PrepareForPrd.tsx # 本番前準備画面
│   │   │   └─ History.tsx
│   │   ├─ components/
│   │   │   ├─ ModuleTree.tsx   # モジュール選択ツリー
│   │   │   ├─ LogViewer.tsx    # リアルタイムログ表示
│   │   │   └─ ConfirmDialog.tsx
│   │   └─ api/                 # バックエンド API クライアント
│   └─ vite.config.ts
│
├─ backend/                     # ASP.NET Core 8 Web API
│   ├─ Controllers/
│   │   ├─ ModulesController.cs  # モジュール一覧取得
│   │   ├─ DeployController.cs   # STG適用実行・SSE
│   │   └─ HistoryController.cs  # 実行履歴
│   ├─ Services/
│   │   ├─ ModuleQueryService.cs # SQL Server/MariaDB クエリ
│   │   ├─ DeployService.cs      # デプロイ処理本体
│   │   └─ FastCopyService.cs    # 本番前準備
│   └─ web.config               # IIS 設定
│
└─ SPEC.md
```

---

## 10. IIS 構成

```
IIS サイト（ポート 80）
├─ / → frontend/dist/ を wwwroot に配置（React 静的ファイル）
└─ /api → ASP.NET Core 8 Web API（In-Process ホスティング）
```

**IIS アプリプールの権限要件:**
- `D:\Tools\SourceControl*` への読み書き実行権限
- `D:\STGENV\` への読み取り権限
- SQL Server への接続権限（Windows 認証 または SQL 認証）
- PowerShell / bat ファイルの実行権限

---

## 11. Boundaries（制約・ルール）

### Always（必ず守る）
- Table・UserDefinedTableType は Git マージのみ。デプロイファイルのコピーは行わない
- ファイル書き込みは SJIS(CP932) で行う（既存スクリプトとの互換性）
- 実行前は必ず確認ダイアログを表示してから実行する
- 複数 DB の実行は順次実行（並列実行しない）

### Ask first（要確認）
- 既存 SQL Server サーバーへの管理 DB 追加
- IIS アプリプール ID の権限変更

### Never（やらない）
- フェーズ1では本番 DB への直接デプロイ
- 確認なしでの自動実行

---

## 12. テスト戦略

| テスト種別 | ツール | 対象 |
|-----------|--------|------|
| バックエンド単体 | xUnit | ファイル操作・SQL生成・エンコーディング処理 |
| フロントエンド単体 | Vitest | コンポーネント・API クライアント |
| 統合テスト | 手動 | IIS 環境での E2E 動作確認 |

---

## 13. フェーズ2（将来の拡張）

- **本番適用**: `UseAtProductionUpdate/3_Deploy_STG2PRD/` の deploy.bat 実行画面
- **緊急適用**: `UseAtProductionUpdate/99_UrgentApplication/` への即時デプロイ機能
- **MariaDB 自動化**: 現在手作業の MariaDB 適用を自動化

---

## 14. 実装進捗（2026-06-22 時点）

### 凡例
| 記号 | 意味 |
|------|------|
| ✅ | 実装完了 |
| 🟡 | モックデータで UI 実装済み（バックエンド未接続） |
| 🔶 | バックエンド実装完了（フロントエンド連携テスト中） |
| ❌ | 未着手 |

---

### フロントエンド（React + TypeScript）

| カテゴリ | ファイル | 状態 | 備考 |
|---------|---------|------|------|
| 設定 | package.json / vite.config.ts / tsconfig.json | ✅ | Vite + React 18 + TypeScript + プロキシ設定 |
| エントリ | index.html / main.tsx | ✅ | Google Fonts 読み込み済み |
| 型定義 | src/types.ts | ✅ | DbName / ModuleType / OpType / Session 型 |
| スタイル | src/index.css | ✅ | デザイントークン・全コンポーネント CSS |
| ルーティング | src/App.tsx | ✅ | BrowserRouter + 4 ページ構成 |
| 共通 | Sidebar.tsx | ✅ | NavLink アクティブ状態・情報カード |
| 共通 | Header.tsx | ✅ | STG 環境バッジ・ユーザー表示 |
| 共通 | StatusBadge.tsx | ✅ | running / success / failed |
| 共通 | ConfirmDialog.tsx | ✅ | チェックボックス確認・Table 「Git のみ」バッジ |
| 共通 | LogViewer.tsx | ✅ | 6 ステップ進行表示・SSE リアルタイム表示対応 |
| ページ | Dashboard.tsx | 🟡 | 統計カード・直近履歴（モックデータ、API 接続予定） |
| ページ | DeployStg.tsx | 🟡 | DB 選択・モジュールツリー・Table 選択対応（モックデータ、API 接続予定） |
| ページ | PrepareForPrd.tsx | 🟡 | 今回適用/保留中の 2 セクション選択 UI（モックデータ、API 接続予定） |
| ページ | History.tsx | 🟡 | フィルター・詳細展開（モックデータ、API 接続予定） |
| API クライアント | src/api/ | ❌ | バックエンド接続用クライアント（フロントエンド最後のタスク） |

**フロントエンド進捗: 11 / 15 タスク完了（94%）** — API クライアント実装で完成

---

### バックエンド（ASP.NET Core 8）

| カテゴリ | ファイル | 状態 | 備考 |
|---------|---------|------|------|
| プロジェクト | MaintenanceManagement.Api.csproj / Program.cs | ✅ | .NET 8 Web API + DI / CORS / Windows 認証設定済み |
| 設定 | appsettings.json | ✅ | DB 接続文字列・パス設定済み |
| IIS 設定 | web.config | 🔶 | In-Process ホスティング設定準備完了 |
| モデル | Models/ (DbConfig / ModuleInfo / DeployModels / PrepareModels) | ✅ | API リクエスト・レスポンス型定義 |
| API | ModulesController.cs | ✅ | xxx_dev DB クエリ → モジュール一覧 JSON |
| API | DeployController.cs | ✅ | STG 適用実行・SSE ログストリーミング |
| API | PrepareController.cs | ✅ | 本番前準備（FastCopy 実行・ファイル移動） |
| API | HistoryController.cs | ✅ | 実行履歴 CRUD |
| サービス | DatabaseService.cs | ✅ | SQLite DB 初期化・テーブル自動作成 |
| サービス | ModuleQueryService.cs | ✅ | SQL Server / MariaDB クエリ実装 |
| サービス | DeployService.cs | ✅ | bat / PS1 実行・SJIS ファイル生成・SSE 配信 |
| サービス | FastCopyService.cs | ✅ | FastCopy.exe 呼び出し・deployed_hold/ 移動 |
| 認証 | Windows 認証（Negotiate） | ✅ | IIS デフォルト認証スキーム設定済み |

**バックエンド進捗: 11 / 12 タスク完了（92%）** — web.config のみ IIS デプロイ時調整

---

### データベース（管理用 SQLite）

| テーブル | 状態 | 備考 |
|---------|------|------|
| DeploySession | ✅ | セッション単位の実行記録（DatabaseService で自動作成） |
| DeploySessionDetail | ✅ | モジュール明細（DatabaseService で自動作成） |
| ProductionReadyLog | ✅ | 本番前準備の実行記録（DatabaseService で自動作成） |

**DB 進捗: 3 / 3 テーブル（100%）** — バックエンド起動時に自動作成完了

---

### 全体サマリー

```
フロントエンド  ███████████████░░░░░░  94%  (14/15)
バックエンド    ███████████████████░░  92%  (11/12)
データベース    ████████████████████░ 100%  ( 3/3)
─────────────────────────────────────────────────
全体           ██████████████████░░░  95%  (28/30)
```

### 次のステップ（推奨順）

1. **フロントエンド API クライアント実装** — モックデータを実 API に接続（残り 1 項目）
2. **ローカル統合テスト** — Vite dev サーバー + バックエンド IIS で E2E 動作確認
3. **IIS デプロイ設定** — `web.config` 調整・アプリプール権限設定・実環境デプロイ
4. **Windows 認証テスト** — ユーザー名の実取得・権限検証
5. **フルシステムテスト** — STG 環境での実際のデプロイ実行・ログ記録・本番前準備 FastCopy 動作確認
