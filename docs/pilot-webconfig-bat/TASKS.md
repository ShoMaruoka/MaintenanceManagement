# Tasks: Pilot環境適用の web.config 切替を bat 実行に変更

対応する仕様: [`SPEC.md`](./SPEC.md)
対応する計画: [`PLAN.md`](./PLAN.md)

**Status: 実装完了（Task 1–4、Checkpoint A/B 通過）。git commit / push は未実施（ユーザー方針）。**

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。

---

## 実行順序

```
T1 → T2 → [Checkpoint A] → T3 → T4 → [Checkpoint B] → Done
 └─ T3 はここから並行可
```

---

## Task 1: bat 実行メソッドを追加し `ExecuteAsync` を差し替え

**Status:** done

**Description:**
`WebSourceDeployService` に `RunPilotWebConfigBatAsync` を追加し、`ExecuteAsync` の `ApplyPilotWebConfig` 呼び出し（現 840〜848 行目）を置き換える。`ApplyPilotWebConfig` は削除する。

**Acceptance criteria:**
- [x] 実行時は `{DestWebSourcePath}\deploy\{Name}.DC.Pilot.bat`、DryRun 時は `{WebSourcePath}\deploy\{Name}.DC.Pilot.bat` の存在を確認する
- [x] 欠落時は `FileNotFoundException`（メッセージにパスを含む）
- [x] 実行時は `cmd.exe /c "chcp 932 > nul && pushd "{deployDir}" && "{batPath}""` で起動し、出力を `DETAIL` ログに流す
- [x] 終了コード ≠ 0 で例外（メッセージに終了コードを含む）
- [x] DryRun 時はプロセスを起動せず `[DRY-RUN]` ログのみ
- [x] 成功ログ: `{target}: web.config 切替 bat を実行しました（{bat名}）`（DryRun は `[DRY-RUN]` 付き）
- [x] `ExecuteAsync` / `DbConfig.Name` の XML コメントを新方式に更新
- [x] `ApplyPilotWebConfig` がソースから削除されている

**Verification:**
- [x] `cd backend && dotnet build` 成功

**Dependencies:** None

**Files:**
- `backend/Services/WebSourceDeployService.cs`
- `backend/Models/DbConfig.cs`（コメントのみ）

**Estimated scope:** S

---

## Task 2: テストを更新・追加

**Status:** done

**Description:**
`.pilot` ファイルを置いて Web ステップを成功させている既存テスト（422 / 456 / 553 / 586 / 625 行目付近）を、`deploy\kaios.DC.Pilot.bat` を置く形に変える。`FakeProcessRunner` は cmd 呼び出しを区別しないため、web.config 切替 bat と SQL deploy.bat を引数で区別できるよう拡張する。

**Acceptance criteria:**
- [x] 既存テストが新方式で通る（SQL エラーログ系テストの前提が崩れない）
- [x] 新規: bat 欠落でターゲット失敗・SQL 適用が実行されない
- [x] 新規: bat 終了コード ≠ 0 で失敗し、pilot2 が実行されない
- [x] 新規: DryRun で bat のプロセスが起動しない
- [x] 新規: pilot 2 台で bat が 2 回、各 `DestWebSourcePath` に対して実行される
- [x] 新規: 起動引数に `pushd` と bat のフルパスが含まれる

**Verification:**
- [x] `cd backend && dotnet test` 成功

**Dependencies:** Task 1

**Files:**
- `backend/Tests/Services/WebSourceDeployServiceSqlSourceTests.cs`

**Estimated scope:** M

---

## Checkpoint A: コア動作

- [x] Task 1–2 完了
- [x] `dotnet build` / `dotnet test` 成功

---

## Task 3: フロント説明文を更新

**Status:** done

**Description:**
`WebSourcePrepare.tsx` 226 行目の「パイロット用 Web.config（例: Web.config.DC.kaios.pilot）を web.config として適用します」を、bat 実行の説明に変える。

**Acceptance criteria:**
- [x] 「各 pilot へのコピー後、pilot 側の deploy\{システム}.DC.Pilot.bat を実行して web.config を切り替えます」相当の文言
- [x] 他の説明（robocopy 順、画像、SQL）は維持

**Verification:**
- [x] `tsc` と `vite build` 成功、文言目視

**Dependencies:** None

**Files:**
- `frontend/src/pages/WebSourcePrepare.tsx`

**Estimated scope:** XS

---

## Task 4: ドキュメント更新

**Status:** done

**Description:**
旧方式のドキュメントに移行注記を入れ、本フォルダの完了チェックを更新する。

**Acceptance criteria:**
- [x] `docs/pilot-webconfig-file-swap/SPEC.md` 冒頭に「本方式は `docs/pilot-webconfig-bat/` で置き換え済み」と注記
- [x] `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md` の web.config 記述に同様の注記
- [x] `VERSIONING_GUIDE` に沿ってバージョンを更新
- [x] 本フォルダの Success Criteria / Status を更新

**Verification:**
- [x] ドキュメント目視

**Dependencies:** Checkpoint A

**Files:**
- `docs/pilot-webconfig-file-swap/SPEC.md`
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`
- `docs/issue27/SPEC.md` / `docs/issue27/PLAN.md`（web.config 手順の文言）
- `docs/pilot-webconfig-bat/*.md`
- `backend/MaintenanceManagement.Api.csproj` / `frontend/package.json` / `frontend/package-lock.json`（1.6.2 → 1.6.3。Git タグ v1.6.3 は未作成）

**Estimated scope:** S

---

## Checkpoint B: 完了

- [x] Task 1–4 すべて完了
- [x] Spec Success Criteria をすべて満たす（実環境での bat 動作は手動確認待ち）
- [x] DryRun のターゲットごとの `[DRY-RUN]` bat ログはテストで確認（実機の手動確認は未実施）
- [x] `graphify update .` 実行
- [x] git commit / push は行わない（ユーザー指示があるまで）
