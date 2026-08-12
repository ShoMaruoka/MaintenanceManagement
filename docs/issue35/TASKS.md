# Tasks: Pilot適用機能の適用フロー変更（Issue #35）

対応する仕様: [`SPEC.md`](./SPEC.md)  
対応する計画: [`PLAN.md`](./PLAN.md)

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。  
git commit / push は行わない（ユーザー方針）。

TDD: 振る舞い変更タスク（T3 / T4 / T5a / T6）は **失敗するテストを先に書いてから** 実装する。

---

## 実行順序

```
T1 → T2 → T3 → T4 → [Checkpoint A]
                │
                └→ T5a → T5b → T6 → T7 → [Checkpoint B] → T8 → [統合] → Done

依存の要点:
- T5a（Mode）は T3 の Skipped に依存（T3 → T5a）
- T5b（集計）は T1 必須、T5a の Mode 語彙と揃える
- T6 RED は T5b 前に書ける。T7 は T5b 後
- T8（画面スキップ）は Checkpoint B の後（D1）
- 並行可: T1 ∥ T2。T4 と T5a は T3 後なら順不同だが既定は T4 先
```

---

## Task 1: ダッシュボード用 `PilotDeploySummary` を追加

**Status:** done

**Description:**  
`PilotDeploySummary` モデルを追加し、`DashboardStats` に `LastPilotKaios` / `LastPilotGos` を載せる。この時点では集計クエリはまだ実装しない（常に null でよい）。

**Acceptance:**

- [x] `PilotDeploySummary` に `DbName` / `ExecutedAt` / `ExecutedBy` がある
- [x] `DashboardStats` に `LastPilotKaios` / `LastPilotGos`（nullable）がある
- [x] 既存の stats 応答フィールドに破壊的変更がない

**Verify:** `cd backend && dotnet build`

**Files:**

- `backend/Models/DashboardModels.cs`（または同系のモデルファイル）

**Dependencies:** None  
**Scope:** XS

---

## Task 2: Pilot `info` レスポンスに STG 適用後パスを追加

**Status:** done

**Description:**  
`WebSourceInfoResponse` と Controller の `info` に `DeployedPath` / `MariaDbDeployedPath` / `FilesPath` を返す。フロント型は T8 で合わせてよい（このタスクは BE のみ）。

**Acceptance:**

- [x] `info` JSON に `deployedPath` / `mariaDbDeployedPath` / `filesPath` が含まれる
- [x] 値は当該 `DbConfig` の派生パスと一致する
- [x] 既存の `webSourcePath` / `commonImagePath` / `pilotTargets` は維持

**Verify:** `cd backend && dotnet build`

**Files:**

- `backend/Models/WebSourcePrepareModels.cs`
- `backend/Controllers/WebSourcePrepareController.cs`

**Dependencies:** None（T1 と並行可）  
**Scope:** XS

---

## Task 3: SQL コピー元切替＋空スキップ＋Skipped 型（TDD）

**Status:** done

**Description:**  
`RunSqlDeployAsync` のコピー元を `DeployedPath` + `MariaDbDeployedPath`（→ `Source\MariaDB`）へ変更。  
コピーは **`*.sql` 専用経路**（共通 `BuildArguments` は触らない。B1）。注入で単体テスト（I3）。DryRun 時も注入デリゲートは呼ばれる（E3）。  
両空時は bat より前に return し、`WebSourceSqlDeployResult.Skipped = true`（A1）。SSE `done` まで載せる。  
`Skipped` 時の SSE/ログ文言は SqlOnly / Both の**両経路**でスキップと分かる文言にする（D5）。  
`Result` 列は触らない（Mode は T5a）。**先に** RED → GREEN。

**Acceptance:**

- [x] `Deploy2PrdPath` を Pilot SQL 経路が参照しない
- [x] 空判定・コピーとも `*.sql` のみ（専用経路）
- [x] パス未設定はエラー（空スキップより先）
- [x] SQL Server のみ / MariaDB のみ / 両方 → 期待配置（注入で検証。DryRun でもデリゲート呼出あり）
- [x] 両空 → bat 呼出 0 回、`Skipped=true`、SSE done に反映（I4 / A1）
- [x] `Skipped` 時、SqlOnly 経路・Both 経路ともログが「SQL適用: スキップ（適用対象 SQL なし）」等になり、「完了しました」と同一でない（D5）
- [x] 片側のみ空 → 存在する側だけコピーし通常継続
- [x] DryRun View 置換が2ソース走査（I2）
- [x] hold / manual はコピー対象外

**Verify:** `cd backend/Tests && dotnet test --filter WebSourceDeployServiceSqlSource`

**Files:**

- `backend/Services/WebSourceDeployService.cs`（`WebSourceSqlDeployResult` も同ファイル末尾・F1）
- `backend/Controllers/WebSourcePrepareController.cs`（done ペイロードに Skipped。Mode 決定は T5a）
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`（新規）

**Dependencies:** None  
**Scope:** M

---

## Task 4: 静的ファイルを `FilesPath` へ切替＋欠落スキップ（TDD）

**Status:** done

**Description:**  
Files コピーを `FilesDeploy2PrdPath` → `FilesPath`。空判定は再帰でファイル0件。判定はターゲットループ外で1回でもよい（F2）。注入で呼出有無を検証。

**Acceptance:**

- [x] Pilot 経路が `FilesDeploy2PrdPath` を参照しない
- [x] ファイルあり → コピー呼出
- [x] 不存在またはファイル0件 → スキップしターゲット成功継続可
- [x] 共通画像コピーは Files の後

**Verify:** `cd backend/Tests && dotnet test`（該当）＋ `dotnet build`

**Files:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/`（T3 と同一または Files 用）

**Dependencies:** Task 3  
**Scope:** S

---

### Checkpoint A — コピー元切替（D1: 画面表示は含めない）

- [x] コード上 Pilot が `Deploy2PrdPath` / `FilesDeploy2PrdPath` を参照しない
- [x] Task 3 / 4 のテストが緑（`Skipped=true` が SSE done に載ることまで）
- [x] `cd backend && dotnet build` 成功

---

## Task 5a: Mode 決定純関数＋Controller 記録（D4 / E4）

**Status:** done

**Description:**  
`ResolveLogMode(step, dryRun, skipped, rowKind)` 等の**純関数**を切り出し、SPEC 2.1 の行別表どおりに Mode を書く。Controller が各ログ行に適用（常時 `"full"` をやめる）。DryRun＋空スキップは DryRun 優先（E2）。Web 行に `sql-skipped` を書かない（D3）。

**Acceptance:**

- [x] 純関数が SPEC の許容 Mode 一覧どおりの値だけを返す（単体テスト）
- [x] Web 行: `both`/`web`/`both-dryrun`/`web-dryrun`
- [x] SQL 行: `sql`/`sql-dryrun`/`sql-skipped`（同時時は dryrun 優先）
- [x] 例外行 TargetName=`-` の Mode が実 step
- [x] Controller が上記で `InsertWebSourceDeployLog` する

**Verify:** `cd backend/Tests && dotnet test --filter ResolveLogMode`（または追加したテスト名）

**Files:**

- `backend/Controllers/WebSourcePrepareController.cs`（または専用ヘルパー）
- `backend/Tests/`（Mode 純関数テスト・新規）

**Dependencies:** Task 3  
**Scope:** S

---

## Task 5b: `GetDashboardStats` の Pilot 最終集計（E4）

**Status:** done

**Description:**  
`GetDashboardStats` で kaios / gos の最終成功を返す。  
除外: Run 内の全行 Mode が除外集合（`both-dryrun`,`web-dryrun`,`sql-dryrun`,`sql-skipped`）に属するときのみ。完全一致 `IN`（E1）。  
`both`＋Web成功＋`sql-skipped` は**採用**（A2）。

**Acceptance:**

- [x] 成功のみの Run が最終として返る
- [x] 失敗混在は除外
- [x] 全行が除外 Mode 集合なら除外
- [x] `both`＋Web成功＋SQL空スキップは最終に採用
- [x] kaios / gos 独立・履歴なしは null
- [x] 既存 prepare / 成功率 / running が壊れない

**Verify:** Task 6 のテスト（TDD なら T6 先に RED）

**Files:**

- `backend/Services/DatabaseService.cs`

**Dependencies:** Task 1、Task 5a（Mode 語彙と揃える）  
**Scope:** S

---

## Task 6: ダッシュボード Pilot 集計の単体テスト（TDD）

**Status:** done

**Description:**  
`DatabaseServiceDashboardStatsTests` を拡張。T5b とセットで RED→GREEN。

**Acceptance:**

- [x] 成功 Run のみ採用
- [x] 部分失敗 Run は除外
- [x] 全行 DryRun・SQL空スキップのみは除外
- [x] `both`＋Web成功＋`sql-skipped` は採用（A2 回帰）
- [x] DB 別独立・最新 ExecutedAt・実行者
- [x] 既存テストが緑

**Verify:** `cd backend/Tests && dotnet test --filter DatabaseServiceDashboardStats`

**Files:**

- `backend/Tests/Services/DatabaseServiceDashboardStatsTests.cs`

**Dependencies:** Task 1（T5b と同時進行可。RED は T5b 前）  
**Scope:** S

---

## Task 7: ダッシュボード UI に kaios / gos カードを追加

**Status:** done

**Description:**  
FE の `DashboardStats` 型を拡張し、`Dashboard.tsx` に Pilot 最終適用カードを2枚追加（日時＋実行者。なしは `—` / 「実行履歴なし」）。

**Acceptance:**

- [x] カードラベルが kaios / gos で分かれている
- [x] 日時は既存 `formatDateTime`、サブに実行者
- [x] null 時の表示が SPEC どおり
- [x] 既存3カードの表示が壊れない

**Verify:** `cd frontend && npm run build`

**Files:**

- `frontend/src/types.ts`
- `frontend/src/pages/Dashboard.tsx`

**Dependencies:** Task 5b  
**Scope:** S

---

### Checkpoint B — ダッシュボード

- [x] Task 5a / 6 テスト緑（A2・Mode 純関数含む）
- [x] 画面上（または型＋ビルド）でカード2枚・日時・実行者
- [x] `npm run build` 成功

---

## Task 8: Pilot 画面 — パス3行＋SQLスキップ表示

**Status:** done

**Description:**  
FE 型に T2 のパスと T3 の `sqlDeploy.skipped` を追加。コピー元・コピー先に **deployed / MariaDB deployed / Files** の3行を追加（N1）。完了画面で SQL スキップを「スキップ」と明示（A1 / D1 の画面確認はここ）。

**Acceptance:**

- [x] コピー元・コピー先に deployed / MariaDB deployed / Files の3行がある
- [x] 既存の STG Web / 共通画像 / pilot ターゲット表示は維持
- [x] `sqlDeploy.skipped === true` のとき「スキップ」表示（✓ 成功と区別）

**Verify:** `cd frontend && npm run build`

**Files:**

- `frontend/src/api/webSourcePrepare.ts`
- `frontend/src/pages/WebSourcePrepare.tsx`

**Dependencies:** Task 2、Task 3  
**Scope:** S

---

## 統合 Checkpoint

- [x] `cd backend && dotnet build`
- [x] `cd backend/Tests && dotnet test`（Issue #35 関連 35 件緑。ManualApply 2 件は fixture パス依存で既存問題）
- [x] `cd frontend && npm run build`
- [ ] 手動: 本番前準備なし・`deployed` に SQL ありで Pilot DryRun または実実行
- [ ] 画面スキップ表示の目視（T8）
- [x] A4: 片側空の bat 挙動 — B1 後は単体テストで担保（記録: [`VERIFICATION.md`](./VERIFICATION.md)）。実機目視は任意
- [x] B2: 非 SQL 補助 — コピー層は `*.sql` 専用経路＋引数テストで確定（VERIFICATION）。実機 bat の Source 内依存目視は表へ追記
- [ ] 手動（可能なら）: 本番前準備後に STG 追加した分が Pilot に載ること
- [ ] SPEC Success Criteria をすべて満たす
- [x] `graphify update .`（コード変更後）

---

## Done の定義

全 Task の Acceptance / Verify と Checkpoint A・B・統合が完了していること。commit / push はユーザー指示があるまで行わない。
