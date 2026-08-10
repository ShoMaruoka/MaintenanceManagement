# Spec: 画像アップロード後の削除機能（Issue #23）

## 対象Issue

GitHub Issue [#23 画像アップロード後の削除機能](https://github.com/ShoMaruoka/MaintenanceManagement/issues/23)

## Objective

誤アップロードなどにより不要になったファイル／空フォルダを、画像情報準備画面から削除できるようにする。現状はアップロード・フォルダ作成・ツリー表示のみで、誤って置いたファイルを Web UI から取り除けない。

- **対象ユーザー**: メンテナンス前に静的ファイルを準備する開発者・運用担当者（issue #20 と同一）
- **対象画面**: 「画像情報準備」（`/images` / `ImagePrepare.tsx`）のみ
- **成功の姿**:
  - ツリー上でファイル／空フォルダを複数選択し、確認後に一括削除できる
  - 削除成功後、ツリーが再読み込みされ、消えたことが画面上で分かる
  - `Images` / `news` / `pdf` のカテゴリルートおよび `Files` 外のパスは削除できない

## 背景・現状

| 項目 | 現状 | 本 issue の方針 |
|------|------|----------------|
| 画像情報準備 | 一覧・アップロード・フォルダ作成のみ（issue #20） | **削除（複数選択）を追加** |
| 削除手段 | OS 上の手作業、または本番前準備での移動時のみ STG 側から消える | Web UI から明示削除 |
| API | `GET tree` / `POST upload` / `POST folders` | `DELETE`（または同等）で複数パス削除を追加 |
| 本番前準備 | 選択ファイルを本番側へ移動（STG 側削除を含む） | **変更なし** |

## ASSUMPTIONS I'M MAKING

1. **削除対象は STG 側 `Deploy_DEV2STG\Files` のみ**（issue #20 の `FilesPath`）。本番フォルダ（`FilesDeploy2PrdPath`）は触らない。
2. **削除可能**: ファイル、および **中身が空のフォルダ**（ファイルもサブフォルダもない）。中身があるフォルダは削除不可（API は 400、UI は選択不可またはエラー表示）。
3. **カテゴリルート（`Images` / `news` / `pdf`）と `Files` 自体は削除不可**。
4. **UI はチェックボックス複数選択 →「選択削除」**。削除前に確認ダイアログ（選択一覧を表示）。既存の上書き確認と同様、`window.confirm` でよい。
5. **物理削除（ハードデリート）**。ゴミ箱・復元は作らない。
6. **DryRun**（`DryRun: true`）時は実削除せず、削除予定パスをレスポンスに含めて成功扱いにする（アップロード／フォルダ作成と同じ）。
7. **削除成功後は必ずツリーを再読み込み**する。
8. **追加の認証・ロール・削除専用の監査ログ画面は作らない**（アップロードと同様）。
9. **リクエスト内のパスはすべて検証してから削除を開始**する。検証失敗が1件でもあれば全体を拒否し、何も削除しない。検証通過後に途中で OS エラーが出た場合は、それまでに削除できた分をレスポンスに含め、エラーを返す（部分成功を許容）。
10. パス指定は既存ツリーの `relativePath`（例: `Images/flash/img/a.png`、`Images/flash/img`）を用いる。既存の `TryResolveRelativeFile` 相当の安全性チェック（`..`、ルート外、カテゴリ検証）を流用・拡張する。

→ 特に 2, 4, 9 に誤りがあれば訂正してください。

## Tech Stack

- Backend: ASP.NET Core（既存 `ImagePrepareController` / `ImagePrepareService` を拡張）
- Frontend: React 18 + TypeScript + Vite（既存 `ImagePrepare.tsx` / `api/imagePrepare.ts` を拡張）
- スタイリング: 既存 `index.css` の `imgprep-*` プレフィックスを踏襲
- 追加依存パッケージ: なし

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Test:   cd backend && dotnet test
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

## Project Structure（変更・追加想定）

```
backend/Controllers/ImagePrepareController.cs  → 削除エンドポイント追加
backend/Services/ImagePrepareService.cs        → Delete ロジック（空フォルダ判定・パス検証）
backend/Models/ImagePrepareModels.cs           → 削除リクエスト／レスポンス DTO
backend/Tests/Services/（新規 or 既存）        → 削除の単体テスト（パス検証・空フォルダ・DryRun）

frontend/src/api/imagePrepare.ts               → deleteImages API クライアント
frontend/src/pages/ImagePrepare.tsx            → チェックボックス・選択削除 UI
frontend/src/index.css                         → 選択行・削除ボタン用スタイル（最小）

docs/issue23/SPEC.md                           → 本仕様書
docs/issue23/PLAN.md                           → 実装計画（次フェーズ）
docs/issue23/TASKS.md                          → タスク分解（その次）
```

## Code Style

既存の `ImagePrepareService` パターンに合わせる。

- パスは必ず `FilesPath` 配下に解決し、`PathSafety.IsUnderRoot` / `TryCombineUnderRoot` を使う
- 公開 API の相対パスは `/` 区切り
- DryRun 時はファイルシステム変更を行わず、レスポンスの `DryRun = true` を返す
- フロントは既存の `reloadTree` / `busy` / `formError` / `message` を再利用

```csharp
// 想定 API 形状（実装時に Controllers / Models へ配置）
public class ImageDeleteRequest
{
    /// <summary>Files ルートからの相対パス一覧（/ 区切り）。ファイルまたは空フォルダ。</summary>
    public List<string> Paths { get; set; } = [];
}

public class ImageDeleteResponse
{
    public string DbName { get; set; } = "";
    public bool DryRun { get; set; }
    public List<string> Deleted { get; set; } = [];
}
```

## Testing Strategy

| レベル | 方針 |
|--------|------|
| Backend 単体 | `ImagePrepareService` の削除：正常（ファイル／空フォルダ）、カテゴリルート拒否、非空フォルダ拒否、パストラバーサル拒否、DryRun で実削除されないこと |
| Frontend | 自動テスト基盤なしのため、手動確認（選択・確認ダイアログ・削除後ツリー更新） |
| ビルドゲート | `dotnet build` / `dotnet test` / `npm run build` が通ること |

## Boundaries

- **Always**
  - `Files` 外・`..`・カテゴリ不正・カテゴリルート削除を拒否する
  - 削除前に確認ダイアログを出す
  - 削除成功後にツリーを再読み込みする
  - DryRun 設定を尊重する
- **Ask first**
  - 非空フォルダの再帰削除へのスコープ拡大
  - 本番フォルダ側の削除
  - 新規 npm / NuGet 依存の追加
- **Never**
  - シークレットのコミット
  - ゴミ箱／復元機能の実装（本 issue スコープ外）
  - 「全部消す」一括操作の実装
  - 本番前準備・Pilot 適用フローの仕様変更

## 機能詳細

### F1. ツリー上の複数選択

- ファイル行・**空フォルダ行**にチェックボックスを付ける
- 中身があるフォルダは削除対象にできない（チェック不可、またはチェックしても削除時にエラー）。推奨: **子が1件以上あるフォルダはチェック不可**（ツリー上の `children.length === 0` を空とみなす）
- カテゴリヘッダ（`Images` / `news` / `pdf`）は選択不可
- 選択件数をツールバーまたはアップロードパネル付近に表示する

### F2. 選択削除

- 「選択削除」ボタン: 1件以上選択かつ非 busy のとき有効
- 確認ダイアログ例: `選択した N 件を削除します。よろしいですか？` + パス一覧（多い場合は先頭数件＋件数）
- 成功メッセージ: `N 件削除しました`（DryRun 時は既存どおり注記）
- 失敗時は `formError` に理由を表示し、可能な範囲でツリーを再読み込みする（部分成功時の表示ずれ防止）

### F3. 削除 API

| Method | Path | 説明 |
|--------|------|------|
| POST または DELETE | `/api/image-prepare/{dbName}/delete` | body: `{ paths: string[] }` |

- **推奨**: `POST .../delete`（複数パス＋JSON ボディのため。既存の `POST folders` / `POST upload` と揃える）
- 各 path について:
  1. 正規化・カテゴリ検証・`Files` 配下検証
  2. カテゴリルート（`Images` 等、セグメント1つのみ）は拒否
  3. 実体がファイル → 削除対象
  4. 実体がディレクトリ → **空のときのみ**削除対象。空でない／存在しないは 400
  5. 存在しないパスは 400（全体拒否）
- レスポンス: `{ dbName, dryRun, deleted: string[] }`
- DryRun: `deleted` に削除予定パスを入れ、ディスクは変更しない

## Out of Scope

- 本番フォルダ（`FilesDeploy2PrdPath`）側の削除
- ゴミ箱・復元
- 「全部消す」一括操作
- ファイルのリネーム／移動 UI
- 画像プレビュー
- 本番前準備・Pilot・STG 適用画面の変更
- 削除専用の監査ログ／履歴画面

## Success Criteria

1. 画像情報準備画面で、ファイルおよび空フォルダをチェックして複数選択できる
2. 「選択削除」→確認→削除が動作し、成功後にツリーが更新されて対象が消えている
3. カテゴリルート・非空フォルダ・`Files` 外パスは削除できない（明確なエラー）
4. DryRun 時はディスク上のファイル／フォルダが残ったまま、成功レスポンス（`dryRun: true`）が返る
5. 既存のアップロード・フォルダ作成・ツリー表示・本番前準備フローが壊れていない
6. `dotnet build` / `dotnet test` / `npm run build` が通る

## 決定事項（回答反映済み）

| # | 項目 | 決定 |
|---|------|------|
| Q1 | 削除対象 | **ファイル + 空フォルダ**（非空フォルダの再帰削除はしない） |
| Q2 | UI | **チェックボックス複数選択 → 選択削除** |
| Q3 | 削除後 | **ツリーを再読み込み** |
| Q4 | スコープ外 | 本番側削除、ゴミ箱復元、「全部消す」 |

## Open Questions

なし（SPEC 承認時に以下で確定）。

| # | 項目 | 決定 |
|---|------|------|
| OQ1 | 部分成功時の HTTP | 検証通過後の途中失敗は **`500` + `{ error, deleted }`**（削除済みパスを含める） |
| OQ2 | 非空フォルダの UI | **チェック不可**（`children.length === 0` のフォルダのみ選択可）。API 側でも非空は 400 で二重防御 |
