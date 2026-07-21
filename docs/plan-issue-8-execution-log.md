# Implementation Plan: 実行履歴の機能強化（Issue #8）

Spec: `docs/spec-issue-8-execution-log.md`

## Overview

デプロイ実行時に既に生成されている実行ログ（`logLines`）をDBに永続化し、履歴詳細画面で確認できるようにする。DBスキーマ → バックエンド保存 → バックエンド取得API → フロントエンド表示、の順で1本の縦スライスとして実装する（機能単位が小さいためフェーズを分ける必要はないが、依存順に4タスクへ分解する）。

## Architecture Decisions

- `DeploySession` テーブルに `LogDetail TEXT` カラムを追加。既存の `ProductionReadyLog.LogDetail` と同一パターンを踏襲し、`AppUser.Role` と同様の `ALTER TABLE ... ADD COLUMN` による後方互換マイグレーションを使う。
- ログ全文は**一覧API（`GetRecentSessions`）には含めず、詳細API（`GetSessionById`）でのみ**返す。理由: 一覧は最大500件を一括取得するためレスポンス肥大化を避ける。`History.tsx` は行クリック時に詳細APIを呼ぶ設計と自然に一致する。
- 対象はデプロイ実行のみ。過去セッションのログは遡及生成しない（NULL/未定義のまま「ログなし」表示）。

## Task List

### Task 1: DeploySession に LogDetail カラムを追加し、保存経路を通す

**Description:** DBスキーマに `LogDetail TEXT` カラムを追加し、`DeployController.StreamDeploy` で蓄積している `logLines` を保存できるようにする。バックエンドの保存側のみを対象とし、取得・表示は別タスク。

**Acceptance criteria:**
- [ ] `DeploySession` テーブルに `LogDetail TEXT` カラムが存在する（新規DB作成時・既存DBへのALTER両方で）
- [ ] `DatabaseService.UpdateDeploySessionStatus` が `logDetail` 引数を受け取り、UPDATE文に含める
- [ ] `DeployController.StreamDeploy` が `logLines.ToString()` を `UpdateDeploySessionStatus` に渡す

**Verification:**
- [ ] `dotnet build` が通る
- [ ] 既存DBファイル（`maintenance.db` 等）を使ってアプリを起動し、例外なく `ALTER TABLE` が実行される（try/catchで無視されるため2回目以降の起動でもエラーにならないことを確認）
- [ ] STGデプロイを1件実行後、SQLiteを直接開き `SELECT LogDetail FROM DeploySession ORDER BY SessionId DESC LIMIT 1;` でログ全文が入っていることを確認

**Dependencies:** None

**Files likely touched:**
- `backend/Services/DatabaseService.cs`
- `backend/Controllers/DeployController.cs`

**Estimated scope:** Small (2 files)

---

### Task 2: 詳細取得APIで LogDetail を返す

**Description:** `DeploySession` モデルに `LogDetail` プロパティを追加し、`GetSessionById`（詳細取得）が `LogDetail` を読み出して返すようにする。`GetRecentSessions`（一覧取得）は変更しない（アーキテクチャ決定どおりログ全文を含めない）。

**Acceptance criteria:**
- [ ] `Models/DeployModels.cs` の `DeploySession` に `public string? LogDetail { get; set; }` が追加されている
- [ ] `GetSessionById` のSELECT文とマッピングに `LogDetail` が含まれる
- [ ] `GetRecentSessions` のSELECT文・レスポンスに `LogDetail` が含まれない（意図的な非対称）

**Verification:**
- [ ] `dotnet build` が通る
- [ ] `GET /api/history/sessions/{id}`（Task 1で実行したセッションのID）のレスポンスJSONに `logDetail` フィールドが含まれ、ログ全文が入っている
- [ ] `GET /api/history/sessions` のレスポンスJSONに `logDetail` フィールドが含まれない

**Dependencies:** Task 1

**Files likely touched:**
- `backend/Models/DeployModels.cs`
- `backend/Services/DatabaseService.cs`

**Estimated scope:** Small (2 files)

---

### Task 3: フロントエンド型・APIクライアントに logDetail を反映

**Description:** `types.ts` の `DeploySession` 型と `api/history.ts` の詳細取得処理に `logDetail` を追加し、`getSession()` の戻り値に含める。

**Acceptance criteria:**
- [ ] `types.ts` の `DeploySession` に `logDetail?: string` が追加されている
- [ ] `api/history.ts` の `ApiDeploySession` interface に `logDetail?: string` が追加されている
- [ ] `formatSession()` が `logDetail` をそのまま `DeploySession` にマッピングする
- [ ] `getSessions()`（一覧）は `logDetail` を扱わなくてよい（バックエンドが返さないため未定義のままでよい）

**Verification:**
- [ ] `npm run build`（frontend）が型エラーなく通る
- [ ] ブラウザDevToolsで `getSession()` 呼び出し後の戻り値に `logDetail` が入っていることを確認（コンソールログ等で一時確認）

**Dependencies:** Task 2

**Files likely touched:**
- `frontend/src/types.ts`
- `frontend/src/api/history.ts`

**Estimated scope:** XS (2 files)

---

### Task 4: History.tsx にログ表示欄を追加

**Description:** セッション詳細展開部（`log-session-detail` 内）に、`logDetail` を等幅フォントで表示するログ本文エリアを追加する。ログが無い場合（過去セッション・実行中）は「ログがありません」等の代替表示にする。

**Acceptance criteria:**
- [ ] セッション行を展開すると、`logDetail` があればログ全文が `<pre>` 等でスクロール可能に表示される
- [ ] `logDetail` が未定義/空の場合、「ログがありません」等の文言が表示され、エラーにならない
- [ ] 失敗セッション（`status === 'failed'`）の既存の赤枠メッセージはそのまま残し、その下にログ本文を表示する

**Verification:**
- [ ] `npm run build` が通る
- [ ] ブラウザで以下を目視確認:
  1. Task 1で成功させたデプロイの履歴を展開 → ログ全文が表示される
  2. 意図的に失敗するデプロイを実行 → 展開してエラー行を含むログが表示される
  3. マイグレーション前の過去セッション（`logDetail` なし）を展開 → 「ログがありません」表示になりエラーにならない

**Dependencies:** Task 3

**Files likely touched:**
- `frontend/src/pages/History.tsx`

**Estimated scope:** Small (1 file)

## Checkpoint: 完了時

- [ ] `dotnet build`（backend）、`npm run build`（frontend）がともに通る
- [ ] STGデプロイの成功・失敗それぞれで実行ログが履歴詳細画面から確認できる
- [ ] 過去セッション（ログなし）の詳細表示がエラーにならない
- [ ] 一覧API（`GET /api/history/sessions`）のレスポンスサイズがログ追加前と変わらない
- [ ] Spec の Success Criteria をすべて満たしている

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 既存DBへのALTER TABLEが本番運用中のファイルに影響 | Medium | `AppUser.Role` で実績のあるtry/catchパターンをそのまま踏襲し、失敗しても無視されるようにする |
| ログ全文が非常に長大化しUI/DBを圧迫するケース | Low | 現状のバッチ処理は6ステップ程度でログ量は限定的。将来的に長大化する場合は別途上限設定を検討（今回スコープ外） |

## Open Questions

なし（Specの範囲で完結）
