# Spec: Pilot適用機能の適用フロー変更（issue #35）

## 対象Issue

GitHub Issue [#35 Pilot適用機能の適用フロー変更](https://github.com/ShoMaruoka/MaintenanceManagement/issues/35)

関連:

- [#25 pilot環境への適用機能](https://github.com/ShoMaruoka/MaintenanceManagement/issues/25) / `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`
- [#27 画像コピー・Viewソース更新](https://github.com/ShoMaruoka/MaintenanceManagement/issues/27) / `docs/issue27/SPEC.md`

## Objective

Pilot環境適用が「本番前準備」の成果物（`Deploy2PrdPath` / `FilesDeploy2PrdPath`）に依存しているため、本番適用の約1週間前より早く Pilot へ載せたいときに本番前準備を先行実行せざるを得ない。

本件では、**STG 適用後の状態**から Pilot へ適用できるように依存を切り離し、ダッシュボードで **kaios / gos ごとの Pilot 最終適用日（成功のみ）** を確認できるようにする。

- **対象ユーザー**: 運用担当者（既存の「Pilot環境適用」画面を利用）
- **対象システム**: kaios / gos（pilot があるシステムのみ。paf・duskin は対象外）
- **成功条件**:
  - 本番前準備を実行しなくても、STG 適用済みの SQL / 静的ファイルから Pilot 適用が完走できる
  - `deployed_manual`（Table / UDTT）は Pilot 自動適用に含めない（Pilot でも手動適用）
  - ダッシュボードに kaios・gos それぞれの Pilot 最終成功日時と実行者が表示される

## 背景・現状（As Is）→ 目標（To Be）

| 項目 | As Is | To Be |
|------|-------|-------|
| Webソースコピー | `WebSourcePath` → 各 pilot | 変更なし |
| 共通画像 | `CommonImagePath` → `DestImagePath` | 変更なし |
| 静的ファイル（Images/news/pdf 等） | `FilesDeploy2PrdPath`（本番前準備後） | **`FilesPath`（`DeployDev2StgPath\Files`）** |
| SQL コピー元 | `Deploy2PrdPath`（本番前準備後） | **`DeployedPath` + `MariaDbDeployedPath`** |
| `deployed_hold` | （本番前準備で選択時のみ） | **含めない** |
| `deployed_manual` | Deploy2Prd の ManualApply 経由の可能性あり | **含めない**（Pilot も手動） |
| 空の SQL 元 | 実質 Deploy2Prd 前提 | **スキップ**（`deploy.bat` 非実行）。結果は `skipped` 相当で記録し、ダッシュボード最終適用には載せない |
| ダッシュボード | 本番前準備の最終実行のみ | **+ kaios / gos の Pilot 最終成功（日時・実行者）カード各1枚** |
| 本番前準備後の Pilot | Deploy2Prd 起点なら準備済み SQL を再適用可能 | **同一モジュールの再適用は想定しない**。準備後は **その後 STG 適用した追加・再修正分**（再び `deployed/` に載ったもの）のみが対象 |

本番前準備自体の挙動・パスは変更しない（本番用フローは現状維持）。

### 運用前提（C1 確定）

- 本番前準備は `deployed/` / `FilesPath` から Deploy2Prd 側へ移し、**元を削除**する。したがって準備直後に未送出が無ければ Pilot SQL/Files は空になる（スキップは正しい）。
- **同一モジュールを Pilot へ再適用する運用は想定しない**（準備済み分を Deploy2Prd から取り直さない）。
- **本番前準備の後に、追加や再修正したモジュールを STG 適用 → Pilot 適用する運用は想定する**（それらは再び `deployed/` / `FilesPath` に載る）。
- Pilot は `deployed/` を消費しない。未送出分は実行のたびに再適用される（View/SP は概ね冪等。データ更新系が混ざる場合は注意。Risks 参照）。

## Tech Stack

既存構成のまま。追加の外部依存なし。

- バックエンド: ASP.NET Core 8（`WebSourceDeployService` / `DatabaseService` / `HistoryController` 等）
- コピーツール: robocopy（既存 `RunRobocopyAsync`、常に `/E`）
- フロントエンド: React 18 + TypeScript + Vite
- 永続化: SQLite（既存 `WebSourceDeployLog` を集計に利用）
- 自動テスト: xUnit（`backend/Tests`）

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Test:   cd backend/Tests && dotnet test
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## Project Structure

```
backend/
  Controllers/          → API（WebSourcePrepare / History 等）
  Models/               → DbConfig, DashboardStats 等
  Services/             → WebSourceDeployService, DatabaseService 等
  Tests/Services/       → ユニットテスト
frontend/src/
  pages/Dashboard.tsx   → ダッシュボード
  pages/WebSourcePrepare.tsx → Pilot環境適用画面
  api/ / types.ts       → API クライアント・型
docs/issue35/           → 本 SPEC / 後続 PLAN・TASKS
```

## Code Style

既存パターンに合わせる。命名は C# PascalCase / TS camelCase（JSON は camelCase）。コメントは「なぜ」を短く日本語で。

```csharp
// SQL: Deploy2PrdPath ではなく STG 適用後の deployed を正とする（Issue #35）
var sqlServerSrc = config.DeployedPath;
var mariaDbSrc = config.MariaDbDeployedPath;
// MariaDB は本番前準備と同じく Source\MariaDB 配下へ載せる
var mariaDbDest = Path.Combine(sourceDir, "MariaDB");
```

```tsx
{/* kaios / gos は別カード。最終は成功 Run のみ */}
<div className="stat-card-label">Pilot 最終適用（kaios）</div>
<div className="stat-card-value">{formatDateTime(lastPilotKaios.executedAt)}</div>
<div className="stat-card-sub">実行者: {lastPilotKaios.executedBy}</div>
```

## 機能仕様 1: SQL / 静的ファイルのコピー元変更

### 1.1 SQL 適用（`RunSqlDeployAsync`）

1. `PilotSqlDeployPath` と `PilotMariaDbSqlDeployPath` の**両方**未設定 → 本ステップ自体をスキップ（`null` 返却）
2. **パス妥当性（未設定 vs 不存在）** — 空スキップより先に判定する
   - `DeployDev2StgPath` 未設定などにより `DeployedPath` / `MariaDbDeployedPath` が絶対パスでない場合 → **従来どおりエラー**（設定ミスを成功扱いにしない）
   - 絶対パスとして妥当だがディレクトリ不存在、または再帰で `*.sql` が0件 → 空扱いへ
3. 使う側の Source を空にしてから再作成（DryRun 時はログのみ）
4. **コピー元・コピー対象**
   - SQL Server: `config.DeployedPath` → `PilotSqlDeploySourcePath`
   - MariaDB: `config.MariaDbDeployedPath` → **`PilotMariaDbSqlDeploySourcePath`**（SQL Server の Source 配下ではない。STG と同様に別ツリー）
   - **空判定・コピーとも `*.sql` を正とする**
5. **含めないもの**: `DeployedHoldPath` / `MariaDbDeployedHoldPath` / `DeployedManualPath`
6. **空扱い（スキップ）**
   - 両ソースとも空のとき: コピーせず、**`deploy.bat` 存在チェックより前に return**（bat 非起動を保証）
   - ログに WARN（「適用対象 SQL なし」）
   - 戻り値・SSE: `WebSourceSqlDeployResult` に **`Skipped: true`**。画面でも「スキップ」と分かるようにする
   - 履歴行の **`Result` は `success` のまま**。識別は Mode（`sql-skipped`）
7. **適用バッチ（B1・PR #37 Blocking #2）**
   - SQL Server: `PilotSqlDeployPath\deploy.bat`（作業ディレクトリ `PilotSqlDeployPath`）
   - MariaDB: **`PilotMariaDbSqlDeployPath\deploy.bat`**（作業ディレクトリ `PilotMariaDbSqlDeployPath`）。本システムは作成しない（事前配置）
   - `*.sql` がある側だけ実行。両方あれば SQL Server → MariaDB の順
   - MariaDB に `*.sql` があるのに `PilotMariaDbSqlDeployPath` 未設定 → **エラー**（コピーのみで Success にしない）
   - SQL Server に `*.sql` があるのに `PilotSqlDeployPath` 未設定 → **エラー**
8. View DB 名置換は両コピー先（または DryRun 時は両ソース）を走査
9. **破壊的変更**: `Deploy2PrdPath` を Pilot SQL のコピー元としては使わない。MariaDB を SQL Server Source\MariaDB に載せない

### 1.2 静的ファイル（各 pilot ターゲットループ内）

- 変更前: `FilesDeploy2PrdPath` → `target.DestWebSourcePath`
- 変更後: **`FilesPath`**（`DeployDev2StgPath\Files`）→ `target.DestWebSourcePath`
- **空判定**: ディレクトリ不存在、または再帰で**ファイルが0件**（空カテゴリフォルダのみ残っている場合は空＝スキップ）
- 空の場合は **スキップして成功**（ログに理由）。ファイルがある場合のみ robocopy `/E`
- `FilesDeploy2PrdPath` は本番前準備用のため Pilot では参照しない
- 共通画像コピー（`CommonImagePath`）の順序・後勝ちは現状維持（Files コピーの後）

### 1.3 画面文言・スキップ表示

- 「コピー元・コピー先」ブロックに **deployed / MariaDB deployed / Files** の行を追加（`info` API 拡張）
- 既存の STG Web / 共通画像 / pilot ターゲット表示は維持
- SQL 空スキップ時は完了画面で **「スキップ」** と明示（`sqlDeploy.skipped` 等。単なる ✓ 成功にしない）

## 機能仕様 2: ダッシュボード — Pilot 最終適用（kaios / gos）

### 2.1 データ定義

既存 `WebSourceDeployLog` を集計する（新規テーブル不要）。

| 項目 | 定義 |
|------|------|
| 対象 DB | `DbName` が `kaios` / `gos` それぞれ |
| 「成功 Run」 | 同一 `RunId` に属する全行の `Result` がすべて `success` |
| スキップ行の Result | **常に `success`**（`Result='skipped'` は使わない）。識別は Mode |
| 最終適用から除外 | Run 内の**全行**が DryRun 系 Mode、または SQL 空スキップのみ（実適用の成功行が1つも無い）のとき。**実適用の成功行（例: pilot1/pilot2 の Web コピー成功）が1つでもあれば採用**（`both`＋SQL空スキップは最終に載る） |
| 除外の判定単位 | Mode の**行単位**（例: `sql-skipped` / `*-dryrun`）。Run 全体を「Mode に sql-skipped を含むから除外」とはしない |
| 最終 | その DB について、除外後の成功 Run のうち最も新しい `ExecutedAt`（同時なら任意で1件） |
| 表示 | **日時**（既存 `formatDateTime`）と **実行者**（`ExecutedBy`） |
| 失敗のみの Run | 最終としては扱わない |
| 履歴なし | カード値は `—`、サブは「実行履歴なし」 |

実行内容が `web` / `sql` / `both` いずれでも、上記を満たせば最終適用として採用する。  
`Mode` 列を活用する（スキーマ変更なし）。現状の常時 `"full"` 固定をやめ、実際の step / dry-run / skipped を記録する。Mode 文字列の決定は **Controller に集約**し、**純関数**（例: `ResolveLogMode`）に切り出して単体テストする。

#### Mode の行別対応表（書き込み側・D3 / E1）

許容 Mode は以下の**有限リストのみ**。集計の除外判定は部分一致ではなく **`IN (...)` 完全一致**で行う。

| 行の種類 | TargetName | Mode（許容値） |
|----------|------------|----------------|
| Web ターゲット | `pilot1` / `pilot2` | `both` / `web` / `both-dryrun` / `web-dryrun` |
| SQL 適用実行 | `sql` | `sql` / `sql-dryrun` |
| SQL 空スキップ | `sql` | `sql-skipped`（DryRun 同時時は **`sql-dryrun` を優先**・E2） |
| 例外・全体失敗 | `-` | 実行していた step（`both` / `web` / `sql` 等）。`Result='failed'` |

**禁止**: Run 内の Web 行（pilot1/pilot2）まで Mode=`sql-skipped` にしない。そうすると実適用成功行が検出できず A2 が無効化される。

**最終適用から除外する Mode 集合（完全一致）**:  
`both-dryrun`, `web-dryrun`, `sql-dryrun`, `sql-skipped`  
→ Run 内の**全行**がこの集合に属するときのみ除外。1行でも集合外（例: `both` / `web` / `sql`）があれば採用。

過去ログの `Mode='full'` は除外集合に含めない（B4: 旧 DryRun が最終に残り得る）。

### 2.2 API

`GET /api/history/stats` の `DashboardStats` に追加する（例）:

```csharp
public class PilotDeploySummary
{
    public string DbName { get; set; } = "";      // kaios | gos
    public string ExecutedAt { get; set; } = "";  // ISO 8601
    public string ExecutedBy { get; set; } = "";
}

// DashboardStats
public PilotDeploySummary? LastPilotKaios { get; set; }
public PilotDeploySummary? LastPilotGos { get; set; }
```

（プロパティ名は実装時に既存 camelCase JSON 方針へ合わせる）

### 2.3 UI

- 既存「本番前準備 最終実行」「直近N日 成功率」「実行中セッション」は維持
- **kaios / gos それぞれ独立した `stat-card` を2枚追加**
  - ラベル例: `Pilot 最終適用（kaios）` / `Pilot 最終適用（gos）`
  - 値: 日時
  - サブ: 実行者（履歴なし時は「実行履歴なし」）

## Testing Strategy

| レベル | 対象 | 置き場・手段 |
|--------|------|--------------|
| ユニット | SQL コピー元切替・空スキップ・MariaDB サブフォルダ配置 | `backend/Tests/Services/`（一時ディレクトリ） |
| ユニット | `GetDashboardStats` の Pilot 最終（成功のみ・DB別） | `DatabaseServiceDashboardStatsTests` を拡張 |
| ビルド | 前後で壊れないこと | `dotnet build` / `dotnet test` / `npm run build` |
| 手動 | DryRun またはローカルダミーフォルダで Pilot 画面実行 | STG 適用後・本番前準備未実施でも SQL/Files が取れること。片側空での `deploy.bat` 挙動も確認 |

カバレッジ数値のゲートは設けない。変更箇所の回帰テストを必須とする。

## Boundaries

- **Always**
  - Pilot SQL は `DeployedPath` + `MariaDbDeployedPath` の `*.sql` のみから取る
  - パス未設定（相対パス等）はエラー。不存在・`*.sql` 0件のみスキップ
  - `deployed_hold` / `deployed_manual` を自動コピーしない
  - 両空スキップは `deploy.bat` 非実行・`Result=success`＋Mode で識別・画面に「スキップ」表示
  - **SQL 空スキップのみ／全行 DryRun** の Run はダッシュボード最終から除外（Web 成功＋SQL空の both は採用）
  - 静的ファイルは `FilesPath` を使い、ファイル0件はスキップ成功
  - ダッシュボードの Pilot 最終は **成功 Run のみ**（除外ルール適用）、**kaios / gos 別カード**、日時＋実行者
  - 変更後は `dotnet test` とフロントビルドで確認する
  - 片側空での `deploy.bat` 挙動はリリース前に必須確認
- **Ask first**
  - 成功 Run の定義を変更する場合
  - スキーマ変更や新規依存の追加
  - `LastPilotKaios`/`LastPilotGos` を配列化する場合（S3・今回は固定プロパティで可）
- **Never**
  - Pilot 適用のために本番前準備を必須に戻さない
  - `Deploy2PrdPath` / `FilesDeploy2PrdPath` を Pilot のコピー元として使い続けない
  - `deployed_manual` を Pilot へ自動適用しない
  - 本番前準備フローや本番受け渡しパスを無断で変えない
  - 秘密情報をコミットしない
  - `Result='skipped'` を入れて both の Web 成功 Run を誤除外しない
  - SQL 空スキップを画面上で単なる「✓ 成功」と同一表示にしない

## Success Criteria

- [ ] Pilot SQL のコピー元が `Deploy2PrdPath` ではなく `DeployedPath`（＋ MariaDB は `Source\MariaDB`）になっている
- [ ] `deployed_hold` / `deployed_manual` はコピーされない
- [ ] 両 SQL 元が空のとき SQL 適用はスキップし、`deploy.bat` を起動せず、画面にスキップと出る
- [ ] SQL 空スキップのみの Run はダッシュボード最終に載せない（`both`＋Web成功＋SQL空は載る）
- [ ] パス未設定はエラー、不存在・空のみスキップ
- [ ] 静的ファイルコピーが `FilesPath` 起点で、ファイル0件はスキップ成功
- [ ] 本番前準備未実施・STG 適用済みの状態から Pilot 適用が完走できる（手動確認）
- [ ] 本番前準備後に STG で追加したモジュールが `deployed/` 経由で Pilot 適用できる（運用前提どおり）
- [ ] ダッシュボードに kaios / gos の Pilot 最終成功カードが各1枚あり、日時と実行者が出る
- [ ] 失敗のみ・全行 DryRun・SQL空スキップのみの Run は最終表示に使われない
- [ ] 片側空（SQL Server のみ／MariaDB のみ）で `deploy.bat` が期待どおり動く（必須手動確認）
- [ ] 既存の本番前準備・STG 適用の挙動を壊さない（関連テスト／ビルド通過）

## Open Questions

1. ~~SQL コピー元~~ → **確定**: `deployed/` のみ。MariaDB も含める。hold / manual は含めない
2. ~~静的ファイル~~ → **確定**: `FilesPath` 全体
3. ~~ダッシュボード~~ → **確定**: 別カード2枚、日時＋実行者、成功のみ
4. ~~空フォルダ~~ → **確定**: スキップ（Mode 識別・最終は「空スキップのみ」の Run を除外）
5. ~~info API~~ → **確定**: deployed / MariaDB / Files パスを返す
6. ~~C1 本番前準備後の再適用~~ → **確定**: 同一モジュール再適用は想定しない。準備後の追加・再修正は想定する
7. ~~A2/A3 除外ルール・Result~~ → **確定**: Result は success のまま。実適用成功行があれば採用。全行 dry-run／SQL空のみなら除外

## Decisions Log

| 日付 | 決定 | 根拠 |
|------|------|------|
| 2026-08-12 | SQL 元は `deployed/` + MariaDB `deployed`。hold なし | ユーザー回答 Q1 |
| 2026-08-12 | 静的ファイルは `FilesPath` | ユーザー回答 Q2 |
| 2026-08-12 | `deployed_manual` は自動適用しない（Pilot も手動） | ユーザー回答 Q3 |
| 2026-08-12 | ダッシュボードは kaios/gos 別カード、日時＋実行者、成功のみ | ユーザー回答 Q4 |
| 2026-08-12 | SQL 元が空ならスキップ（最終適用除外） | ユーザー回答 Q5 + レビュー C1/S2 |
| 2026-08-12 | 成功 Run = 同一 RunId の全ログ行が success | 部分成功を最終扱いしないため |
| 2026-08-12 | 同一モジュール再適用は想定しない。準備後の追加・再修正は想定 | ユーザー回答（C1） |
| 2026-08-12 | DryRun は最終適用から除外（Mode で識別） | レビュー S1 採用 |
| 2026-08-12 | 空スキップは Mode で識別・最終除外（Result は success） | レビュー S2 / A3 |
| 2026-08-12 | kaios/gos 配列化は見送り（固定プロパティ） | レビュー S3・今回スコープ外 |
| 2026-08-12 | 除外は「実適用成功行が無い Run のみ」。both+Web成功+SQL空は採用 | レビュー A2 |
| 2026-08-12 | skipped は結果型＋SSE＋FE で明示（T3/T8） | レビュー A1 |
| 2026-08-12 | Mode 行別対応表・有限リスト・集計は完全一致 | レビュー D3 / E1 |
| 2026-08-12 | DryRun＋空スキップ同時は DryRun 優先（`sql-dryrun`） | レビュー E2 |
| 2026-08-12 | Mode 決定は純関数＋単体テスト（T5a） | レビュー D4 |
| 2026-08-12 | Skipped 時ログ文言（2経路）を T3 受入に含める | レビュー D5 |
| 2026-08-12 | Pilot MariaDB は別 `PilotMariaDbSqlDeployPath`＋専用 bat で自動適用（B1） | PR #37 Blocking #2・ユーザー確定 |
