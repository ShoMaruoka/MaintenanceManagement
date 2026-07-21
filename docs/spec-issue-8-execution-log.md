# Spec: 実行履歴の機能強化（Issue #8）

## Objective

**AsIs:** 実行ログが確認できない
**ToBe:** 実行ログが確認できる

デプロイ実行（`DeployController.StreamDeploy`）では、実行中の各ステップの標準出力・エラー内容を `LogEntry` としてSSE配信しているが、セッション終了後は `logLines`（StringBuilder）ごと破棄され、DBにもAPIにも残らない。DBに保存されるのは実行結果（成功/失敗）、最後のエラーメッセージ1行、モジュール単位の成否のみ。

失敗時に画面には「エラーが発生しました。実行ログを確認してください」と表示されるが、確認できるログが実際には存在しない（`frontend/src/pages/History.tsx:164`）。

利用者（運用担当者）が実行履歴の詳細を開いたときに、そのセッションで実際に何が起きたか（各ステップの標準出力、bat実行結果、複数のエラー行を含む）を後から確認できるようにする。

## Scope（ユーザー確認済み）

- 対象: デプロイ実行（`DeployController` / `DeployService`）のログのみ。`FastCopyService` 等の他の実行系は対象外。
- 保存方式: DBに全文保存（既存の `ProductionReadyLog.LogDetail TEXT` カラムと同様のパターン）。
- 既存データ: 対象外。マイグレーション後の新規実行分からログを記録する。過去セッションは「ログなし」のまま。

## Tech Stack

- Backend: ASP.NET Core (C#), SQLite (`Microsoft.Data.Sqlite`)
- Frontend: React + TypeScript

## Project Structure（変更対象）

```
backend/Models/DeployModels.cs        → DeploySession に LogDetail プロパティ追加
backend/Services/DatabaseService.cs   → DeploySession テーブルに LogDetail カラム追加、Insert/Update/Get処理修正
backend/Controllers/DeployController.cs → logLines を UpdateDeploySessionStatus 経由でDB保存
backend/Controllers/HistoryController.cs → 変更不要（GetSessionById が LogDetail を返すよう修正されれば自動的に含まれる）
frontend/src/types.ts                 → DeploySession 型に logDetail?: string 追加
frontend/src/api/history.ts           → ApiDeploySession / formatSession に logDetail を追加
frontend/src/pages/History.tsx        → セッション詳細展開部にログ表示欄（<pre>相当）を追加
```

## Design

### 1. DBスキーマ

`DeploySession` テーブルに `LogDetail TEXT` カラムを追加（既存 `ProductionReadyLog.LogDetail` と同じパターン）。

既存DBとの後方互換のため、`EnsureCreated()` 内で `AppUser.Role` と同様に `ALTER TABLE DeploySession ADD COLUMN LogDetail TEXT;` を try/catch で追加する。

### 2. バックエンド

- `DatabaseService.UpdateDeploySessionStatus` にログ全文を受け取る引数 `logDetail` を追加し、UPDATE文に含める。
- `DeployController.StreamDeploy` で蓄積している `logLines.ToString()` を、ストリーム終了後の `UpdateDeploySessionStatus` 呼び出しに渡す。
- `DatabaseService.GetSessionById` / `GetRecentSessions` が `LogDetail` を読み出し `DeploySession.LogDetail` にセットする。
  - 一覧APIは行数が多くなるとレスポンスが肥大化するため、`GetRecentSessions` では `LogDetail` を含めず、詳細API（`GetSessionById` → `GET /api/history/sessions/{id}`）でのみ返す方針とする（`History.tsx` は行クリック時に詳細APIを呼んでいるため、この設計と自然に一致する）。
- `Models/DeployModels.cs` の `DeploySession` に `public string? LogDetail { get; set; }` を追加。

### 3. フロントエンド

- `types.ts` の `DeploySession` に `logDetail?: string` を追加。
- `api/history.ts`: `ApiDeploySession` interface に `logDetail?: string` を追加し、`getSession()` の戻り値（詳細取得時のみ）に含める。一覧取得（`getSessions`）はそのままで問題ない（バックエンドが返さないため）。
- `History.tsx` のセッション詳細展開部（`log-session-detail` 内）に、ログ本文表示エリアを追加する。
  - `<pre>` またはスクロール可能な `<div>` で等幅フォント表示。
  - ログが無い場合（過去セッション or 実行中）は「ログがありません」等の表示。
  - 失敗時の既存メッセージ「エラーが発生しました。実行ログを確認してください」の下にログ本文を表示する形にする。

## Code Style

既存コードの慣習に従う。C#側は式形式のSQLコマンド（`"""..."""` raw string）、フロントは既存の `table-row` / `log-session-detail` 等のクラス名パターンを踏襲。

## Testing Strategy

- 既存にテストプロジェクトがあるか要確認。無ければ手動確認で代替。
- 手動確認手順:
  1. STGデプロイを1件実行し成功させる → 履歴詳細を開いてログ本文が表示されることを確認
  2. 意図的に失敗するデプロイを実行 → ログ本文にエラー行が含まれることを確認
  3. マイグレーション前に存在した過去セッションの詳細を開く → エラーにならず「ログなし」表示になることを確認

## Boundaries

- Always: 既存のDeploySessionテーブルのカラム追加は後方互換の ALTER TABLE 方式を踏襲する
- Ask first: なし（スコープ内の変更は承認済み）
- Never: 過去セッションのログを遡って生成しない、FastCopyService等スコープ外の実行系には手を入れない

## Success Criteria

- デプロイ実行後、履歴詳細画面でそのセッションの実行ログ全文（各ステップの標準出力・エラー行）が確認できる
- 過去セッション（マイグレーション前）の詳細を開いてもエラーにならず、ログなしとして扱われる
- 一覧APIのレスポンスサイズはログ全文を含まないため増大しない

## Open Questions

- なし（スコープ確認済み）
