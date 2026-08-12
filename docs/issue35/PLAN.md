# Implementation Plan: Pilot適用機能の適用フロー変更（issue #35）

対応する仕様: [`SPEC.md`](./SPEC.md)  
敵対検証: [`REVIEW.md`](./REVIEW.md)（第1〜第3ラウンド反映済み）

関連:

- [`../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md)
- [`../issue27/SPEC.md`](../issue27/SPEC.md)

## Overview

Pilot環境適用が本番前準備の成果物（`Deploy2PrdPath` / `FilesDeploy2PrdPath`）に依存しているのをやめ、**STG 適用後の状態**（`DeployedPath` / `MariaDbDeployedPath` / `FilesPath`）から適用できるようにする。あわせてダッシュボードに **kaios / gos 別の Pilot 最終成功（日時・実行者）** を表示する。

新規画面・新規テーブルは作らない。既存の `WebSourceDeployService`・`WebSourceDeployLog`・`GetDashboardStats`・Dashboard UI を拡張する。

```
スライス A（コピー元切替）          スライス B（ダッシュボード）
RunSqlDeploy のソース変更            PilotDeploySummary モデル
  + FilesPath 切替                     + Mode 純関数（T5a）
  + 空スキップ（Skipped フラグ）         + GetDashboardStats（T5b）
  + info / SSE Skipped                 + FE types / Dashboard カード
  + T8: パス3行＋スキップ画面表示
```

## Architecture Decisions

1. **SQL コピーは2ソース・2パス（`*.sql` のみ）— B1**
   - `DeployedPath` → `PilotSqlDeploySourcePath`（フラット）
   - `MariaDbDeployedPath` → `PilotSqlDeploySourcePath\MariaDB`
   - 空判定・コピーとも **`*.sql` を正**とする
   - 共通 `BuildArguments`（Web／画像／Files）にはファイル名フィルタを足さない
   - **SQL コピー専用**に `*.sql` フィルタ付き経路を用意し、他呼出に影響させない

2. **未設定 vs 不存在の判定順序（I1）**
   - 先にパス妥当性（絶対パス）。未設定相当は **エラー**
   - その後、不存在または `*.sql` 0件なら空スキップ

3. **空スキップ（S2 / A1 / A3 / D5）**
   - Source 初期化は行う。コピー・View置換・`deploy.bat` は行わない（bat 存在チェックより前に return）
   - `WebSourceSqlDeployResult` に **`Skipped`** を追加し SSE `done` に載せる
   - 履歴の **`Result` は `success`**。識別は Mode
   - ログ文言: `Skipped` 時は SqlOnly / Both の**両経路**で「SQL適用: スキップ（適用対象 SQL なし）」等とし、「完了しました」と同一にしない
   - 片側のみ空 → 存在する側だけコピーして通常継続（A4: bat 必須確認）

4. **静的ファイルは `FilesPath`、空＝再帰でファイル0件（S5 / F2）**
   - 空カテゴリのみ残存もスキップ
   - ファイル有無の判定は **ターゲットループ外で1回**行い、結果を各 pilot で再利用してよい（任意最適化）
   - ファイルがあるときのみ robocopy `/E`

5. **ダッシュボード集計（S1 / S6 / A2 / A3 / E1）**
   - 「成功 Run」= 同一 `RunId` の全行 `Result = 'success'`
   - **除外**: Run 内の全行の Mode が除外集合に属するときのみ（完全一致 `IN`）
   - 除外集合: `both-dryrun`, `web-dryrun`, `sql-dryrun`, `sql-skipped`
   - **採用例**: `both` で pilot1/pilot2 成功 ＋ sql 行 Mode=`sql-skipped`
   - `LastPilotKaios` / `LastPilotGos` 固定（S3 見送り）

6. **`Mode` 列（D3 / D4 / E1 / E2 / B3）**
   - 許容値は SPEC 2.1 の行別対応表に限る（有限リスト）
   - **Mode 決定は Controller 側の純関数**（例: `ResolveLogMode(step, dryRun, skipped, rowKind)`）に切り出し、**T5a** で単体テスト
   - DryRun ＋ 空スキップ同時 → **DryRun 優先**（`sql-dryrun`）
   - Web 行に `sql-skipped` を書かない
   - Service は `Skipped` 等の事実を返し、Controller が Mode に落とす
   - 所有: **T5a**（Mode 記録）／**T5b**（集計クエリ）

7. **テスト戦略・注入と DryRun（I3 / I4 / E3）**
   - コピー／bat を注入可能にし、実 robocopy は回さない
   - **DryRun 時も注入デリゲートは呼ばれる**（呼出回数・元先の検証が可能）。実ファイル変更はデリゲート／内側が no-op とする。現行 `RunRobocopyAsync` 内側の DryRun early-return と同等の「副作用なし」を維持する
   - bat 非起動: bat を配置したうえで実行デリゲート呼出 0 回（空スキップ時）
   - DryRun View 置換は2ソース走査

8. **運用前提（C1）／過去ログ（B4）**
   - 同一モジュール再適用は想定しない。準備後の追加・再修正は想定
   - Pilot は `deployed/` を消費しない（S7）
   - 既存行はすべて `Mode='full'`。除外導入後も**過去の DryRun は最終に載り得る**

9. **変更しないもの**
   - 本番前準備、Deploy2Prd パス、Web／共通画像／web.config／View 置換ルール本体
   - `WebSourceDeployLog` スキーマ（列追加なし）

## Component Map

| コンポーネント | 役割 | 依存 |
|----------------|------|------|
| `WebSourceDeployService` | SQL/Files 切替・空スキップ・`*.sql` 専用コピー・Skipped・ログ文言 | `DbConfig` |
| `ResolveLogMode`（純関数） | Mode 文字列決定 | step / dryRun / skipped / rowKind |
| `WebSourcePrepareController` | info・Mode 適用・SSE | Service, Config |
| `DatabaseService.GetDashboardStats` | 成功 Run（除外集合 IN） | `WebSourceDeployLog` |
| `Dashboard.tsx` | kaios/gos カード | stats API |
| `WebSourcePrepare.tsx` | パス3行＋スキップ表示 | info / done |
| Tests | 注入・Mode 純関数・stats・Skipped | 一時dir / SQLite |

## Implementation Order

```
Phase 1  Foundation
  └─ T1 PilotDeploySummary / DashboardStats
  └─ T2 WebSourceInfoResponse パス

Phase 2  スライス A（T3 → T4 → Checkpoint A）
  └─ 注入＋RunSqlDeploy＋Skipped＋ログ文言
  └─ FilesPath 切替
  └─ 単体テスト（SSE Skipped まで。画面はまだ見ない）

Phase 3  スライス B（T3 後: T5a → T5b → T6 → T7 → Checkpoint B）
  └─ T5a Mode 純関数＋Controller 記録
  └─ T5b GetDashboardStats
  └─ T6 集計テスト / T7 Dashboard カード

Phase 4  T8（パス3行＋スキップ画面）→ 統合（片側空 bat 必須）
```

## Risks & Mitigations

| リスク | 影響 | 対策 |
|--------|------|------|
| `deploy.bat` が Deploy2Prd 前提 | 配置失敗 | Source 階層を本番前準備と揃える |
| 片側空で bat が非0（A4） | SQL 失敗 | **必須**手動確認 |
| 非 SQL 補助ファイルが運ばれない（B2） | bat 依存崩れ | 必須手動確認 |
| Web 行まで `sql-skipped`（D3） | A2 無効化 | 行別表＋`ResolveLogMode` テスト |
| Mode 部分一致集計（E1） | 静かに壊れる | 除外は有限リストの `IN` |
| 過去 `Mode='full'`（B4） | 初回表示想定外 | 注記のみ |
| Pilot が deployed を消費しない（S7） | 多重適用 | 運用注意 |

## Verification Checkpoints

### Checkpoint A（T4 完了時・FE 画面は含まない — D1）

- [ ] Pilot が Deploy2Prd / FilesDeploy2Prd を参照しない
- [ ] 両空 → `Skipped=true` が SSE `done` に載る・bat 未起動・スキップ用ログ文言
- [ ] SQL Server のみ / MariaDB のみ / 両方 → 期待配置（`*.sql` フィルタ）
- [ ] DryRun View 2ソース走査・未設定はエラー
- [ ] FilesPath ファイル0件スキップ
- [ ] `dotnet test` 緑

### Checkpoint B（T7 完了時）

- [ ] 実適用成功行があれば最終採用（both+SQL空含む）
- [ ] 全行が除外 Mode 集合なら除外
- [ ] Mode 純関数テスト緑
- [ ] kaios/gos 独立・カード2枚
- [ ] 既存カード壊れない

### Checkpoint 統合（T8 含む）

- [ ] `dotnet build` / `dotnet test` / `npm run build`
- [ ] 画面スキップ表示（T8）
- [ ] 手動: 準備なし・deployed ありで Pilot
- [ ] **必須**手動: 片側空の `deploy.bat`
- [ ] **必須**手動: 非 SQL 補助ファイル依存の有無（B2）
- [ ] 手動（可能なら）: 準備後に STG 追加した分が Pilot に載る

## Out of Scope

- 本番前準備フロー変更、`deployed_manual` 自動適用、IIS 再起動、履歴専用画面、hold 選択 UI、`LastPilot*` 配列化、Deploy2Prd からの同一モジュール再適用

## Next Step

[`TASKS.md`](./TASKS.md) に従い T1 から実装する。
