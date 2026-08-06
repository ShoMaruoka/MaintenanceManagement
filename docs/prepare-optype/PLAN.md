# Implementation Plan: 本番前準備画面での操作区分（新規／更新／削除）表示

対象 SPEC: `docs/prepare-optype/SPEC.md`（案A: SQLite `DeploySessionDetail` からの逆引き）

## 実装状況（2026-08-06 時点）

Task 1〜5 すべて実装済み。`dotnet test` 67 件全パス、`dotnet build` / `npm run build` 通過。**未コミット**（CLAUDE.md「勝手に commit / push しない」および Checkpoint C に従い、人間の承認待ち）。

実装中に判明し、SPEC へ反映済みの実データ由来の変更点:
- `DeploySessionDetail.ModuleName` に `dbo.` 付きで記録された行が実在するため、正規化をモジュール名側にも適用（SPEC Assumption 2）
- `OpType` に `更新2` という不正値が実在するため、既知 3 区分以外は `不明` へ寄せる（SPEC Assumption 5a）

**未実施**: ブラウザでの目視確認（Task 3〜5 の Verification にある手動チェック）。実行環境で API を起動できなかったため、画面表示は未検証。

## Overview

`deployed/` の SQL ファイル一覧に操作区分バッジを表示する。区分の供給元は既存の SQLite `DeploySessionDetail` で、書き込み処理の追加は無い。バックエンドで「ファイル名 → 操作区分」を解決して API に載せ（Phase 1）、カード表示にバッジと削除強調を入れ（Phase 2）、比較ビューと TSV に反映する（Phase 3）。全体で S〜M サイズのタスク 5 本。

## Architecture Decisions

### AD1: 区分の分類ロジックは C# の純粋関数に集約し、SQL 側では分類しない

SPEC の Code Style では `ROW_NUMBER() OVER (PARTITION BY ModuleType, ModuleName)` で SQL 側だけで最新行に絞る案を示していたが、**これでは重複排除の粒度が誤っている**。逆引きのキーは `ModuleType` ではなく「DbType（sqlserver / mariadb）＋モジュール名」であり（SPEC Assumption 3）、`ModuleType` 単位で絞ると DbType が同じ別 ModuleType 同士の新旧関係が崩れる。

かといって `CASE WHEN ModuleType IN (...) THEN 'mariadb'` を SQL に書くと、DbType 判定ルールが SQL と C# の 2 箇所に重複する。ルールの重複は行数の削減より高くつく。

**採用**: SQL は `DetailId ASC` 順で対象 DB の明細を素直に返し、C# 側で `OpTypeResolver.ToDbType()` を通しながら辞書へ後勝ちで畳み込む。分類ルールの定義は 1 箇所（`OpTypeResolver`）だけになり、そのまま単体テストできる。全行読みになるが、対象は 1 DB 分の明細でありモジュール数オーダー。社内ツールの画面ロード 1 回あたりのコストとして許容範囲。

→ **SPEC の Code Style 節のクエリ例は Task 1 で本方針に差し替える**（SPEC は生きた文書として更新する）。

### AD2: 分類・正規化は `OpTypeResolver`（新規 static クラス）に切り出す

`DatabaseService` に埋めると、テストのたびに一時 SQLite が必要になる。純粋関数（ファイル名正規化・DbType 判定・キー生成・フォールバック）を `backend/Services/OpTypeResolver.cs` に分離し、大半のテストを I/O 無しで書けるようにする。`DatabaseService` はクエリと畳み込みのみを担う。

### AD3: バッジは既存の `op-badge` パレットに揃える（Open Question 1 の回答）

`index.css` に既に操作区分用の配色が存在し、`ConfirmDialog` / `DeployStg` / `SessionDetailTable` で使われている:

| 区分 | color | background | border |
|------|-------|-----------|--------|
| 新規 | `#137a4c` | `#e6f4ec` | `#c3e3d0` |
| 更新 | `#1f5fd0` | `#e7f0fd` | `#cfe0fb` |
| 削除 | `#c5283d` | `#fcebed` | `#f3c0c5` |
| 不明 | `#8a9099` | `#f0f2f5` | （枠なし） |

本番前準備画面のバッジは `prep-manual-badge` と同じ 9px・`padding: 1px 5px` の極小サイズなので、`op-badge` をそのまま使わず **`prep-optype-badge` を新設し、色だけ上表から取る**。不明はグレー（`prep-file-db-badge` と同系）で控えめにし、ノイズを抑えつつ「区分が引けていない」ことは分かるようにする（Open Question 3 → **表示する**）。

### AD4: 比較ビューは `○` を区分 1 文字に置き換える（Open Question 2 の回答）

現行のセル表示 `○` / `○(適用予定)` を、`新` / `更` / `削` / `?` に置き換える。保留中で適用予定のセルは既存どおり `更(適用予定)` のように括弧付き＋`prep-compare-mark-pending` スタイルで表す。バッジを `○` の隣に添える案は列幅が膨らみ、DB 4 列のテーブルでは横スクロールが増えるため却下。TSV も同じ表現に揃うので `toTsv` の変更は最小で済む。凡例（`prep-compare-legend`）の文言も追随させる。

### AD5: 既知の制約 — MariaDB のプロシージャと関数の同名衝突

MySQL/MariaDB ではプロシージャと関数が別名前空間のため `Stored:foo` と `MariaDbFunction:foo` が共存しうるが、どちらも `deployed/foo.sql` という同一ファイル名になる。これは Git リポジトリの `Stored/` フォルダ構成に由来する**既存の制約**であり本件で新たに生じるものではない。逆引きでは後勝ち（新しい `DetailId`）を採用する。実運用で衝突は確認されていないため、対処は行わずテストで挙動を固定するに留める。

## Dependency Graph

```
OpTypeResolver（純粋関数）
    │
    ├── DatabaseService.GetLatestOpTypes()  ← IX_DeploySessionDetail_SessionId
    │        │
    │        └── PrepareController.GetFiles / PrepareFileInfo.OpType
    │                 │
    │                 └── api/prepare.ts (ApiPrepareFileInfo.opType)
    │                          │
    │                          ├── PrepareForPrd.tsx（バッジ → 削除強調）
    │                          │
    │                          └── prepareCompare.ts → PrepareCompareView.tsx
    │
    └── （テスト）OpTypeResolverTests
```

---

## Task List

### Phase 1: Foundation（バックエンド）

## Task 1: 操作区分の逆引きロジックを実装する

**Description:** ファイル名から操作区分を引くための純粋関数群（`OpTypeResolver`）と、SQLite から DB 単位で「DbType:モジュール名 → 最新 OpType」の辞書を返す `DatabaseService.GetLatestOpTypes()` を追加する。TDD で進める（先にテストを書く）。あわせて JOIN 用のインデックスを `EnsureCreated` に追加し、AD1 に沿って SPEC の Code Style 節を更新する。

**Acceptance criteria:**
- [ ] `OpTypeResolver` が DbType 判定（`Stored` / `MariaDbFunction` / `MariaDbTable` / 旧値 `MariaDB` → mariadb、他 → sqlserver）、ファイル名正規化（`dbo.X.sql`→`X`、`X.sql`→`X`）、キー生成、`不明` フォールバックを提供する
- [ ] `DatabaseService.GetLatestOpTypes(dbName)` が対象 DB の明細を `DetailId ASC` で読み、後勝ちで畳み込んだ辞書を返す
- [ ] `EnsureCreated` に `IX_DeploySessionDetail_SessionId` が `CREATE INDEX IF NOT EXISTS` で追加され、既存 DB を壊さない

**Verification:**
- [ ] `dotnet test backend/Tests/Tests.csproj` が全件パス
- [ ] 新規テストが以下を網羅: 正規化3ケース / DbType 判定（旧値 `MariaDB` 含む）/ SQL Server と MariaDB の同名モジュールを取り違えない / 同一モジュール複数回デプロイで最新が勝つ / 明細に無いファイルが `不明` / セッションが `failed` でも区分が引ける（SPEC Assumption 6）/ AD5 の同名衝突で後勝ち
- [ ] 既存 DB ファイルに対して 2 回 `EnsureCreated` を呼んでも例外が出ない

**Dependencies:** None

**Files likely touched:**
- `backend/Services/OpTypeResolver.cs`（新規）
- `backend/Services/DatabaseService.cs`
- `backend/Tests/Services/OpTypeResolverTests.cs`（新規）
- `backend/Tests/Services/DatabaseServiceOpTypeTests.cs`（新規・一時 SQLite ファイルを使用）
- `docs/prepare-optype/SPEC.md`（Code Style 節のクエリ例を AD1 に差し替え）

**Estimated scope:** M（5 ファイル）

---

## Task 2: API レスポンスに操作区分を載せる

**Description:** `PrepareFileInfo` に `OpType` を追加し、`PrepareController.GetFiles` が DB ごとに 1 回だけ Task 1 の辞書を取得して各ファイルに区分を詰めるようにする。`ReadFiles` は現在 static メソッドなので、辞書を引数で受け取る形に変更する。

**Acceptance criteria:**
- [ ] `GET /api/prepare/files` の各 `files[]` に `opType`（`新規` / `更新` / `削除` / `不明`）が含まれる
- [ ] 辞書の取得は DB（`DbConfig`）ごとに 1 回（ファイルごとにクエリを投げない）
- [ ] `PrepareSelection`（リクエスト DTO）は変更しない（SPEC Assumption 1）

**Verification:**
- [ ] `dotnet build` が通り、`dotnet test` が全件パス
- [ ] 手動: アプリを起動して `/api/prepare/files` を叩き、deployed/hold のファイルに `opType` が入っていること、および実行履歴の区分と一致していることを確認
- [ ] 手動: SQLite に該当明細が無いファイル（手動配置したダミー `.sql`）が `不明` で返り、一覧から消えないこと

**Dependencies:** Task 1

**Files likely touched:**
- `backend/Models/PrepareModels.cs`
- `backend/Controllers/PrepareController.cs`

**Estimated scope:** S（2 ファイル）

---

### Checkpoint A: バックエンド完了

- [ ] `dotnet test` 全件パス、`dotnet build` 警告なし
- [ ] `/api/prepare/files` が実データで正しい区分を返す（実行履歴画面の区分と突き合わせて一致）
- [ ] フロントは未変更のまま動作し、画面が壊れていない（`opType` を無視しても既存表示に影響しない）
- [ ] **人間のレビューを受けてから Phase 2 へ進む**

---

### Phase 2: カード表示（フロントエンド）

## Task 3: ファイル行に操作区分バッジを表示する

**Description:** API の型に `opType` を追加し、本番前準備画面のカード表示（「今回適用する（SQL）」「保留中（SQL）」の両セクション）の各ファイル行に区分バッジを表示する。配色は AD3 のとおり `prep-optype-badge` を新設する。

**Acceptance criteria:**
- [ ] deployed／保留中の各ファイル行に `[新規]` `[更新]` `[削除]` `[不明]` のバッジが、ファイル名と DB バッジの間に表示される
- [ ] 配色が AD3 の表と一致し、不明はグレーで控えめに出る
- [ ] 画像・静的ファイルセクションと手動適用セクションの表示は変わらない

**Verification:**
- [ ] `npm run build` が通る（型エラーなし）
- [ ] 手動: 4 区分すべてのバッジが意図した配色で表示される
- [ ] 手動: 選択・全選択／全解除・件数カウントの挙動が変わっていない

**Dependencies:** Task 2

**Files likely touched:**
- `frontend/src/api/prepare.ts`
- `frontend/src/pages/PrepareForPrd.tsx`
- `frontend/src/index.css`

**Estimated scope:** S（3 ファイル）

---

## Task 4: 削除区分を強調する

**Description:** 削除区分の行を赤系で強調し、「今回適用する（SQL）」セクションヘッダに「うち削除 N 件」を表示、実行確認ダイアログの文言にも削除件数を含める。選択の初期状態は現状どおり（削除も既定でチェック済み）で変更しない。

**Acceptance criteria:**
- [ ] 削除区分の行が赤系背景（`prep-file-item-delete`）で表示され、選択時も判別できる
- [ ] deployed セクションのヘッダに「うち削除 N 件」が出る。0 件のときは表示しない（保留中セクションには出さない = SPEC Assumption 11）
- [ ] 実行確認ダイアログ（`pageState === 'confirm'` の警告ボックス）の文言に、選択中の削除件数が含まれる

**Verification:**
- [ ] `npm run build` が通る
- [ ] 手動: 削除ファイルがある DB / ない DB の両方でヘッダ表示を確認（0 件時に「うち削除 0 件」が出ないこと）
- [ ] 手動: 削除ファイルのチェックを外すと確認ダイアログの削除件数が減る
- [ ] 手動: 実行して従来どおりコピー／保留移動が行われる（DryRun で確認）

**Dependencies:** Task 3

**Files likely touched:**
- `frontend/src/pages/PrepareForPrd.tsx`
- `frontend/src/index.css`

**Estimated scope:** S（2 ファイル）

---

### Checkpoint B: カード表示完了

- [ ] `npm run build` が通る
- [ ] 4 区分のバッジ表示・削除強調・削除件数サマリ・確認ダイアログ文言がすべて動作
- [ ] DryRun で実行まで通し、選択・保留の既存挙動に変化がないことを確認
- [ ] **人間のレビューを受けてから Phase 3 へ進む**

---

### Phase 3: 比較ビュー

## Task 5: 比較ビューと TSV に操作区分を反映する

**Description:** AD4 に沿って、比較ビューのセル表示 `○` を区分 1 文字（`新`/`更`/`削`/`?`）に置き換える。区分は DB ごとに異なりうるため `CompareCell` にセル単位で保持する（SPEC Assumption 9）。TSV 出力と凡例も追随させる。

**Acceptance criteria:**
- [ ] `PrepareCompareFile` / `CompareCell` が `opType` を保持し、セルに区分 1 文字が区分別の色で表示される
- [ ] 保留中セクションで適用予定のセルは `更(適用予定)` のような括弧付き表記になる
- [ ] コピー／ダウンロードした TSV に同じ表記が出力され、凡例の文言が新表記に一致する

**Verification:**
- [ ] `npm run build` が通る
- [ ] 手動: 同じファイル名でも DB ごとに区分が異なるケースで、セルごとに正しい区分が出ることを確認
- [ ] 手動: コピーした TSV を Excel に貼り、区分が読めることを確認
- [ ] 手動: 一部 DB のみに存在する行（`prep-compare-row-unique`）の強調表示が壊れていない

**Dependencies:** Task 4

**Files likely touched:**
- `frontend/src/lib/prepareCompare.ts`
- `frontend/src/components/PrepareCompareView.tsx`
- `frontend/src/index.css`

**Estimated scope:** S（3 ファイル）

---

### Checkpoint C: 完了

- [ ] SPEC の Success Criteria 10 項目をすべて確認
- [ ] `dotnet test` 全件パス、`npm run build` 通過
- [ ] 選択・実行・保留の既存挙動に回帰なし（DryRun で通し確認）
- [ ] SPEC の Open Questions が Plan の AD3〜AD5 で解決済みであることを SPEC 側に反映
- [ ] **commit は人間の承認後に行う**

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `DeploySessionDetail` の実データに想定外の `ModuleType` が入っており DbType 判定が外れる | Med | Task 1 の前に実 DB の `SELECT DISTINCT ModuleType FROM DeploySessionDetail` を確認する。未知の値は sqlserver 側にフォールバックし、区分は付くが誤って MariaDB 扱いにはしない |
| 本番稼働中の SQLite に履歴が乏しく、大半が `不明` になる | Med | 機能としては正しい挙動。Checkpoint A の実データ確認で「どのくらい引けるか」を早期に測り、期待値が低すぎるなら人間と方針を再確認する（案B へ切り替える判断ポイント） |
| MariaDB のプロシージャ／関数の同名衝突（AD5） | Low | 後勝ちで固定し、テストで挙動を明示。実運用での発生は未確認 |
| 比較ビューの表記変更が既存の運用手順書（TSV を貼る運用）と食い違う | Low | Checkpoint C で TSV の実物を人間に確認してもらう。`○` に戻す判断も可能なよう、表記生成は `toTsv` の 1 箇所に閉じ込める |
| `ReadFiles` の static 化解除でシグネチャが変わり、他所から呼ばれていた場合に破壊 | Low | 現在の呼び出し元は `GetFiles` のみ（確認済み）。ビルドで検出可能 |

## Parallelization

- **逐次必須**: Task 1 → 2 → 3 → 4 → 5（型が下流へ伝播するため）
- **並行可能**: Task 3 と Task 5 は Task 2 完了後なら理論上並行できるが、両方 `index.css` を触るため競合を避けて逐次で進める

## Open Questions

SPEC の Open Questions 1〜3 は AD3・AD4 で解決済み。新規の未解決事項はなし。

Plan 実行前の確認事項:
- Task 1 冒頭で実 SQLite の `ModuleType` 実値を確認したい。開発環境の `maintenance.db` のパスを教えてもらえると Risk 1 を早期に潰せる（未提供でも判定ルールは SPEC Assumption 3 のとおり実装して進行可能）。
