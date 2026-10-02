# Tasks: v1.6.2 Pilot SQL Server エラーログ検知

対応する仕様: [`SPEC.md`](./SPEC.md)  
対応する計画: [`PLAN.md`](./PLAN.md)

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。  
git commit / push は行わない。

TDD: T1 / T2 / T3 は **失敗するテストを先に書いてから** 実装する。

---

## 実行順序

```
T1 → T2 → [Checkpoint A] → T3 → T4 → [Checkpoint B] → Done
```

並行できるタスクはない。

---

## Task 1: エラーログの今回分を判定する

**Status:** done

**Description:**  
`deployerror_yyyymmdd.log` の起動前スナップショットと事後ファイルから、今回増えた本文があるかを決める pure な処理を足す。`RunSqlDeployAsync` にはまだ接続しない。

**Acceptance criteria:**

- [x] 起動前に無く、事後に空白以外の本文があるファイルは、今回のエラーであり本文はファイル全体
- [x] サイズが増えたときは、増えたバイト範囲だけが今回分になる
- [x] サイズも更新時刻も変わらない既存ファイルはエラーなし
- [x] 空ファイル、および空白だけの増加はエラーなし
- [x] 同じサイズ、またはサイズが減っていて、最終更新時刻が起動時刻の 2 秒前以降なら、ファイル全体が今回分になる
- [x] 最終更新時刻が起動時刻の 2 秒より前で、サイズが増えていなければエラーなし
- [x] Shift-JIS で復号できない増分は、本文なしの「読めなかった」エラーになる

**Verification:**

- [x] 失敗テストを先に追加して RED を確認する
- [x] `cd backend && dotnet test --filter WebSourceDeployServiceSqlSourceTests`

**Dependencies:** None

**Files likely touched:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`

**Estimated scope:** S（2 ファイル）

---

## Task 2: SQL Server deploy.bat の成否に接続する

**Status:** done

**Description:**  
`RunSqlDeployAsync` が SQL Server 用 `deploy.bat` の直前にエラーログを控え、終了後に Task 1 の判定を使う。今回分があれば失敗結果を返し、上限内の本文を出力コールバックへ出す。その場合は MariaDB 用バッチを起動しない。DryRun はエラーログを見ない。

**Acceptance criteria:**

- [x] 終了コード 0 で、cmd 実行中にエラーログが新規作成され本文があるとき、`Success` は false。`ErrorMessage` は終了コード 0 とログパスを含み、本文そのものは含まない
- [x] 今回分の本文が出力コールバックに出る。50 行または 8,000 文字を超える分は出さず、省略とパスが 1 行出る
- [x] 同日の既存ログが cmd 中に変わらなければ成功のまま
- [x] 空ファイル、空白だけの追記では成功のまま
- [x] 終了コードが 0 以外で今回分が無いときは、現行の終了コードメッセージだけで失敗する
- [x] 終了コードが 0 以外で今回分があるときは、終了コードメッセージにログパスが付く
- [x] SQL Server と MariaDB の両方に `*.sql` がある状態で SQL Server をエラーログ失敗にしたとき、MariaDB 用 `deploy.bat` は呼ばれない
- [x] DryRun は、エラーログがディスクにあっても成功し、cmd を起動しない
- [x] 日跨ぎ（起動日と終了日が違う）のとき、終了日のファイルに今回分があれば失敗にする

**Verification:**

- [x] 失敗テストを先に追加して RED を確認する
- [x] `cd backend && dotnet test --filter WebSourceDeployServiceSqlSourceTests`
- [x] 既存の `RunSqlDeploy_*` が、Fake の既定（cmd 終了コード 0・ファイルを書かない）のまま通る

**Dependencies:** Task 1

**Files likely touched:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`

**Estimated scope:** M（2 ファイル）

実装メモ:

- `FakeProcessRunner` の既定挙動は変えない。テスト側で cmd 起動時にだけログファイルを書く
- 日跨ぎは時計をモックせず、判定に渡す「起動時刻」「終了時刻」をテストから差し替えられる形にする（本番は `DateTime.Now`）
- 履歴行と `done.success` はコントローラが `sqlDeploy.Success` を既に見ている。コントローラは触らない

---

## Checkpoint: 検知（A）

- [x] 終了コード 0 でも、今回増えたエラーログで SQL 適用が失敗する
- [x] 未更新の同日ログでは成功する
- [x] `cd backend && dotnet test --filter WebSourceDeployServiceSqlSourceTests` が通る
- [ ] 人間が確認してから Task 3 へ進む

---

## Task 3: 「両方」の最終行を SQL 失敗に合わせる

**Status:** done

**Description:**  
Web コピーが成功しても、SQL 適用結果が失敗なら、Pilot 適用ログの最後の一行を失敗文言にする。SQL スキップ時の完了文言は変えない。

**Acceptance criteria:**

- [x] 「両方」で Web が成功し、SQL Server エラーログに今回分があるとき、最終行が `❌ Pilot環境適用が中断されました` になる
- [x] その直前に `SQL適用: 失敗しました` が出る
- [x] 「両方」で SQL がスキップのとき、最終行は `✅ Pilot環境適用が完了しました` のまま
- [x] 「SQL適用のみ」でエラーログに今回分があるとき、最終行が `❌ Pilot環境適用が中断されました` になる

**Verification:**

- [x] 失敗テストを先に追加して RED を確認する
- [x] `cd backend && dotnet test --filter WebSourceDeployServiceSqlSourceTests`

**Dependencies:** Task 2

**Files likely touched:**

- `backend/Services/WebSourceDeployService.cs`
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`

**Estimated scope:** S（2 ファイル）

実装メモ:

- 「両方」の実コピーは Fake がファイルを運ばない。Web ステップを成功させるため、テストは `DestWebSourcePath` に `Web.config.DC.kaios.pilot` を事前に置く
- 最終行の判定は `sqlDeployResult is { Success: false }` を Web 側の失敗に加える。スキップは `Success == true` なので完了文言のままになる

---

## Task 4: 版番号を 1.6.2 に揃える

**Status:** done

**Description:**  
画面と API の版番号を 1.6.2 にする。Git タグは打たない。

**Acceptance criteria:**

- [x] `backend/MaintenanceManagement.Api.csproj` の `<Version>` が `1.6.2`
- [x] `frontend/package.json` の `"version"` が `1.6.2`
- [x] 上記以外の機能コードは変えない

**Verification:**

- [x] `cd backend && dotnet test`
- [x] `cd frontend && npm run build`

**Dependencies:** Task 3

**Files likely touched:**

- `backend/MaintenanceManagement.Api.csproj`
- `frontend/package.json`

**Estimated scope:** XS（2 ファイル）

---

## Checkpoint: Complete（B）

- [x] SPEC の Success Criteria を満たす
- [x] `cd backend && dotnet test` が通る
- [x] `cd frontend && npm run build` が通る
- [x] commit / push / タグは打っていない
