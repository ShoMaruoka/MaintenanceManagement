# 敵対検証レビュー: Pilot適用機能の適用フロー変更（Issue #35）

対象文書: [`SPEC.md`](./SPEC.md) / [`PLAN.md`](./PLAN.md) / [`TASKS.md`](./TASKS.md)
実施日: 2026-08-12（第1〜第4ラウンド）
方式: 文書と既存実装（`backend/Services` / `backend/Controllers` / `frontend/src`）の突き合わせによる敵対検証

**現在の判定: Approve — 実装着手可。ブロッカーなし**（第4ラウンド）。
第1〜第3ラウンドの指摘はすべて文書に反映済み。第4ラウンドの G1〜G5 は実装中（T5a 着手時）に決めれば十分で、文書修正を待たずに **T1 から着手して問題ない**。

判定の推移: 第1 Request changes → 第2 Approve with required changes → 第3 Approve → 第4 Approve（ブロッカーなし）。

指摘の総数: Critical 1 / Important 10 / Suggestion 十数件 — すべて解消、または見送りを明示的に記録。

## 第1ラウンド（2026-08-12）

### 総評

3文書は相互に整合しており、方向性（`deployed/` 起点化＋成功 Run 集計）は妥当。
ただし **運用上の無音の後退が1件（Critical）**、**設計・テスト計画の穴が5件（Important）** ある。

**判定: Request changes（実装着手前に文書修正）**

必須対応: C1 の方針決定と SPEC 反映、I1〜I5 の SPEC/PLAN/TASKS への反映。
特に C1（本番前準備後の空振り）と I3（テスト手段の実現性）は、着手後に判明すると手戻りが大きい。

---

### Critical（第1ラウンド）

#### C1. 本番前準備を実行すると `deployed/` が空になり、以後の Pilot 適用が「成功したのに何も適用しない」

`backend/Services/FastCopyService.cs:87-101` / `:126-140` のとおり、本番前準備は
`DeployedPath` / `MariaDbDeployedPath` からファイルを Deploy2Prd へコピーし
**`File.Delete(src)` で元を削除**する。静的ファイルも同様（`:184-196` で Copy→Delete）。

つまり `deployed/` と `FilesPath` は「STG 適用済み **かつ** 本番前準備 未実施」のプールである。
SPEC の「空 → スキップして成功」と組み合わせると:

- 本番前準備を実行済み → `deployed/` 空 → Pilot 適用が `deploy.bat` 非実行で **Success** →
  ダッシュボードには「Pilot 最終適用 成功」が載る
- 従来（Deploy2Prd 起点）は、本番前準備後こそ Pilot に SQL が流れていた

**本番適用直前に pilot を再構築する**運用が、変更後は無言で空振りする。
SPEC の Objective は「早期に載せたい」だけを解いており、この逆方向のケースが
Success Criteria にもリスク表にもない。

対策案（いずれか要決定）:

- 「両ソース空」かつ `Deploy2PrdPath` に `*.sql` がある場合は **WARN ログ＋画面警告**（適用はスキップのまま）
- 空スキップ時の結果を `success` ではなく `skipped` 相当として記録し、ダッシュボードの「最終適用」に採用しない

最低でも SPEC に「本番前準備実施後は Pilot に SQL が流れない」を As-Is/To-Be 表と Risks に明記する。

**確認事項**: 「本番前準備後に Pilot を再適用する運用」は実在するか。
実在しないなら SPEC に「Pilot 適用は本番前準備より前に行う前提」と明記して閉じられる。
実在するなら WARN 追加をスコープに入れる必要がある。

---

### Important（第1ラウンド）

#### I1. 「未設定」と「空」の区別が仕様にない（誤設定を握りつぶす）

`DeployedPath` は `DeployDev2StgPath` からの派生（`backend/Models/DbConfig.cs:42`）。
`DeployDev2StgPath` 未設定だと相対パスになり、現行は `ValidateDeployPaths`
（`backend/Services/WebSourceDeployService.cs:64-67`）が「絶対パスである必要があります」で**失敗**する。
SPEC どおり「ディレクトリ不存在 → スキップ成功」を先に判定すると、**設定ミスが成功扱い**になる。
判定順序（未設定は従来どおりエラー／不存在のみスキップ）を SPEC 1.1-5 に明記すること。

#### I2. DryRun 時の View 置換走査先が「2ソース」に対応していない

現行は `replaceDir` 1つ（`backend/Services/WebSourceDeployService.cs:249`）。
変更後のコピー元は `DeployedPath` と `MariaDbDeployedPath` の2つなので、
DryRun プレビューは**両方を走査**する必要がある。
TASKS T3 の受入基準は「走査先が新コピー元側になる」のみで、2ディレクトリ化の要求が落ちている。

#### I3. T3 の受入基準がテスト手段と矛盾する（DryRun では配置を検証できない）

`RunRobocopyAsync` は DryRun で**ログだけ出して return 1**
（`backend/Services/WebSourceDeployService.cs:108-112`）。
したがって PLAN 6「DryRun で単体テスト」では T3 の「期待配置でコピーされる」を検証できない。
取り得る選択肢:

- コピー処理を差し替え可能にする（`Func<string,string,Task<int>>` 注入 or `protected virtual`）
- 実 robocopy を回す（Windows 前提の統合テスト扱いにする）

PLAN/TASKS のどちらかに**どちらを取るか**を書かないと、T3 は着手時に詰まる。

#### I4. 「`deploy.bat` 未起動」を証明するテスト設計が未定義

現行は bat 不在なら `Success=false` を返す（`backend/Services/WebSourceDeployService.cs:270-271`）。
空スキップは **bat 存在チェックより前**に return する必要があり（そう書かれていない）、
かつ「bat を実際に配置した上で起動されないこと」を確認しないと非起動を証明できない。
T3 受入の「`deploy.bat` 未起動（テスト）」に検証方法を追記すること。

#### I5. 片側だけ空のとき、外部提供の `deploy.bat` が耐えるか未検証

SPEC 1.1-5「片側だけ空なら存在する側だけコピーして通常継続」。
この場合 `Source` 直下が 0 件で `Source\MariaDB` だけ、という状態で
pilot の `deploy.bat`（本システム管理外・事前配置）が起動する。
0 件時に非 0 終了する bat なら**失敗になる**。
PLAN の Risks に「bat が Deploy2Prd 前提のパスを持つ」はあるが、**0 件入力**のリスクは未記載。
手動確認項目に加えること。

---

### Suggestions（要検討・ブロッカーではない）

#### S1. DryRun 実行がダッシュボードの「最終適用」に載る

`DryRun` は appsettings のグローバルフラグ（`backend/Services/WebSourceDeployService.cs:34`、
`appsettings_sample.json` の既定は `true`）だが、
`backend/Controllers/WebSourcePrepareController.cs:103-115` は DryRun でも `success` を記録する。
`WebSourceDeployLog` スキーマ不変の縛りがあるので、
既存 `Mode` 列に `full-dryrun` を入れて集計から除外する、が最小手当て。

#### S2. 空スキップ成功も「Pilot 最終適用」として表示される

C1 と同根。`Mode` に `web` / `sql` / `sql-skipped` を実際に入れる
（現在は常に `"full"` 固定でモード情報が死んでいる）と、集計と画面表示の精度が上がる。

#### S3. `kaios` / `gos` のハードコードが3箇所目になる

`AllowedDbNames`（`backend/Controllers/WebSourcePrepareController.cs:158`）と重複する。
`LastPilotKaios` / `LastPilotGos` という**プロパティ名でのDB名固定**より、
`Dictionary<string, PilotDeploySummary>`（または配列）で返す方が pilot 追加時に壊れない。
UI 側は「pilot がある DB を全部カード化」で済む。

#### S4. 空判定（`*.sql` 件数）とコピー範囲（robocopy `/E` で全ファイル）が非対称

`deployed/` に `.sql` 以外だけがある場合、スキップ成功でそのファイルは pilot へ行かない。
実害は薄いが SPEC に一文（「判定・コピーとも `*.sql` を正とする」等）が要る。

#### S5. Files の空判定は「エントリ有無」ではなく「ファイル再帰有無」で

`ImagePrepareService` は空フォルダを削除してもカテゴリ直下は残す
（`backend/Tests/Services/ImagePrepareServiceDeleteTests.cs:122` が明示）。
PLAN 3 の「配下にエントリが無い」だと、空カテゴリだけ残った状態で robocopy が走る。

#### S6. 集計 SQL は 1 本にまとめられる

`MIN(Result)='success' AND MAX(Result)='success'` は現状値が2種なので正しく動くが、
意図が読みにくく将来値追加で壊れる。
`SUM(CASE WHEN Result <> 'success' THEN 1 ELSE 0 END) = 0` を推奨。
`GROUP BY DbName` の1クエリで kaios/gos 両方取れる（現状 `GetDashboardStats` は既に4クエリ発行）。

#### S7. Pilot 適用は `deployed/` を消費しない

Pilot を複数回実行すると未送出分すべてが毎回再適用される
（View/SP は冪等だが、データ更新系 SQL が混ざると多重実行）。
Deploy2Prd 起点でも同性質だったが、リセット契機が「本番適用」から「本番前準備」へ変わる点は Risks に明記を。

---

### Nit / FYI

- **N1. TASKS T8 の前提が実物と違う。** `WebSourcePrepare.tsx` に「本番前準備前提の説明文」は存在しない。
  実在するのは「コピー元・コピー先」ブロック（STG / 共通画像 / pilot ターゲットの3行、
  `frontend/src/pages/WebSourcePrepare.tsx:243-248`）。
  受入基準は「説明文が…になっていない」ではなく
  「deployed / MariaDB deployed / Files の3行が追加されている」に直すこと。
- **N2. PLAN の「hold / manual は混入しない」は正しい**（`backend/Models/DbConfig.cs:42-50` で
  3つとも `DeploySourcePath` 直下の兄弟）。一方、現行は Deploy2Prd 全量コピーで
  `ManualApply` サブフォルダが pilot Source に入っていた。これが入らなくなる点は意図どおりだが差分として記録。
- **N3.** `info` API へのサーバーパス露出は、既に `webSourcePath` 等を返しているため新規リスクではない。

---

### 対応チェックリスト（第1ラウンド分・すべて反映済み）

- [x] C1: 本番前準備後の Pilot 再適用運用の有無を確認し、SPEC に反映（必要なら WARN をスコープ追加）
  - 確定: 同一モジュール再適用は想定しない。準備後の追加・再修正は想定。空スキップは `skipped`＋最終除外（WARN ログ）
- [x] I1: 未設定 vs 不存在 の判定順序を SPEC 1.1 に明記
- [x] I2: TASKS T3 受入に「2ソース走査」を追加
- [x] I3: コピー処理のテスト戦略（注入）を PLAN に確定
- [x] I4: `deploy.bat` 非起動の検証方法を T3 に追記
- [x] I5: 片側空での `deploy.bat` 挙動を手動確認項目に追加
- [x] S1〜S3: 実装前に方針決定（S1/S2 採用、S3 見送り）
- [x] N1: TASKS T8 の受入基準を実物に合わせる

### 文書反映メモ（2026-08-12）

ユーザー回答により C1 を閉じた。SPEC / PLAN / TASKS を更新済み。


---

## 第2ラウンド（2026-08-12・修正版に対する再検証）

### 総評

第1ラウンドの C1 / I1〜I5 / S1〜S7 / N1 は**すべて文書に反映済み**であることを確認した。
文書の質は明確に向上している。一方、**修正によって新たに生じた（または格下げされた）穴が4件**ある。
いずれも文書上の確定作業で閉じられ、設計の作り直しは不要。

**判定: Approve with required changes（実装着手は A1〜A4 の反映後）**

### 第1ラウンド指摘の消化確認

| 前回 | 状態 | 確認内容 |
|---|---|---|
| C1 | 解決 | SPEC「運用前提（C1確定）」＋ As-Is/To-Be 表に行追加。運用判断（同一モジュール再適用なし／準備後の追加分は想定）が明記 |
| I1 | 解決 | SPEC 1.1-2 で「パス妥当性 → 空判定」の順序を明示 |
| I2 | 解決 | SPEC 1.1-7・T3 受入に2ソース走査 |
| I3 | 解決 | PLAN 7 で注入方式に確定。実 robocopy を回さない方針も明記 |
| I4 | 解決 | 「bat 存在チェックより前に return」＋「bat 配置のうえ呼出0回」まで具体化 |
| I5 | **部分** | 記載はされたが統合 Checkpoint で「（任意）」に格下げ → A4 |
| S1/S2 | 解決 | Mode による DryRun / skipped 除外を採用 |
| S3〜S7 / N1 | 解決 | 見送り理由も含め記録 |

**FYI**: `WebSourceDeployLog` は現状**書き込み専用**（`DatabaseService.cs` に SELECT なし、History/FE にも参照なし）。
`Mode` を `"full"` 固定からやめる変更のブラスト半径はゼロで、S1/S2 の方針は安全に取れる。

### Important（第2ラウンド）

#### A1. `skipped` を Service → API → 画面へ運ぶ経路がどのタスクにも属していない

SPEC 1.1-6 は「結果は `skipped` 相当」「**画面／SSE でもスキップである旨が分かること**」と書くが、実体は:

- `WebSourceSqlDeployResult` は `(Success, ExitCode, ErrorMessage)` の3項のみ
  （`backend/Services/WebSourceDeployService.cs:242-276`）。**skipped を表現できない**
- FE は `result.sqlDeploy.success` で ✓/✗ を出すだけ
  （`frontend/src/pages/WebSourcePrepare.tsx:185-190`）→ **スキップが「✓ 成功」と表示される**

最低でも `WebSourceSqlDeployResult` への項目追加（`Skipped` フラグ or ステータス enum）＋
SSE `done` ペイロード＋ FE 型・表示の変更が必要。だが T3 の Files 欄はサービスとテストのみ、
T8 はパス3行のみで、**どのタスクも所有していない**。

対応: T3 に BE 側（型追加）、T8 か新規タスクに FE 表示を割り当てる。

#### A2. 除外ルールが SPEC と PLAN/TASKS で食い違っている（実装が分岐する）

- SPEC 2.1 表: 除外は「**SQL 空スキップのみ**の Run」＋「実行内容が web/sql/both いずれでも成功 Run なら採用」
- PLAN 5 / T5: 「Mode が dry-run 系または `sql-skipped` を**含む** Run」を除外

`step=both` で **Web コピー成功 ＋ SQL 空スキップ** の Run が、SPEC では採用・TASKS では除外になる。
「本番前準備後に Web だけ pilot を更新した」ケースで表示が変わる、実運用に効く差。

対応（推奨）: 「Run 内に実適用の成功行が1つでもあれば採用。全行が skipped / dry-run なら除外」に統一
（＝ SPEC 側の読みを正とする）。

#### A3. スキップ行の `Result` 列に何を入れるかが未定義（A2 と連動して壊れる）

`Result` は現状 `'success'` / `'failed'` の2値で、集計条件は「同一 RunId の全行が `success`」。
ここに `Result='skipped'` を入れると、**Web コピーが成功した both Run まで丸ごと除外**される
（全行 success 条件を満たさなくなるため）。

対応: SPEC が「Mode 列で表す」としている以上、**`Result` は `success` のまま**とし、
除外は Mode の**行単位判定**で行う、と SPEC 2.1 に一文で確定する。
現状はどちらにも読め、T5/T6 の実装者が判断を迫られる。

#### A4. 「片側だけ空」は例外ケースではなく常態。必須確認に戻すべき

`appsettings.Development.json` では kaios / gos とも MariaDB 接続を持つが、MariaDB のモジュール更新は
SQL Server より頻度が低く、**「SQL Server 側だけ `.sql` あり／MariaDB 側0件」は日常的に発生**する。
この状態で `Source\MariaDB` が空のまま外部提供の `deploy.bat` が起動する。

にもかかわらず統合 Checkpoint では「（任意）片側空での `deploy.bat` 挙動確認」に格下げされている。
**リリース前に必ず踏むパス**なので `（任意）` を外して必須にする。逆側（MariaDB のみ更新）も同様。

### Suggestions（第2ラウンド）

- **B1. `*.sql` 限定コピーの実現手段が未記載。** SPEC 1.1-4 が「コピーも `*.sql` のみ」に変わったが、
  `BuildArguments`（`backend/Services/WebSourceDeployService.cs:168-175`）は Web ソース／画像／Files
  コピーと**共通**でファイル名フィルタを持たない。SQL コピー専用にフィルタ付き経路を足す
  （他の呼出に影響させない）ことを PLAN 1 に追記。注入デリゲートのシグネチャにも影響する。
- **B2. `*.sql` 限定化の副作用。** 従来は Deploy2Prd 全量コピーだったため、`deploy.bat` が参照する
  非 SQL の補助ファイルが Deploy2Prd 側にあった場合は運ばれなくなる。
  PLAN の Risks に「非 SQL 補助ファイルへの依存がないことを手動確認」を追加。
- **B3. Mode を誰が決めるかが T3/T5 に分散。** T5 の Files 欄が「未着手なら本タスクまたは T3 連動で実施」
  と曖昧。Controller は既に `IConfiguration` を受けている
  （`backend/Controllers/WebSourcePrepareController.cs:32`）ので DryRun 判定は可能。
  **Mode 文字列の決定は Controller に集約し、所有タスクを T5 に一本化**するのが素直。
- **B4. 過去ログの扱い。** 既存行はすべて `Mode='full'` で、過去の DryRun 実行も `success` として残る。
  除外ロジック導入後も**過去の DryRun は最終適用に載り得る**。実害は薄いが、初回表示が想定と違う
  可能性として PLAN に一行あると混乱を防げる。

### 対応チェックリスト（第2ラウンド）

- [x] A1: `WebSourceSqlDeployResult` の skipped 表現・SSE・FE 表示を T3 / T8 へ割当
- [x] A2: 除外ルールを「実適用成功行が無い Run のみ除外」に統一（SPEC 2.1 / PLAN 5 / T5）
- [x] A3: スキップ行の `Result` は `success` のまま、識別は Mode と SPEC に明記
- [x] A4: 統合 Checkpoint の片側空 `deploy.bat` 確認から「（任意）」を外す
- [x] B1: `*.sql` フィルタの実現手段を PLAN 1 に追記
- [x] B2: 非 SQL 補助ファイル依存の確認を Risks / 統合 Checkpoint に追加
- [x] B3: Mode 決定の所有タスクを T5（Controller 集約）に一本化
- [x] B4: 過去ログ（Mode='full' の旧 DryRun）の扱いを PLAN に注記

### 文書反映メモ（第2ラウンド・2026-08-12）

A1〜A4 / B1〜B4 を SPEC / PLAN / TASKS に反映済み。実装着手可。

### 検証で参照した実コード（第2ラウンド）

| 確認点 | 参照 |
|---|---|
| skipped を表現できない戻り値型 | `backend/Services/WebSourceDeployService.cs:242-276` |
| FE の ✓/✗ 表示 | `frontend/src/pages/WebSourcePrepare.tsx:185-190` |
| robocopy 引数が全コピー共通 | `backend/Services/WebSourceDeployService.cs:168-175` |
| `WebSourceDeployLog` は書き込み専用 | `backend/Services/DatabaseService.cs:55-67, 247-252` |
| Controller が `IConfiguration` 保持 | `backend/Controllers/WebSourcePrepareController.cs:32` |
| kaios / gos とも MariaDB 接続あり | `backend/appsettings.Development.json:20, 35` |


---

## 第3ラウンド（2026-08-12・A1〜A4 / B1〜B4 反映版に対する再検証）

### 総評

第2ラウンドの A1〜A4・B1〜B4 は**すべて正しく反映**されている。
特に A2/A3 の統一（SPEC 2.1「除外の判定単位＝Mode の**行単位**」行）は、レビュー側の推奨より明快に書かれている。
**Critical・Important-blocker はなし。** 残るのは文書内の順序矛盾と、A2 の再発を防ぐ最後の一手。

**判定: Approve — 実装着手可**（着手前に D1〜D3 の反映を推奨。D4/D5 は該当タスク着手時でよい）

### 第2ラウンド指摘の反映確認

| 前回 | 状態 | 根拠 |
|---|---|---|
| A1 | 解決 | SPEC 1.1-6 / 1.3、T3（`Skipped` + SSE）、T8（`sqlDeploy.skipped` 表示）で BE→FE が繋がった |
| A2 | 解決 | SPEC 2.1 に「実適用成功行が1つでもあれば採用」「`both`＋`sql-skipped` は載る」を明記。PLAN 5・T5・T6 も一致 |
| A3 | 解決 | 「`Result` は常に `success`、識別は Mode」＋ Never 節に誤除外の禁止まで記載 |
| A4 | 解決 | SPEC Success Criteria・PLAN / TASKS 統合 Checkpoint すべて「**必須**手動」 |
| B1 | 解決 | 共通 `BuildArguments` は不変、SQL 専用経路を用意（PLAN 1） |
| B2 | 解決 | 非 SQL 補助ファイル依存の確認を Risks＋統合 Checkpoint に追加 |
| B3 | 解決 | Mode 決定は Controller に集約、所有タスクは T5 |
| B4 | 解決 | 過去 `Mode='full'` 行の扱いを PLAN 8 に注記 |

### Important（第3ラウンド）

#### D1. PLAN の Checkpoint A に、まだ実装されていない FE 作業が入っている

PLAN Checkpoint A: 「両空 → Skipped・bat 未起動・**画面スキップ表示**」。
しかし画面表示は T8（Checkpoint B の**後**）であり、TASKS 順どおり進めると Checkpoint A は T4 時点で満たせない。

対応: Checkpoint A は「`Skipped=true` が SSE `done` に載る」までにし、画面表示の確認は Checkpoint B か統合へ移す。

#### D2. TASKS 冒頭の実行順序図が、更新後の依存関係と食い違う

図は「T3〜T4 と T5〜T7 は T2 完了後に並行可」のままだが、T5 は Mode `sql-skipped` を書くため T3 の
`Skipped` に依存し、T5 の Dependencies 欄自身も「T3 後推奨」と書いている。

対応: 図を `T3 → T5` を含む形へ修正（実質の並行可能範囲は T1/T2 と T6 の RED まで）。

#### D3. 「どの行にどの Mode を書くか」の対応表がまだ無い — A2 が再発しうる唯一の経路

SPEC 2.1 は判定単位が行単位であることまでは書いたが、**書き込み側の対応表**が無い。
実装者が「Run 全体の Mode を `sql-skipped` にする（target 行も含めて）」と実装すると、
**実適用成功行が検出できず、A2 で守ったはずの `both`＋Web 成功 Run が丸ごと除外**される。
A2 の修正が無効化される唯一の経路。

対応: SPEC 2.1 か PLAN 6 に以下程度の表を置く。

| 行 | TargetName | Mode の例 |
|---|---|---|
| Web ターゲット | `pilot1` / `pilot2` | `both` / `web`（DryRun 時 `both-dryrun` / `web-dryrun`） |
| SQL | `sql` | `sql`（適用実行）/ `sql-skipped`（空スキップ）/ `sql-dryrun` |
| 例外 | `-` | `both` 等の実 step（`Result='failed'`） |

#### D4. Controller の Mode 決定ロジックにテストが当たらない

T6 は `DatabaseService` の集計テストで、**Controller が正しい Mode を書くか**は検証されない
（Controller のユニットテストは存在しない）。つまり D3 の事故はテスト全緑のまま本番に出る。

対応: Mode 文字列の決定を **static な純関数**（例: `ResolveLogMode(step, dryRun, skipped, isSqlRow)`）に
切り出し、T5 の受入に「その関数の単体テスト」を追加する。D3 と対になる守りになる。

#### D5. スキップ時の SSE ログ文言が「完了しました」のまま

`SQL適用: 完了しました` は2箇所（`backend/Services/WebSourceDeployService.cs:523-526` の SqlOnly 経路、
`:646-649` の Both 経路）にあり、`Success` だけで分岐する。`Skipped=true` でもここは「完了しました」＋
「✅ Pilot環境適用が完了しました」と出るため、**ログだけ見ると通常成功と区別できない**。
SPEC 1.3 は完了画面の表示しか要求しておらず、T3 の受入にもログ文言が無い。

対応: T3 の受入に「`Skipped` 時のログが `SQL適用: スキップ（適用対象 SQL なし）` 等になる（**2箇所とも**）」を追加。

### Suggestions（第3ラウンド）

- **E1. Mode の語彙を有限リストとして固定する。** 集計側を `Mode LIKE '%dryrun%'` のような部分一致で書くと、
  `sql-skipped` の漏れや将来の値追加で静かに壊れる。PLAN 6 に許容値の一覧（D3 の表の右列）を書き、
  集計は `IN (...)` の完全一致集合で判定、と決めておく。
- **E2. DryRun ＋ 空スキップの同時発生時の Mode が未定義。** どちらも除外対象なので集計結果は変わらないが、
  `sql-dryrun` と `sql-skipped` のどちらを書くかが実装者依存。E1 の一覧に「DryRun が優先」等を1行。
- **E3. 注入デリゲートと DryRun 分岐の境界を決める。** 現状 DryRun 判定は `RunRobocopyAsync` の内側
  （`backend/Services/WebSourceDeployService.cs:108-112`）。コピーを注入化したとき DryRun 時に
  デリゲートが**呼ばれるのか呼ばれないのか**を PLAN 7 に明記しないと、「呼出回数」を見る T3 のテストが
  仕様なしで書かれる。
- **E4. T5 が Scope M で3責務**（Mode 記録／集計クエリ／除外ロジック）を持ち、T7 の依存でもある。
  `T5a: Controller の Mode 記録` ／ `T5b: GetDashboardStats 集計` に割ると、D4 の純関数テストも
  自然に T5a へ収まる。

### Nit（第3ラウンド）

- **F1. T3 の Files 欄が事実と違う。** `WebSourceSqlDeployResult` は `backend/Models/` ではなく
  **`backend/Services/WebSourceDeployService.cs:688`** に定義されている
  （`WebSourceDeployTargetResult` / `WebSourceDeployStep` も同ファイル末尾）。
  T3 の Files から `backend/Models/` の行を落とすかパスを修正する。
- **F2.** Files の空判定は pilot ターゲットのループ外で1回にすると2台分の再帰列挙が1回で済む
  （`EnumerateFiles().Any()` は短絡するので実害はほぼゼロ。好みの範囲）。

### 対応チェックリスト（第3ラウンド）

- [x] D1: PLAN Checkpoint A から「画面スキップ表示」を外し、Checkpoint B / 統合へ移す
- [x] D2: TASKS 実行順序図を `T3 → T5a` を含む形へ修正
- [x] D3: Mode の行別対応表を SPEC 2.1 に追加
- [x] D4: Mode 決定を純関数に切り出し、T5a 受入に単体テストを追加
- [x] D5: `Skipped` 時のログ文言（2箇所）を T3 受入に追加
- [x] E1: Mode 語彙の有限リスト化＋集計は完全一致で判定
- [x] E2: DryRun ＋ 空スキップ同時発生時は DryRun 優先（`sql-dryrun`）
- [x] E3: DryRun 時も注入デリゲートを呼ぶと PLAN 7 に明記
- [x] E4: T5 を T5a / T5b に分割
- [x] F1: T3 の Files 欄の `WebSourceSqlDeployResult` 定義パスを修正
- [x] F2: Files 空判定をターゲットループ外へ（任意・PLAN/T4 に記載）

### 文書反映メモ（第3ラウンド・2026-08-12）

D1〜D5 / E1〜E4 / F1〜F2 を SPEC / PLAN / TASKS に反映済み。実装着手可。

### 検証で参照した実コード（第3ラウンド）

| 確認点 | 参照 |
|---|---|
| `WebSourceSqlDeployResult` の定義場所（Models ではない） | `backend/Services/WebSourceDeployService.cs:688` |
| スキップ時も「完了しました」と出るログ分岐（SqlOnly 経路） | `backend/Services/WebSourceDeployService.cs:523-526` |
| 同（Both 経路） | `backend/Services/WebSourceDeployService.cs:646-649` |
| DryRun 判定がコピー関数の内側にある | `backend/Services/WebSourceDeployService.cs:108-112` |
| 例外時の catch 行（TargetName='-'、Mode 固定） | `backend/Controllers/WebSourcePrepareController.cs:135` |


---

## 第4ラウンド（2026-08-12・D1〜D5 / E1〜E4 / F1〜F2 反映版に対する再検証）

### 総評

第3ラウンドの指摘は**全11件、過不足なく反映**されている。
特に SPEC 2.1 の「Mode の行別対応表＋除外集合の完全一致リスト＋禁止事項」は、
A2 が実装段階で無効化される経路（Web 行にまで `sql-skipped` を書く実装）を実際に塞いでいる。

**この回で新たに見つかった問題に、着手をブロックするものはない。** 実装中に潰せる2件＋軽微3件のみ。

**判定: Approve — 実装着手可。ブロッカーなし。**

### 第3ラウンド指摘の反映確認

| 前回 | 状態 | 根拠 |
|---|---|---|
| D1 | 解決 | Checkpoint A に「FE 画面は含まない」を明記、画面確認は統合へ。Implementation Order も「画面はまだ見ない」と一致 |
| D2 | 解決 | 実行順序図が `T3 → T5a` を含む形に。依存の要点5行も追加 |
| D3 | 解決 | SPEC 2.1 に行別対応表＋**禁止**（Web 行に `sql-skipped` を書かない）＋除外集合を明記 |
| D4 | 解決 | `ResolveLogMode` の純関数化と単体テストが T5a の受入に |
| D5 | 解決 | SqlOnly / Both **両経路**のログ文言が T3 受入と PLAN 3 に |
| E1 | 解決 | 有限リスト化＋`IN` 完全一致（SPEC 2.1 / PLAN 5 / T5b が同一集合） |
| E2 | 解決 | DryRun 優先を SPEC 表・PLAN 6・T5a に |
| E3 | 解決 | 「DryRun 時もデリゲートは呼ばれる／副作用なし」を PLAN 7 に |
| E4 | 解決 | T5a / T5b に分割、依存も整合 |
| F1 | 解決 | T3 Files が `WebSourceDeployService.cs`（同ファイル末尾）に修正 |
| F2 | 解決 | ループ外1回判定を PLAN 4 に（任意最適化として） |

### Important（第4ラウンド・軽微／実装中に対応可）

#### G1. Mode 語彙の**定数の置き場所**だけが決まっていない

E1 で語彙は有限リスト化されたが、文字列を**生成する側**（T5a: Controller の `ResolveLogMode`）と
**消費する側**（T5b: `DatabaseService` の除外 `IN`）に、同じリテラルが独立して書かれる構造のまま。
T5a のテストと T6 のテストもそれぞれ独自にリテラルを書くため、
**両者がズレても両方のテストが緑になり得る**（例: 生成 `sql-skipped` ／ 除外 `sql_skipped`）。

対応: `WebSourceDeployModes` のような共有定数（`const string SqlSkipped = "sql-skipped";` ＋
除外集合の配列）を1箇所に置き、Controller・DatabaseService・テストすべてがそれを参照する、と PLAN 6 に一行。
実装コストはほぼゼロで、D3/D4 の守りが閉じる。

#### G2. Controller が DryRun をどう知るかが未定義（既存の前例あり）

T5a は Controller で `*-dryrun` を決めるが、DryRun フラグは `WebSourceDeployService` の
**private `_dryRun`**（`backend/Services/WebSourceDeployService.cs:34`）で Controller からは見えない。
Controller が `IConfiguration` から同じキーを読み直す実装になりがちだが、それは同一設定への二重依存。

このリポジトリには既に前例がある — **`ImagePrepareService.IsDryRun`**
（`backend/Services/ImagePrepareService.cs:36`）が公開プロパティで呼び出し側に露出している。
`WebSourceDeployService.IsDryRun` を同じ形で足し、Controller はそれを使う、と PLAN 6 に一行。既存パターンにも揃う。

### Suggestions / Nit（第4ラウンド）

- **G3. `DbName` 比較の大小文字。** 集計クエリで `DbName = 'kaios'` と書くと、SQLite の `=` は ASCII で
  大小文字を区別する。挿入値は `config.Name` 由来で、`AllowedDbNames` の判定は `OrdinalIgnoreCase`
  （`backend/Controllers/WebSourcePrepareController.cs:158`）なので、設定が `"Kaios"` でも登録は通り、
  集計だけ null になる。実際の `appsettings` は小文字なので実害はないが、`COLLATE NOCASE` を付けておくと事故らない。
- **G4（Nit）. T3 の受入1行に2つの主張が混在。** 「SQL Server のみ / MariaDB のみ / 両方 → 期待配置
  （注入で検証。**DryRun でもデリゲート呼出あり**）」は、配置の検証（`dryRun=false`）と
  デリゲート呼出の検証（`dryRun=true`）という別ケースを1行に畳んでいる。2行に割ると
  「DryRun で配置を assert する」誤読を防げる。
- **G5（FYI）. 既存インデックスは新クエリに効かない。** `IX_WebSourceDeployLog_DbName_ExecutedAt` は
  `(DbName, ExecutedAt DESC)` で、新しい集計は `RunId` でグルーピングする。ただし Pilot 実行1回あたり
  3行程度・件数は年間でも数千行規模のため**対応不要**。

### 対応チェックリスト（第4ラウンド）

- [ ] G1: Mode 語彙の共有定数クラスを PLAN 6 に明記（生成側・消費側・テストが同一定数を参照）
- [ ] G2: `WebSourceDeployService.IsDryRun` を公開し Controller が利用（`ImagePrepareService` に倣う）
- [ ] G3: 集計クエリの `DbName` 比較に `COLLATE NOCASE`（任意）
- [ ] G4: T3 の受入を「配置検証（dryRun=false）」「デリゲート呼出（dryRun=true）」の2行に分割（任意）
- [ ] G5: 対応不要（FYI）

### 検証で参照した実コード（第4ラウンド）

| 確認点 | 参照 |
|---|---|
| DryRun が private で Controller から見えない | `backend/Services/WebSourceDeployService.cs:34` |
| `IsDryRun` 公開の既存前例 | `backend/Services/ImagePrepareService.cs:36` |
| DbName 判定が OrdinalIgnoreCase | `backend/Controllers/WebSourcePrepareController.cs:158` |
| 既存インデックスの定義 | `backend/Services/DatabaseService.cs:66-67` |

---

## 総括（第1〜第4ラウンド）

| ラウンド | 判定 | 新規指摘 |
|---|---|---|
| 第1 | Request changes | Critical 1（C1）／ Important 5（I1〜I5）／ Suggestion 7 ／ Nit 3 |
| 第2 | Approve with required changes | Important 4（A1〜A4）／ Suggestion 4（B1〜B4） |
| 第3 | Approve | Important 5（D1〜D5）／ Suggestion 4（E1〜E4）／ Nit 2（F1〜F2） |
| 第4 | Approve（ブロッカーなし） | Important 2（G1〜G2）／ Suggestion 2 ／ FYI 1 |

未対応で残るのは G1〜G4 のみ（いずれも T5a 着手時に決定すれば足りる）。**T1 から実装着手可。**
