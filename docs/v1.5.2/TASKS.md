# Tasks: v1.5.2 Pilot ログ履歴・ログ領域・画像情報準備の反映

対応する仕様: [`SPEC.md`](./SPEC.md)  
対応する計画: [`PLAN.md`](./PLAN.md)

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。  
git commit / push は行わない。

TDD: T1 / T3 / T4 は **失敗するテストを先に書いてから** 実装する。

---

## 実行順序

```
T1 → T2 → T3 → T4 → T5 → [Checkpoint A]
                      └── T6 → T7 → T8 → [Checkpoint B] → T9 → Done

並行可: T1 ∥ T3。T8 は T2 以降なら T6 と順不同。
```

---

## Task 1: 画像情報準備カテゴリを DestWebSourcePath へコピー

**Status:** done

**Description:**  
Pilot の Files コピーを、`FilesPath` 丸ごと 1 回から、`Images` / `news` / `pdf` のカテゴリ単位に変える。ファイルがあるカテゴリだけ `{FilesPath}\{cat}` → `{DestWebSourcePath}\{cat}` へ robocopy する。共通画像は触らない。

**Acceptance criteria:**

- [x] カテゴリ名は `ImagePrepareService.AllowedCategories` を使う（配列の再定義なし）
- [x] ファイルがあるカテゴリについて、DryRun ログにそのカテゴリの src/dest が含まれる
- [x] 空カテゴリは robocopy せず、スキップ理由がログに出る
- [x] 3 カテゴリとも空なら「画像情報準備の適用対象なし」が出る
- [x] `CommonImagePath` → `DestImagePath` のログ／呼び出しは現状維持
- [x] `FilesDeploy2PrdPath` をコピー元にしない
- [x] 既存 `ExecuteAsync_Files_*` テストを新仕様に合わせて更新し、通す

**Verification:**

- [x] 失敗テストを先に追加して RED を確認する
- [x] `cd backend && dotnet test --filter WebSourceDeployService`

**Dependencies:** None  
**Files likely touched:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`

**Estimated scope:** S

---

## Task 2: Pilot 画面説明を画像情報準備と共通画像で分ける

**Status:** done

**Description:**  
`WebSourcePrepare.tsx` の導入文とコピー元・先表示で、画像情報準備（各フォルダ → `DestWebSourcePath`）と共通画像（`CommonImagePath` → `DestImagePath`）を分けて書く。ロジックは変えない。

**Acceptance criteria:**

- [x] 説明文に画像情報準備の `Images` / `news` / `pdf` → Web ルートが書いてある
- [x] 共通画像は別行／別文で `Images\products` 向けだと分かる
- [x] 既存のパス表示（`filesPath` / `commonImagePath` / 各 dest）は残る

**Verification:**

- [x] `cd frontend && npm run build`

**Dependencies:** Task 1  
**Files likely touched:**

- `frontend/src/pages/WebSourcePrepare.tsx`

**Estimated scope:** XS

---

## Task 3: SSE 全文を WebSourceDeployLog.LogDetail へ保存

**Status:** done

**Description:**  
`WebSourcePrepareController.StreamDeploy` で SSE に出した各行を、`DeployController` と同じ書式で蓄積し、その Run の全 `InsertWebSourceDeployLog` に渡す。スキーマは変えない。

**Acceptance criteria:**

- [x] 蓄積書式は `{timestamp} [{level}] {message}` の改行連結
- [x] 成功・失敗・例外のいずれでも、その Run の全行に同じ全文が入る
- [x] 短い `ErrorMessage` だけを `LogDetail` に書く経路は残さない
- [x] 書式関数は単体テストできる（Controller から切り出す）

**Verification:**

- [x] 書式ヘルパーのテストが通る: `cd backend && dotnet test --filter FormatLog`
- [x] `cd backend && dotnet build`

**Dependencies:** None（T1 と並行可。既定順は T2 の後）  
**Files likely touched:**

- `backend/Controllers/WebSourcePrepareController.cs`
- 書式ヘルパー（既存 Controller 隣、または小さな static クラス）
- `backend/Tests/Controllers/`（新規テスト）

**Estimated scope:** S

---

## Task 4: Pilot Run の集約取得と stepLabel

**Status:** done

**Description:**  
`WebSourceDeployLog` を `RunId` で束ねて一覧／詳細を返す。一覧は `LogDetail` を読まない。`stepLabel` は Mode 集合からの純関数にする。

**Acceptance criteria:**

- [x] 一覧: runId, dbName, executedAt（最古）, executedBy, stepLabel, result, summary。`logDetail` なし
- [x] result は1行でも failed なら failed
- [x] 詳細: 上記 + targets[] + logDetail（その Run のいずれかの行。同一全文）
- [x] 存在しない runId は null（Controller が 404）
- [x] stepLabel: sql 系のみ → SQLのみ / web 系のみ → Webのみ / 他 → 両方。全行 dryrun なら `（DryRun）` 付き
- [x] 既存の dashboard 集計クエリは変えない

**Verification:**

- [x] 失敗テストを先に書く
- [x] `cd backend && dotnet test --filter PilotRun`

**Dependencies:** None（T3 と独立。既定順は T3 の後）  
**Files likely touched:**

- `backend/Models/`（PilotRun 用 DTO 新規）
- `backend/Services/DatabaseService.cs`
- `backend/Controllers/WebSourceDeployLogMode.cs` または隣接の stepLabel 純関数
- `backend/Tests/Services/`（一時 SQLite。`DatabaseServiceDashboardStatsTests` と同じ型）

**Estimated scope:** M

---

## Task 5: History API に pilot-runs を追加

**Status:** done

**Description:**  
`HistoryController` に一覧と詳細を追加する。既存の sessions / prepare / stats は壊さない。

**Acceptance criteria:**

- [x] `GET /api/history/pilot-runs?limit=100`（limit は 1〜500 に clamp。sessions と同じ）
- [x] `GET /api/history/pilot-runs/{runId}` — 無ければ 404
- [x] 一覧 JSON に `logDetail` が無い
- [x] 詳細 JSON に `logDetail` と `targets` がある

**Verification:**

- [x] `cd backend && dotnet build`

**Dependencies:** Task 4  
**Files likely touched:**

- `backend/Controllers/HistoryController.cs`

**Estimated scope:** XS

---

## Checkpoint A: After Tasks 1–5

- [x] `dotnet test` が通る
- [x] `dotnet build` が通る
- [x] カテゴリコピーのログが期待どおり（T1）
- [ ] 新規実行分の `LogDetail` に全文が入る（T3。手動または DB 確認）
- [x] 一覧／詳細 API の形が SPEC 1.3 と一致する
- [ ] 人間レビューしてからフロントへ

---

## Task 6: フロントの Pilot Run 型と API クライアント

**Status:** done

**Description:**  
`types.ts` と `api/history.ts` に Pilot Run の型と `getPilotRuns` / `getPilotRun` を追加する。日時整形は既存 `formatDateTime` / `formatExecutedAt` を流用する。

**Acceptance criteria:**

- [x] 一覧・詳細の型が API JSON（camelCase）と一致する
- [x] `getPilotRuns(limit)` / `getPilotRun(runId)` がある
- [x] 既存の sessions / stats クライアントは壊さない

**Verification:**

- [x] `cd frontend && npm run build`

**Dependencies:** Task 5  
**Files likely touched:**

- `frontend/src/types.ts`
- `frontend/src/api/history.ts`

**Estimated scope:** S

---

## Task 7: 実行履歴に Pilot 行と種別フィルタを載せる

**Status:** done

**Description:**  
`History.tsx` で STG セッションと Pilot Run を混ぜて表示する。種別フィルタ（すべて / STG適用 / Pilot適用）を追加する。Pilot 行の展開でターゲット成否とログ全文を出す。

**Acceptance criteria:**

- [x] 既定フィルタは「すべて」。日時降順の1テーブル
- [x] Pilot 行の「モジュール」列は `Pilot適用（kaios）` など SPEC 1.4 の要約
- [x] 行キーは種別＋ id（sessionId と runId を混同しない）
- [x] Pilot 展開: ターゲット別成否 + ログ。空なら「ログがありません（v1.5.2 より前の実行は全文未保存）」
- [x] Pilot 詳細ログは STG より高い（`min(70vh, 720px)` 目安）。STG の `.log-detail-full-log` 高さは変えない
- [x] DB フィルタは Pilot 行にも効く
- [x] STG 行の既存展開（モジュール表 + ログ）は維持

**Verification:**

- [x] `cd frontend && npm run build`
- [ ] 手動: フィルタ切替、STG / Pilot それぞれの展開、旧データ相当（logDetail 空）の文言

**Dependencies:** Task 6  
**Files likely touched:**

- `frontend/src/pages/History.tsx`
- `frontend/src/index.css`

**Estimated scope:** M

---

## Task 8: Pilot 適用画面のログ領域を広げる

**Status:** done

**Description:**  
実行中／完了のログ枠から `maxHeight: 400` を外し、STG 適用と同様に残り高さを使う。自動スクロールは維持する。本番前準備のログ枠は触らない。

**Acceptance criteria:**

- [x] ログカードが縦フレックスで、ログ本体が `flex: 1` かつ `min-height: 320px`
- [x] 完了時にターゲット別結果が出ても、ログが 400px で頭打ちにならない
- [x] 末尾追従は維持
- [x] `PrepareForPrd.tsx` のログ枠は変更しない

**Verification:**

- [x] `cd frontend && npm run build`
- [ ] 手動: 実行中・完了の両方でログ領域が画面の残り高さを使う

**Dependencies:** Task 2（説明と同時に触ると衝突しやすい。T2 後）  
**Files likely touched:**

- `frontend/src/pages/WebSourcePrepare.tsx`
- `frontend/src/index.css`（クラスを切る場合）

**Estimated scope:** S

---

## Checkpoint B: After Tasks 6–8

- [x] `npm run build` が通る
- [ ] 実行履歴で Pilot 過去分のログが見える（新規実行分）
- [ ] Pilot 適用画面のログが広い
- [ ] STG 履歴・本番前準備ログ枠に回帰がない
- [ ] 人間レビュー

---

## Task 9: 版番号を 1.5.2 に揃える

**Status:** done

**Description:**  
`docs/VERSIONING_GUIDE.md` どおり、リポジトリ内の版番号 2 箇所を `1.5.2` にする。タグ打ち・commit はしない。

**Acceptance criteria:**

- [x] `backend/MaintenanceManagement.Api.csproj` の `<Version>` が `1.5.2`
- [x] `frontend/package.json` の `"version"` が `1.5.2`
- [x] コメントの旧版参照があれば直す

**Verification:**

- [x] `cd backend && dotnet build`
- [x] `cd frontend && npm run build`

**Dependencies:** Tasks 1–8  
**Files likely touched:**

- `backend/MaintenanceManagement.Api.csproj`
- `frontend/package.json`

**Estimated scope:** XS

---

## Checkpoint: Complete

- [ ] SPEC Success Criteria をすべて満たす
- [ ] `dotnet test` / `npm run build`
- [x] 版表示が 1.5.2
- [ ] レビュー可能（commit / push なし）
