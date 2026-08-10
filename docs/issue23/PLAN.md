# Implementation Plan: 画像アップロード後の削除機能（Issue #23）

## Overview

画像情報準備画面（issue #20）に、誤アップロード救済のための **複数選択削除** を追加する。対象は STG 側 `FilesPath` 配下のファイルおよび空フォルダのみ。本番前準備・他画面は変更しない。

詳細仕様は [SPEC.md](./SPEC.md) を正とする。

実装順は依存の底から垂直スライスする:

```
パス解決の拡張（ファイル／フォルダ）
    → Delete サービス＋単体テスト
        → DELETE API（POST .../delete）
            → フロント API クライアント
                → ツリー複数選択 UI ＋ 選択削除
```

## Architecture Decisions

1. **エンドポイントは `POST /api/image-prepare/{dbName}/delete`**
   - JSON body `{ paths: string[] }`
   - 既存の `POST folders` / `POST upload` と揃える（複数パス＋ボディのため DELETE メソッドは使わない）

2. **パス解決はサービス層に集約**
   - 現行 `TryResolveRelativeFile` は「末尾＝ファイル」前提で、空フォルダパス（例: `Images/flash/img`）にはそのまま使えない
   - 新規に `TryResolveRelativeEntry`（仮名）を追加し、相対パスを `Files` 配下の絶対パスに解決する
   - セグメント数: 最低 2（カテゴリ + 名前）。カテゴリルート単独（`Images` 等）は拒否
   - サブフォルダ深度は既存どおり最大 2（`カテゴリ/sub1/sub2` まで。ファイルなら `カテゴリ/sub1/sub2/file.ext` でセグメント最大 4）
   - 既存 `TryResolveRelativeFile` は本番前準備など他呼び出しがあるため **破壊的変更を避ける**（削除専用の解決を追加、または内部共通化して既存を薄いラッパに）

3. **削除アルゴリズム（1 リクエスト）**
   1. `paths` が空 → 400
   2. 各 path を正規化・解決・種別判定（File / Directory / Missing）
   3. Directory かつ非空 → 400（全体拒否、何も消さない）
   4. カテゴリルート・不正パス・存在しない → 400（全体拒否）
   5. 重複 path は正規化後にユニーク化（大文字小文字無視）
   6. 検証 OK 後、DryRun でなければ削除実行（ファイル: `File.Delete`、空フォルダ: `Directory.Delete`）
   7. 途中の IO 失敗 → それまでに消えたパスを `deleted` に入れ、**500** + `{ error, deleted }`

4. **空フォルダの定義**
   - `Directory.EnumerateFileSystemEntries(dir)` が 0 件
   - UI 側はツリーの `children.length === 0` を空とみなしてチェック可能にする（二重防御: API でも再判定）

5. **UI**
   - `TreeNode` にチェックボックス（ファイルは常に可、フォルダは `children.length === 0` のときのみ）
   - 選択状態は `Set<string>`（`relativePath`）を `ImagePrepare` が保持し、props で渡す
   - カテゴリヘッダは選択不可
   - 「選択削除」ボタン＋ `window.confirm`（パス一覧、多いときは先頭数件＋件数）
   - 成功／失敗後とも `reloadTree`（SPEC: 成功後必須。失敗時も表示ずれ防止で再読込）

6. **テスト**
   - Backend: 一時ディレクトリ＋ `DbConfig.FilesPath` 相当を指すフィクスチャで `ImagePrepareService.Delete` を単体テスト
   - Frontend: 自動テストなし（手動確認チェックリストを Checkpoint に記載）

7. **変更しないもの**
   - `PrepareController` / `FastCopyService` / 本番前準備 UI
   - カテゴリホワイトリスト・拡張子制限・MaxUploadBytes
   - 認証モデル

## Component Map

| コンポーネント | 役割 | 依存 |
|----------------|------|------|
| `ImagePrepareModels` | `ImageDeleteRequest` / `ImageDeleteResponse` | なし |
| `ImagePrepareService.Delete` | 検証・DryRun・削除 | PathSafety, DbConfig, Models |
| `ImagePrepareController` | HTTP 入出力・400/404/500 マッピング | Service, DbConfigs |
| `imagePrepare.ts` | `deleteImageEntries` クライアント | fetch |
| `ImagePrepare.tsx` | 選択状態・確認・削除ボタン・ツリー更新 | API, TreeNode |
| 単体テスト | Delete の正常／拒否／DryRun | Service |

## Risks & Mitigations

| リスク | 影響 | 対策 |
|--------|------|------|
| `TryResolveRelativeFile` を共用変更して Prepare 側が壊れる | 本番前準備の画像移動が失敗 | 削除用解決を追加するか、共通化後に既存呼び出しの回帰を `dotnet test` で確認 |
| UI の「空」と実ディスクの「空」がずれる（他プロセスが書き込み） | チェックできたが API 400 | エラーメッセージをそのまま表示し、ツリー再読込 |
| 親フォルダと子を同時選択 | 子削除後に親が空になり消せる／順序問題 | 検証時点で両方存在すれば OK。削除は **深いパス優先**（パスセグメント数の降順）で実行し、親を後から消せるようにする |
| 部分成功のクライアント表示 | 消えた／残ったが不明 | 500 時も `deleted` をパースできればメッセージに含め、必ず `reloadTree` |

## Implementation Order（フェーズ）

### Phase 1: Backend Delete（垂直スライスの土台）

- DTO 追加
- パス解決（エントリ用）＋ `Delete` メソッド
- 単体テスト（ファイル削除、空フォルダ、非空拒否、ルート拒否、トラバーサル拒否、DryRun、深いパス優先）
- Controller エンドポイント

**Checkpoint A:** `dotnet test` 緑。API を直接叩いてファイル／空フォルダが消えること。

### Phase 2: Frontend Delete UI

- `deleteImageEntries` API
- ツリーチェックボックス＋選択 Set
- 選択削除ボタン・confirm・メッセージ
- 最小 CSS

**Checkpoint B:** 画面から複数選択削除でき、成功後ツリーが更新される。非空フォルダはチェック不可。

### Phase 3: 回帰確認

- アップロード・フォルダ作成・DB 切替が従来どおり動く
- `dotnet build` / `dotnet test` / `npm run build`

## Parallel vs Sequential

| 作業 | 並列可？ |
|------|----------|
| Phase 1 内の Models + Service + Tests | Service と Tests は順次（先に Service） |
| Controller | Service 完了後 |
| Frontend | Phase 1 Checkpoint A の後（契約固定後） |
| CSS | Frontend と同時可 |

## Verification Commands

```
cd backend && dotnet build
cd backend && dotnet test
cd frontend && npm run build
```

手動（Checkpoint B）:

1. ダミーファイルをアップロード → チェック → 選択削除 → ツリーから消える
2. 空フォルダを作成 → チェック → 削除できる
3. ファイル入りフォルダはチェックできない
4. カテゴリルートにチェックがない
5. DryRun=true 時はディスク上に残る

## Out of Scope（再掲）

本番側削除、ゴミ箱、全部消す、リネーム、プレビュー、他画面変更、削除専用監査ログ。

## Next Step

PLAN 承認済み。タスク分解は [TASKS.md](./TASKS.md)。Task 1–6 実装済み（手動 Checkpoint はユーザー確認）。
