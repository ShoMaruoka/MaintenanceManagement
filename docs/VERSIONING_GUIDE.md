# バージョニング運用ガイド

リリース版の採番、版番号の更新箇所、タグ付けの手順をまとめる。

---

## 採番ルール

セマンティックバージョニング（`MAJOR.MINOR.PATCH`）に従う。

| 位置 | 上げる条件 | 例 |
|------|-----------|-----|
| MAJOR | 既存の運用手順が通用しなくなる破壊的変更 | 設定ファイルの構造変更、対象 DB エンジンの入れ替え |
| MINOR | 後方互換のある機能追加 | MariaDB 適用機能の追加（1.0.0 → 1.1.0） |
| PATCH | バグ修正・UI 調整 | サイドバー幅調整、スタイル修正 |

---

## 版番号を書く場所は 3 箇所

**この 3 つは必ず同じ値に揃える。** 揃っていないと画面に警告が出る（後述）。

| # | ファイル | 記述 |
|---|---------|------|
| 1 | `backend/MaintenanceManagement.Api.csproj` | `<Version>1.5.1</Version>` |
| 2 | `frontend/package.json` | `"version": "1.5.1"` |
| 3 | Git タグ | `v1.5.1`（`v` プレフィックス付き） |

`frontend/vite.config.ts` は `package.json` の値を `__APP_VERSION__` としてビルド時に埋め込むため、
フロント側で版番号を直接書く必要はない。

---

## 画面での見え方

サイドバーのロゴ「Maintenance Mgr」直下に `v1.2.1` が表示される
（`frontend/src/components/Sidebar.tsx`、スタイルは `index.css` の `.sidebar-version`）。

画面表示に使うのはフロントエンド側の版番号。起動時に `GET /api/version` を呼んでバックエンドの版と照合する。

| 状態 | 表示 | tooltip |
|------|------|---------|
| 一致 | `v1.2.1`（グレー） | 画面・API ともに v1.2.1 |
| 不一致 | `v1.2.1 ⚠`（警告色） | バージョン不一致 — 画面: v1.2.1 / API: v1.2.0 |
| API 取得失敗 | `v1.2.1 ⚠`（警告色） | 画面: v1.2.1 / API: バージョンを取得できませんでした |

**⚠ が出たらデプロイが片方しか通っていない。** フロントエンドは `backend/wwwroot` に配置され、
バックエンドは DLL として配信されるため、片方だけ古いまま動く状態が起こり得る。この表示はそれを検知するためにある。

---

## バックエンドの版の公開

`backend/Controllers/VersionController.cs` が `GET /api/version` で以下を返す。

```json
{
  "version": "1.2.1",
  "informationalVersion": "1.2.1+<ビルド元の commit hash>"
}
```

- `version` — `.csproj` の `<Version>` そのもの。画面表示と照合に使う
- `informationalVersion` — commit hash 付き。**どのコミットがビルドされたかを特定できる**

commit hash は .NET 8 の Source Link が自動で付与するため、Jenkins 側での追加設定は不要。
Git 情報のない環境でビルドした場合は hash が付かず `1.2.1` のみになる。

---

## リリース手順

対象の変更が `main` にマージ済みであることを前提とする。

### 1. 版番号を更新する

```bash
# backend/MaintenanceManagement.Api.csproj の <Version> を書き換え
# frontend/package.json の "version" を書き換え
```

### 2. ビルドが通ることを確認する

```bash
cd frontend && npm run build          # tsc + vite build
dotnet build backend/MaintenanceManagement.Api.csproj
```

### 3. 版番号の更新をコミットする

```bash
git add backend/MaintenanceManagement.Api.csproj frontend/package.json
git commit -m "Bump version to 1.2.1"
git push origin main
```

### 4. 注釈付きタグを打つ

`-a` を付けて注釈付きタグにする。メッセージに変更内容を箇条書きで残す。

```bash
git tag -a v1.2.1 -m "v1.2.1 — <リリース名>

- 変更点 1
- 変更点 2"

git push origin v1.2.1
```

### 5. 動作確認

デプロイ後、画面のサイドバーに新しい版番号が ⚠ なしで表示されることを確認する。

```bash
curl http://<サーバー>/api/version
```

---

## タグ運用の方針

- **タグは `main` のコミットにのみ打つ。** feature ブランチには打たない
- **タグは打ち直さない。** 一度 push したタグを動かすと、そのタグを取得済みの環境と食い違う。
  間違えた場合は削除せず、次の PATCH を打ち直す
- **PR マージのたびに打つ必要はない。** 実際にサーバーへ配信する単位で打つ

---

## 既存タグの履歴

初回のタグ付けは 2026-08-06 に遡って実施した。それ以前はタグ運用がなかった。

| タグ | コミット | 日付 | 内容 |
|------|---------|------|------|
| `v1.0.0` | `a8e2ce2` | 2026-07-28 | MVP。SQL Server 専用。STG 適用 / 本番前準備 / 実行履歴 / ユーザ管理 / Web ソース配信（Pilot）/ 画像準備 / 手動適用 |
| `v1.1.0` | `8ea6ac2` | 2026-08-05 | MariaDB 適用機能（PR #30, issue22）。テーブル探索、DeployService パイプライン対応、削除候補検知、FUNCTION 対応、エンジン別ツリー分割 |
| `v1.1.1` | `1c645ba` | 2026-08-06 | 本番準備画面の操作種別タグ付け、選択オブジェクトのエンジン別グループ化、UI 調整 |
| `v1.2.0` | — | 2026-08-06 | バージョン表示機能。`/api/version` の追加、サイドバーへの版番号表示、フロント／バックエンドの版不一致検知 |
| `v1.2.1` | — | 2026-08-06 | ダッシュボードのサマリーカードを実データに接続。`/api/history/stats` の追加（本番前準備の最終実行、直近30日の成功率、実行中セッション数） |
| `v1.3.0` | — | 2026-08-07 | 操作区分（新規／更新）の自動判定（Issue #32）。STG DB 存在を優先し Git をフォールバック。固定バッジ化、選択状態の Set 化 |
| `v1.4.0` | — | 2026-08-10 | 画像情報準備の削除機能（Issue #23）。ツリー複数選択・空フォルダ削除・`POST .../delete`・DryRun／部分成功対応 |
| `v1.5.0` | — | 2026-08-12 | Pilot適用フロー変更（Issue #35）。SQL/Files を STG 適用後（`deployed/` / `FilesPath`）起点に切替、ダッシュボードに kaios/gos 別の Pilot 最終適用表示。**設定:** pilot で MariaDB SQL を扱う DB は `PilotMariaDbSqlDeployPath`（専用 `deploy.bat` 配置先）の追加が必須。未設定のまま `MariaDbDeployedPath` に `*.sql` があると SQL 適用ステップが例外終了する |

### 補足: MariaDB 対応の境界について

`MySqlConnector` への依存はリポジトリの初回コミット（`8cd1d88`, 2026-06-23）から存在する。
そのため「MariaDB 対応前」を依存関係では切れない。`6257632`（2026-07-21）で
「一時的に MariaDB モジュールをモジュールツリーから非表示にする」対応が入っており、
実際に機能として利用可能になったのは PR #30 のマージ以降。この機能実装の有無を境界とした。
