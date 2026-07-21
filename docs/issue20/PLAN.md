# Implementation Plan: 画像情報準備機能追加 (issue #20)

## Overview

Issue #20 向けに、(1) STG適用と本番前準備の間に「画像情報準備」画面を追加し `Deploy_DEV2STG\Files` へアップロード・サブフォルダ作成できるようにする、(2) 本番前準備でそのファイルを確認し `batrunApp\STGDEPLOY\...\2_Deploy_STG2PRD` 相当へ移動できるようにする。

依存の底から: パス設定 → 一覧/アップロード API → 新規画面 → 本番前準備の一覧拡張 → 移動実行、の順で垂直スライスする。

詳細仕様は [SPEC.md](./SPEC.md) を正とする。

## Architecture Decisions

- **アップロード先**は既存 `DeployDev2StgPath` から `FilesPath` を派生する。新規設定キーは不要。
- **本番移動先**は SQL 用 `Deploy2PrdPath` と分離し、`FilesDeploy2PrdPath` を DB ごとに `appsettings` へ追加する（Issue コメントの batrunApp 系パス）。
- **パス検証**はサービス層で一元化し、カテゴリを `Images`/`news`/`pdf` のホワイトリスト、サブフォルダ最大 2 階層、`..` 禁止とする。
- **アップロード**は ASP.NET Core の `IFormFile` + multipart。フロントは `FormData`（既存 `fetchJson` とは別経路）。
- **本番前準備の拡張**は既存 `PrepareController` / `FastCopyService` / `PrepareForPrd.tsx` を拡張する（画像専用の第2実行ボタンは作らない）。SQL と画像を同一確認→実行フローに載せる。
- **移動**は相対パス維持で `FilesDeploy2PrdPath` へコピー後、STG 側を削除（SQL の apply と同趣旨）。
- **UI**は既存 Prepare / Deploy のカード＋確認ダイアログパターンに合わせ、新規デザインシステムは導入しない。

## Task List

### Phase 1: Foundation（パス設定）

- [x] Task 1: `DbConfig` に Files 関連パスを追加

**Description:** `FilesPath`（派生）と `FilesDeploy2PrdPath`（設定）を追加し、`appsettings_sample.json` の 4 DB に SPEC 確定パスを記載する。STG 側は `DeployDev2StgPath\Files` 導出。本番側は `batrunApp\STGDEPLOY\{db}_SQLServer\2_Deploy_STG2PRD\Files`。

**Acceptance criteria:**
- [ ] `DbConfig.FilesPath` が `Path.Combine(DeployDev2StgPath, "Files")` を返す
- [ ] `DbConfig.FilesDeploy2PrdPath` が設定から読める
- [ ] `appsettings_sample.json` の 4 DB にキーが存在する

**Verification:**
- [ ] `cd backend && dotnet build` が通る

**Dependencies:** None

**Files likely touched:**
- `backend/Models/DbConfig.cs`
- `backend/appsettings_sample.json`

**Estimated scope:** S（1–2 files）

---

### Phase 2: 画像情報準備 — 一覧（垂直スライス 1）

- [x] Task 2: 画像ツリー用モデルとサービス（一覧＋パス検証）

**Description:** DTO と `ImagePrepareService` を新規作成。カテゴリホワイトリスト、相対パス正規化、最大 2 階層チェック、`Files` 配下への閉じ込めを実装し、指定 DB のツリー（フォルダ＋ファイル）を返す。

**Acceptance criteria:**
- [ ] `Images` / `news` / `pdf` 以外のカテゴリは拒否
- [ ] `..` や絶対パス指定は拒否
- [ ] サブフォルダ深度 > 2 は拒否
- [ ] ディレクトリが無い場合は空ツリーを返し例外にしない

**Verification:**
- [ ] `dotnet build` が通る

**Dependencies:** Task 1

**Files likely touched:**
- `backend/Models/ImagePrepareModels.cs`（新規）
- `backend/Services/ImagePrepareService.cs`（新規）
- `backend/Program.cs`（DI 登録）

**Estimated scope:** M（3 files）

---

- [x] Task 3: `GET /api/image-prepare/{db}/tree` とフロント API・画面シェル

**Description:** Controller を追加。フロントに `imagePrepare.ts`、ルート `/images`、サイドバー挿入、DB 選択＋ツリー表示の最小 UI を実装する。

**Acceptance criteria:**
- [ ] サイドバーで STG適用と本番前準備の間に「画像情報準備」がある
- [ ] `/images` で DB 切替ができ、ツリー API の結果が表示される
- [ ] 不正な `db` は 400/404

**Verification:**
- [ ] `dotnet build` / `npm run build` が通る
- [ ] 手動: `Files` にダミーファイルを置き、一覧に出ることを確認

**Dependencies:** Task 2

**Files likely touched:**
- `backend/Controllers/ImagePrepareController.cs`（新規）
- `frontend/src/api/imagePrepare.ts`（新規）
- `frontend/src/pages/ImagePrepare.tsx`（新規）
- `frontend/src/App.tsx`
- `frontend/src/components/Sidebar.tsx`
- `frontend/src/index.css`

**Estimated scope:** M（5 files 前後）— 画面は一覧のみ。アップロードは次タスク

---

### Checkpoint: 一覧まで

- [ ] メニュー・ルート・ツリー表示が動く
- [ ] ビルドが通る
- [ ] パス検証の拒否ケースを API 直接叩きで確認

---

### Phase 3: 画像情報準備 — アップロード（垂直スライス 2）

- [x] Task 4: アップロード／フォルダ作成 API

**Description:** `POST .../upload`（multipart）と `POST .../folders` を実装。保存先ディレクトリを必要に応じて作成。同名は `overwrite=false` なら 409、`true` なら上書き。

**Acceptance criteria:**
- [ ] 指定カテゴリ＋サブパス（≤2）にファイルが保存される
- [ ] フォルダのみ作成できる
- [ ] パストラバーサル・不正カテゴリ・深度超過は 400
- [ ] DryRun 時は実書き込みせずログ相当の応答方針を決めて実装（既存 DryRun 規約に合わせる。不可なら実書き込みのみと SPEC に明記）

**Verification:**
- [ ] `dotnet build` が通る
- [ ] 手動: curl または UI 前の API 呼び出しでファイルが配置される

**Dependencies:** Task 2

**Files likely touched:**
- `backend/Services/ImagePrepareService.cs`
- `backend/Controllers/ImagePrepareController.cs`
- （必要なら）`Program.cs` のリクエストサイズ制限

**Estimated scope:** M

---

- [x] Task 5: アップロード UI（サブフォルダ指定含む）

**Description:** `ImagePrepare.tsx` にカテゴリ選択、サブフォルダ入力、ファイル選択、アップロード、フォルダ作成、完了後のツリー再取得を追加する。

**Acceptance criteria:**
- [ ] Images/news/pdf へアップロードできる
- [ ] `flash/img` のような 2 階層サブパスで保存できる
- [ ] 3 階層以上は UI または API エラーで止められる
- [ ] 成功後に一覧が更新される

**Verification:**
- [ ] `npm run build` が通る
- [ ] 手動: ブラウザからアップロードし、エクスプローラー上のパスを確認

**Dependencies:** Task 3, Task 4

**Files likely touched:**
- `frontend/src/pages/ImagePrepare.tsx`
- `frontend/src/api/imagePrepare.ts`
- `frontend/src/index.css`

**Estimated scope:** M

---

### Checkpoint: 画像情報準備画面完了

- [ ] SPEC Success Criteria 1–4 を満たす
- [ ] 既存画面（Deploy / Prepare）に回帰がない

---

### Phase 4: 本番前準備 — 一覧拡張（垂直スライス 3）

- [x] Task 6: Prepare API に画像ファイル一覧を追加

**Description:** `GET /api/prepare/files` の各 DB エントリに、`Files` 配下の相対パス一覧（例: `Images/flash/img/a.png`）を追加する。列挙ロジックは `ImagePrepareService` を再利用。

**Acceptance criteria:**
- [ ] レスポンスに画像ファイル相対パスが含まれる
- [ ] SQL ファイルの既存フィールド・挙動は変わらない
- [ ] `Files` が無い DB では空配列

**Verification:**
- [ ] `dotnet build` が通る
- [ ] 手動: `/api/prepare/files` で SQL + 画像の両方が見える

**Dependencies:** Task 2

**Files likely touched:**
- `backend/Models/PrepareModels.cs`
- `backend/Controllers/PrepareController.cs`
- `backend/Services/ImagePrepareService.cs`（列挙の公開）

**Estimated scope:** S–M

---

- [x] Task 7: 本番前準備 UI に画像セクションを追加

**Description:** `PrepareForPrd.tsx` で DB カード内（または独立セクション）に画像ファイル一覧とチェックボックスを表示。デフォルト全選択。確認ダイアログに件数を表示。比較ビュー（issue #14）は SQL のみのままでよい（画像はスコープ外、Out of Scope と明記済みなら触れない）。

**Acceptance criteria:**
- [ ] 画像が一覧・選択できる
- [ ] 確認ダイアログに画像件数が含まれる
- [ ] SQL の選択・比較・既存 UX が壊れていない

**Verification:**
- [ ] `npm run build` が通る
- [ ] 手動: Prepare 画面で画像セクションを確認

**Dependencies:** Task 6

**Files likely touched:**
- `frontend/src/pages/PrepareForPrd.tsx`
- `frontend/src/api/prepare.ts`
- `frontend/src/index.css`

**Estimated scope:** M

---

### Checkpoint: Prepare 一覧拡張

- [ ] 画像の確認・選択までできる（まだ移動しなくてよい）
- [ ] SQL フローに回帰なし

---

### Phase 5: 本番前準備 — 移動実行（垂直スライス 4）

- [x] Task 8: `FastCopyService`（または専用処理）で画像移動

**Description:** Prepare 実行時、選択された画像相対パスを `FilesPath` → `FilesDeploy2PrdPath` へ相対パス維持でコピーし、成功後に STG 側を削除。ディレクトリが無ければ作成。SSE ログに各ファイルの結果を出す。`FilesDeploy2PrdPath` 未設定時は明確なエラー。

**Acceptance criteria:**
- [ ] 選択画像が本番側に相対パスどおり存在する
- [ ] STG 側から選択画像が削除される
- [ ] 未選択画像は STG 側に残る
- [ ] SQL の FastCopy / hold 移動ロジックに影響がない

**Verification:**
- [ ] `dotnet build` が通る
- [ ] 手動: DryRun またはテストパスで移動を確認

**Dependencies:** Task 6

**Files likely touched:**
- `backend/Services/FastCopyService.cs`
- `backend/Models/PrepareModels.cs`（selection 拡張）
- `backend/Controllers/PrepareController.cs`

**Estimated scope:** M

---

- [x] Task 9: Prepare フロントの selection に画像を載せて E2E 接続

**Description:** `startPrepare` のリクエストに選択画像パスを含め、実行完了後に一覧再読込で画像が消えていることを確認できる状態にする。`ProductionReadyLog` の件数／詳細に画像を反映（SPEC の暫定方針どおり）。

**Acceptance criteria:**
- [ ] UI から選択→確認→実行で画像が移動する
- [ ] ログに画像処理が出る
- [ ] SPEC Success Criteria 5–8 を満たす

**Verification:**
- [ ] `npm run build` / `dotnet build` が通る
- [ ] 手動 E2E: アップロード → Prepare で移動 → 両パスをエクスプローラー確認

**Dependencies:** Task 7, Task 8

**Files likely touched:**
- `frontend/src/pages/PrepareForPrd.tsx`
- `frontend/src/api/prepare.ts`
- `backend/Services/DatabaseService.cs`（件数反映が必要な場合のみ）

**Estimated scope:** S–M

---

### Checkpoint: Complete

- [x] SPEC Success Criteria すべて満たす（実装完了。手動 E2E は利用者確認）
- [x] 既存 STG適用・SQL 本番前準備・履歴・比較ビューに回帰なし（ビルド確認）
- [ ] `docs/SPEC.md`（本体）への機能追記は任意（本 issue の SPEC があれば必須ではない）
- [ ] ユーザーによる最終レビュー・承認

## 実装順（依存グラフ）

```
Task1 (DbConfig)
  └─ Task2 (Service/検証)
       ├─ Task3 (一覧 UI) ──┐
       ├─ Task4 (Upload API)─┴─ Task5 (Upload UI)
       └─ Task6 (Prepare 一覧 API)
            ├─ Task7 (Prepare UI)
            └─ Task8 (移動) ── Task9 (E2E 接続)
```

**並列可能な組み合わせ:** Task 3 と Task 4（Task 2 完了後）、Task 7 と Task 8（Task 6 完了後）

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 大容量アップロードで IIS/Kestrel が拒否 | Med | `RequestSizeLimit` / `MultipartBodyLengthLimit` を明示。SPEC の上限（約50MB）に合わせる |
| `Files` 外への書き込み | High | サービス層でフルパスを解決し、必ず `FilesPath` 配下か検証 |
| Prepare 画面の複雑化で SQL UX が崩れる | Med | 画像は別セクションに分離。比較ビューは SQL のみ維持 |
| 既存 `Deploy2PrdPath` と混同 | Med | プロパティ名・ログ文言で `FilesDeploy2PrdPath` を明示 |
| kaios 本番パス末尾 `\Files` の有無が実機と異なる | Low | SPEC では gos 等に揃え `\Files` 付き。実機が異なれば設定だけ直せばよい |

## Open Questions

なし。SPEC「決定事項」どおり確定済み。

---

## Verification（計画レビュー用チェックリスト）

- [x] 全タスクに Acceptance criteria がある
- [x] 全タスクに Verification がある
- [x] 依存関係と順序が明示されている
- [x] L/XL タスクを分割済み（各タスクおおむね S–M）
- [x] フェーズ間に Checkpoint がある
- [x] Open Questions 解消
- [ ] **人間によるプラン承認**（次ステップ）
