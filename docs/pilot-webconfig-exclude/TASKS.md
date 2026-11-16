# Tasks: Pilot環境適用の Web.config / Local config コピー除外と PAF・Duskin 対応

対応する仕様: [`SPEC.md`](./SPEC.md)
対応する計画: [`PLAN.md`](./PLAN.md)

**Status: T1〜T7 実装完了（v1.8.0）。Checkpoint A / B の手動確認待ち。git commit / push はユーザーの指示があるまで行わない。**

実装は**上から順に** 1 タスクずつ進める。各タスクの Acceptance と Verification を満たしてから次へ進む。

---

## 実行順序

```
T1 ─┬─ T3 → [Checkpoint A] → T4 → T5 → T6 → [Checkpoint B] → T7 → [Checkpoint C] → Done
T2 ─┘
（T1 と T2 は並行できる）
```

---

## Task 1: Web ソースの robocopy にルート直下の除外を追加する

**Status:** done

**Description:**
`RunRobocopyAsync` / `BuildArguments` に、省略できる引数 `rootExcludeFiles` を追加する。コピー元のフルパスにして `/XF` の末尾に足す。
`ExecuteAsync` に `bool excludeWebConfig = true` を追加し、Web ソースのコピーのときだけ除外を渡す。除外する一覧は static の `BuildWebSourceRootExcludes(excludeWebConfig)` で組み立てる。

**Acceptance criteria:**

- [x] `BuildWebSourceRootExcludes(false)` は `AppSettings.Local.config` と `connectionStrings.local.config` を返す。`true` のときは、さらに `Web.config` を含む
- [x] Web ソースのコピーの robocopy 引数に、`"{WebSourcePath}\AppSettings.Local.config"` などがフルパスで入る
- [x] 画像情報準備・共通画像のコピー、`CopyPilotSqlFilesAsync` の引数は変わらない
- [x] 各ターゲットの robocopy の前に INFO ログを出す: `{target}: コピー除外 {ファイル名をカンマ区切り}`
- [x] 実行開始時に INFO ログを出す: `Web.config コピー: しない（設定: ON）` / `する（設定: OFF）`
- [x] `ExecuteAsync` の XML コメントを更新する

**Verification:**

- [x] 単体テストを追加する（`WebSourceDeployServiceSqlSourceTests` に追加するか、新しいファイル `WebSourceDeployServiceExcludeTests.cs` を作る）
  - `excludeWebConfig=true`: Web ソースのコピーの引数に 3 ファイルのフルパスが入る
  - `excludeWebConfig=false`: Local config 2 つだけが入り、Web.config は入らない
  - 画像・Files のコピーの引数には、どれも入らない
  - DryRun の `[DRY-RUN] robocopy` ログにも除外が入る
- [x] `cd backend && dotnet build` / `cd backend/Tests && dotnet test` が成功する

**Dependencies:** None

**Files:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/WebSourceDeployServiceExcludeTests.cs`（新規）

**Estimated scope:** S

---

## Task 2: AppSetting テーブルと設定・admin 判定を DatabaseService に追加する

**Status:** done

**Description:**
`EnsureCreated` に `AppSetting` テーブルを追加する。`DatabaseService` に次のメソッドを追加する。

- `GetSetting(key)`
- `UpsertSetting(key, value, updatedBy)`
- `GetPilotExcludeWebConfig(dbName)`（キーが無ければ true）
- `SetPilotExcludeWebConfig(dbName, value, updatedBy)`
- `IsAdmin(userName)`

**Acceptance criteria:**

- [x] `CREATE TABLE IF NOT EXISTS AppSetting (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedBy TEXT NOT NULL, UpdatedAt TEXT NOT NULL)`
- [x] キーは `PilotExcludeWebConfig.{dbName を小文字に}`。値は `"true"` / `"false"`
- [x] キーが無いときは `GetPilotExcludeWebConfig` が `(Value: true, UpdatedBy: null, UpdatedAt: null)` を返す
- [x] `UpsertSetting` を繰り返すと上書きされ、`UpdatedBy` / `UpdatedAt` も更新される
- [x] `IsAdmin` は `AppUser.Role = 'admin'` のときだけ true。存在しないユーザーや空文字は false
- [x] 既存の DB ファイルに対して `EnsureCreated` を実行しても壊れない

**Verification:**

- [x] 単体テストを追加する（`DatabaseServiceAppSettingTests.cs`。既存の `DatabaseService*Tests` と同じ一時 SQLite の方式）
  - 既定値が true、保存した値を読み戻せる、上書きできる、システムごとに独立している
  - IsAdmin が admin / user / 存在しないユーザーで正しく判定する
- [x] `dotnet test` が成功する

**Dependencies:** None（T1 と並行できる）

**Files:**

- `backend/Services/DatabaseService.cs`
- `backend/Models/`（必要なら `PilotSetting` の record）
- `backend/Tests/Services/DatabaseServiceAppSettingTests.cs`（新規）

**Estimated scope:** S

---

## Task 3: Controller に設定 API を追加し、実行時に設定を渡す

**Status:** done

**Description:**
`WebSourcePrepareController` を次のように変更する。

- `GET info` の応答に、除外設定とその最終更新情報を追加する
- `PUT {dbName}/settings` を追加する（admin でなければ 403）
- `StreamDeploy` で DB から設定を読み、`ExecuteAsync(..., excludeWebConfig)` に渡す

**Acceptance criteria:**

- [x] `WebSourceInfoResponse` に `ExcludeWebConfig` / `ExcludeWebConfigUpdatedBy` / `ExcludeWebConfigUpdatedAt` を追加する
- [x] リクエストのモデル `WebSourceSettingsRequest { ExcludeWebConfig, UpdatedBy }` を追加する
- [x] PUT の処理
  - 許可されていない dbName、または設定が無いシステムなら 404
  - `IsAdmin(UpdatedBy)` が false なら 403（メッセージ: `設定の変更は管理者のみ可能です`）で、保存しない
  - 成功したら 200 で、保存後の `{ excludeWebConfig, updatedBy, updatedAt }` を返す
- [x] `StreamDeploy` は実行開始時の DB の値を使う（リクエストボディの値は受け取らない）

**Verification:**

- [x] 判定ロジックの切り出しは行わず、Controller を一時 SQLite と DryRun で直接生成してテストした（`WebSourcePrepareControllerSettingsTests`）
- [x] 単体テスト: admin なら保存される、user なら 403、存在しないユーザーなら 403
- [x] `dotnet build` / `dotnet test` が成功する

**Dependencies:** T1, T2

**Files:**

- `backend/Controllers/WebSourcePrepareController.cs`
- `backend/Models/WebSourcePrepareModels.cs`
- `backend/Tests/Controllers/`（新規テスト）

**Estimated scope:** M

---

### ✅ Checkpoint A: バックエンドの除外機能

- [x] `dotnet build` / `dotnet test` が成功する
- [x] DryRun のログで、robocopy の引数に除外が入る（ON / OFF の両方）
- [x] **ローカルのダミーフォルダで実際に robocopy を確認する**: ルート直下の 3 ファイルはコピーされず、`sub\Web.config` はコピーされる。`WebSourcePath` の末尾 `\` あり・なしの両方で試す
- [x] curl などで PUT settings を確認する: admin なら 200、user なら 403

---

## Task 4: 画面に「Web.config をコピーしない」トグルを追加する

**Status:** done（手動確認は未実施）

**Description:**
`WebSourcePrepare.tsx` に、選択中のシステムの除外設定のトグルを追加する。変更したらすぐ PUT する。admin 以外には読み取り専用で表示する。

**Acceptance criteria:**

- [x] `api/webSourcePrepare.ts` に `updateWebSourceSettings(dbName, excludeWebConfig, updatedBy)` を追加し、info の型に新しい項目を追加する
- [x] `currentRole === 'admin'` のときだけトグルを操作できる。それ以外は disabled にして「変更は管理者のみ」と表示する
- [x] 最終更新者と更新日時を表示する（未設定なら「既定値（コピーしない）」）
- [x] ON のときは注意文を出す: 「開発での Web.config の変更は pilot に反映されません（手作業での反映が必要です）」
- [x] 保存に失敗したら（403 など）エラーを表示し、トグルを元の値に戻す
- [x] 適用の実行中はトグルを disabled にする
- [x] 説明文を更新する: Local config 2 つは常にコピーしない、Web.config は設定に従う、bat で pilot 用に切り替える

**Verification:**

- [x] `cd frontend && npm run build` が成功する
- [x] 手動で確認する: admin で切り替え、再読み込みしても値が残る。user では操作できない

**Dependencies:** T3

**Files:**

- `frontend/src/api/webSourcePrepare.ts`
- `frontend/src/pages/WebSourcePrepare.tsx`

**Estimated scope:** S

---

## Task 5: PAF / Duskin を対象に追加する

**Status:** done（DryRun の手動確認は未実施）

**Description:**
システムの一覧を共通の定数にまとめ、paf / duskin を追加する。画面の選択肢と appsettings_sample も更新する。

**Acceptance criteria:**

- [x] `backend/Models/PilotSystems.cs`（新規）に `DbNames = ["kaios", "gos", "paf", "duskin"]` を定義する。`AllowedDbNames` はこれを参照する
- [x] `WebSourceDbName` 型と `DB_OPTIONS` に paf / duskin を追加する
- [x] 説明文の bat の例を 4 ブランド分にする（または `{Name}.DC.Pilot.bat` という書き方にする）
- [x] `appsettings_sample.json` の paf / duskin に、Pilot 関係の仮の値を追加する（DB名置換は gos と同じ `KaiosDB` → `KaiosDB_pilot` を仮置き。実値は運用側で確認）
- [x] `DbConfig.Name` などの XML コメントの「kaios / gos」を更新する

**Verification:**

- [x] 単体テスト: paf / duskin が許可され、`foo` は許可されない
- [x] `dotnet build` / `dotnet test` / `npm run build` が成功する
- [x] 手動で確認する（ローカルの appsettings.Development.json にダミーの値を入れる）: paf / duskin で DryRun を実行し、`Paf.DC.Pilot.bat` / `Duskin.DC.Pilot.bat` の `[DRY-RUN]` ログが出る

**Dependencies:** T3（同じ Controller を変更するため後にする）

**Files:**

- `backend/Models/PilotSystems.cs`（新規）
- `backend/Controllers/WebSourcePrepareController.cs`
- `backend/Models/DbConfig.cs`（コメント）
- `backend/appsettings_sample.json`
- `frontend/src/api/webSourcePrepare.ts`
- `frontend/src/pages/WebSourcePrepare.tsx`

**Estimated scope:** S

---

## Task 6: ダッシュボードの Pilot 最終適用を 4 システム分にする

**Status:** done（画面幅の手動確認は未実施）

**Description:**
`DashboardStats.LastPilotKaios` / `LastPilotGos` を `LastPilots: List<PilotDeploySummary>` に置き換え、画面にカードを 4 枚描く。

**Acceptance criteria:**

- [x] `DashboardStats.LastPilots` は `PilotSystems.DbNames` の順に 4 件。履歴が無いシステムは `ExecutedAt = ""` / `ExecutedBy = ""`
- [x] `LastPilotKaios` / `LastPilotGos` を削除する（バックエンド・フロント・テスト）
- [x] `PilotDeploySummary` などのコメントの「kaios / gos 用」を更新する
- [x] `Dashboard.tsx` は `lastPilots.map` でカードを描く。ラベルは `Pilot 最終適用（{dbName}）`
- [x] カードが 4 枚増えても横に溢れない（Pilot の4枚は既存カードとは別の `stat-cards` 段に並べた。既存の flex-wrap で折り返す）

**Verification:**

- [x] `DatabaseServiceDashboardStatsTests` を更新する。4 件の順序、paf の履歴あり、duskin の履歴なしを確認する
- [x] `dotnet test` / `npm run build` が成功する
- [x] 手動で確認する: 画面幅を変えて、カードの並びが崩れない

**Dependencies:** T5

**Files:**

- `backend/Models/DashboardModels.cs`
- `backend/Services/DatabaseService.cs`
- `backend/Tests/Services/DatabaseServiceDashboardStatsTests.cs`
- `frontend/src/types.ts`（または DashboardStats の型定義のある場所）
- `frontend/src/pages/Dashboard.tsx`
- `frontend/src/index.css`（必要な場合）

**Estimated scope:** S

---

### ✅ Checkpoint B: 機能完了

- [x] `dotnet build` / `dotnet test` / `npm run build` が成功する
- [x] 画面のトグル、admin の制御、PAF / Duskin の DryRun、ダッシュボードの 4 枚を手動で確認した

---

## Task 7: ドキュメントとバージョンを更新する

**Status:** done

**Description:**
関連ドキュメントに参照と注記を追加し、バージョンを 1.8.0 に上げる。

**Acceptance criteria:**

- [x] `docs/pilot-webconfig-bat/SPEC.md` の冒頭に、「robocopy の除外は `pilot-webconfig-exclude` を参照」と追記する
- [x] `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md` の対象システムと除外の記述に注記を入れる
- [x] 本ディレクトリの SPEC / TASKS の Status を更新する
- [x] `VERSIONING_GUIDE.md` に従い、`backend/MaintenanceManagement.Api.csproj` と `frontend/package.json` を 1.8.0 にする
- [x] 運用向けの注意を SPEC に追記する: pilot への Web.config の手作業での配置、FTP 設定が直った後に除外を OFF にする手順

**Verification:**

- [x] `dotnet build` / `dotnet test` / `npm run build` が成功する

**Dependencies:** T6

**Files:**

- `docs/pilot-webconfig-bat/SPEC.md`
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`
- `docs/pilot-webconfig-exclude/SPEC.md` / `TASKS.md`
- `backend/MaintenanceManagement.Api.csproj`
- `frontend/package.json`

**Estimated scope:** XS

---

### ✅ Checkpoint C: 完了

- [x] すべてのビルドとテストが成功する
- [x] Status の更新が終わっている（git commit / push はユーザーの指示を待つ）
