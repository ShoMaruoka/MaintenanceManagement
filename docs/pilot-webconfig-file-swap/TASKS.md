# Tasks: Pilot環境適用の web.config ファイル差し替え

対応する仕様: [`SPEC.md`](./SPEC.md)  
対応する計画: [`PLAN.md`](./PLAN.md)

**Status: 実装完了**（`/build auto`・2026-07-28）。git commit / push は未実施（ユーザー方針）。

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。  
（Task 5 のみ Task 1–2 と並行可。Task 6 は Checkpoint A 後推奨。）

---

## 実行順序

```
T1 → T2 → [Checkpoint A] → T3 → T4 → T5 → T6 → [Checkpoint B] → Done
         └─ T5 はここから並行可
```

---

## Task 1: `ApplyPilotWebConfig` を追加

**Status:** done

**Description:**  
`WebSourceDeployService` に、コピー先ディレクトリ内の `Web.config.DC.{dbConfigName}.pilot` を `web.config` へ上書きする静的メソッドを追加する。この時点では `ExecuteAsync` からはまだ呼ばない（既存置換はそのまま）。

**Acceptance criteria:**
- [x] `public static void ApplyPilotWebConfig(string destWebSourcePath, string dbConfigName, bool dryRun)` が存在する
- [x] ソースパスは `Path.Combine(destWebSourcePath, $"Web.config.DC.{dbConfigName}.pilot")`
- [x] デストパスは `Path.Combine(destWebSourcePath, "web.config")`
- [x] ソース欠落時は `FileNotFoundException`（メッセージにパスを含む）
- [x] `dryRun=true` のとき存在チェックのみで `File.Copy` しない
- [x] `dryRun=false` のとき `File.Copy(source, dest, overwrite: true)` し、ソースファイルは削除しない

**Verification:**
- [x] `cd backend && dotnet build` 成功
- [x] 一時フォルダでメソッドを直接呼んで DryRun / 上書き / 欠落の3経路を確認（Checkpoint A）

**Dependencies:** None

**Files:**
- `backend/Services/WebSourceDeployService.cs`

**Estimated scope:** XS（1ファイル）

---

## Task 2: `ExecuteAsync` を差し替え呼び出しに切替＆置換コード削除

**Status:** done

**Description:**  
各 `PilotTarget` ループ末尾の `ReplaceConnectionStrings` 呼び出しを `ApplyPilotWebConfig` に置き換える。XML 行置換関連メソッドと `using System.Xml` を削除し、ログ・XML コメントを新方式に合わせて更新する。

**Acceptance criteria:**
- [x] 画像コピー（またはスキップログ）の直後に `ApplyPilotWebConfig(target.DestWebSourcePath, config.Name, _dryRun)` が呼ばれる
- [x] 成功ログにパイロット用ファイル名（または適用完了）と DryRun 時の `[DRY-RUN]` が含まれる
- [x] `ReplaceConnectionStrings` / `FindActiveConnectionStringLine` / `EscapeXmlAttribute` がソースから削除されている
- [x] `using System.Xml` が削除されている
- [x] 差し替え失敗時は既存の `catch` でターゲット失敗・`break`（以降スキップ）になる

**Verification:**
- [x] `cd backend && dotnet build` 成功
- [x] コード上に `ReplaceConnectionStrings` / `PilotConnectionStrings` の呼び出しが残っていない

**Dependencies:** Task 1

**Files:**
- `backend/Services/WebSourceDeployService.cs`

**Estimated scope:** S（1ファイル）

---

## Checkpoint A: コア動作

- [x] Task 1–2 完了
- [x] `dotnet build` 成功
- [x] 一時フォルダ検証: DryRun 非書き込み / 上書き成功 / `.pilot` 残留 / 欠落で FileNotFoundException → `OK ApplyPilotWebConfig Checkpoint A`
- [x] 挿入位置・ログ文言は Spec / Plan どおり（画像の直後、ファイル名付きログ）

→ Checkpoint A 通過後に Task 3 へ。

---

## Task 3: `PilotConnectionStrings` モデル削除

**Status:** done

**Description:**  
`DbConfig.PilotConnectionStrings` プロパティと `PilotConnectionString` クラスを削除する。コード参照が残っていないことを確認する。

**Acceptance criteria:**
- [x] `PilotConnectionString` クラスが `DbConfig.cs` から削除されている
- [x] `DbConfig.PilotConnectionStrings` が削除されている
- [x] `backend/` 配下の `.cs` に `PilotConnectionString` / `PilotConnectionStrings` の参照が無い

**Verification:**
- [x] `dotnet build` 成功
- [x] Grep で `backend/**/*.cs` に `PilotConnectionString` なし

**Dependencies:** Task 2

**Files:**
- `backend/Models/DbConfig.cs`

**Estimated scope:** XS（1ファイル）

---

## Task 4: `appsettings_sample.json` からキー削除

**Status:** done

**Description:**  
kaios / gos の `PilotConnectionStrings` 配列を sample から削除する。他の appsettings に同キーがあれば同様に削除（Development / 本番 json は現状キー無し）。

**Acceptance criteria:**
- [x] `appsettings_sample.json` に `PilotConnectionStrings` が存在しない
- [x] JSON として valid
- [x] 運用向け注記: Issue #25 SPEC §7 に手動削除推奨を記載

**Verification:**
- [x] `backend/**/*.json` に `PilotConnectionStrings` なし

**Dependencies:** Task 3

**Files:**
- `backend/appsettings_sample.json`

**Estimated scope:** XS（1ファイル）

---

## Task 5: フロント説明文を更新

**Status:** done

**Description:**  
`WebSourcePrepare.tsx` の画面説明から「接続文字列を書き換え」を削除し、パイロット用 Web.config ファイルを `web.config` として適用する旨に書き換える。

**Acceptance criteria:**
- [x] 説明文に「接続文字列を書き換え」が無い
- [x] パイロット用ファイル（例: `Web.config.DC.kaios.pilot`）を `web.config` に適用する説明がある
- [x] 他の説明（robocopy 順、画像、SQL）は維持

**Verification:**
- [x] 文言目視済み

**Dependencies:** None

**Files:**
- `frontend/src/pages/WebSourcePrepare.tsx`

**Estimated scope:** XS（1ファイル）

---

## Task 6: 関連ドキュメントを現行仕様に合わせる

**Status:** done

**Description:**  
Issue #25 / #27 のドキュメントで「web.config 接続文字列置換」と読める箇所に、本改修への移行注記または文言更新を入れる。

**Acceptance criteria:**
- [x] `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md` の §7 に廃止・移行注記と本 SPEC へのリンク
- [x] `docs/issue27/SPEC.md` の web.config 行をファイル差し替えに更新
- [x] `docs/issue27/PLAN.md` の参照を更新
- [x] 本フォルダの完了チェックを更新

**Verification:**
- [x] ドキュメント目視済み

**Dependencies:** Checkpoint A

**Files:**
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`
- `docs/issue27/SPEC.md`
- `docs/issue27/PLAN.md`
- `docs/pilot-webconfig-file-swap/*.md`

**Estimated scope:** S（3〜5ファイル）

---

## Checkpoint B: 完了

- [x] Task 1–6 すべて完了
- [x] Spec Success Criteria をすべて満たす
- [x] `dotnet build` 成功
- [x] `graphify update .` 実行済み
- [x] git commit / push は行わない（ユーザー指示があるまで）

### Spec Success Criteria マッピング

| Spec 条件 | 対応 Task | 結果 |
|-----------|-----------|------|
| 差し替えメソッド・置換コード削除 | T1, T2 | done |
| モデル削除 | T3 | done |
| sample からキー削除 | T4 | done |
| DryRun / 欠落エラー / ファイル残留 | T1, Checkpoint A | done |
| FE 説明文 | T5 | done |
| Issue #25 / #27 docs | T6 | done |
| `dotnet build` | 各 Task / Checkpoint | done |

---

## Out of Scope（実装しない）

- パイロット用ファイル内容のバリデーション
- IIS 再起動
- バックアップ
- 自動テストプロジェクト新設
- commit / push

---

**完了:** `/build auto` により Task 1–6 を実装済み。コミットはユーザー依頼時に行う。
