# Spec: v1.6.2 Pilot SQL Server 適用のエラーログ検知

## Objective

Pilot 環境適用で SQL Server 用 `deploy.bat` が終了コード 0 のまま終わっても、同回に `log\deployerror_yyyymmdd.log` へ SQL エラーが書かれていた場合は、SQL 適用を失敗として扱う。

運用で起きた事象は次のとおり。

- テーブル未更新のため不明な項目が残り、ストアドプロシージャの更新が SQL Server 上で失敗した
- 失敗内容は `{PilotSqlDeployPath}\log\deployerror_yyyymmdd.log` に出ていた
- `deploy.bat` 自体は終了コード 0 で戻るため、Pilot 適用ログ・実行履歴の SQL 行・画面の完了判定は成功になっていた

- **対象ユーザー**: Pilot 環境適用を実行する運用担当者
- **対象システム**: Pilot SQL 適用がある DB（kaios / gos）。paf / duskin は Pilot 対象外のまま
- **版番号**: `1.6.1` → **`1.6.2`**（PATCH。判定漏れの修正。ユーザー指定）

### 成功の定義

- SQL Server 用 `deploy.bat` が 0 で終わっても、その実行でエラーログに本文が増えていれば、SQL 適用は失敗になり、増えた本文が Pilot 適用ログに残る
- 同日の過去失敗で残っているエラーログは、今回触っていなければ失敗にしない
- 「両方」実行で Web コピーは成功し SQL だけ失敗した場合、最後の一行も失敗になる
- MariaDB 用 `deploy.bat` と STG 適用の `deploy.bat` の成否判定は変えない

---

## ASSUMPTIONS I'M MAKING

実装に入る前に、次を仮定する。誤りがあれば訂正すること。

1. **エラーログの場所**は、SQL Server 用バッチの作業ディレクトリである `PilotSqlDeployPath` 直下の `log\deployerror_{yyyyMMdd}.log` である。`yyyyMMdd` はバッチを動かしたマシンのローカル日付。`Source\log` や MariaDB 用フォルダは見ない。
2. **ファイルはエラーがあった回だけ作られる、または追記される。** 成功時に空ファイルや定型ヘッダだけを毎回書く挙動はない。空白だけの増加は失敗にしない。
3. **文字コードは Shift-JIS** である（既存の bat / robocopy 出力と同じ）。読めない場合も失敗にし、パスと「読めなかった」旨をログに出す。
4. **外部の `deploy.bat` は変更しない。** 本システムはバッチを作らない（既存方針のまま）。終了コードを 1 にする修正は Pilot サーバー側の別作業であり、本版の成否条件にしない。
5. **対象は Pilot の SQL Server 適用だけ**である。MariaDB 用 `deploy.bat`、STG 適用（`DeployService.Step5_Deploy`）は終了コード判定のまま。
6. **「両方」の最終行**も直す。現状は Web コピーの成否だけを見て `✅ Pilot環境適用が完了しました` を出す。SQL 失敗時は `❌ Pilot環境適用が中断されました` にする。`SQL適用のみ` は、SQL 結果が失敗になれば既存の分岐で同じ文言になる。
7. **SQL Server をエラーログで失敗にしたあとは MariaDB 用バッチを起動しない。** 終了コード非 0 のときと同じ早期 return である。
8. git commit / push / タグ作成は行わない（ユーザー指示と `docs/VERSIONING_GUIDE.md` の「タグはリリース時」）。

→ 訂正がなければこの仮定で PLAN に進む。

---

## Tech Stack

既存のまま。新規依存なし。

| レイヤー | 技術 |
|---------|------|
| バックエンド | ASP.NET Core 8 |
| フロントエンド | React 18 + TypeScript + Vite（本版で画面コンポーネントは変えない。失敗結果の既存表示を使う） |
| 進捗 | 既存 SSE |
| テスト | xUnit（`backend/Tests`） |

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Test:   cd backend && dotnet test
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

版番号は次の 2 ファイルを `1.6.2` に揃える（`docs/VERSIONING_GUIDE.md`）。Git タグは打たない。

- `backend/MaintenanceManagement.Api.csproj` の `<Version>`
- `frontend/package.json` の `"version"`

---

## Project Structure

```
backend/
  Services/WebSourceDeployService.cs          → SQL Server deploy.bat 後のエラーログ判定、両方実行の最終行
  Tests/Services/WebSourceDeployServiceSqlSourceTests.cs
                                              → エラーログの増分・未更新・空ファイル
docs/v1.6.2/                                  → 本 SPEC / 後続 PLAN・TASKS
```

触らないもの:

- `deploy.bat`（リポジトリ外・事前配置）
- `DeployService`（STG 適用）
- `WebSourcePrepareController` の成否保存（`sqlDeploy.Success` を既に履歴と `done.success` に使っている）
- フロントの結果表示（`sqlDeploy.success` / `errorMessage` の既存描画）

---

## Code Style

既存の `RunSqlDeployAsync` に合わせる。ログ文言は日本語。判定ロジックはテストできる static メソッドに分ける。

```csharp
// bat 起動前のサイズ・更新時刻・全文の SHA-256 を控え、戻ったあと「今回分の本文」だけを失敗理由にする。
// 同日の古い deployerror_yyyyMMdd.log が残っていても、今回触っていなければ成功のまま（ロック中でも本文は読まない）。
// 先頭が起動前の全文と一致するときだけ追記とみなし、それ以外の変化はファイル全体を今回分とする。
var before = SqlDeployErrorLog.Capture(logPath);
var batExitCode = await RunDeployBatAsync(...);
var errorLog = SqlDeployErrorLog.ReadNewContent(logPath, before, startedUtc, Encoding.GetEncoding("shift_jis"));
if (batExitCode != 0 || errorLog.HasNewError)
    return new WebSourceSqlDeployResult(false, batExitCode, BuildSqlServerBatFailureMessage(...));
```

エラーログ本文は Pilot ログへ行単位で出す。1 回の適用で載せる本文は **先頭 50 行かつ 8,000 文字**まで。超えた分は省略し、ファイルパスを残す。

---

## 機能仕様: SQL Server エラーログを SQL 適用の成否に含める

### As Is

| 項目 | 現状 |
|------|------|
| SQL Server 成否 | `PilotSqlDeployPath\deploy.bat` の終了コードが 0 なら成功 |
| エラーの実体 | バッチが `log\deployerror_yyyymmdd.log` に書き、終了コードは 0 のことがある |
| 履歴 | `WebSourceDeployLog` の SQL 行は `sqlDeploy.Success` が true のため `success` |
| 画面 | `done.success` が true。SQL 行も成功 |
| 「両方」の最終行 | Web ターゲットの失敗だけを見る。SQL が失敗でも `✅ Pilot環境適用が完了しました` になり得る |
| MariaDB | 専用 `deploy.bat` の終了コードのみ。本事象の対象外 |

### To Be

`deploy.bat` の終了コードに加え、次のファイルを見る。

```
{PilotSqlDeployPath}\log\deployerror_{yyyyMMdd}.log
```

日付はバッチ起動直前のローカル日付とする。起動前と終了後で日付が違う場合（日跨ぎ）は、両方のファイル名を見る。

#### 判定

バッチ起動直前に、対象ファイルごとに次を控える。無い場合は「無し・サイズ 0」。

- 存在するか
- ファイルサイズ
- 最終更新時刻（UTC）

バッチ終了後（終了コードが 0 でも 0 でなくても）に読み直す。次のすべてを満たすとき **今回のエラー**とする。

1. ファイルが存在する
2. 本文に空白以外の文字がある
3. 次のいずれか
   - 起動前は存在しなかった
   - サイズが増えた
   - 最終更新時刻が起動直前以降（ファイルシステム時刻のずれを吸収するため、起動時刻の 2 秒前以降でも更新とみなす）

サイズも更新時刻も変わっていなければ、本文は読まない。ファイルがロックされていても失敗にしない。

変化があるときだけ本文を読む。サイズが増えていて、起動後の先頭が起動前の全文（SHA-256）と一致するときだけ、増えた末尾を今回分とする（追記）。一致しないとき、サイズが減ったとき、同じサイズで更新時刻だけ新しいときは、ファイル全体を今回分とする（作り直し）。起動前の中身を読めなかった場合も、増分とはみなさずファイル全体を今回分とする。

空白だけの新規・追記はエラーにしない。変わったファイルを読めないときだけ「読めなかった」失敗にする。

#### 失敗時の動き

- `WebSourceSqlDeployResult.Success` を false にする
- `ErrorMessage` に、終了コードとエラーログのパスを含める
  - 終了コード 0: `SQL Server deploy.bat は終了コード 0 でしたが、エラーログに出力があります: {パス}`
  - 終了コード非 0: 既存の `SQL Server deploy.bat がエラー終了しました (exit code N)` に、ログパスを足す
- 今回分の本文を Pilot 適用ログに出す（上限は Code Style のとおり）
- MariaDB 用 `deploy.bat` は起動しない
- 実行履歴の SQL 行は `failed`、`done.success` は false（コントローラの既存分岐）

終了コードが非 0 で、エラーログに今回分が無い場合は、現行どおり終了コードだけで失敗にする。

#### 成功のままにする場合

- 対象ファイルが無い
- 起動前からあり、サイズも更新時刻も変わっていない（同日の過去エラー。ロック中を含む）
- 増分が空白のみ
- DryRun（バッチ未実行のため、ディスク上の既存ログは見ない）

#### 「両方」実行の最終行

Web コピーが成功でも、SQL 適用結果の `Success == false` なら最終行は失敗にする。

| 状態 | 最終行 |
|------|--------|
| Web 失敗 | `❌ Pilot環境適用が中断されました`（現状どおり。SQL は実行しない） |
| Web 成功かつ SQL 失敗 | `❌ Pilot環境適用が中断されました` |
| Web 成功かつ SQL 成功、または SQL スキップ | `✅ Pilot環境適用が完了しました` |
| SQL 適用のみで SQL 失敗 | `❌ Pilot環境適用が中断されました`（現状の分岐のまま） |
| SQL 適用のみで SQL スキップ | `⏭ Pilot環境適用をスキップしました（適用対象なし）`（変えない） |

SQL 失敗時も、その前の行 `SQL適用: 失敗しました (...)` は出す。

---

## Testing Strategy

| レベル | 対象 | 手段 |
|--------|------|------|
| ユニット | 起動前スナップショットと事後ファイルから、今回分の本文・「エラーあり」を決める | `SqlDeployErrorLog` の static メソッドを直接テスト |
| ユニット | バッチ実行中にエラーログが新規作成・追記されたら `RunSqlDeployAsync` が失敗し、本文をコールバックへ出す | `FakeProcessRunner` が cmd 起動時にログファイルを書いてから終了コード 0 を返す |
| ユニット | 同日の既存ログがバッチ中に変わらなければ成功 | 起動前にファイルを置き、Fake は何も書かない |
| ユニット | 空ファイル、または空白だけの追記は成功 | 同上 |
| ユニット | 終了コード非 0 は、エラーログが無くても失敗 | Fake が 1 を返す |
| ユニット | 「両方」で Web 成功・SQL エラーログありのとき、最終行が失敗文言 | `ExecuteAsync` |
| ユニット | DryRun はエラーログを見ない | 既存 DryRun テストに、古いエラーログが残っていても成功することを足す |
| ビルド | 参照切れなし | `dotnet test`、`npm run build` |

カバレッジ数値ゲートは設けない。実サーバーの `deploy.bat` はリポジトリ外のため、結合試験は手動確認とする。

---

## Boundaries

- **Always**
  - 成否は終了コードと、今回増えたエラーログ本文の両方で決める
  - 同日に残っている過去のエラーログだけで失敗にしない
  - エラーログ本文（上限内）を Pilot 適用ログに残す
  - 版番号はリポジトリ内 2 箇所を `1.6.2` に揃える。タグは打たない
  - 変更後は `dotnet test` と `npm run build`
- **Ask first**
  - MariaDB 用バッチにも同じエラーログ検知を広げること
  - STG 適用（`DeployService`）の `deploy.bat` に同じ検知を入れること
  - エラーログのパス・ファイル名・文字コードが本仕様と違う場合の設定項目追加
  - 本文上限（50 行 / 8,000 文字）の変更
- **Never**
  - リポジトリ外の `deploy.bat` を本変更の前提にしない
  - エラーログの存在だけを見て、同日の過去分まで失敗にしない
  - DryRun で実在するエラーログを失敗理由にしない
  - git commit / push、タグ作成（指示があるまで）
  - 秘密情報を SPEC に載せない

---

## Success Criteria

- [ ] SQL Server `deploy.bat` が終了コード 0 で、`log\deployerror_yyyyMMdd.log` が今回新規作成され本文があるとき、SQL 適用は失敗になり、本文が Pilot ログに出る
- [ ] 同ファイルが今回追記されたとき、増えた部分がログに出て失敗になる
- [ ] 同日のエラーログが起動前からあり、今回サイズも更新時刻も変わらないとき、SQL 適用は成功のまま
- [ ] 空ファイル、または空白だけの増加では失敗にしない
- [ ] 終了コードが 0 以外のときは、現行どおり失敗になる
- [ ] 上記の失敗で実行履歴の SQL 行が `failed` になり、完了イベントの `success` が false になる（既存の `sqlDeploy.Success` 経路）
- [ ] 「両方」で Web が成功し SQL だけ失敗したとき、最終行が `❌ Pilot環境適用が中断されました` になる
- [ ] DryRun はバッチもエラーログも見ない
- [ ] MariaDB 用バッチは、SQL Server をエラーログで失敗にしたあと起動しない。SQL Server が成功のときの MariaDB 判定は終了コードのまま
- [ ] `dotnet test` と `npm run build` が通る
- [ ] フロント／バックの版番号が `1.6.2` で一致する

---

## Open Questions

なし。ログの場所・日付・文字コード・SQL Server 限定は Assumptions に置いた。実ファイルのサンプルが Assumptions と違う場合は、PLAN の前に訂正する。

---

## Decisions Log

| 日付 | 決定 | 根拠 |
|------|------|------|
| 2026-10-01 | 終了コード 0 でも、今回分の `deployerror_yyyymmdd.log` があれば SQL Server 適用は失敗 | ユーザー報告。バッチは成功終了し、エラーはログファイルのみ |
| 2026-10-01 | ファイルの有無ではなく、起動前後のサイズと更新時刻で今回分を切る | 同日再実行で過去ログを誤検知しないため |
| 2026-10-02 | サイズ増加は、先頭が起動前の内容と一致するときだけ追記とする。未変化のファイルは読まない | レビュー。上書きで長くなったログの先頭欠落、およびロックされた過去ログの誤失敗を避ける。実 `deploy.bat` はリポジトリ外のため追記か上書きかは未確認 |
| 2026-10-01 | バッチファイル自体は変更しない | 事前配置であり、本リポジトリの管理外 |

---

## Related

- [`PLAN.md`](./PLAN.md) / [`TASKS.md`](./TASKS.md) — 本版の実装計画とタスク
- `docs/VERSIONING_GUIDE.md` — 版番号の揃え方
- `docs/issue35/SPEC.md` — Pilot SQL 適用は `PilotSqlDeployPath\deploy.bat` の終了コードで成否を見ている
- `backend/Services/WebSourceDeployService.cs` — `RunSqlDeployAsync`
- `backend/Controllers/WebSourcePrepareController.cs` — `sqlDeploy.Success` を履歴と `done.success` に使う
