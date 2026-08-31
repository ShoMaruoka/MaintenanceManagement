# Implementation Plan: v1.5.2 Pilot ログ履歴・ログ領域・画像情報準備の反映

対応する仕様: [`SPEC.md`](./SPEC.md)

関連:

- [`../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md)
- [`../issue35/SPEC.md`](../issue35/SPEC.md)
- [`../issue20/SPEC.md`](../issue20/SPEC.md)
- [`../spec-issue-8-execution-log.md`](../spec-issue-8-execution-log.md)

## Overview

3 つの縦スライスで、既存の Pilot 適用（`WebSourceDeployService` / `WebSourceDeployLog` / 実行履歴）を拡張する。新規テーブルは作らない。

```
スライス A（画像情報準備 → DestWebSourcePath）     スライス B（ログ保存）
カテゴリ単位 robocopy + ログ                      SSE 全文を LogDetail へ
  + Pilot 画面の説明文

スライス C（実行履歴）                             スライス D（UI / 版）
RunId 一覧・詳細 API                               Pilot ログ枠を残り高さに
  + History 種別フィルタ                           版番号 1.5.2
```

## Architecture Decisions

1. **画像情報準備はカテゴリ単位コピー（SPEC 3）**
   - 対象は `ImagePrepareService.AllowedCategories`（`Images` / `news` / `pdf`）のみ。二重定義しない
   - コピー元: `{FilesPath}\{category}` → 先: `{DestWebSourcePath}\{category}`
   - カテゴリにファイルがあるときだけ `RunRobocopyAsync`（既存 `/E`）。空はスキップして理由をログ
   - 3 つとも空なら「画像情報準備の適用対象なし」を1行出す
   - 判定はターゲットループ外でカテゴリごとに1回行い、pilot1/pilot2 で再利用する（現行 `filesPathReady` と同じ）
   - `CommonImagePath` / `DestImagePath` / `FilesDeploy2PrdPath` は触らない
   - `SQL適用のみ` では走らせない（現行どおりターゲットループに入らない）

2. **ログ全文はスキーマ変更なし**
   - `WebSourceDeployLog.LogDetail` に、STG 適用と同じ `timestamp [LEVEL] message` を改行連結して書く
   - 同一 `RunId` の全行に同じ全文を入れる（詳細取得はどの行でも可）
   - 旧行（短い ErrorMessage または null）はマイグレーションしない
   - 蓄積は `WebSourcePrepareController` の SSE 書き出しと同時（`DeployController` の `logLines` と同じ）
   - 一覧 API は `LogDetail` を SELECT しない

3. **履歴は API を分け、画面で混ぜる**
   - 既存 `GET /api/history/sessions` は壊さない
   - 追加: `GET /api/history/pilot-runs` と `GET /api/history/pilot-runs/{runId}`
   - 実行履歴画面が両方を取得し、日時降順で1テーブルに載せる
   - 種別フィルタ（すべて / STG適用 / Pilot適用）はフロントのみ。既定は `すべて`（Open Question 2 の仮定）

4. **Run の集約ルール**
   - 1 Run = 同一 `RunId`
   - `executedAt` = その Run の最古 `ExecutedAt`
   - `result` = 1行でも `failed` なら `failed`、それ以外 `success`
   - `stepLabel` は Mode 集合から決める純関数（テスト可能）
     - `sql` / `sql-dryrun` / `sql-skipped` のみ → `SQLのみ`
     - `web` / `web-dryrun` のみ（sql 行なし） → `Webのみ`
     - それ以外（`both` 系、web+sql 混在） → `両方`
     - 全行が `*-dryrun` ならラベル末尾に `（DryRun）`
   - `summary` は `TargetName` ごと（pilot1 / pilot2 / sql）の成否を並べる

5. **旧ログの表示**
   - `logDetail` が空 → 「ログがありません（v1.5.2 より前の実行は全文未保存）」
   - 値がある（短いエラー文を含む）→ そのまま表示。捏造しない

6. **ログ領域**
   - Pilot 実行中／完了: STG 適用に倣い、ヘッダー・結果・戻るボタン以外を `flex: 1`。`min-height: 320px`。`maxHeight: 400` は外す
   - 履歴の Pilot 詳細だけ `.log-detail-full-log` を高くする（`min(70vh, 720px)`）。STG 詳細は現状維持

7. **テスト**
   - 実 robocopy は回さない。現行どおり DryRun または注入で引数・ログを検証
   - 履歴クエリは `DatabaseServiceDashboardStatsTests` と同じ一時 SQLite
   - TDD: T1 / T3 / T4 は失敗テストを先に書く

8. **変更しないもの**
   - 本番前準備、共通画像、`FilesDeploy2PrdPath`、STG 適用ログ枠、本番前準備ログ枠
   - `WebSourceDeployLog` の列追加
   - git commit / push

## 依存グラフ

```
T1 カテゴリコピー
  └── T2 Pilot 画面説明
T3 SSE 全文保存 ─────────────────────┐
T4 Run 集約クエリ + stepLabel 純関数 ─┼─ T5 History API
                                      └── T6 FE クライアント
                                            └── T7 History UI（T8 と独立）
T8 Pilot ログ枠（T2 後が望ましい。T1 とは独立）
T9 版番号（すべて完了後）
```

既定の実装順（1 エージェント）:

```
T1 → T2 → T3 → T4 → T5 → [Checkpoint A]
                      └── T6 → T7 → T8 → [Checkpoint B] → T9 → Done
```

並行可: T1 ∥ T3。T8 は T2 以降なら T6 と順不同。

## Task List

### Phase 1: 画像情報準備の反映

- [x] Task 1: `Images` / `news` / `pdf` を `DestWebSourcePath` へカテゴリ単位コピー
- [x] Task 2: Pilot 画面の説明を画像情報準備と共通画像で分ける

### Checkpoint: 画像

- [ ] カテゴリにファイルがあるとき、そのフォルダへの robocopy 予定／実行がログに出る
- [ ] 空カテゴリはスキップ理由が出る。共通画像の経路は変わらない
- [ ] `dotnet test` が通る

### Phase 2: ログ保存と履歴 API

- [x] Task 3: SSE 全文を `WebSourceDeployLog.LogDetail` へ保存
- [x] Task 4: RunId 集約の取得と `stepLabel`
- [x] Task 5: `GET /api/history/pilot-runs` / `{runId}`

### Checkpoint: Foundation（A）

- [ ] 新規 Pilot 実行後、DB の `LogDetail` に SSE 相当の全文がある
- [ ] 一覧 API に `logDetail` が無い。詳細 API にはある
- [ ] `dotnet test` が通る
- [ ] 人間レビューしてから History UI へ

### Phase 3: 実行履歴 UI・ログ枠・版

- [x] Task 6: フロントの Pilot Run 型と API クライアント
- [x] Task 7: 実行履歴に Pilot 行と種別フィルタ
- [x] Task 8: Pilot 適用画面のログ領域を広げる
- [x] Task 9: 版番号を 1.5.2 に揃える

### Checkpoint: Complete（B）

- [ ] SPEC の Success Criteria を満たす
- [ ] `dotnet test` / `npm run build`
- [ ] レビュー可能

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `FilesPath` 丸ごとコピーをカテゴリ単位に変えて既存テストが壊れる | Med | T1 で既存 `ExecuteAsync_Files_*` を先に書き換え、意図を固定する |
| `Images\products` で共通画像が後勝ちし、同名ファイルが上書きされる | Low | SPEC どおり。ログで両ステップを区別する。DestImagePath へは二重コピーしない |
| 本番前準備後は `FilesPath` が空 | Med | Open Question 1 は A（載せない）。空なら「適用対象なし」と出す |
| 全文ログで SQLite / レスポンスが肥大 | Low | 一覧は全文なし。上限は設けない（Open Question 3 の仮定） |
| 旧 `LogDetail`（短いエラー）を全文と誤認 | Low | 空は未保存メッセージ。値があればそのまま出す |
| History で STG と Pilot の ID が衝突 | Med | 行キーは `kind + id`（sessionId vs runId）。クリック展開も種別で分岐 |

## Open Questions（SPEC から持ち越し・仮定で進める）

1. 本番前準備後の `FilesDeploy2PrdPath` 再コピー → **A（しない）**
2. 履歴フィルタ既定 → **すべて**
3. ログ文字数上限 → **なし**
