# Spec: v1.5.2 Pilot 適用ログ履歴・ログ領域拡大・画像情報準備ファイルの Pilot 反映

## Objective

v1.5.1 までの Pilot 環境適用について、運用担当が次の3点を満たせるようにする。

1. **実行履歴から過去の Pilot 適用ログを参照できる**（実行画面を離れたあとも、その回の SSE 全文が残る）
2. **Pilot 適用中／完了後のログ表示領域を広げる**（robocopy の行が多いときに 400px では不足）
3. **画像情報準備で置いたファイルが Pilot 環境に載る**（現状「載らない」と報告されている不具合の解消）

- **対象ユーザー**: 運用担当者（既存の「Pilot環境適用」「実行履歴」「画像情報準備」を使う人）
- **対象システム**: Pilot がある **kaios / gos** のみ（paf / duskin は Pilot 対象外のまま）
- **版番号**: `1.5.1` → **`1.5.2`**（PATCH。バグ修正＋ UI 調整。ユーザー指定）

### 成功の定義

- Pilot 適用を1回実行したあと、実行履歴からその Run を開き、実行中に見えていたログ全文を再表示できる
- Pilot 適用画面のログ領域が、STG 適用のログビューと同程度に画面の残り高さを使う
- 画像情報準備のアップロード先（`FilesPath` 配下の `Images` / `news` / `pdf`）が、Web ソースコピー対象の Pilot 適用時に各 `DestWebSourcePath` へ同じフォルダ名で載る。共通画像は現状どおり動いており本版では変えない。コピー／スキップの理由はログに残る

---

## ASSUMPTIONS I'M MAKING

実装に入る前に、次を仮定する。誤りがあれば訂正すること。

1. **「実行履歴から過去分を見たい」** は、既存メニュー「実行履歴」（`/history`）に Pilot 適用 Run を載せる意味である。Pilot 適用画面内に過去ログ一覧を新設しない。
2. **保存するログ** は、実行中 SSE に流した行と同一の全文である（レベル・時刻・メッセージ）。現状 `WebSourceDeployLog.LogDetail` に入っている短い `ErrorMessage` だけでは足りない。
3. **1.5.2 より前の Pilot Run** は全文を持たない。展開時は「ログなし（この実行より前は全文未保存）」または当時の短いエラー文を出す。遡及生成しない。
4. **ログ領域を広げる** 対象は Pilot 適用画面の実行中／完了ログである。STG 適用・本番前準備のログ枠は変えない。実行履歴の Pilot 詳細ログも、現行 STG 詳細（`max-height: 260px`）より広くする。
5. **画像情報準備の正（ユーザー確認済み 2026-08-31）**: アップロード先の各フォルダ（`FilesPath\Images` / `news` / `pdf`）を、各 Pilot の **`DestWebSourcePath` 直下**へ同じフォルダ名でコピーする。`CommonImagePath` → `DestImagePath` は適用できていたので **変更しない**。`FilesDeploy2PrdPath` は Pilot のコピー元に戻さない。
6. **「載らない」の切り分け**（実装時にログとテストで確定する）:
   - コード上は Issue #35 で `FilesPath` 全体 → `DestWebSourcePath` の robocopy がある。空ならスキップ成功
   - 空スキップ、`step=sql`、本番前準備後の空 `FilesPath`、またはカテゴリ単位で見えない、が候補
   - 本版ではカテゴリ（`Images` / `news` / `pdf`）ごとにコピーし、空カテゴリは個別スキップしてログに出す（ユーザー表現「各フォルダを DestWebSourcePath に適用」に合わせる）
7. **本番前準備済みファイルを Pilot に載せる再適用** は、Issue #35 の「同一モジュール再適用は想定しない」を維持する（Open Question 1。未回答のため仮定のまま）。
8. git commit / push は行わない（ユーザー指示があるまで）。

→ 訂正がなければこの仮定で PLAN に進む。

---

## Tech Stack

既存のまま。新規依存なし。

| レイヤー | 技術 |
|---------|------|
| バックエンド | ASP.NET Core 8、SQLite（`WebSourceDeployLog`） |
| フロントエンド | React 18 + TypeScript + Vite |
| 進捗 | 既存 SSE |
| テスト | xUnit（`backend/Tests`）、フロントは `npm run build` |

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Test:   cd backend && dotnet test
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

版番号は次の2ファイルを `1.5.2` に揃える（`docs/VERSIONING_GUIDE.md`）。

- `backend/MaintenanceManagement.Api.csproj` の `<Version>`
- `frontend/package.json` の `"version"`

---

## Project Structure

```
backend/
  Controllers/HistoryController.cs          → Pilot Run 一覧・詳細 API
  Controllers/WebSourcePrepareController.cs → SSE 全文を蓄積し WebSourceDeployLog.LogDetail へ保存
  Services/DatabaseService.cs               → RunId 単位の一覧／詳細取得
  Services/WebSourceDeployService.cs        → Files コピーの明示ログ（件数・元・先）
  Tests/                                    → ログ保存・Files コピー回帰
frontend/src/
  pages/History.tsx                         → 種別フィルタ＋ Pilot 行の展開
  pages/WebSourcePrepare.tsx                → ログ領域を viewport 残り高さに
  api/history.ts / types.ts                 → Pilot Run 型
  index.css                                 → ログ領域・履歴詳細の高さ
docs/v1.5.2/                                → 本 SPEC / 後続 PLAN・TASKS
```

---

## Code Style

既存パターンに合わせる。C# は PascalCase、JSON は camelCase。ログ文言は日本語。

```csharp
// 実行終了後、同一 RunId の各行に同じ全文を書く（詳細 API はどの行からでも取れる）
_db.InsertWebSourceDeployLog(runId, config.Name, r.TargetName, mode, executedBy,
    r.Success ? "success" : "failed", fullLog);
```

```tsx
{/* STG 適用 LogViewer と同様、ヘッダー以外の残り高さをログに使う */}
<div
  ref={logRef}
  className="pilot-run-log"
  style={{ flex: 1, minHeight: 320, overflowY: 'auto' }}
>
```

---

## 機能仕様 1: Pilot 適用ログを実行履歴から参照する

### 1.1 As Is

| 項目 | 現状 |
|------|------|
| 実行中ログ | SSE で Pilot 画面にだけ表示。画面を離れると消える |
| `WebSourceDeployLog` | 書き込み済み。`LogDetail` には **ターゲットの短い ErrorMessage** だけ入る（成功時は null） |
| 実行履歴画面 | `DeploySession`（STG 適用）のみ。Pilot Run は出ない |
| ダッシュボード | Pilot 最終成功日時のみ（ログ本文なし） |

Issue #8 は STG 適用（`DeploySession.LogDetail`）だけが対象。Pilot は対象外のまま。

### 1.2 保存

- `WebSourcePrepareController.StreamDeploy` で SSE に出した各行を、STG 適用と同様に全文蓄積する
- フォーマット: 実行画面と同じ `timestamp [LEVEL] message` を改行連結
- 実行終了後（成功／失敗／例外）に、その Run の `InsertWebSourceDeployLog` へ **同一の全文** を渡す
- スキーマ変更はしない（既存 `LogDetail TEXT` を全文用途に使う）
- 一覧 API は `LogDetail` を返さない（Issue #8 と同じ。レスポンス肥大化防止）
- 詳細 API のみ全文を返す
- 既存行（短い ErrorMessage だけ）はマイグレーションしない

### 1.3 履歴 API

`HistoryController` に追加する。既存の sessions / prepare / stats は壊さない。

| メソッド | パス | 内容 |
|---------|------|------|
| GET | `/api/history/pilot-runs?limit=100` | RunId で束ねた一覧。`LogDetail` なし |
| GET | `/api/history/pilot-runs/{runId}` | ターゲット行＋ SQL 行＋ `logDetail` |

一覧の1件（Run）:

| フィールド | 意味 |
|-----------|------|
| runId | 既存 GUID |
| dbName | kaios / gos |
| executedAt | その Run の最古 `ExecutedAt` |
| executedBy | 実行者 |
| stepLabel | Mode から表示用（両方 / Webのみ / SQLのみ / DryRun 等） |
| result | 1行でも `failed` なら `failed`、それ以外 `success` |
| summary | 例: `pilot1✓  pilot2✓  SQL–` |

詳細は上記＋ `targets[]`（targetName, result, mode）＋ `logDetail`。

### 1.4 実行履歴 UI

- 既存の STG セッション一覧を残す
- **種別フィルタ**を追加: `すべて` / `STG適用` / `Pilot適用`（既定は `すべて`）
- 一覧は日時降順。Pilot 行は「モジュール」列に `Pilot適用（kaios）` のような要約を出す
- Pilot 行クリックで展開: ターゲット別成否＋ログ本文（`<pre>`、STG の `log-detail-full-log` を流用し高さを広げる）
- 全文が無い旧データ: 「ログがありません（v1.5.2 より前の実行は全文未保存）」
- DB フィルタ（kaios / gos / …）は Pilot 行にも適用する。paf / duskin の Pilot 行は元々無い

---

## 機能仕様 2: Pilot 適用画面のログ領域を広げる

### 2.1 As Is

`WebSourcePrepare.tsx` のログ枠: `minHeight: 240` / `maxHeight: 400`。本番前準備画面と同じ固定高さ。

STG 適用は `height: calc(100vh - 52px - 40px)` で残り高さを使う。

### 2.2 To Be

- 実行中・完了のログカードを縦フレックスにし、**ビューポートの残り高さ**をログに割り当てる
  - 目安: ヘッダー・結果サマリー・戻るボタンを除いた領域。`min-height: 320px`
  - 完了時に「ターゲット別結果」が出ても、ログが 400px で打ち切られないこと
- 自動スクロール（末尾追従）は維持
- 実行履歴の Pilot 詳細ログ: `.log-detail-full-log` を Pilot 展開時だけ高くする（目安 `min(70vh, 720px)`）。STG セッション詳細の高さは変えない

---

## 機能仕様 3: 画像情報準備ファイルの Pilot 反映

### 3.1 確定した期待（2026-08-31）

共通画像は **すでに Pilot に載っている**。今回載っていなかったのは画像情報準備のファイルである。

| アップロード先（画像情報準備） | Pilot でのコピー先 |
|------------------------------|-------------------|
| `{FilesPath}\Images\...` | `{DestWebSourcePath}\Images\...` |
| `{FilesPath}\news\...` | `{DestWebSourcePath}\news\...` |
| `{FilesPath}\pdf\...` | `{DestWebSourcePath}\pdf\...` |

相対パスは維持する（例: `Images/flash/img/a.png` → `{DestWebSourcePath}\Images\flash\img\a.png`）。

### 3.2 経路の整理

| 系統 | コピー元 | コピー先 | 本版 |
|------|----------|----------|------|
| 画像情報準備 | `FilesPath\{Images\|news\|pdf}` | （保管のみ） | 変更なし |
| Pilot Files（不具合対象） | 各カテゴリフォルダ | 各 `DestWebSourcePath\{カテゴリ}` | **載るようにする** |
| 共通画像 | `CommonImagePath` | 各 `DestImagePath`（`Images\products`） | **変更なし（適用済み）** |
| 本番前準備 | `FilesPath` から移動 | `FilesDeploy2PrdPath` | 変更なし。Pilot は見ない |

現行実装は `FilesPath` 丸ごと → `DestWebSourcePath` の1回 robocopy。空ならスキップ。  
本版は **カテゴリごとに** コピーする（空カテゴリはスキップ、ファイルがあるカテゴリだけ robocopy）。結果は同じ相対配置になるが、ログで「Images はコピー / news は空」が分かる。

順序（Web ソースコピー対象時、ターゲットごと）:

1. `WebSourcePath` → `DestWebSourcePath`（既存）
2. **`FilesPath\{Images,news,pdf}` → `DestWebSourcePath\{同名}`（本版で明示化）**
3. `CommonImagePath` → `DestImagePath`（既存・後勝ち。`Images\products` で名前が重なった場合のみ共通画像が上書き）

`両方` / `Webソースコピーのみ` で 2 を実行する。`SQL適用のみ` では実行しない。

### 3.3 本版でやること

1. カテゴリごとに「画像情報準備 Files: `{category}` → `{DestWebSourcePath}\{category}`」を STEP/INFO で出す
2. 空カテゴリは「スキップ（ファイルなし）」と理由を出す。3カテゴリとも空なら、まとめて「画像情報準備の適用対象なし」と出す
3. コピー成功時は robocopy 終了コードと、そのカテゴリのファイル件数を出す
4. Pilot 適用画面の説明で、画像情報準備（各フォルダ → `DestWebSourcePath`）と共通画像を分けて書く
5. カテゴリにファイルがあるのにスキップ／未コピーになる経路があれば修正する（回帰テスト必須）
6. `CommonImagePath` / `DestImagePath` / `FilesDeploy2PrdPath` は触らない

### 3.4 やらないこと

- 共通画像のコピー元・先を変えること
- 画像情報準備ファイルを `DestImagePath` へ二重コピーすること（`Images\products` 配下は `DestWebSourcePath\Images\products` に相対パスどおり載る）
- `FilesDeploy2PrdPath` を Pilot のコピー元にすること（Open Question 1 が B になるまで）

---

## Testing Strategy

| レベル | 対象 | 手段 |
|--------|------|------|
| ユニット | SSE 相当の行を蓄積した文字列が `InsertWebSourceDeployLog` に渡る | Controller または蓄積ヘルパーのテスト。実 SSE は必須にしない |
| ユニット | RunId 一覧／詳細。一覧に `LogDetail` が無いこと | `DatabaseService` テスト |
| ユニット | `Images` / `news` / `pdf` のいずれかにあるときそのカテゴリの robocopy が走る／空カテゴリはスキップ。共通画像の引数は変えない | 既存 `WebSourceDeployServiceSqlSourceTests` を拡張 |
| ビルド | 参照切れなし | `dotnet build` / `dotnet test` / `npm run build` |
| 手動 | DryRun またはローカルダミー | 下記 Success Criteria |

カバレッジ数値ゲートは設けない。

---

## Boundaries

- **Always**
  - Pilot ログ全文は実行終了時に DB へ残す。一覧 API に全文を載せない
  - 旧 Run を捏造しない
  - 画像情報準備の Pilot コピーは `FilesPath` の `Images` / `news` / `pdf` → 各 `DestWebSourcePath` 同名フォルダ。共通画像は触らない
  - robocopy は `/E` のみ（`/MIR` 禁止）
  - 版番号 3 箇所のうちリポジトリ内 2 箇所を `1.5.2` に揃える（タグはリリース時）
  - 変更後は `dotnet test` と `npm run build`
- **Ask first**
  - 本番前準備後の `FilesDeploy2PrdPath` を Pilot にも載せるか（Open Question 1）
  - `WebSourceDeployLog` のスキーマ追加（ErrorMessage 列など）
  - 実行履歴を STG / Pilot 以外（本番前準備）まで広げること
- **Never**
  - 共通画像パスの差し替えを無断でやらない
  - 本番前準備・STG 適用のログ枠を本版で広げない
  - 秘密情報を SPEC / コミットに載せない
  - git commit / push（指示があるまで）

---

## Success Criteria

- [ ] Pilot 適用（成功・失敗どちらでも）のあと、実行履歴でその Run を開くと、実行画面と同じ内容のログ全文が見える
- [ ] 実行履歴の一覧 API レスポンスにログ全文が含まれない
- [ ] v1.5.2 より前の Pilot 行を開いても落ちず、全文なしと分かる
- [ ] Pilot 適用画面のログ領域が、結果サマリー表示中も画面の残り高さを使い、400px で頭打ちにならない
- [ ] 画像情報準備の `Images` / `news` / `pdf` に置いたファイルが、`両方` または `Webソースコピーのみ` 実行時に各 `DestWebSourcePath` の同名フォルダへ相対パスどおりコピーされる（DryRun ならその robocopy 予定がログに出る）
- [ ] 空カテゴリはスキップ理由がログに出る。3つとも空なら「適用対象なし」と分かる
- [ ] 共通画像（`CommonImagePath` → `DestImagePath`）の挙動は 1.5.1 から変わらない
- [ ] 画面説明で画像情報準備と共通画像のコピー先が区別できる
- [ ] `dotnet test` と `npm run build` が通る
- [ ] フロント／バックの版表示が `1.5.2` で一致する

---

## Open Questions

1. **本番前準備のあと**に Pilot 適用した場合、`FilesPath` は空（ファイルは `FilesDeploy2PrdPath` へ移動済み）になる。このとき画像情報準備済みファイルを Pilot に載せるか？
   - **A（仮定・Issue #35 維持）**: 載せない。準備後の再適用は想定しない。空ならスキップし、理由をログに出す
   - **B**: `FilesPath` が空でも `FilesDeploy2PrdPath` から追加コピーする（#35 の「Deploy2Prd を見ない」を部分的に戻す）
   - → どちらにするか

2. 実行履歴の既定フィルタは `すべて` でよいか。Pilot だけを既定にする必要はあるか

3. ログ全文の上限（極端に長い robocopy 出力）は設けないでよいか。設けるなら上限文字数

---

## Decisions Log

| 日付 | 決定 | 根拠 |
|------|------|------|
| 2026-08-31 | 共通画像は対象外（既に適用できている） | ユーザー確認 |
| 2026-08-31 | 画像情報準備の各フォルダ（Images / news / pdf）を `DestWebSourcePath` 直下へ同じフォルダ名でコピーする | ユーザー確認 |

---

## Related

- [`PLAN.md`](./PLAN.md) / [`TASKS.md`](./TASKS.md) — 本版の実装計画とタスク
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md` — Pilot 適用の元仕様
- `docs/issue27/SPEC.md` — 共通画像コピー
- `docs/issue35/SPEC.md` — SQL/Files を `deployed` / `FilesPath` 起点に変更。`FilesDeploy2PrdPath` は使わない
- `docs/issue20/SPEC.md` — 画像情報準備（アップロード先 = `FilesPath`）
- `docs/spec-issue-8-execution-log.md` — STG 適用ログの DB 保存（Pilot は対象外）
- `docs/VERSIONING_GUIDE.md` — 版番号の揃え方
