# Spec: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)

## 対象Issue

GitHub Issue [#27 pilot環境適用機能の機能追加（画像コピー・Viewソース更新）](https://github.com/ShoMaruoka/MaintenanceManagement/issues/27)
（関連: [#25 pilot環境への適用機能](https://github.com/ShoMaruoka/MaintenanceManagement/issues/25) / `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`）

## Objective

Issue #25 で実装した「Pilot環境適用」機能（STG → pilot1/pilot2 への Webソースコピー ＋ SQL適用）に、
まだ手作業で残っている以下2点を追加し、pilot環境適用を画面操作だけで完結させる。

1. **画像コピー**: STG環境の共通画像フォルダ `E:\Kaios_Image\Common_Image` を、各 pilot サーバーの
   画像公開パス（`...\Images\products`）へコピーする
2. **Viewのソース更新**: pilot 環境へ SQL を適用する際、View 定義中の DB 名参照 `KaiosDB` を
   pilot 用 DB 名 `KaiosDB_pilot` へ書き換えてから適用する

- **対象ユーザー**: 運用担当者（Issue #25 と同一。既存の「Pilot環境適用」画面を利用）
- **対象システム**: kaios / gos（pilot環境が存在するのはこの2システムのみ。paf・duskin は対象外）
  - 画像コピー: kaios / gos の両方が対象（コピー元は共通フォルダを共用）
  - Viewソース更新: kaios / gos の両方が対象。ただし置換するのは **`KaiosDB` 参照のみ**
    （gos の View も `KaiosDB` を参照しているため。`GosDB` は置換しない）
- **成功条件**:
  - pilot1/pilot2 の `Images\products` に STG の共通画像が反映される
  - pilot に適用された View（kaios / gos とも）が `KaiosDB_pilot` を参照する定義になっている
  - 上記2点が既存の画面・SSEログ・履歴の仕組みにそのまま載る

## 背景・現状

| 項目 | 現状 | 本 issue の方針 |
|------|------|----------------|
| Webソースコピー | `WebSourcePath` → 各 pilot の `DestWebSourcePath` へ robocopy（実装済み） | 変更なし |
| 静的ファイルコピー | `FilesDeploy2PrdPath`（Images/news/pdf）→ pilot Webルート直下へ robocopy（実装済み） | 変更なし（画像コピーはこの**後**に実行） |
| 共通画像コピー | `E:\Kaios_Image\Common_Image` → pilot の `Images\products` を**手作業** | robocopy で自動化（本 issue） |
| web.config 適用 | パイロット用ファイル差し替え（`Web.config.DC.{name}.pilot` → `web.config`）。詳細は `docs/pilot-webconfig-file-swap/SPEC.md` | 本 issue では変更なし（後続で差し替え方式へ移行済み） |
| SQL適用 | `Deploy2PrdPath` → `PilotSqlDeployPath\Source` へコピー ＋ `deploy.bat` 実行（実装済み） | コピー後・`deploy.bat` 実行前に View の DB 名置換を挿入（本 issue） |
| View の DB 名 | `KaiosDB` のまま pilot へ適用され、pilot から STG/本番 DB を参照してしまう | `KaiosDB_pilot` へ自動置換（本 issue） |

## Tech Stack

Issue #25 と同一。追加の依存関係なし。

- バックエンド: ASP.NET Core 8（`WebSourceDeployService` / `WebSourcePrepareController` を拡張）
- コピーツール: robocopy（既存 `RunRobocopyAsync` を再利用。常に `/E`）
- フロントエンド: React 18 + TypeScript + Vite（既存 `WebSourcePrepare.tsx` を拡張）
- 進捗通知: SSE（既存の仕組み）
- 設定管理: `appsettings.json` の `DbConfigs`
- 自動テスト基盤: 未導入（`dotnet build` / `npm run build` ＋ ローカルダミーフォルダ・DryRun での手動確認）

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## 実装スコープ（変更・新規ファイル）

| ファイル | 変更内容 |
|---------|---------|
| `backend/Models/DbConfig.cs` | `CommonImagePath`（画像コピー元）、`PilotTarget.DestImagePath`（画像コピー先）、`PilotSqlDbNameReplacements`（View の DB 名置換ルール）を追加 |
| `backend/appsettings_sample.json` | kaios / gos に上記設定を追記（View 置換ルールは両システムとも `KaiosDB` → `KaiosDB_pilot` の1件） |
| `backend/Services/WebSourceDeployService.cs` | ①ターゲットループ内に共通画像コピーを追加、②`RunSqlDeployAsync` に View ソース置換ステップを追加、③`BuildArguments` の `/MT:8` を `/MT:32` へ変更 |
| `backend/Models/WebSourcePrepareModels.cs` | `info` レスポンスに画像コピー元・コピー先を追加 |
| `backend/Controllers/WebSourcePrepareController.cs` | 変更は最小（画像コピー結果は既存ターゲット結果に内包するため、履歴記録の構造は変えない） |
| `frontend/src/api/webSourcePrepare.ts` | `ApiWebSourceInfo` に画像パス情報の型を追加 |
| `frontend/src/pages/WebSourcePrepare.tsx` | コピー元・コピー先表示に画像パスを追加、説明文を更新 |
| `docs/issue27/SPEC.md` / `docs/issue27/PLAN.md` | 本仕様書・実装計画 |

## 機能仕様 1: 画像コピー

### 設定

```jsonc
{
  "Name": "kaios",
  // STG 側の共通画像フォルダ（コピー元）。kaios / gos とも同じフォルダを共用する。
  "CommonImagePath": "E:\\Kaios_Image\\Common_Image",
  "PilotTargets": [
    {
      "Name": "pilot1",
      "DestWebSourcePath": "\\\\10.194.5.64\\WWW_KAIOS_pilot",
      // pilot 側の画像コピー先（導出せず明示設定する）
      "DestImagePath": "\\\\10.194.5.64\\WWW_KAIOS_pilot\\Images\\products"
    },
    {
      "Name": "pilot2",
      "DestWebSourcePath": "\\\\10.194.5.65\\WWW_KAIOS_pilot",
      "DestImagePath": "\\\\10.194.5.65\\WWW_KAIOS_pilot\\Images\\products"
    }
  ]
}
```

Issue #27 記載のコピー先（gos も `CommonImagePath` は同一フォルダ）:

| システム | pilot1 | pilot2 |
|---------|--------|--------|
| kaios | `\\10.194.5.64\WWW_KAIOS_pilot\Images\products` | `\\10.194.5.65\WWW_KAIOS_pilot\Images\products` |
| gos | `\\10.194.5.67\WWW_GOS_pilot\Images\products` | `\\10.194.5.68\WWW_GOS_pilot\Images\products` |

### 処理

- 位置づけは **Webソースコピーの一部**。実行内容の選択肢（`両方` / `Webソースコピーのみ` / `SQL適用のみ`）は
  現状の3択のまま変更せず、`両方` と `Webソースコピーのみ` で画像コピーも実行される
- ターゲットごとの実行順序:
  1. `WebSourcePath` → `DestWebSourcePath`（既存）
  2. `FilesDeploy2PrdPath` → `DestWebSourcePath`（既存）
  3. **`CommonImagePath` → `DestImagePath`（本 issue で追加）**
  4. パイロット用 `Web.config.DC.{Name}.pilot` を `web.config` として適用（`docs/pilot-webconfig-file-swap`）
- 2 と 3 で対象が重複した場合、**後に実行される共通画像コピー側が上書き（後勝ち）** となる
- コピーは既存 `RunRobocopyAsync` をそのまま使用（`/E /MT:32 /R:2 /W:5 /NP /XX` ＋ `WebSourceDeploy:ExcludeFiles`/`ExcludeDirs`）。
  除外設定は Webソースコピーと共通のものを適用する（画像の拡張子は除外対象に含まれないため実害なし）
- 画像はファイル件数が多くなるため、robocopy のマルチスレッド数を現行の `/MT:8` から **`/MT:32` へ引き上げる**
  （運用担当が手動実施していた値と同じ）。`RunRobocopyAsync` は全コピー共通のため、Webソースコピー・Files コピー・
  SQL コピーも同じ `/MT:32` になる（§ robocopy のスレッド数 参照）
- `/MIR` は使用しない（pilot 側にのみ存在する画像は削除しない）
- パス検証は既存 `ValidateDeployPaths`（空文字・相対パス・src=dest 一致・ローカルドライブルート指定を拒否）
- `CommonImagePath` または `DestImagePath` が未設定の場合、そのターゲットの画像コピーはスキップし、
  スキップした旨をログに出力する（オプトイン）
- robocopy 終了コード 0〜7 は成功、8 以上はエラー
- **失敗時はそのターゲットを失敗として扱い、以降（次ターゲット・SQL適用）を中断する**（既存の Files コピー失敗時と同じ）
- 履歴（`WebSourceDeployLog`）は既存の pilot1 / pilot2 レコードに内包する。失敗時は
  エラーメッセージで「画像コピーの失敗」と判別できる文言（例: `画像コピー robocopy exit code 8`）を記録する
- `DryRun=true` のときは実ファイルへ書き込まない（既存 `RunRobocopyAsync` の DryRun 動作をそのまま利用）

## robocopy のスレッド数（/MT）

`WebSourceDeployService.BuildArguments` の `/MT:8` を **`/MT:32` に変更する**（固定値。設定項目は増やさない）。

- 理由: 共通画像フォルダは小サイズ・大量ファイルになりやすく、ネットワーク越し（UNC）のコピーでは
  スレッド数を上げた方が明確に速い。運用担当が手動 FastCopy/robocopy で実施していた際も `/MT:32` を使用していた
- `RunRobocopyAsync` は全コピーで共通のため、Webソースコピー・Files コピー・SQL コピーにも同じ値が適用される
- robocopy の `/MT` の許容範囲は 1〜128。32 はその範囲内
- 影響: `/MT` を上げるとスレッドごとに出力がまとまるため、SSE ログの行順は現在（`/MT:8`）よりさらに前後しやすくなる。
  ログは進捗確認用であり順序に依存した処理は行っていないため、機能上の問題はない

## 機能仕様 2: Viewのソース更新（DB 名置換）

### 背景

pilot の DB 名は `KaiosDB_pilot`。View 定義内に `KaiosDB.dbo.XXX` のような 3 部名称や `USE [KaiosDB]` が
含まれていると、pilot に適用しても STG/本番側 DB を参照してしまうため、適用直前に DB 名を差し替える。

gos システムの View も `KaiosDB` を参照しているため、**kaios / gos の両システムが本処理の対象**となる。
置換するのは `KaiosDB` 参照のみで、`GosDB` は置換しない（gos の pilot 適用でも DB 名 `GosDB` はそのまま）。

### 設定

置換ルールはシステム名ではなく「置換元 DB 名 → 置換先 DB 名」のリストとして `DbConfig` ごとに持つ。
kaios / gos とも同じ1件のルールを設定する。

```jsonc
// kaios / gos の両方に同じ設定を入れる
{
  "Name": "gos",
  "PilotSqlDbNameReplacements": [
    { "From": "KaiosDB", "To": "KaiosDB_pilot" }
  ]
}
```

- コードにシステム別の分岐は設けない。設定が空（未設定）の場合は本処理をスキップし、従来どおりの SQL 適用を行う

### 処理

実行タイミングは SQL適用ステップの中。`Deploy2PrdPath` → `PilotSqlDeployPath\Source` へのコピー完了後、
`deploy.bat` 実行前に行う。

1. `PilotSqlDeployPath\Source` 配下の `*.sql` を列挙する
2. 各ファイルの内容に `CREATE VIEW` / `ALTER VIEW` / `CREATE OR ALTER VIEW`（大文字小文字を区別しない。
   識別子や空白の揺れを許容する正規表現で判定）が含まれるものだけを対象とする
3. 対象ファイルについて、各置換ルールの `From` を `To` に置換する
   - 正規表現 `(?<![A-Za-z0-9_])KaiosDB(?![A-Za-z0-9_])`（`RegexOptions.IgnoreCase`）でマッチさせる
   - この境界指定により、`KaiosDB.dbo.X` / `[KaiosDB].[dbo].[X]` / `USE KaiosDB` はすべて対象になり、
     `KaiosDB_pilot`（適用済み）・`KaiosDB2` / `MyKaiosDB` のような別名は対象外になる
   - 表記ゆれ（`kaiosdb` 等）もヒットさせ、**置換後は設定値の表記（`KaiosDB_pilot`）に統一**する
4. 置換対象が 0 件のファイル・0 件の実行でも正常として続行する（置換不要な View は当然あるため）。
   置換したファイル数・箇所数は SSE ログに出力する
5. **書き換えるのはコピー先（`PilotSqlDeployPath\Source`）のみ**。コピー元（`Deploy2PrdPath`）や
   Git リポジトリのソースは一切変更しない
6. 文字コード・改行コードは元ファイルの状態を維持する
   - BOM あり: UTF-8 / UTF-16 LE・BE を検出し、同じエンコーディングで書き戻す
   - BOM なし: Shift-JIS → UTF-8（BOMなし）の順でラウンドトリップ検証し、元バイト列を再現できる方を採用する
   - どちらでも再現できない場合は置換せず警告ログを出してスキップする（無自覚な文字化け書き戻しを防ぐ）
7. `DryRun=true` のときはファイルへ書き込まず、「置換予定」としてログにのみ出力する。
   DryRun では robocopy が Source へ実コピーしないため、プレビューの走査対象は `Deploy2PrdPath` とする
   （書き込みは行わない。本番実行時の書き込み対象は常に `PilotSqlDeployPath\Source`。走査対象ディレクトリはログに出力する）

## データフロー（Issue #25 からの差分を【追加】で表記）

```
POST /api/web-source-prepare/{dbName}/stream
  → WebSourceDeployService.ExecuteAsync
     for each target in PilotTargets（pilot1 → pilot2 の順）:
       1. パス検証（ValidateDeployPaths）
       2. WebSourcePath        → target.DestWebSourcePath へ robocopy /E
       3. FilesDeploy2PrdPath  → target.DestWebSourcePath へ robocopy /E
       4.【追加】CommonImagePath → target.DestImagePath へ robocopy /E
                （未設定ならスキップ。失敗時はターゲット失敗として以降を中断）
       5. パイロット用 Web.config.DC.{Name}.pilot を web.config として適用
          （docs/pilot-webconfig-file-swap。旧: connectionStrings 置換）
       6. ターゲット結果を WebSourceDeployLog へ記録（画像コピー結果を内包）

     SQL適用ステップ（Webソースコピーが全成功した場合のみ / step=sql なら無条件）:
       7. PilotSqlDeployPath\Source を空にする
       8. Deploy2PrdPath → PilotSqlDeployPath\Source へ robocopy /E
       9.【追加】Source 配下の View 定義 SQL の DB 名を置換（KaiosDB → KaiosDB_pilot）
                （PilotSqlDbNameReplacements 未設定ならスキップ）
       10. deploy.bat を実行
       11. SQL適用結果を WebSourceDeployLog へ記録（TargetName="sql"）

     done イベントで targets / sqlDeploy を返す（構造は既存のまま）
```

## 受け入れ条件（Acceptance Criteria）

### 画像コピー

- [x] `CommonImagePath`（システム単位）と `PilotTargets[].DestImagePath`（ターゲット単位）を `appsettings.json` で設定できる
- [x] `両方` / `Webソースコピーのみ` で実行したとき、pilot1 → pilot2 の順に共通画像がコピーされる
- [x] コピーは robocopy `/E`（削除同期なし）で行われ、`/MIR` は使用されない
- [x] robocopy のスレッド数が `/MT:32` になっている（Webソースコピー・Files コピー・SQL コピーを含む全コピー共通）
- [x] Files コピー（`FilesDeploy2PrdPath`）の後に画像コピーが実行される（重複時は共通画像側が後勝ち）
- [x] コピー元・コピー先パスが `ValidateDeployPaths` で検証される
- [x] 進捗が SSE ログに表示され、どのターゲットの画像コピーかが区別できる
- [x] robocopy 終了コード 8 以上のとき、そのターゲットを失敗として以降（次ターゲット・SQL適用）を中断する
- [x] 失敗内容が「画像コピーの失敗」と判別できる形で履歴（既存の pilot1/pilot2 レコード）に記録される
- [x] `CommonImagePath` または `DestImagePath` 未設定時はスキップし、その旨をログ出力する
- [x] `DryRun=true` のとき実ファイルへ書き込まない
- [x] 画面のコピー元・コピー先表示に画像パスが追加されている

### Viewのソース更新

- [x] `PilotSqlDeployPath\Source` へのコピー後・`deploy.bat` 実行前に置換が行われる
- [x] `CREATE VIEW` / `ALTER VIEW` / `CREATE OR ALTER VIEW` を含む `.sql` のみが対象になる
- [x] `KaiosDB.dbo.X` / `[KaiosDB].[dbo].[X]` / `USE KaiosDB` が `KaiosDB_pilot` を参照する形に置換される
- [x] `KaiosDB_pilot` / `KaiosDB2` / `MyKaiosDB` は置換されない（二重変換・誤変換をしない）
- [x] 大文字小文字の表記ゆれもヒットし、置換後は設定値の表記に統一される
- [x] 置換対象が 0 件でも正常として続行し、置換ファイル数・箇所数がログに出る
- [x] コピー元 `Deploy2PrdPath` および Git リポジトリのファイルは変更されない
- [x] 元ファイルの文字コード（BOM ありは維持、BOM なしは SJIS→UTF-8 のラウンドトリップ検証。判定不可はスキップ＋警告）と改行が維持される
- [x] `DryRun=true` のとき書き込まず、置換予定のみログ出力される（プレビュー走査は `Deploy2PrdPath`。走査対象ディレクトリをログ出力。実書き込みは常に `Source` のみ）
- [x] エンコーディング判定不可でスキップした件数はサマリに含め、WARN レベルで目立つようにする（robocopy DETAIL に埋もれない）
- [x] kaios / gos のどちらの適用でも同じルール（`KaiosDB` → `KaiosDB_pilot`）で置換される
- [x] gos の適用時、`GosDB` は置換されない
- [x] `PilotSqlDbNameReplacements` 未設定のとき、置換ステップは行われず従来どおり適用される

## Boundaries

- **Always**
  - robocopy は常に `/E`（削除同期なし）。`/MIR` は使用しない
  - コピー前に `ValidateDeployPaths` でパス検証を行う
  - `DryRun=true` 時は画像コピー・SQL 置換のどちらも実ファイルへ書き込まない
  - SQL の置換はコピー先（`PilotSqlDeployPath\Source`）に対してのみ行う
  - 実行結果を履歴（`WebSourceDeployLog`）へ記録する
- **Ask first**
  - 実行内容の選択肢（3択）を増やす必要が生じた場合
  - View 以外のオブジェクト（StoredProcedure / Function 等）も置換対象に含める必要が生じた場合
  - 画像コピー先を設定ではなく `DestWebSourcePath` からの導出に変える場合
- **Never**
  - 画像コピー先で `/MIR` による既存ファイル削除を行わない
  - `deploy.bat` の内容を本システムが生成・書き換えしない（Issue #25 の方針を継承）
  - コピー元資材（`Deploy2PrdPath`・Git リポジトリ）を書き換えない
  - View 以外の SQL ファイルを無断で置換対象にしない

## 検証方針

- `dotnet build` / `npm run build` が通ること
- ローカルにダミーフォルダ（コピー元画像・pilot 相当のコピー先・`PilotSqlDeployPath\Source` 相当）を用意し、
  `DryRun=true` および `DryRun=false` で以下を確認する
  - 画像がコピーされること、DryRun ではコピーされないこと
  - View 判定と DB 名置換が期待どおり動作すること（`KaiosDB_pilot` の二重変換が起きないこと）
  - 元ファイルの文字コード・改行・置換対象外の行が変化しないこと
- 実サーバー（pilot1/pilot2、実 `deploy.bat`）での動作確認は本スコープ外（Issue #25 と同様に別途手動で実施）

## 決定事項（ヒアリング結果）

| # | 論点 | 決定 |
|---|------|------|
| 1 | gos の画像コピー元 | kaios と同じ `E:\Kaios_Image\Common_Image` を共用する |
| 2 | 画像コピー先の設定方法 | `PilotTargets[].DestImagePath` として明示設定する（導出しない） |
| 3 | 実行内容の選択肢 | 増やさない。画像コピーは Webソースコピーの一部として実行する |
| 4 | Files コピーとの重複 | 重複可。Files コピーの後に画像コピーを実行し、共通画像側を後勝ちとする |
| 5 | 画像コピー失敗時 | そのターゲットを失敗として以降（次ターゲット・SQL適用）を中断する |
| 6 | View の判定方法 | ファイル内容に `CREATE`/`ALTER VIEW` を含むものを View とみなす |
| 7 | 置換対象の書式 | 単語境界付きで `KaiosDB` / `[KaiosDB]` の両方。`_pilot` 付きや `KaiosDB2` は対象外 |
| 8 | 大文字小文字 | 区別せずヒットさせ、置換後は設定値の表記に統一する |
| 9 | 置換 0 件時 | 正常として続行（件数をログ出力） |
| 10 | gos の DB 名置換 | gos の View も `KaiosDB` を参照するため、gos にも `KaiosDB` → `KaiosDB_pilot` のルールを設定する。`GosDB` → `GosDB_pilot` の置換は行わない |
| 11 | 履歴記録 | 画像コピー結果は既存の pilot1/pilot2 レコードに内包する |
| 12 | 除外パターン | 既存の `WebSourceDeploy:ExcludeFiles`/`ExcludeDirs` をそのまま適用する |
| 13 | 検証範囲 | ローカルダミーフォルダ＋DryRun まで。実サーバー確認は別途手動 |
| 14 | robocopy のスレッド数 | `/MT:8` → `/MT:32` に固定値を変更。設定項目は増やさず、全コピー（Webソース・Files・SQL・画像）で共通とする |

## Open Questions（残課題）

1. gos 側の実際の `WWW_GOS_pilot` 共有ルートパス（`DestWebSourcePath`）が Issue #25 時点で未確定のため、
   `DestImagePath` の実値は運用担当の確認後に `appsettings.json` へ設定する（サンプル値のみコミットする）
2. 実サーバー（pilot1/pilot2）への UNC 到達性・書き込み権限は Issue #25 から引き続き未確認
