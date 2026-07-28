# Graph Report - MaintenanceManagement  (2026-07-28)

## Corpus Check
- 88 files · ~42,854 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1074 nodes · 1638 edges · 49 communities (48 shown, 1 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 27 edges (avg confidence: 0.79)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `b798ff88`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- DbConfig
- ImagePrepareService
- WebSourceDeployService
- DatabaseService
- DeployService
- http
- Spec: 画像情報準備機能追加 (issue #20)
- package.json
- types.ts
- Jenkins + IIS + Kestrel デプロイガイド
- compilerOptions
- Issue #1 実装仕様: ユーザー選択機能
- メンテナンス管理 Web アプリ 仕様書
- App.tsx
- .GetModulesAsync
- webSourcePrepare.ts
- deploy.ts
- PrepareForPrd.tsx
- ローカルテスト手順書
- Spec: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)
- fetchJson
- PrepareLogViewer.tsx
- MaintenanceManagement.Api.csproj
- ConfirmDialog.tsx
- SPEC: 実行履歴の機能強化（Issue #3）
- SPEC: Issue #7 — STG適用の複数DB横断実行
- 実装計画: Issue #14 — 本番前準備(Prepare)画面の横並び比較機能
- Spec: 本番前準備(Prepare)画面 - 横並び比較機能
- 実装計画: Issue #7 — STG適用の複数DB横断実行
- Spec: STG適用画面「削除」モジュールの検出方式見直し (issue #9)
- SPEC: STG → pilot サーバーへのWebソース配布機能（Issue #25）
- SPEC: Issue #5 — 選択機能の強化
- Implementation Plan: Pilot環境適用の web.config ファイル差し替え
- SPEC: ダッシュボード実行履歴の詳細表示（Issue #3 追加仕様）
- Spec: 実行履歴の機能強化（Issue #8）
- Spec: Pilot環境適用の web.config をパイロット用ファイル差し替えに変更
- Implementation Plan: Issue #5 — 選択機能の強化
- Task List
- Implementation Plan: STG適用画面「削除」モジュールの検出方式見直し (issue #9)
- Tasks: Pilot環境適用の web.config ファイル差し替え
- Implementation Plan: 実行履歴の機能強化（Issue #8）
- Spec: モジュールの適用区分の一括変更機能 (issue #10)
- Implementation Plan: モジュールの適用区分の一括変更機能 (issue #10)
- Task List
- Implementation Plan: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)
- Implementation Plan: STG → pilot Webソース配布機能（Issue #25）
- テスト用ドライランバッチ

## God Nodes (most connected - your core abstractions)
1. `DbConfig` - 33 edges
2. `DbName` - 29 edges
3. `MaintenanceManagement.Api.Models` - 21 edges
4. `DatabaseService` - 21 edges
5. `DeployService` - 21 edges
6. `ImagePrepareService` - 21 edges
7. `WebSourceDeployService` - 21 edges
8. `useUser()` - 18 edges
9. `LogEntry` - 17 edges
10. `ManualApplyService` - 17 edges

## Surprising Connections (you probably didn't know these)
- `ApiDeployRequest` --references--> `DbName`  [EXTRACTED]
  frontend/src/api/deploy.ts → frontend/src/types.ts
- `ApiPrepareDbEntry` --references--> `DbName`  [EXTRACTED]
  frontend/src/api/prepare.ts → frontend/src/types.ts
- `ConfirmDialog()` --calls--> `useUser()`  [EXTRACTED]
  frontend/src/components/ConfirmDialog.tsx → frontend/src/context/UserContext.tsx
- `Props` --references--> `DbName`  [EXTRACTED]
  frontend/src/components/PrepareLogViewer.tsx → frontend/src/types.ts
- `DeployController` --references--> `DeployService`  [EXTRACTED]
  backend/Controllers/DeployController.cs → backend/Services/DeployService.cs

## Import Cycles
- None detected.

## Communities (49 total, 1 thin omitted)

### Community 0 - "DbConfig"
Cohesion: 0.06
Nodes (46): applied, CancellationToken, ChannelReader, HttpGet, HttpPost, IActionResult, JsonSerializerOptions, List (+38 more)

### Community 1 - "ImagePrepareService"
Cohesion: 0.08
Nodes (27): HttpGet, HttpPost, IActionResult, IFormFile, List, ImagePrepareController, List, ImageCategoryNode (+19 more)

### Community 2 - "WebSourceDeployService"
Cohesion: 0.06
Nodes (34): CancellationToken, ChannelReader, HttpGet, HttpPost, IActionResult, JsonSerializerOptions, List, string (+26 more)

### Community 3 - "DatabaseService"
Cohesion: 0.06
Nodes (27): CancellationToken, HttpPost, JsonSerializerOptions, List, Task, DeployController, HttpGet, IActionResult (+19 more)

### Community 4 - "DeployService"
Cohesion: 0.26
Nodes (14): DeployModule, DeployRequest, LogEntry, bool, CancellationToken, ChannelReader, ChannelWriter, HashSet (+6 more)

### Community 5 - "http"
Cohesion: 0.07
Nodes (28): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, applicationUrl (+20 more)

### Community 6 - "Spec: 画像情報準備機能追加 (issue #20)"
Cohesion: 0.05
Nodes (40): Architecture Decisions, Checkpoint: Complete, Checkpoint: Prepare 一覧拡張, Checkpoint: 一覧まで, Checkpoint: 画像情報準備画面完了, Implementation Plan: 画像情報準備機能追加 (issue #20), Open Questions, Overview (+32 more)

### Community 7 - "package.json"
Cohesion: 0.07
Nodes (26): dependencies, react, react-dom, react-router-dom, devDependencies, @types/react, @types/react-dom, typescript (+18 more)

### Community 8 - "types.ts"
Cohesion: 0.05
Nodes (64): ApiDeploySession, buildModuleSummary(), formatExecutedAt(), formatSession(), getSession(), getSessions(), ApiImageCategoryNode, ApiImageCreateFolderResponse (+56 more)

### Community 9 - "Jenkins + IIS + Kestrel デプロイガイド"
Cohesion: 0.05
Nodes (38): 1-1. Windows Server 環境要件, 1-2. IIS アプリケーションプール作成, 1-3. IIS サイト作成（ポート 57010）, 1-4. IIS 認証設定, 2-1. URL Rewrite モジュールのインストール, 2-2. web.config にリバースプロキシルール追加, 2-3. デプロイ先フォルダの準備, 3-1. appsettings.json を Kestrel 用に設定 (+30 more)

### Community 10 - "compilerOptions"
Cohesion: 0.09
Nodes (21): compilerOptions, allowImportingTsExtensions, isolatedModules, jsx, lib, module, moduleResolution, noEmit (+13 more)

### Community 11 - "Issue #1 実装仕様: ユーザー選択機能"
Cohesion: 0.05
Nodes (38): 1. Objective（目的）, 2-1. 起動フロー, 2-2. ユーザー切り替え, 2-3. 実行履歴への反映, 2. 機能詳細, 3. データベース変更（SQLite）, 4-1. 新規ファイル, 4-2. DatabaseService の変更 (+30 more)

### Community 12 - "メンテナンス管理 Web アプリ 仕様書"
Cohesion: 0.06
Nodes (33): 10. IIS 構成, 11. Boundaries（制約・ルール）, 12. テスト戦略, 13. フェーズ2（将来の拡張）, 14. 実装進捗（2026-06-23 時点）, 1. Objective（目的）, 2. 対象システム・DB, 3. Core Features（機能一覧） (+25 more)

### Community 13 - "App.tsx"
Cohesion: 0.16
Nodes (13): AdminRoute(), PAGE_TITLES, ProtectedRoute(), Header(), Props, ADMIN_ITEMS, NAV_ITEMS, Sidebar() (+5 more)

### Community 14 - ".GetModulesAsync"
Cohesion: 0.19
Nodes (12): HttpGet, IActionResult, List, Task, ModulesController, List, ModuleInfo, ModuleListResponse (+4 more)

### Community 15 - "webSourcePrepare.ts"
Cohesion: 0.16
Nodes (17): ApiWebSourceDeployDone, ApiWebSourceDeployRequest, ApiWebSourceInfo, ApiWebSourcePilotTargetInfo, ApiWebSourceSqlDeployResult, ApiWebSourceStreamEvent, ApiWebSourceTargetResult, getWebSourceInfo() (+9 more)

### Community 16 - "deploy.ts"
Cohesion: 0.19
Nodes (13): ApiDeployDone, ApiDeployModule, ApiDeployRequest, ApiDeployStreamEvent, ApiLogEntry, isDeployDone(), startDeploy(), INITIAL_STEP_STATES() (+5 more)

### Community 17 - "PrepareForPrd.tsx"
Cohesion: 0.12
Nodes (24): ApiManualApplyItem, ApiPrepareDbEntry, ApiPrepareDone, ApiPrepareFileInfo, ApiPrepareImageSelection, ApiPrepareLogEntry, ApiPrepareManualSelection, ApiPrepareRequest (+16 more)

### Community 18 - "ローカルテスト手順書"
Cohesion: 0.08
Nodes (25): 1-1. SQL Server 接続設定, 1-2. MariaDB 接続設定, 1-3. DryRun モード（実ファイル・バッチを実行しない）, 1-4. パス設定, 1-5. 設定ファイル全体サンプル（DryRun + ローカル SQL Server）, 1. 接続設定, 2. サーバー起動手順, 3-1. ダッシュボード (+17 more)

### Community 19 - "Spec: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)"
Cohesion: 0.09
Nodes (23): Boundaries, Commands, Objective, Open Questions（残課題）, robocopy のスレッド数（/MT）, Spec: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27), Tech Stack, Viewのソース更新 (+15 more)

### Community 20 - "fetchJson"
Cohesion: 0.36
Nodes (8): fetchJson(), FetchOptions, addUser(), deleteUser(), getUsers(), UserManagePage(), UserSelectPage(), AppUser

### Community 21 - "PrepareLogViewer.tsx"
Cohesion: 0.33
Nodes (6): fetchStream(), ApiLogEntry, ApiPrepareDone, ApiPrepareStreamEvent, PrepareLogViewer(), Props

### Community 22 - "MaintenanceManagement.Api.csproj"
Cohesion: 0.29
Nodes (5): net8.0, Microsoft.Data.SqlClient (5.2.2), Microsoft.Data.Sqlite (8.0.11), MySqlConnector (2.3.7), Microsoft.NET.Sdk.Web

### Community 23 - "ConfirmDialog.tsx"
Cohesion: 0.40
Nodes (5): ConfirmDialog(), OP_LABEL_CLASS, Props, Props, MultiDbModules

### Community 25 - "SPEC: 実行履歴の機能強化（Issue #3）"
Cohesion: 0.10
Nodes (20): 10. Boundaries（制約）, 1. Objective（目的）, 2. 対象ユーザー, 3. As Is / To Be, 4. Core Features（機能要件）, 5. 受け入れ条件（Acceptance Criteria）, 6. 技術スタック, 7. 実装スコープ（変更ファイル） (+12 more)

### Community 26 - "SPEC: Issue #7 — STG適用の複数DB横断実行"
Cohesion: 0.10
Nodes (19): 1. Objective, 2. 現状の問題, 3-1. 確認ダイアログで全DB・全選択モジュールを表示する, 3-2. 実行は全DBのモジュールを順次処理する, 3-3. ログビューアで複数DBの実行進捗を表示する, 3-4. 実行完了後の選択クリア, 3. To Be（受け入れ条件）, 4-1. 型定義の追加 (+11 more)

### Community 27 - "実装計画: Issue #14 — 本番前準備(Prepare)画面の横並び比較機能"
Cohesion: 0.11
Nodes (17): Checkpoint: Phase 1-2完了後, Checkpoint: 全タスク完了後, Overview, Phase 1: 比較データ生成ロジック, Phase 2: 比較ビューUIコンポーネント, Phase 3: 既存画面への統合, Task 1: 比較データ生成・TSV変換ユーティリティを作成する, Task 2: `PrepareCompareView.tsx` を作成する (+9 more)

### Community 28 - "Spec: 本番前準備(Prepare)画面 - 横並び比較機能"
Cohesion: 0.11
Nodes (17): 1. 表示切り替え, 2. 比較表のデータ構造, 3. 差分の視覚的強調, 4. テキストエクスポート, Boundaries, Code Style, Commands, Open Questions (+9 more)

### Community 29 - "実装計画: Issue #7 — STG適用の複数DB横断実行"
Cohesion: 0.12
Nodes (16): Checkpoint: 全タスク完了後, Overview, Phase 1: 型定義の追加, Phase 2: コンポーネントの Props 変更（下位から上位へ）, Phase 3: DeployStg.tsx の変更（呼び出し側の更新）, Task 1: `types.ts` に `MultiDbModules` 型を追加, Task 2: `ConfirmDialog.tsx` を複数DB表示に対応する, Task 3: `LogViewer.tsx` を複数DB順次実行に対応する (+8 more)

### Community 30 - "Spec: STG適用画面「削除」モジュールの検出方式見直し (issue #9)"
Cohesion: 0.12
Nodes (16): 1. バックエンド: 削除候補の検出, 2. `ModuleInfo` へのフラグ追加, 3. フロントエンド: 型・APIクライアント, 4. フロントエンド: ツリー表示・操作区分の固定, Boundaries, Code Style, Commands, Objective (+8 more)

### Community 31 - "SPEC: STG → pilot サーバーへのWebソース配布機能（Issue #25）"
Cohesion: 0.12
Nodes (16): 1. Objective（目的）, 2. As Is / To Be, 3. 受け入れ条件（Acceptance Criteria）, 4. 技術スタック, 5. 実装スコープ（変更・新規ファイル）, 6. データフロー, 7.0 旧仕様（歴史記録・Issue #25 実装当時）, 7.1 実ファイル（kaios/gos）での検証結果 (+8 more)

### Community 32 - "SPEC: Issue #5 — 選択機能の強化"
Cohesion: 0.13
Nodes (14): 1. Objective, 2. 現状の問題, 3-1. DB切替時に選択を保持する, 3-2. 全DBの選択状況を確認できる, 3. To Be（受け入れ条件）, 4-1. 状態設計の変更, 4-2. DB切替時の変更, 4-3. 選択状況サマリーパネル（新規コンポーネント） (+6 more)

### Community 33 - "Implementation Plan: Pilot環境適用の web.config ファイル差し替え"
Cohesion: 0.13
Nodes (15): Architecture Decisions, Checkpoint A（コア動作）, Checkpoint B（掃除・整合）, Checkpoint: 完了条件（Spec Success Criteria 対応）, Dependency Graph, Implementation Plan: Pilot環境適用の web.config ファイル差し替え, Out of Scope, Overview (+7 more)

### Community 34 - "SPEC: ダッシュボード実行履歴の詳細表示（Issue #3 追加仕様）"
Cohesion: 0.13
Nodes (14): 1. Objective（目的）, 2. As Is / To Be, 3. 受け入れ条件（Acceptance Criteria）, 4. 技術スタック, 5. 実装スコープ（変更ファイル）, 6. データフロー, 7. UI 詳細, 8. Boundaries（制約） (+6 more)

### Community 35 - "Spec: 実行履歴の機能強化（Issue #8）"
Cohesion: 0.13
Nodes (14): 1. DBスキーマ, 2. バックエンド, 3. フロントエンド, Boundaries, Code Style, Design, Objective, Open Questions (+6 more)

### Community 36 - "Spec: Pilot環境適用の web.config をパイロット用ファイル差し替えに変更"
Cohesion: 0.14
Nodes (14): Boundaries, Code Style, Commands, Objective, Open Questions, Project Structure, Spec: Pilot環境適用の web.config をパイロット用ファイル差し替えに変更, Success Criteria (+6 more)

### Community 37 - "Implementation Plan: Issue #5 — 選択機能の強化"
Cohesion: 0.15
Nodes (12): Architecture Decisions, Checkpoint: Phase 1, Checkpoint: Phase 2（完了）, Implementation Plan: Issue #5 — 選択機能の強化, Open Questions, Overview, Phase 1: 状態設計の変更と DB切替修正, Phase 2: 全DB選択状況サマリーUIの追加 (+4 more)

### Community 38 - "Task List"
Cohesion: 0.15
Nodes (13): Checkpoint: Backend Core, Checkpoint: Complete, Checkpoint: Foundation, Checkpoint: Frontend, Checkpoint: SQL適用機能, Checkpoint: 実行ステップの個別再実行, Phase 1: Foundation（設定・データモデル）, Phase 2: Backend Core（robocopy実行・web.config書き換え・API） (+5 more)

### Community 39 - "Implementation Plan: STG適用画面「削除」モジュールの検出方式見直し (issue #9)"
Cohesion: 0.17
Nodes (11): Architecture Decisions, Checkpoint: バックエンド完了, Checkpoint: フロントエンド完了・全体E2E確認, Implementation Plan: STG適用画面「削除」モジュールの検出方式見直し (issue #9), Open Questions, Overview, Phase 1: バックエンド — 検出ロジック, Phase 2: フロントエンド — 型・APIクライアント (+3 more)

### Community 40 - "Tasks: Pilot環境適用の web.config ファイル差し替え"
Cohesion: 0.17
Nodes (12): Checkpoint A: コア動作, Checkpoint B: 完了, Out of Scope（実装しない）, Spec Success Criteria マッピング, Task 1: `ApplyPilotWebConfig` を追加, Task 2: `ExecuteAsync` を差し替え呼び出しに切替＆置換コード削除, Task 3: `PilotConnectionStrings` モデル削除, Task 4: `appsettings_sample.json` からキー削除 (+4 more)

### Community 41 - "Implementation Plan: 実行履歴の機能強化（Issue #8）"
Cohesion: 0.17
Nodes (11): Architecture Decisions, Checkpoint: 完了時, Implementation Plan: 実行履歴の機能強化（Issue #8）, Open Questions, Overview, Risks and Mitigations, Task 1: DeploySession に LogDetail カラムを追加し、保存経路を通す, Task 2: 詳細取得APIで LogDetail を返す (+3 more)

### Community 42 - "Spec: モジュールの適用区分の一括変更機能 (issue #10)"
Cohesion: 0.18
Nodes (10): Boundaries, Code Style, Commands, Objective, Open Questions, Project Structure, Spec: モジュールの適用区分の一括変更機能 (issue #10), Success Criteria (+2 more)

### Community 43 - "Implementation Plan: モジュールの適用区分の一括変更機能 (issue #10)"
Cohesion: 0.20
Nodes (9): Architecture Decisions, Checkpoint: Complete, Implementation Plan: モジュールの適用区分の一括変更機能 (issue #10), Open Questions, Overview, Phase 1: 一括変更ロジック, Phase 2: 一括変更UI, Risks and Mitigations (+1 more)

### Community 44 - "Task List"
Cohesion: 0.22
Nodes (9): Checkpoint: Complete, Checkpoint: Foundation, Checkpoint: Viewソース更新, Checkpoint: 画像コピー, Phase 1: Foundation（設定・データモデル）, Phase 2: 画像コピー（機能スライス1）, Phase 3: Viewソース更新（機能スライス2）, Phase 4: 通し確認・ドキュメント (+1 more)

### Community 46 - "Implementation Plan: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27)"
Cohesion: 0.29
Nodes (7): Architecture Decisions, Implementation Plan: pilot環境適用機能の機能追加（画像コピー・Viewソース更新）(issue #27), Open Questions, Overview, Risks and Mitigations, 並行可能な作業, 依存関係

### Community 47 - "Implementation Plan: STG → pilot Webソース配布機能（Issue #25）"
Cohesion: 0.40
Nodes (5): Architecture Decisions, Implementation Plan: STG → pilot Webソース配布機能（Issue #25）, Open Questions, Overview, Risks and Mitigations

## Knowledge Gaps
- **493 isolated node(s):** `net8.0`, `Microsoft.Data.SqlClient (5.2.2)`, `Microsoft.Data.Sqlite (8.0.11)`, `MySqlConnector (2.3.7)`, `Microsoft.NET.Sdk.Web` (+488 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DbConfig` connect `DbConfig` to `ImagePrepareService`, `WebSourceDeployService`, `DeployService`, `.GetModulesAsync`?**
  _High betweenness centrality (0.020) - this node is a cross-community bridge._
- **Why does `DatabaseService` connect `DatabaseService` to `DbConfig`, `WebSourceDeployService`?**
  _High betweenness centrality (0.011) - this node is a cross-community bridge._
- **Why does `MaintenanceManagement.Api.Models` connect `DatabaseService` to `DbConfig`, `ImagePrepareService`, `WebSourceDeployService`, `DeployService`, `.GetModulesAsync`?**
  _High betweenness centrality (0.009) - this node is a cross-community bridge._
- **What connects `net8.0`, `Microsoft.Data.SqlClient (5.2.2)`, `Microsoft.Data.Sqlite (8.0.11)` to the rest of the system?**
  _493 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `DbConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.056338028169014086 - nodes in this community are weakly interconnected._
- **Should `ImagePrepareService` be split into smaller, more focused modules?**
  _Cohesion score 0.07928118393234672 - nodes in this community are weakly interconnected._
- **Should `WebSourceDeployService` be split into smaller, more focused modules?**
  _Cohesion score 0.06265664160401002 - nodes in this community are weakly interconnected._