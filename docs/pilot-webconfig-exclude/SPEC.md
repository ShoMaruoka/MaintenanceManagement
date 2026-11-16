# Spec: Pilot環境適用の Web.config / Local config コピー除外と PAF・Duskin 対応

## 関連ドキュメント

- `docs/pilot-webconfig-bat/SPEC.md`（現行仕様: web.config 切替を pilot 側 bat で実行。本 Spec はその上に追加する）
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`（Issue #25: Pilot環境適用の全体仕様）
- `docs/issue35/SPEC.md`（ダッシュボードの Pilot 最終適用カード）
- Webソース側: `kaien_rep_scm/deploy/README.txt`, `deploy/copyLocalOverlay.bat`

## Objective

1. **Web.config をコピーしない**
   いまの Web.config には環境によって値が違う FTP 設定が入っている。STG の Web.config をそのまま robocopy すると、介援隊 pilot の FTP 設定が全 pilot に配布されてしまう。
   FTP 設定は今後直す予定があるが、それまではルート直下の `Web.config` をコピーしないことで回避する。この除外は**画面から ON/OFF でき、設定を DB に保存する**。
2. **Local config 2 ファイルを常にコピーしない**
   `AppSettings.Local.config` / `connectionStrings.local.config` は STG 用の値になっている。これらはルート直下に限り、常にコピー対象から外す（固定・設定不可）。pilot 用の値は、これまでどおり切替 bat が overlays から書き込む。
3. **PAF / Duskin を対象に追加する**
   Pilot環境適用の対象を kaios / gos から **kaios / gos / paf / duskin** に増やす。

- **利用者**: 運用担当者（「Pilot環境適用」画面）
- **成功の定義**:
  - Web ソースの robocopy で、コピー元ルート直下の `AppSettings.Local.config` / `connectionStrings.local.config` が常にコピーされない
  - 「Web.config をコピーしない」設定が ON のとき、コピー元ルート直下の `Web.config` がコピーされない。OFF のときはコピーされる
  - 設定は DB に保存され、アプリを再起動しても残る
  - サブフォルダにある同じ名前のファイル、画像・Files・SQL のコピーには影響しない
  - paf / duskin で Pilot環境適用（Web / SQL / 両方）が実行でき、`deploy\Paf.DC.Pilot.bat` / `deploy\Duskin.DC.Pilot.bat` が実行される
  - ダッシュボードに Pilot 最終適用のカードが 4 システム分表示される

## 背景・前提（確認済み）

| 項目 | 内容 |
|------|------|
| 環境ごとの値の置き場所 | `AppSettings.Local.config`（SiteName / MaintenanceMode / 機能フラグ）と `connectionStrings.local.config`（接続先）。Web.config は `configSource` / `file` 属性でこの 2 つを読み込む |
| 例外 | FTP 設定だけはいまも Web.config に直接書かれていて、環境ごとに値が違う（今後直す予定） |
| 切替 bat の処理 | `copyLocalOverlay.bat` が overlays の 2 ファイルをサイト直下にコピーし、Web.config の更新日時を変える（IIS が再起動する）。**コピー先に Web.config が無いとエラー（exit 1）になる** |
| bat の実行場所 | STG サーバー（本システム）の cmd.exe から UNC 経由で実行する。B・C サーバー側でプロセスは起動しない。ファイル操作は SMB 経由で行う |
| 4 ブランドの bat | `Kaios` / `Gos` / `Paf` / `Duskin` の `.DC.Pilot.bat` と overlays がすべて Web ソースにある |

## 設計方針

### 1. robocopy の除外（Web ソースのコピーだけ）

- `RunRobocopyAsync` に「ルート直下の除外ファイル」を渡せるようにする。Web ソースのコピー（`WebSourcePath` → `DestWebSourcePath`）のときだけ使う。
- 除外は**コピー元のフルパス**で `/XF` に追加する。例: `/XF ... "{WebSourcePath}\Web.config" "{WebSourcePath}\AppSettings.Local.config" "{WebSourcePath}\connectionStrings.local.config"`
  - ファイル名だけで指定すると、サブフォルダの同じ名前のファイルまで除外されるため。
- 画像情報準備・共通画像・SQL のコピーの引数は変えない。既存の `WebSourceDeploy:ExcludeFiles` / `ExcludeDirs` も変えない。
- 除外したファイルは、ターゲットごとにログ（INFO）へ出す。例: `pilot1: コピー除外 Web.config / AppSettings.Local.config / connectionStrings.local.config`

### 2. 「Web.config をコピーしない」設定（DB に保存）

- SQLite に設定テーブルを追加する（`EnsureCreated` の `CREATE TABLE IF NOT EXISTS` に追記）。

  ```sql
  CREATE TABLE IF NOT EXISTS AppSetting (
      Key       TEXT PRIMARY KEY,
      Value     TEXT NOT NULL,
      UpdatedBy TEXT NOT NULL,
      UpdatedAt TEXT NOT NULL
  );
  ```

  - キー: `PilotExcludeWebConfig.{dbName}`（例: `PilotExcludeWebConfig.kaios`）。値: `"true"` / `"false"`
  - **キーが無いときは `true`（除外する）として扱う**。導入した直後から安全側で動かすため。
- API（`WebSourcePrepareController`）
  - `GET /api/web-source-prepare/{dbName}/info` の応答に `excludeWebConfig: boolean` を追加する
  - `PUT /api/web-source-prepare/{dbName}/settings` のボディは `{ excludeWebConfig: boolean, updatedBy: string }`。DB に保存し、保存後の値を返す
    - **admin に限る**: `updatedBy` を `AppUser.UserName` で調べ、`Role = 'admin'` でなければ **403** を返し、保存しない（ユーザーが見つからない場合も 403）
    - 注意: 本システムにはサーバー側の認証が無く、admin の判定は画面とリクエストの自己申告で行っている（ユーザー管理画面と同じ水準）。悪意のある偽装は防げないが、うっかり変更してしまうことは防げる
  - `info` の応答に `excludeWebConfigUpdatedBy` / `excludeWebConfigUpdatedAt` を追加する（設定が無いときは null）
- 実行時（`StreamDeploy`）は**実行開始の時点で DB から値を読み**、`ExecuteAsync` に渡す。画面から送られた値は使わない。
- 開始ログに設定値を出す。例: `Web.config コピー: しない（設定: ON）`。この出力は既存の LogDetail を通じて履歴にも残る。

### 3. 画面（`WebSourcePrepare.tsx`）

- 選択中のシステムについて、「Web.config をコピーしない」のトグル（チェックボックス）を表示する。変更したらすぐ PUT で保存する。
  - トグルを操作できるのは `currentRole === 'admin'` のときだけ。admin 以外には現在の値を読み取り専用で表示する（「変更は管理者のみ」と添える）
  - 最終更新者と更新日時を併記する
  - PUT が 403 などで失敗したらエラーを表示し、トグルを元の値に戻す
- 説明文を更新する: Local config 2 ファイルは常にコピーしないこと、Web.config は設定に従うこと、bat で pilot 用の値に切り替えること。
- 設定が ON のときは注意文を出す: 「開発での Web.config の変更は pilot に反映されません（手作業での反映が必要です）」
- 適用の実行中は、トグルを変更できないようにする。

### 4. PAF / Duskin の追加

| 箇所 | 変更内容 |
|------|----------|
| `WebSourcePrepareController.AllowedDbNames` | `["kaios", "gos", "paf", "duskin"]` |
| `frontend/src/api/webSourcePrepare.ts` | `WebSourceDbName` に `'paf' \| 'duskin'` を追加 |
| `WebSourcePrepare.tsx` | `DB_OPTIONS` に paf / duskin を追加。説明文の bat の例も更新 |
| `appsettings_sample.json` | paf / duskin に `WebSourcePath` / `CommonImagePath` / `PilotTargets` / `PilotSqlDeployPath` / `PilotMariaDbSqlDeployPath` / `PilotSqlDbNameReplacements` の仮の値を追加（例: `PafDB` → `PafDB_pilot`） |
| `appsettings.json`（実環境） | 実際の値は運用側が手元で設定する（本対応では編集しない） |
| bat の名前 | 変更なし（`{Name}.DC.Pilot.bat`。大文字と小文字は区別しない） |

### 5. ダッシュボード

- `DashboardStats.LastPilotKaios` / `LastPilotGos` を、**`LastPilots: List<PilotDeploySummary>`**（kaios / gos / paf / duskin の順。履歴が無いシステムは `ExecutedAt` を空にする）に置き換える。
  - フロントとバックエンドは同時に配布するので、互換用のプロパティは残さない。
- `Dashboard.tsx` は `lastPilots` からカードを 4 枚描く。並びが崩れないかは画面幅を変えて確認する。

## Tech Stack / Commands

既存のまま。依存は追加しない。

```
Backend Build:  cd backend && dotnet build
Backend Test:   cd backend/Tests && dotnet test
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## Testing Strategy

| レベル | 方針 |
|--------|------|
| 単体（robocopy 引数） | Web ソースのコピーで、Local config 2 つのフルパスが常に `/XF` に入ること。`excludeWebConfig=true/false` で Web.config のフルパスが入るか入らないかが切り替わること。画像・Files のコピーの引数は変わらないこと |
| 単体（設定） | `DatabaseService` で、キーが無いときは true、保存した値が読めること、上書きできること |
| 単体（Controller） | paf / duskin が許可されること。それ以外（例: `foo`）は 404 になること。設定の PUT で、admin なら保存されること。user や存在しないユーザーなら 403 になり、保存されないこと |
| 単体（ダッシュボード） | `DatabaseServiceDashboardStatsTests` を 4 システム分の形に合わせて更新する |
| 結合（手動） | DryRun で、`[DRY-RUN] robocopy` の引数に除外が入っていること。トグルを変えると次回実行の引数が変わること。paf / duskin で DryRun が通ること |
| 実環境（運用） | pilot の Web.config（FTP 設定）が変わらないこと。bat 実行後に Local config が pilot 用になること |

## Boundaries

- **Always**
  - 除外はコピー元ルート直下のフルパスで指定する
  - Local config 2 ファイルは設定に関係なく常に除外する
  - 実行時の Web.config 除外は DB の値で決める
  - 切替 bat はこれまでどおり、各ターゲットで 1 回実行する
- **Ask first**
  - 画像・Files・SQL のコピーの除外ルールを変えること
  - 設定テーブルを Pilot 以外の用途に広げること
  - bat の名前の付け方や引数を変えること
- **Never**
  - アプリから pilot の Web.config / Local config の中身を書き換えること
  - 実環境の `appsettings.json` を編集すること
  - git commit / push（ユーザーの指示があるまで）

## 処理フロー（変更後）

各 `PilotTarget` について:

1. `WebSourcePath` → `DestWebSourcePath` を robocopy する。**除外: Local config 2 ファイル（常に）＋ Web.config（設定が ON のとき）**
2. 画像情報準備 `FilesPath\{Images,news,pdf}` → `DestWebSourcePath\{同名}`（変更なし）
3. 共通画像 `CommonImagePath` → `DestImagePath`（変更なし）
4. `DestWebSourcePath\deploy\{Name}.DC.Pilot.bat` を実行する（変更なし）
5. 次のターゲットへ。全ターゲットが成功したら SQL 適用（変更なし）

## 運用上の注意

### pilot への Web.config の初回配置（手作業）

- Web.config の除外が ON の間、本システムは pilot の `DestWebSourcePath\Web.config` を作成も上書きもしない。
- 切替 bat（`deploy\{Name}.DC.Pilot.bat` → `copyLocalOverlay.bat`）は Web.config が無いとエラーになる。新しい pilot を用意したときや Web.config が無いときは、**Pilot環境適用の前に、運用担当者が pilot 用の Web.config（FTP 設定はその pilot のもの）を `DestWebSourcePath` 直下に手作業で置く**。
- `AppSettings.Local.config` / `connectionStrings.local.config` は bat が `deploy\overlays` から書き込むため、手作業での配置は不要。

### FTP 設定が直った後に除外を OFF にする手順

Web.config の FTP 設定が環境に依存しなくなり、STG の Web.config をそのまま pilot に配ってよい状態になったら、次の手順で戻す。

1. admin ユーザーで画面に入り、「Pilot環境適用」でシステムを選ぶ
2. 「Web.config の扱い」のチェックを外す（OFF = コピーする）。設定はシステムごとなので、対象のシステムそれぞれで行う
3. 「最終更新」に変更者と日時が出ることを確認する
4. Pilot環境適用を実行し、ログに `Web.config コピー: する（設定: OFF）` が出ることと、除外の一覧が Local config 2 ファイルだけになっていることを確認する
5. pilot 側で Web.config の FTP 設定が想定どおりであること、アプリが動くことを確認する

Local config 2 ファイルの除外は設定に関係なく常に有効で、OFF にしても変わらない。

## Success Criteria

- [x] Web ソースのコピーで、Local config 2 ファイルがルート直下に限り常に除外される
- [x] Web.config の除外をシステムごとに画面から切り替えられ、DB に保存され、次回の実行から反映される（初期値は ON）
- [x] 設定を変更できるのは admin だけ（画面ではトグルを操作不可にし、API は 403 を返す）
- [x] 実行ログに Web.config の除外設定と除外したファイルが出る
- [x] paf / duskin を画面で選んで Pilot環境適用を実行できる
- [x] ダッシュボードに Pilot 最終適用カードが 4 システム分出る
- [x] `dotnet build` / `dotnet test` / `npm run build` が成功する
- [x] バージョンを更新する（VERSIONING_GUIDE に従う。1.8.0）

※ 単体テストとビルドで確認済み。実 robocopy・画面操作・画面幅の手動確認（Checkpoint A / B）は別途実施する。

## Open Questions

なし（確認事項は回答済み）。

| # | 確認事項 | 決定 |
|---|----------|------|
| 1 | Web.config の除外設定は、システムごとか全体で 1 つか | **システムごと** |
| 2 | 設定の変更を admin に限るか | **admin に限る**（画面ではトグルを操作不可、API は 403。変更者と日時を記録する） |
| 3 | pilot 側に Web.config が無い初回の配置 | **運用で手作業で置く**。bat は Web.config が無いとエラーになる。本システムでは対応しない |
| 4 | PAF / Duskin の設定値 | appsettings_sample には仮の値を入れる。実際の値は運用側が手元で設定する |
| 5 | ダッシュボード | Pilot 最終適用カードを 4 システム分にする |

---

**Status**: 実装完了（v1.8.0、T1〜T7）。Checkpoint A / B の手動確認待ち。commit はユーザーの指示待ち。
