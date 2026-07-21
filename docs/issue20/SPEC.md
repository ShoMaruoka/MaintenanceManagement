# Spec: 画像情報準備機能追加 (issue #20)

## 対象Issue

GitHub Issue [#20 画像情報準備機能追加](https://github.com/ShoMaruoka/MaintenanceManagement/issues/20)

## Objective

本番適用用の静的ファイル（画像・ニュース・PDF）を、現状の手作業フォルダ操作から Web UI で管理できるようにする。

1. **画像情報準備**（新規メニュー）: STG 側 `Deploy_DEV2STG\Files` 配下へファイルをアップロードし、必要に応じてサブフォルダを作成する
2. **本番前準備の拡張**: アップロード済みファイルを確認し、本番用フォルダ（`batrunApp\STGDEPLOY\...`）へ移動する

- 対象ユーザー: メンテナンス前日までに静的ファイルを準備する開発者・運用担当者
- 成功条件:
  - STG適用と本番前準備の間に「画像情報準備」メニューがあり、DB ごとに `Images` / `news` / `pdf` へアップロードできる
  - アップロード時に最大 2 階層までのサブフォルダを作成できる
  - 本番前準備で当該ファイルを確認し、本番フォルダへ移動できる

## 背景・現状

| 項目 | 現状 | 本 issue の方針 |
|------|------|----------------|
| 静的ファイル保管 | `Deploy_DEV2STG\Files\{Images\|news\|pdf}` を手作業で操作 | Web から一覧・アップロード・サブフォルダ作成 |
| 本番への受け渡し | 手作業で `batrunApp\STGDEPLOY\...` へコピー/移動 | 本番前準備画面から確認・移動 |
| SQL の本番前準備 | `UseAtProductionUpdate\...\2_Deploy_STG2PRD`（既存 `Deploy2PrdPath`） | **変更なし**（静的ファイルの本番先とは別系統） |
| ファイルアップロード API | 未実装 | multipart で新規実装 |

## Tech Stack

- バックエンド: ASP.NET Core 8（`backend/Controllers`, `Services`, `Models`）
- フロントエンド: React 18 + TypeScript + Vite
- スタイリング: 既存どおり `index.css` + クラス名（新規プレフィックス例: `imgprep-*`）
- 自動テスト基盤: 未導入（`dotnet build` / `npm run build` + 手動確認）

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## Project Structure（想定）

```
backend/Models/DbConfig.cs                 → Files パス派生・本番先パス追加
backend/Models/ImagePrepareModels.cs       → 一覧・アップロード用 DTO（新規）
backend/Controllers/ImagePrepareController.cs → 一覧・アップロード API（新規）
backend/Services/ImagePrepareService.cs    → フォルダ列挙・保存・パス検証（新規）
backend/Controllers/PrepareController.cs   → 画像ファイル一覧をレスポンスに含める
backend/Services/FastCopyService.cs        → 画像の本番フォルダへの移動を追加
backend/appsettings_sample.json            → FilesDeploy2PrdPath 等を追記

frontend/src/App.tsx                       → ルート・ページタイトル
frontend/src/components/Sidebar.tsx        → メニュー挿入（/deploy と /prepare の間）
frontend/src/pages/ImagePrepare.tsx        → 画像情報準備画面（新規）
frontend/src/api/imagePrepare.ts           → API クライアント（新規）
frontend/src/pages/PrepareForPrd.tsx       → 画像セクションの確認・選択 UI
frontend/src/api/prepare.ts                → 型・リクエスト拡張
frontend/src/types.ts                      → 必要なら型追加
frontend/src/index.css                     → スタイル追加
```

## パス設計

### アップロード先（STG 側保管）— 確定

既存 `DeployDev2StgPath` から導出する（新規設定キーは不要）。回答により各 DB の正式パスは以下。

| DB | FilesPath（STG 側保管） |
|----|-------------------------|
| kaios | `D:\Tools\SourceControl\Deploy_DEV2STG\Files` |
| gos | `D:\Tools\SourceControl_Gos\Deploy_DEV2STG\Files` |
| paf | `D:\Tools\SourceControl_Paf\Deploy_DEV2STG\Files` |
| duskin | `D:\Tools\SourceControl_DuskinRN\Deploy_DEV2STG\Files` |

```
{DeployDev2StgPath}\Files\
  ├─ Images\          # ルートカテゴリ（固定・作成可）
  ├─ news\
  └─ pdf\
       └─ {sub1}\     # 任意サブフォルダ（最大 2 階層）
            └─ {sub2}\
                 └─ file.pdf
```

例（kaios）:

```
D:\Tools\SourceControl\Deploy_DEV2STG\Files\Images\flash\img\banner.png
```

```csharp
public string FilesPath => Path.Combine(DeployDev2StgPath, "Files");
```

### 本番移動先 — 確定

SQL 用の既存 `Deploy2PrdPath`（`UseAtProductionUpdate\...`）とは**別系統**。DB ごとに `FilesDeploy2PrdPath` を明示設定する。

| DB | FilesDeploy2PrdPath（本番移動先） |
|----|----------------------------------|
| kaios | `D:\Tools\batrunApp\STGDEPLOY\kaios_SQLServer\2_Deploy_STG2PRD\Files` |
| gos | `D:\Tools\batrunApp\STGDEPLOY\gos_SQLServer\2_Deploy_STG2PRD\Files` |
| paf | `D:\Tools\batrunApp\STGDEPLOY\paf_SQLServer\2_Deploy_STG2PRD\Files` |
| duskin | `D:\Tools\batrunApp\STGDEPLOY\duskinrn_SQLServer\2_Deploy_STG2PRD\Files` |

※ kaios は Issue コメントでは `...\2_Deploy_STG2PRD`（末尾 `\Files` なし）だったが、gos/paf/duskin と揃え末尾 `\Files` とする。異なる場合は訂正すること。

```csharp
public string FilesDeploy2PrdPath { get; set; } = "";
```

移動時は **STG 側と同じ構造**（相対パス維持）:

```
from: {FilesPath}\Images\flash\img\banner.png
to:   {FilesDeploy2PrdPath}\Images\flash\img\banner.png
```

例（kaios）:

```
D:\Tools\batrunApp\STGDEPLOY\kaios_SQLServer\2_Deploy_STG2PRD\Files\Images\flash\img\banner.png
```

## 機能詳細

### F1. メニュー・画面シェル

- サイドバー: `/deploy`（STG 適用）と `/prepare`（本番前準備）の間に「画像情報準備」を追加
- ルート: `/images`（確定）
- DB 選択: 既存と同様 kaios / gos / paf / duskin

### F2. フォルダ・ファイル一覧

- 選択中 DB の `Files` 配下をカテゴリ（Images / news / pdf）単位でツリーまたはリスト表示
- 存在しないカテゴリルートは一覧取得時に空として扱い、アップロード時に必要なら作成する
- パス検証: `Files` 外へのトラバーサル（`..` 等）を拒否

### F3. アップロード

- カテゴリ（Images / news / pdf）を選択
- 任意でサブフォルダパスを指定（最大 2 階層。例: `flash/img`）
- 許可拡張子: 画像系 + pdf + 一般的な静的ファイル（実装時にホワイトリスト定義）
- 最大サイズ: 約 50MB（IIS/Kestrel の multipart 上限に合わせる）
- 複数ファイル選択可（実装初期は単一でも可。PLAN で確定）
- multipart/form-data で POST
- 同名ファイルが既にある場合: API は `overwrite=false` で 409。UI で確認後に `overwrite=true` で上書き

### F4. サブフォルダ作成

- アップロード先指定としてサブフォルダを作成できる（単独「フォルダ作成」ボタンでも可）
- 階層はルートカテゴリ直下から最大 2（例: `Images\flash\img`）
- フォルダ名の禁止文字・空セグメントを拒否

### F5. 本番前準備での確認・移動

- 既存の SQL（deployed / hold）一覧に加え、各 DB の `Files` 配下ファイルをセクション表示
- デフォルト選択: **全選択**
- 実行時: チェック済みファイルを `FilesDeploy2PrdPath` へ **移動**（コピー後に STG 側削除。相対パス＝同じ構造を維持）
- 移動後、STG 側に空になった**サブフォルダのみ**削除する（`Images` / `news` / `pdf` のカテゴリルートと `Files` 自体は残す）
- 未チェック: STG 側 `Files` に残留
- 確認ダイアログに画像ファイル件数を含める
- 実行ログ（SSE）に画像移動の成否を出す
- 履歴: `ProductionReadyLog` の件数は SQL+画像の合計とし、ログ詳細に画像処理を明記する

## API 案

### 画像情報準備

| Method | Path | 説明 |
|--------|------|------|
| GET | `/api/image-prepare/{db}/tree` | `Files` 配下ツリー（カテゴリ・サブフォルダ・ファイル） |
| POST | `/api/image-prepare/{db}/upload` | multipart: category, relativeSubPath, files[], overwrite? |
| POST | `/api/image-prepare/{db}/folders` | body: category, relativeSubPath（フォルダ作成のみ） |

### 本番前準備（拡張）

| Method | Path | 変更 |
|--------|------|------|
| GET | `/api/prepare/files` | 各 DB に `imageFiles[]`（相対パス）を追加 |
| POST | `/api/prepare/stream` | selection に画像相対パスを含め、移動処理を実行 |

## Out of Scope

- 画像のプレビュー表示（サムネイル）
- ファイル削除・リネーム専用 UI（必要なら別 issue）
- 本番当日の適用（フェーズ2）
- SQL 用 `Deploy2PrdPath` の変更
- ドラッグ&ドロップ以外の高度なアップローダ UI
- 実行履歴画面への画像専用タブ追加（本番前準備ログ詳細での確認で足りる想定）

## Success Criteria

1. サイドバーに「画像情報準備」が STG適用と本番前準備の間に表示される
2. DB を切り替え、`Images` / `news` / `pdf` の内容を一覧できる
3. ファイルをアップロードでき、指定サブフォルダ（最大 2 階層）に保存される
4. `Files` 外パスや 3 階層以上のサブフォルダ指定は拒否される
5. 本番前準備で画像ファイルが一覧・選択でき、確認ダイアログに含まれる
6. 実行後、選択画像が `FilesDeploy2PrdPath` 側に相対パス維持で存在し、STG 側 `Files` から消える
7. 既存の SQL 本番前準備フロー（deployed / hold / MariaDB）が壊れていない
8. `dotnet build` / `npm run build` が通る

## 決定事項（回答反映済み）

| # | 項目 | 決定 |
|---|------|------|
| 1 | STG 側 `Files` パス | 上記「アップロード先」表（`DeployDev2StgPath\Files`） |
| 1b | 本番移動先 `FilesDeploy2PrdPath` | 上記「本番移動先」表（4 DB すべて確定） |
| 2 | 本番先のディレクトリ構造 | STG 側と**同じ構造**（相対パス維持） |
| 3 | 移動 vs コピー | **移動**＝コピー後に STG 側削除 |
| 4 | 拡張子・サイズ | 画像系 + pdf + 一般的な静的ファイル、上限おおよそ **50MB** |
| 5 | 同名ファイル | **確認後上書き**（409 → UI 確認 → overwrite） |
| 6 | Prepare でのデフォルト選択 | **全選択** |
| 7 | ルートパス | **`/images`**（ラベル「画像情報準備」） |

## Open Questions

なし（実装ブロック要因は解消済み）。
