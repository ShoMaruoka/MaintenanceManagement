# Implementation Plan: Pilot環境適用の Web.config / Local config コピー除外と PAF・Duskin 対応

対応する仕様: [`SPEC.md`](./SPEC.md)
前提となる現行仕様: [`../pilot-webconfig-bat/SPEC.md`](../pilot-webconfig-bat/SPEC.md)

## Overview

次の 3 つを、それぞれ単独でビルド・テストできる単位で順に入れる。

1. **除外処理**（Service）
   Web ソースの robocopy に「コピー元ルート直下のファイルのフルパス除外」を追加する。Local config 2 つは常に除外し、Web.config は引数で指定されたときだけ除外する。
2. **除外設定**（DB / API / 画面）
   `AppSetting` テーブルに、システムごとの `PilotExcludeWebConfig.{dbName}` を保存する。変更は admin だけ。実行時は DB の値を読んで Service に渡す。
3. **対象拡大**
   PAF / Duskin を許可し、ダッシュボードのカードを 4 システム分にする。

切替 bat の実行、画像・Files・SQL のコピー、SQL 適用の流れは変えない。

## Architecture Decisions

1. **除外は呼び出し側から明示的に渡す**
   `RunRobocopyAsync(src, dest, onOutputLine, ct, rootExcludeFiles = null)` のように、省略できる引数を足す。`BuildArguments` は、渡されたファイル名を `Path.Combine(src, name)` でフルパスにして `/XF` の末尾に追加する。
   - 画像・Files のコピーはこの引数を渡さないので、引数が変わらないことが構造上保証される。
   - 除外するファイル名の一覧は `ExecuteAsync` 側で 1 回だけ組み立てる（`BuildWebSourceRootExcludes(excludeWebConfig)`。static で単体テストできるようにする）。

2. **`ExecuteAsync` は除外設定を引数で受け取る**
   `ExecuteAsync(config, writer, ct, step, excludeWebConfig = true)` とする。Service は DB に依存させない。
   - DB から読むのは Controller の役割。既存の `_db` 注入を使う。
   - 省略したときは `true`（安全側）。既存のテストの呼び出しはそのまま通る。

3. **設定の保存は汎用の key-value テーブルにする**
   `AppSetting(Key PK, Value, UpdatedBy, UpdatedAt)` とし、`DatabaseService` に `GetSetting(key)` と `UpsertSetting(key, value, updatedBy)` を追加する。
   - 既定値の解釈（キーが無ければ true）は、`GetPilotExcludeWebConfig(dbName)` に閉じ込める。
   - 既存 DB は `CREATE TABLE IF NOT EXISTS` で移行する（既存の方式と同じ）。

4. **admin の判定はサーバーでも行う**
   `DatabaseService.IsAdmin(userName)` を追加する（`AppUser.Role = 'admin'`）。PUT では、admin でなければ 403 を返す。
   - 認証基盤は無いので、自己申告の `updatedBy` を信じる前提。うっかり変更することを防ぐ水準で、SPEC に明記済み。

5. **ダッシュボードはリストに置き換える**
   `LastPilotKaios` / `LastPilotGos` を削除し、`LastPilots: List<PilotDeploySummary>` にする。システムの並びは Controller / Service 共通の定数 `PilotDbNames = ["kaios","gos","paf","duskin"]` を使う。
   - この定数は `WebSourcePrepareController.AllowedDbNames` と同じ一覧なので、1 か所（例: `Models/PilotSystems.cs`）にまとめて両方から参照する。

## Dependency Graph

```
T1: robocopy ルート直下除外（Service）
 │
 ├─ T2: AppSetting テーブル + DatabaseService（設定 / IsAdmin）   ← T1 と独立。並行可
 │
 └─ T3: Controller（info 拡張 / PUT settings / 実行時に設定を渡す）  ← T1, T2
        │
        ├─ [Checkpoint A: バックエンドの除外機能]
        │
        ├─ T4: 画面のトグル（admin だけ操作可）  ← T3
        │
T5: PAF / Duskin 追加（PilotSystems 定数 / Controller / 画面 / sample）  ← T3（Controller の衝突回避のため後にする）
 │
T6: ダッシュボード 4 システム化  ← T5（PilotSystems 定数を使う）
 │
 ├─ [Checkpoint B: 機能完了]
 │
T7: ドキュメント・バージョン 1.8.0
 │
 └─ [Checkpoint C: 完了]
```

## Risks & Mitigations

| リスク | 影響 | 緩和 |
|--------|------|------|
| robocopy の `/XF` にフルパスを渡したとき、意図どおりに一致しない | 除外が効かず、STG の Web.config が配布される | robocopy の `/XF` はフルパスでの指定に対応している。DryRun の引数ログで確認し、ローカルのダミーフォルダで実際に robocopy を動かして確認する（Checkpoint A の手動確認項目） |
| `WebSourcePath` の末尾に `\` があるなど、パスの表記ゆれ | 一致しない | `Path.Combine` で組み立てる。ダミーフォルダでの確認で、末尾 `\` あり・なしの両方を試す |
| pilot に Web.config が無い状態で除外する | bat が `Web.config not found` で失敗する | 運用で手作業で置く（SPEC で決定済み）。bat のエラー出力は DETAIL ログに出るので原因はわかる |
| ダッシュボードの型変更でフロントとバックエンドがずれる | カードが空になる | 同じリリースで配布する。`npm run build` の型チェックで検出する |
| カード 4 枚追加で、ダッシュボードの横幅が溢れる | 表示崩れ | `stat-card` のグリッド定義を確認し、必要なら折り返しを入れる。画面幅を変えて目視確認する |
| 既存テストが `DashboardStats.LastPilotKaios` を参照している | ビルドエラー | T6 で一緒に直す |
| admin 判定が自己申告 | 偽装されると変更できる | 既存のユーザー管理と同じ水準として許容（SPEC に明記済み） |

## Verification Checkpoints

### Checkpoint A（バックエンドの除外機能。T1〜T3 の後）

- [ ] `dotnet build` / `dotnet test` が成功する
- [ ] DryRun で `[DRY-RUN] robocopy` の引数に、Local config 2 つのフルパス（常に）と Web.config のフルパス（設定が ON のとき）が入る
- [ ] ローカルのダミーフォルダで実際に robocopy を動かし、ルート直下の 3 ファイルがコピーされず、サブフォルダの同じ名前のファイルはコピーされることを確認する
- [ ] PUT settings: admin なら 200 で値が保存される。user なら 403 で保存されない

### Checkpoint B（機能完了。T4〜T6 の後）

- [ ] `npm run build` が成功する
- [ ] 画面: admin はトグルを操作でき、user は読み取り専用。最終更新者と日時が出る
- [ ] paf / duskin を選んで DryRun を実行でき、`Paf.DC.Pilot.bat` / `Duskin.DC.Pilot.bat` の `[DRY-RUN]` ログが出る
- [ ] ダッシュボードに 4 枚のカードが出て、履歴が無いシステムは「実行履歴なし」と表示される

### Checkpoint C（完了。T7 の後）

- [ ] ドキュメント（本 SPEC の Status、`pilot-webconfig-bat/SPEC.md` からの参照、`SPEC_ISSUE25` の注記）が更新されている
- [ ] バージョン 1.8.0（`csproj` / `package.json`）
- [ ] すべてのテストとビルドが成功する

## Out of Scope

- FTP 設定を Local config に移すこと（Web ソース側で今後対応予定）
- 実環境の `appsettings.json` の編集（PAF / Duskin の実際の値）
- pilot に Web.config を最初に置く作業（運用で実施）
- サーバー側の認証基盤の導入
- git commit / push / タグ作成
