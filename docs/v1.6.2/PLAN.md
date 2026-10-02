# Implementation Plan: v1.6.2 Pilot SQL Server エラーログ検知

対応する仕様: [`SPEC.md`](./SPEC.md)

関連:

- [`../issue35/SPEC.md`](../issue35/SPEC.md) — Pilot SQL 適用は `deploy.bat` の終了コードで成否を見ている
- [`../VERSIONING_GUIDE.md`](../VERSIONING_GUIDE.md) — 版番号は `csproj` と `package.json`

## Overview

Pilot の SQL Server 適用に、`deployerror_yyyymmdd.log` の今回分を成否へ足す。画面・履歴 API・コントローラは変えない。`Success == false` が既存の失敗経路に乗る。

```
T1 判定（ファイルの前後比較）
  └── T2 RunSqlDeployAsync へ接続（失敗・本文・MariaDB 非起動）
        └── T3 「両方」の最終行
              └── [Checkpoint] → T4 版番号 1.6.2 → Done
```

1 エージェントで上から順に実装する。並行できるタスクはない。

## Architecture Decisions

1. **判定は `WebSourceDeployService.cs` 内の internal static クラス `SqlDeployErrorLog` に置く**
   - 新規ファイルは作らない。テストは既存の `InternalsVisibleTo` 経由で直接呼ぶ
   - 入出力はスナップショットと比較結果。ディスク読みはそのクラスに閉じ、`RunSqlDeployAsync` は「失敗にするか」「ログに何を出すか」だけを持つ

2. **今回分の切り方（SPEC の判定をそのまま実装する）**
   - 起動直前に `Exists` / `Length` / `LastWriteTimeUtc` と全文の SHA-256 を控える。ファイルが無いときは Exists=false、Length=0。中身が読めないときはハッシュなし
   - 事後は先にサイズと更新時刻だけを見る。起動前からあり、どちらも変わっていなければ本文を読まずエラーなし（ロック中の過去ログを含む）
   - 変化があるとき本文を読む。起動前は無かった、サイズが増えた、または最終更新時刻が起動時刻の 2 秒前以降で、取り出した本文に空白以外があるときエラー
   - サイズが増え、先頭が起動前のハッシュと一致するときだけ増分を Shift-JIS で復号する。一致しない、ハッシュが無い、サイズが減った、同じサイズで更新時刻だけ新しいときはファイル全体
   - 空白だけ、または条件を満たさないときはエラーなし
   - 復号は `DecoderExceptionFallback` 付きの Shift-JIS。変化したファイルの読み取りまたは復号に失敗したら本文は出さず「読めなかった」としてエラー扱いにする

3. **見るパス**
   - `{PilotSqlDeployPath}\log\deployerror_{yyyyMMdd}.log`
   - 日付はバッチ起動直前のローカル日付。終了後のローカル日付が違うときだけ、終了日のファイルも見る（起動前スナップショットは「無かった」扱い）
   - 両方に今回分があるときは、両方のパスを失敗メッセージに含め、本文は日付の古い方からログへ出す

4. **失敗メッセージと本文の出し分け**
   - `ErrorMessage` は短い文だけ（終了コードとパス）。本文は入れない
   - 終了コード 0 かつ今回分あり: `SQL Server deploy.bat は終了コード 0 でしたが、エラーログに出力があります: {パス}`
   - 終了コード非 0 かつ今回分あり: 既存文の末尾に `。エラーログ: {パス}` を足す
   - 終了コード非 0 かつ今回分なし: 既存文のまま（パスは足さない）
   - 本文は `onOutputLine` へ 1 行ずつ。先頭 50 行かつ 8,000 文字で打ち切り、次の 1 行で `（以降省略） {パス}` を出す
   - 複数パスがあるときの 50 行 / 8,000 文字は、合計で数える

5. **MariaDB は起動しない**
   - SQL Server を終了コードまたはエラーログで失敗にしたら、現行の早期 return のまま MariaDB 用 `deploy.bat` に進まない

6. **「両方」の最終行**
   - `sqlDeployResult.Success == false`（例外で作った失敗結果を含む）のとき、Web が成功でも最終行は `❌ Pilot環境適用が中断されました`
   - SQL スキップ（`Success == true` かつ `Skipped`）は `✅ Pilot環境適用が完了しました` のまま
   - `SQL適用のみ` の最終行は既存分岐のまま。T2 で `Success` が false になれば失敗文言になる

7. **DryRun**
   - バッチを起動しない現行の return より前でエラーログを見ない。ディスク上の過去ログは無視する

8. **テストのバッチ代行**
   - `FakeProcessRunner` に、cmd.exe 起動時の任意処理と終了コードを足す。既定は今どおり終了コード 0、追加処理なし
   - 既存テストは既定のまま通る。エラーログを書くテストだけ、cmd 起動のタイミングでファイルを書く

9. **変えないもの**
   - `deploy.bat`、`DeployService`（STG）、MariaDB の成否判定、コントローラ、フロントの描画
   - 履歴が `failed` になることと `done.success == false` は、コントローラが既に `sqlDeploy.Success` を見ているので新規テストを足さない
   - git commit / push / タグ

## 依存グラフ

```
T1 SqlDeployErrorLog
  └── T2 RunSqlDeployAsync 接続
        └── T3 両方の最終行
              └── T4 版番号
```

## Task List

詳細な Acceptance / Verify は [`TASKS.md`](./TASKS.md)。

### Phase 1: 検知

- [x] Task 1: エラーログの今回分を pure に判定する
- [x] Task 2: SQL Server `deploy.bat` の成否にその判定を接続する

### Checkpoint: 検知

- [x] 終了コード 0 でも、今回増えたエラーログで `RunSqlDeployAsync` が失敗する
- [x] 同日の未更新ログでは成功のまま
- [x] `dotnet test --filter WebSourceDeployServiceSqlSourceTests` が通る
- [ ] 人間が T3 へ進めてよいか確認する

### Phase 2: 最終行と版

- [x] Task 3: 「両方」の最終行を SQL 失敗に合わせる
- [x] Task 4: 版番号を 1.6.2 に揃える

### Checkpoint: Complete

- [x] SPEC の Success Criteria を満たす
- [x] `dotnet test` と `npm run build` が通る
- [x] レビュー可能。commit / push はしない

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 同日の過去ログを今回の失敗と誤認する | High | 起動前スナップショット。サイズも更新時刻も変わらなければ成功 |
| 追記位置が Shift-JIS の多バイト文字の途中になる | Low | 増分バイトだけを復号する。復号例外は「読めなかった」失敗にする。SQL エラー行は行単位で切れる想定 |
| バッチ終了直後にログがまだロックされている | Med | `IOException` は失敗にする。成功にはしない |
| 日跨ぎでファイル名がずれる | Low | 起動日と終了日が違うときだけ両方のパスを見る |
| Fake の既定変更で既存テストが落ちる | Med | cmd の既定は終了コード 0・副作用なし。T2 の最初に既存テストが緑のままか確認する |
| 「両方」の変更で SQL スキップまで失敗表示になる | Med | `Success == false` のときだけ最終行を失敗にする。スキップは T3 で回帰確認する |
| 実バッチのログパスが仕様と違う | High | 本版では設定項目を足さない。パスが違うと検知できない。Assumptions 1 が違う場合は実装前に SPEC を直す |

## Open Questions

なし。SPEC の Assumptions をそのまま使う。
