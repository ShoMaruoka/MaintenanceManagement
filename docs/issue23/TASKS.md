# Tasks: 画像アップロード後の削除機能（Issue #23）

対応する仕様: `[SPEC.md](./SPEC.md)`  
対応する計画: `[PLAN.md](./PLAN.md)`

**Status: 実装完了**（Checkpoint B 手動確認 OK / バージョン 1.4.0）

実装は **上から順** に 1 タスクずつ。各タスク完了後に Acceptance / Verify を満たしてから次へ進む。  
git commit / push は行わない（ユーザー方針）。

---

## 実行順序

```
T1 → T2 → T3 → T4 → [Checkpoint A] → T5 → T6 → [Checkpoint B] → Done
```

---

## Task 1: 削除用 DTO を追加

**Status:** done

**Description:**  
`ImagePrepareModels.cs` に削除リクエスト／レスポンス型を追加する。この時点では Service / Controller はまだ触らない。

**Acceptance criteria:**

- [x] `ImageDeleteRequest` に `List<string> Paths` がある
- [x] `ImageDeleteResponse` に `DbName` / `DryRun` / `List<string> Deleted` がある
- [x] 既存のアップロード・フォルダ・ツリー用 DTO に破壊的変更がない

**Verification:**

- [x] `cd backend && dotnet build` 成功

**Dependencies:** None

**Files:**

- `backend/Models/ImagePrepareModels.cs`

**Estimated scope:** XS（1 file）

---



## Task 2: `ImagePrepareService` にパス解決拡張と `Delete` を実装

**Status:** done

**Description:**  
ファイル／フォルダ両対応の相対パス解決を追加し、`Delete(DbConfig, IReadOnlyList<string> paths)` を実装する。  
既存 `TryResolveRelativeFile` の公開シグネチャと挙動は維持する（`FastCopyService` 互換）。内部共通化してよい。

**Acceptance criteria:**

- [x] 相対パスを `Files` 配下に解決でき、カテゴリルート単独（`Images` 等）は拒否する
- [x] `paths` 空・不正・存在しない・非空フォルダは `ArgumentException`（呼び出し側で 400）— **検証段階では一切削除しない**
- [x] ファイルは削除、空フォルダのみ削除可
- [x] 削除実行順に深さソートは行わない（実空フォルダのみのため不要）
- [x] 重複 path は正規化後に大文字小文字無視でユニーク化
- [x] `DryRun=true` 時はディスク変更なしで `Deleted` に予定パスを返す
- [x] 検証通過後の途中 IO 失敗時は、それまでに消えたパスを `Deleted` に残し、例外で上位に伝える（Controller で 500 化）
- [x] レビュー対応: `plannedSet`／深さソート廃止、共有リゾルバから深さ制限除去、PartialDelete テスト必須

**Verification:**

- [x] `cd backend && dotnet build` 成功

**Dependencies:** Task 1

**Files:**

- `backend/Services/ImagePrepareService.cs`

**Estimated scope:** M（1 file、ロジック中心）

---



## Task 3: `Delete` の単体テストを追加

**Status:** done

**Description:**  
一時ディレクトリを `DeployDev2StgPath`（→ `FilesPath`）に見立て、`ImagePrepareService.Delete` の正常系・拒否系・DryRun を xUnit で検証する。

**Acceptance criteria:**

- [x] ファイル削除が成功し、ディスク上から消える（`DryRun=false`）
- [x] 空フォルダ削除が成功する
- [x] 非空フォルダ・カテゴリルート・`..` トラバーサル・存在しないパスは例外（削除されない）
- [x] `DryRun=true` ではファイルが残る
- [x] 親子を同時指定した場合は拒否し、子→親の順次削除は可能
- [x] 子込み親指定の回帰・深いパス削除・PartialDelete をカバー

**Verification:**

- [x] `cd backend && dotnet test --filter ImagePrepare`（または追加したテストクラス名）が成功
- [x] 既存テストも `dotnet test` で成功（※ ManualApply 2件は `test/Kaios_MariaDB_rep` 欠如の既存環境依存）

**Dependencies:** Task 2

**Files:**

- `backend/Tests/Services/ImagePrepareServiceDeleteTests.cs`（新規）

**Estimated scope:** S–M（1 file）

---



## Task 4: `POST .../delete` エンドポイントを追加

**Status:** done

**Description:**  
`ImagePrepareController` に削除 API を追加し、例外を HTTP ステータスへマッピングする。

**Acceptance criteria:**

- [x] `POST /api/image-prepare/{dbName}/delete` が `[FromBody] ImageDeleteRequest` を受け取る
- [x] 未知の DB → 404
- [x] `ArgumentException` → 400 `{ error }`
- [x] 成功 → 200 `ImageDeleteResponse`
- [x] 検証後の実行時例外（IO 等）→ 500 `{ error, deleted }`（削除済みがあれば含める）

**Verification:**

- [x] `cd backend && dotnet build` 成功
- [x] （任意）ローカル起動後に API を直接叩き、ファイル削除を確認

**Dependencies:** Task 2（Task 3 完了推奨）

**Files:**

- `backend/Controllers/ImagePrepareController.cs`

**Estimated scope:** S（1 file）

---



## Checkpoint A: Backend 完了

- [x] Task 1–4 完了
- [x] `dotnet build` / `dotnet test --filter ImagePrepare` 成功
- [x] 一時フォルダまたはローカル `Files` で: ファイル削除 / 空フォルダ削除 / 非空・ルート拒否 / DryRun 非削除 を確認

→ 通過後に Task 5 へ。

---



## Task 5: フロント API クライアントに削除を追加

**Status:** done

**Description:**  
`imagePrepare.ts` に削除 API 呼び出しと、部分成功（500 + `deleted`）を扱えるエラー型を追加する。

**Acceptance criteria:**

- [x] `deleteImageEntries(dbName, paths)` が `POST .../delete` を呼ぶ
- [x] 200 時は `ApiImageDeleteResponse`（`dbName` / `dryRun` / `deleted`）を返す
- [x] 400 は通常 Error（メッセージに `error`）
- [x] 500 で `deleted` がある場合、削除済みパスを参照できる（専用 Error クラス可）

**Verification:**

- [x] `cd frontend && npm run build` 成功（`npx tsc --noEmit && npx vite build`）

**Dependencies:** Checkpoint A（契約固定後）

**Files:**

- `frontend/src/api/imagePrepare.ts`

**Estimated scope:** S（1 file）

---



## Task 6: ツリー複数選択 UI と選択削除

**Status:** done

**Description:**  
`ImagePrepare.tsx` にチェックボックス選択・選択削除ボタン・confirm・削除後の `reloadTree` を実装する。必要最小限のスタイルを `index.css` に追加する。

**Acceptance criteria:**

- [x] ファイルは常にチェック可、フォルダは `children.length === 0` のときのみチェック可
- [x] カテゴリヘッダは選択不可
- [x] 選択件数表示と「選択削除」ボタン（0 件または busy 時は disabled）
- [x] confirm に件数（とパス概要）を表示し、キャンセルで何もしない
- [x] 成功メッセージ表示（DryRun 注記あり）、**必ず** `reloadTree`
- [x] 失敗時もエラー表示し、可能なら `reloadTree`（部分成功の表示ずれ防止）
- [x] DB 切替時に選択状態をクリアする

**Verification:**

- [x] `cd frontend && npm run build` 成功
- [x] 手動: SPEC Success Criteria 1–4 相当を画面で確認

**Dependencies:** Task 5

**Files:**

- `frontend/src/pages/ImagePrepare.tsx`
- `frontend/src/index.css`

**Estimated scope:** M（2 files）

---



## Checkpoint B: E2E 手動確認 ＋ 回帰

- [x] Task 5–6 完了
- [x] 画面: 複数ファイル選択削除 → ツリー更新
- [x] 画面: 空フォルダ削除可、非空フォルダはチェック不可
- [x] 画面: カテゴリルートにチェックなし
- [x] 既存: アップロード・フォルダ作成・DB 切替が動作
- [x] `dotnet build` / `dotnet test --filter ImagePrepare` / `npm run build` 成功

→ Issue #23 実装完了。版番号は **1.4.0**（commit / push / タグはユーザー指示待ち）。

---

## 完了定義（Definition of Done）

- [x] SPEC Success Criteria すべて満たす（手動確認 OK）
- [x] PLAN の Out of Scope を実装していない
- [x] `docs/issue23/SPEC.md` / `PLAN.md` / `TASKS.md` の Status を更新済み
- [x] コード変更後は `graphify update .` を実行（グラフが存在する場合）
- [x] バージョンを 1.4.0 に更新済み（タグ付けは main マージ後）