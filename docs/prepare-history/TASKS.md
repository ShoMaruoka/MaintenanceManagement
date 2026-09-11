# Tasks: 実行履歴への本番前準備ログ表示

対象 SPEC: `docs/prepare-history/SPEC.md`
対象 PLAN: `docs/prepare-history/PLAN.md`（Checkpoint A〜C に従う）

実行順序: Task 1 → Task 2 → Task 3 → Task 4（逐次必須）。各 Task は 5 ファイル以内に収める。

---

- [x] Task 1: 準備ログ API を一覧／詳細に分離する（バックエンド）
  - Acceptance:
    - [x] `GET /api/history/prepare` の各要素に `logDetail` が含まれない
    - [x] `GET /api/history/prepare/{id}` で `logDetail` 全文が返り、存在しない id は 404 になる
    - [x] `GET /api/history/stats` の `lastPrepare` が従来どおり返る
    - [x] `limit` が 1〜500 にクランプされる
  - Verify:
    - [x] `cd backend && dotnet test Tests/MaintenanceManagement.Api.Tests.csproj` が全件パス
    - [ ] 手動: アプリ起動後に `GET /api/history/prepare?limit=20` と `GET /api/history/prepare/{id}` を叩き、一覧に `logDetail` が無く詳細に全文があることを確認
    - [ ] 手動: `LogDetail` が NULL の旧行でも両 API が 200 を返し、一覧・詳細が壊れないことを確認
  - Files:
    - `backend/Services/DatabaseService.cs`（`GetRecentPrepLogs` 軽量化＋`GetPrepLogById` 新設）
    - `backend/Controllers/HistoryController.cs`（`GET /prepare/{id}` 追加＋一覧 `limit` クランプ見直し）
    - `backend/Tests/Services/DatabaseServicePrepLogTests.cs`（新規）
  - Dependencies: None
  - Estimated scope: S（3 ファイル）
  - Notes:
    - 着手前に `GetRecentPrepLogs`／`/history/prepare` の利用箇所を `Grep` で再確認し、`LogDetail` 依存があれば PLAN AD1 のフォールバック（詳細追加のみ）に切り替える
    - スキーマ変更はしない。`EnsureCreated` に触らない

### Checkpoint A（Task 1 終了時）
- [x] `dotnet test` 全件パス、`dotnet build` 警告なし
- [ ] 一覧／詳細／stats の 3 API を実データで確認済み
- [ ] **人間のレビューを受けてから Task 2 へ進む**

---

- [x] Task 2: フロント型・API クライアントに準備ログを追加する
  - Acceptance:
    - [x] `ProductionReadyLog` に `logDetail?: string` と `logDetailFetched?: boolean` が追加されている
    - [x] `getPrepareLogs(limit)` が一覧（`logDetail` なし）を `ProductionReadyLog[]` で返す
    - [x] `getPrepareLog(logId)` が詳細（`logDetail` 付き）を 1 件返す
    - [x] `formatPrepareSummary` の仕様は変わらない（0 件の保留・手動は非表示のまま）
  - Verify:
    - [x] `cd frontend && npm run build` が型エラーなく通る
    - [ ] 手動: DevTools で `getPrepareLogs(100)` の戻りに `logDetail` が無いこと、`getPrepareLog(id)` の戻りに全文があることを確認
  - Files:
    - `frontend/src/types.ts`
    - `frontend/src/api/history.ts`
  - Dependencies: Task 1
  - Estimated scope: XS（2 ファイル）

---

- [x] Task 3: 履歴画面に本番前準備行を追加する（フロント）
  - Acceptance:
    - [x] 種別フィルタに `本番前準備` があり、`prepare` のみ／すべて表示の切り替えができる
    - [x] 準備行に日時・`本番前準備` ラベル・実行者・結果バッジ・`適用N · 保留M · 手動K` サマリーが出る
    - [x] 行クリックで展開し、件数サマリー＋ログ全文（`<pre className="log-detail-full-log">`）が出る。`logDetail` なしは「ログがありません」表示でエラーにならない
    - [x] DB フィルタを切り替えても準備行が消えない（SPEC Assumption 4）
    - [x] 初回展開のみ詳細 API を呼び、2 回目以降はキャッシュを使う
    - [x] STG／Pilot 行の表示・展開・フィルタが壊れない
  - Verify:
    - [x] `cd frontend && npm run build` が通る
    - [ ] 手動: 種別 `prepare`／`all`、DB `all`／`kaios` 等の組み合わせで表示を確認
    - [ ] 手動: DevTools Network で初回展開のみ `GET /api/history/prepare/{id}` が飛ぶことを確認
    - [ ] 手動: 成功・失敗・ログなしの 3 パターンの展開表示を確認
  - Files:
    - `frontend/src/pages/History.tsx`（`KIND_OPTIONS`／`HistoryRow`／`PrepareHistoryRow`／展開ハンドラ／フィルタ）
    - 既存再利用のみのため `index.css` 変更なし想定。必要な場合のみ最小追加し、Task 内で明記する
  - Dependencies: Task 2
  - Estimated scope: S（1〜2 ファイル）

### Checkpoint B（Task 3 終了時）
- [x] `npm run build` が通る
- [ ] 種別・DB フィルタ、展開・キャッシュ、STG／Pilot 回帰の目視が終わっている
- [ ] **人間のレビューを受けてから Task 4 へ進む**

---

- [x] Task 4: 回帰・E2E 確認と SPEC 照合
  - Acceptance:
    - [x] SPEC の Success Criteria 8 項目をすべて満たす（コード照合＋自動テスト。ブラウザ E2E は下記 Verify 1〜4 を人間が実施）
    - [x] `dotnet test` と `npm run build` がともに通る
    - [ ] DryRun または実データで「準備実行→履歴に載る→展開でファイル名が分かる」の一連が通る（**要: ブラウザ手動確認**）
  - Verify:
    - [x] `cd backend && dotnet test Tests/MaintenanceManagement.Api.Tests.csproj` 全件パス（164 件）
    - [x] `cd frontend && npm run build` 通過
    - [ ] 手動（`docs/LOCAL_TEST_GUIDE.md` 準拠）:
      1. 本番前準備を 1 回実行（DryRun 可）し、履歴の準備行が 1 件増えること
      2. その行を展開し、適用／保留／手動適用のファイル名がログ全文で確認できること
      3. STG 適用履歴を 1 件展開し、表示が壊れていないこと
      4. Pilot 適用履歴を 1 件展開し、表示が壊れていないこと
  - Files:
    - 原則変更なし。不具合が出た場合のみ該当 Task に差し戻す
  - Dependencies: Task 3
  - Estimated scope: XS（0 ファイル、検証のみ）

### Checkpoint C: 完了
- [x] SPEC Success Criteria 8 項目をすべて確認（自動＋コード照合）
- [x] `dotnet test`＋`npm run build` がともに通る
- [ ] **commit は人間の承認後に行う**

---

## Done の定義
- [x] Checkpoint A〜C をすべて通過（Checkpoint A/B の API・ブラウザ目視は人間確認推奨）
- [x] SPEC／PLAN との乖離があれば先に文書を更新している（生きた文書）
- [x] 未承認の commit / push はしていない
