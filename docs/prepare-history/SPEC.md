# Spec: 実行履歴への本番前準備ログ表示（prepare-history）

## Objective

本番前準備画面（`frontend/src/pages/PrepareForPrd.tsx`）は実行直前の選択一覧を見せるが、実行後は `deployed/` が空になり「今回どれを本番適用対象として送ったか」を後から確認できない。`backToSelect()` が `logLines` をクリアして再読込するためである。

本件では、バックエンドに既に永続化されている `ProductionReadyLog`（件数＋`LogDetail` 全文）を実行履歴画面（`frontend/src/pages/History.tsx`）に載せ、不具合調査・監査証跡として「いつ・誰が・何件・何を」適用したかを事後確認できるようにする。

### 対象ユーザー
本番前準備を実行・確認する運用担当者（admin / user 全員）。

### 成功の姿
- 実行履歴で種別 `本番前準備` を選択し、過去の準備実行（日時・実行者・適用／保留／手動件数・結果）の一覧を見られる。
- 行クリックで展開し、その回に適用／保留／消化したファイル名（SQL・画像・手動適用）を含むログ全文を見られる。
- 本番前準備画面に戻っても、履歴側に証跡が残るため「今回どれを適用したか」が失われない。
- STG 適用・Pilot 適用の既存履歴表示は壊れない。

### 採用方針（B案: 既存 `ProductionReadyLog` の再利用）
新規テーブル・新規記録処理は作らない。`FastCopyService.ExecuteAsync` が既に `DETAIL` 行ごとにファイル名を `LogDetail` に残し、`DatabaseService.InsertProductionReadyLog` で保存している。これを正とし、フロント表示だけを追加する。

検討した他案と判断:

| 案 | 内容 | 判断 |
|----|------|------|
| **B（採用）** | 実行履歴に `ProductionReadyLog` を表示 | 永続的・監査可能。バックエンド貯金（`GET /api/history/prepare`、`GetRecentPrepLogs`）の未利用分を使うだけで最小工数 |
| A | 完了画面に適用一覧を残すだけ（メモリ表示） | 却下ではないが単独では不十分。リロードで消えるため恒久対策にならない。本件のスコープ外（必要なら別途追加） |
| C | TSVコピー／履歴リンク付き | B の拡張として将来検討。今回は `logId` 返却・TSV 生成はやらない |
| D | 適用ファイルの構造化保存（JSON カラム新設） | 将来の見やすさ改善案。今回はテキスト `LogDetail` の `<pre>` 表示で成立するため見送り |

### スコープ外（今回やらないこと）
- `PrepareForPrd.tsx` の完了画面改修（適用一覧の保持・TSV・履歴リンク）
- `ProductionReadyLog` のスキーマ変更（カラム追加・JSON 化）
- `FastCopyService`／`Deploy2Prd` 移動ロジックの変更
- ダッシュボード・サイドバーの最終準備サマリーの変更（現状維持）
- Pilot／STG 履歴の表示仕様変更

## ASSUMPTIONS I'M MAKING

1. **表示専用の追加であり、記録側は変えない。** `InsertProductionReadyLog`／`FastCopyService` のログ文面は現状のまま正とする。文面改善が必要になっても別 issue とする。
2. **一覧 API は `LogDetail` を含めず、詳細 API で全文を返す。** `plan-issue-8-execution-log`（DeploySession の LogDetail 対応）と同一パターン。現状 `GetRecentPrepLogs` は一覧で全文を返すが、20〜100 件取得時の肥大化を避けるため分離する。
3. **過去の実行分は遡及生成しない。** `LogDetail` が NULL の行は「ログがありません」表示にする（v1.5.2 以前の Pilot と同扱い）。
4. **本番前準備ログは DB 横断の 1 実行＝1 行である。** `ProductionReadyLog` に `DbName` が無いため、履歴の DB フィルタ（kaios/gos/paf/duskin）は準備行に適用しない（常に表示）。種別フィルタ（`all/stg/pilot/prepare`）でのみ絞り込む。DB 別の内訳はログ本文で確認する。
5. **ログ本文は `<pre>` 全文表示で十分とする。** `DETAIL` 行に `→ {fileName}`／`→ MariaDB/{file}`／`→ {relativePath}`／手動適用行が既に含まれるため、パースしてテーブル化しなくても「どれを適用したか」は判別できる。
6. **認証・権限は既存の履歴画面と同一。** admin / user の区別は変えない。
7. **git commit / push は行わない。** 人間の承認があるまで作業ツリーに留める。

→ 誤りがあれば訂正してください。特に 2, 4, 5 は UI 仕様に直結します。

## Core Features

| ID | 機能名 | 説明 |
|----|--------|------|
| P1 | 準備ログ一覧取得の軽量化 | `GetRecentPrepLogs` を `LogDetail` なしにし、`GetPrepLogById`（`LogDetail` 付き）を新設。`GET /api/history/prepare/{id}` を追加 |
| P2 | フロント型・API 追加 | `ProductionReadyLog` に `logDetail?` 追加、`getPrepareLogs()`／`getPrepareLog(id)` 追加、`formatPrepareSummary` は再利用 |
| P3 | 履歴画面への準備行追加 | `KIND_OPTIONS` に `prepare` 追加、`HistoryRow` に `prepare` 種別追加、展開で件数サマリー＋`<pre>` ログ全文表示。既存 CSS（`.log-session-detail`／`.log-detail-full-log`）を再利用 |

## Tech Stack

既存構成を踏襲。新規依存の追加なし。

- Backend: ASP.NET Core 8 / C#、`Microsoft.Data.Sqlite`（既存）
- Frontend: React 18 + TypeScript + Vite
- Test: xUnit（`backend/Tests/MaintenanceManagement.Api.Tests.csproj`）

## Commands

```
Backend Build:  cd backend && dotnet build MaintenanceManagement.Api.csproj
Backend Test:   cd backend && dotnet test Tests/MaintenanceManagement.Api.Tests.csproj
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## Project Structure

```
backend/
  Controllers/HistoryController.cs   → GET /prepare 一覧＋ GET /prepare/{id} 詳細を追加
  Services/DatabaseService.cs        → GetRecentPrepLogs 軽量化＋ GetPrepLogById 新設
  Models/PrepareModels.cs            → ProductionReadyLog（変更なし想定。必要なら注釈のみ）
  Tests/Services/DatabaseServicePrepLogTests.cs（新規） → 一覧／詳細の回帰テスト
frontend/src/
  types.ts                           → ProductionReadyLog に logDetail?/detailsFetched? を追加
  api/history.ts                     → getPrepareLogs()/getPrepareLog()＋型追加
  pages/History.tsx                  → KIND_OPTIONS/HistoryRow/PrepareHistoryRow 追加
  index.css                          → 変更なし想定（既存クラス再利用）
docs/prepare-history/
  SPEC.md                            → 本仕様書
  PLAN.md                            → 実装計画
  TASKS.md                           → 実行タスク分解
```

## Code Style

既存の STG／Pilot 履歴の遅延ロードパターンを踏襲する。一覧では件数だけ、展開時に詳細を取得してキャッシュする。

**Backend** — パラメータ化クエリ＋`IF NOT EXISTS` 不要（スキーマ変更なし）。一覧は軽量、詳細のみ全文：

```csharp
cmd.CommandText = """
    SELECT LogId, ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, ManualFiles
    FROM ProductionReadyLog ORDER BY LogId DESC LIMIT $limit;
    """;
cmd.Parameters.AddWithValue("$limit", limit);
```

```csharp
cmd.CommandText = """
    SELECT LogId, ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, LogDetail, ManualFiles
    FROM ProductionReadyLog WHERE LogId = $logId;
    """;
```

**Frontend** — `StgHistoryRow`／`PilotHistoryRow` と同形の行コンポーネントを追加し、既存クラスを再利用する：

```tsx
<div className="log-session-detail">
  <div className="log-detail-title">本番前準備詳細</div>
  <div>{formatPrepareSummary(log)}</div>
  {log.logDetail
    ? <pre className="log-detail-full-log">{log.logDetail}</pre>
    : <div>ログがありません</div>}
</div>
```

命名は `PrepareHistoryRow`、`isPrepareDetail`、`handleExpandPrepare` とし、`StatusBadge` の `success`／`failed`  mapping は Pilot に合わせる。

## Testing Strategy

- **バックエンド単体テスト（xUnit / backend/Tests）**
  - `InsertProductionReadyLog` 後に一覧取得で `LogDetail` が含まれないこと
  - 詳細取得で `LogDetail` 全文が返ること（`DETAIL` 行を含む）
  - `LogDetail` が NULL の旧行でも一覧・詳細が例外なく返り、マッピングで null になること
  - `limit` クランプ（1〜500）が効くこと
  - 既存の `DatabaseServiceDashboardStatsTests`／`DatabaseServicePilotRunTests` と同様、一時 SQLite ファイルで完結させる
- **フロントエンド**: `npm run build` の型チェック＋手動確認（`docs/LOCAL_TEST_GUIDE.md` の履歴手順に準拠）
  - 種別フィルタ `prepare` で準備行のみに絞れること
  - DB フィルタを変えても準備行が消えないこと（Assumption 4）
  - 初回展開時のみ詳細 API を呼び、2 回目はキャッシュを使うこと
  - STG／Pilot 行の表示・展開が壊れていないこと
- **回帰**: `dotnet test` 全件パス。`PrepareController`／`FastCopyService` の挙動は変えないため DryRun 実行の回帰は Task 4 の目視のみ。

## Boundaries

- **Always:**
  - 一覧 API に `LogDetail` 全文を含めない（レスポンス肥大化を避ける）
  - `LogDetail` なしの旧行でもエラーにせず「ログがありません」表示にする
  - 既存の CSS クラス（`.log-session-detail`、`.log-detail-full-log`）を再利用し、新規スタイルは作らない（必要な場合のみ最小追加）
  - 変更後は `dotnet test` と `npm run build` を通す
- **Ask first:**
  - `ProductionReadyLog` のスキーマ変更（JSON カラム・`DbName` 追加等）が必要になった場合
  - DB フィルタで準備行を除外する仕様に変える場合（今回は常に表示で確定）
  - ログ本文のパース・テーブル化に踏み込む場合（今回は `<pre>` で確定）
- **Never:**
  - `FastCopyService` の移動・削除ロジックの変更
  - `ProductionReadyLog` への書き込み内容・タイミングの変更
  - 未承認での commit / push
  - STG／Pilot 履歴の一覧集計テキストの変更

## Success Criteria

- [x] 実行履歴の種別フィルタに `本番前準備` があり、選択すると準備実行のみに絞れる
- [x] 準備行に日時・実行者・結果バッジ（成功／失敗）・`適用N · 保留M · 手動K` サマリーが表示される
- [x] 行クリックで展開し、その回のログ全文（適用／保留／手動適用の `DETAIL` 行を含む）が表示される
- [x] `LogDetail` が NULL の行では「ログがありません」と表示され、エラーにならない
- [x] 2 回目以降の展開では詳細 API を再呼び出ししない（キャッシュ利用）
- [x] DB フィルタを変更しても準備行が消えない（または仕様変更時は SPEC 更新済み）
- [x] STG／Pilot 行の表示・展開・フィルタに回帰がない（コード上 STG／Pilot 行は未変更。ブラウザ目視は Task 4 Verify 参照）
- [x] `dotnet test` 全件パス（164 件）、`npm run build` が通る

## Open Questions

1. DB フィルタ適用外（常に表示）で運用上問題ないか。準備 1 回が複数 DB を束ねるため現状案は「絞らない」だが、特定 DB の担当者がノイズに感じる場合は将来 `DbName` 保持を検討する（今回はやらない）。
2. 一覧の既定 `limit` を 20 のままにするか、STG（100）／Pilot（100）に揃えるか。PLAN で確定する（既定は 20 維持＋画面側 100 件取得を想定）。
3. 失敗時の `held/manual` が 0 でもサマリーに載せるか。現行 `formatPrepareSummary`（0 の保留・手動は非表示）をそのまま使う想定だが、文言確定は PLAN で行う。
