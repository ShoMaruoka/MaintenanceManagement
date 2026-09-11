# Implementation Plan: 実行履歴への本番前準備ログ表示

対象 SPEC: `docs/prepare-history/SPEC.md`（B案: 既存 `ProductionReadyLog` の再利用）

## Overview

バックエンドに既にあるが画面から見えていない `ProductionReadyLog.LogDetail` を、実行履歴画面の第三の種別 `prepare` として見せる。記録側・スキーマ変更はゼロ。バックエンドで一覧／詳細 API を分離し（Phase 1）、フロントの型・API・履歴行を追加する（Phase 2）。全体で S サイズのタスク 4 本。`plan-issue-8-execution-log` の DeploySession 対応と同一の縦スライス構成。

## Architecture Decisions

### AD1: 一覧は軽量・詳細は全文（SPEC Assumption 2 の具体化）

現状 `GetRecentPrepLogs` は一覧で `LogDetail` 全文を返す。20 件程度でも 1 回の準備ログは数十〜数百行になりうるため、`GetRecentSessions`（LogDetail を含まない）と同様に分離する。

- `GetRecentPrepLogs(limit)` → `LogDetail` なし（`LogId/ExecutedBy/ExecutedAt/AppliedFiles/HeldFiles/Result/ManualFiles` のみ）
- `GetPrepLogById(logId)` → 全カラム＋`LogDetail` 付き（新規）
- `GET /api/history/prepare` → 一覧、`GET /api/history/prepare/{id}` → 詳細（新規）

既存クライアント（ダッシュボードの `lastPrepare`、`Sidebar` の最終準備表示）は件数サマリーしか使わないため影響なし。`GetDashboardStats` の `lastPrepare` 取得は変更しない。

### AD2: スキーマ変更なし・書き込み経路に触らない

`ProductionReadyLog` テーブルは `LogDetail TEXT` を既に持つ（`docs/SPEC.md` §8）。`EnsureCreated`／`InsertProductionReadyLog`／`FastCopyService` には手を入れない。マイグレーション不要、既存 DB を壊さない。

### AD3: DB フィルタは準備行に適用しない（SPEC Assumption 4 の具体化）

`ProductionReadyLog` に `DbName` が無い以上、DB フィルタで除外する根拠が作れない。`History.tsx` の `filtered` では `kind === 'prepare'` の行を DB フィルタから除外し、常に表示する。DB 別の内訳はログ本文（`▶ kaios` 等の `STEP` 行）で確認する運用とする。将来 `DbName` を持たせたくなっても、本件ではやらない。

### AD4: フロントは STG／Pilot の遅延ロードをそのまま踏襲する

- 一覧ロード時：`getSessions(100)`＋`getPilotRuns(100)` に `getPrepareLogs(100)` を追加して並列取得
- 行展開時：初回のみ `getPrepareLog(logId)` を呼び、`detailsFetched` 的なフラグ（`logDetailFetched`）でキャッシュ。以降は再取得しない
- 表示は `StgHistoryRow`／`PilotHistoryRow` と同形の `PrepareHistoryRow` を新設し、`.log-session-detail`／`.log-detail-full-log` を再利用。新規 CSS は作らない
- 結果バッジは `StatusBadge` の `success`／`failed` に寄せる（`Result` が `success` 以外は `failed` 扱い、Pilot と同一ルール）
- 日時整形は `formatExecutedAt` 相当（`MM-DD HH:mm`）に揃える。一覧行のモジュール列には `formatPrepareSummary`（`適用N · 保留M · 手動K`）を使う

### AD5: ログ本文はパースしない

`FastCopyService` の `DETAIL` 行（`→ {file}`／`→ MariaDB/{file}`／`→ {relativePath}`／手動適用行）をそのまま `<pre>` に出す。テーブル化・ハイライトは将来の構造化保存（別 issue）とし、本件では工数を抑える。`LogDetail` が NULL／空の旧行は Pilot と同文言で「ログがありません」表示にする。

## Dependency Graph

```
DatabaseService.GetRecentPrepLogs（軽量化）
    │
    ├── DatabaseService.GetPrepLogById（新規）
    │        │
    │        └── HistoryController GET /prepare, GET /prepare/{id}
    │                 │
    │                 └── api/history.ts getPrepareLogs/getPrepareLog
    │                          │
    │                          ├── types.ts ProductionReadyLog 拡張
    │                          │
    │                          └── History.tsx PrepareHistoryRow
    │                                   │
    │                                   └── 既存 CSS・StatusBadge・formatPrepareSummary 再利用
    │
    └── （テスト）DatabaseServicePrepLogTests（新規）
```

## Implementation Order

1. Phase 1（バックエンド）: Task 1 → Checkpoint A（`dotnet test`＋API 手動確認）
2. Phase 2（フロント）: Task 2 → Task 3 → Checkpoint B（`npm run build`＋画面目視）
3. Task 4（回帰・E2E）で SPEC Success Criteria をすべて確認して完了

Task 2 と Task 1 は型の受け渡しがあるため逐次。Task 2→Task 3 も逐次。バックエンドテスト（Task 1 内）とフロント作業の並行は可能だが、`index.css` 競合はないため実質逐次で進める想定。

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 既存クライアントが `GET /prepare` の全文返却に依存している | Med | 現状の利用はダッシュボード・サイドバーの件数表示のみ（`LogDetail` 未参照）であることを Task 1 着手前に `Grep` で再確認する。依存があれば一覧の SELECT 変更を見送り、詳細追加のみにするフォールバックを持つ |
| `LogDetail` が巨大で詳細 API が重い | Low | 詳細は 1 件取得のみ。STG／Pilot と同じ `<pre>`＋スクロール表示にし、上限対応は将来課題とする（今回スコープ外） |
| 準備 1 回が複数 DB を束ねるため DB フィルタの挙動に違和感が出る | Low | AD3 のとおり常に表示し、行内に `formatPrepareSummary`＋ログ本文の `STEP` 行で内訳を示す。違和感があれば SPEC Open Q1 として人間判断に戻す |
| STG（100 件）／Pilot（100 件）と準備（20 件）で件数感がずれる | Low | 画面側は 100 件取得、一覧 API の既定は 20 維持。`limit` クランプ（1〜500）を Task 1 で明示し、SPEC Open Q2 をここで解決する |
| `Result` に `success`／`failed` 以外の値が入る | Low | Pilot と同一ルール（`failed` 以外は成功扱いせず、`failed` か否かで分岐）ではなく、`StatusBadge` に渡す前に正規化する。実データ確認で未知値があれば Task 3 でフォールバックを足す |

## Verification Checkpoints

### Checkpoint A: バックエンド完了
- [ ] `dotnet test Tests/MaintenanceManagement.Api.Tests.csproj` 全件パス
- [ ] `GET /api/history/prepare?limit=20` に `logDetail` が含まれないこと
- [ ] `GET /api/history/prepare/{id}` に `logDetail` 全文（`DETAIL` 行）が含まれること
- [ ] `GET /api/history/stats` の `lastPrepare` が従来どおり返ること
- [ ] **人間のレビューを受けてから Phase 2 へ進む**

### Checkpoint B: フロント完了
- [ ] `npm run build`（frontend）が型エラーなく通る
- [ ] 種別フィルタ `prepare` で絞り込みが効くこと
- [ ] DB フィルタを切り替えても準備行が消えないこと
- [ ] 初回展開のみ詳細 API を呼び、2 回目は呼ばないこと（DevTools Network で確認）
- [ ] STG／Pilot 行の表示・展開に回帰がないこと
- [ ] **人間のレビューを受けてから Task 4 へ進む**

### Checkpoint C: 完了（Task 4 終了時）
- [ ] SPEC の Success Criteria 8 項目をすべて確認
- [ ] `dotnet test`＋`npm run build` がともに通る
- [ ] `docs/LOCAL_TEST_GUIDE.md` の履歴手順に沿った目視が終わっている
- [ ] **commit は人間の承認後に行う**

## Parallelization

- **逐次必須**: Task 1 → Task 2 → Task 3 → Task 4（API 形状→型→画面→検証の依存）
- **並行可能**: Task 1 のテスト追加と Task 2 の型定義は理論上並行できるが、API 形状確定前の手戻りを避けるため逐次推奨

## Open Questions（PLAN での解決）

- SPEC Open Q2（一覧件数）: 解決。API 既定 `limit=20` 維持、画面側は `getPrepareLogs(100)` で STG／Pilot に揃える。`limit` は `Math.Clamp(limit, 1, 500)` で既存踏襲。
- SPEC Open Q3（サマリー文言）: 解決。`formatPrepareSummary` をそのまま使う（0 件の保留・手動は非表示）。文言変更はしない。
- 新規の未解決事項なし。SPEC Open Q1（DB フィルタ）は AD3 で確定済みだが、目視で違和感があれば人間判断に戻す。
