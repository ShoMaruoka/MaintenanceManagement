# Implementation Plan: 操作区分（新規／更新）の自動判定 (issue #32)

対応する仕様: [`spec.md`](./spec.md)

## Overview

STG適用画面（`DeployStg.tsx`）で手動選択していた操作区分「新規」「更新」を、既存の
削除候補判定（`ModuleQueryService.FindDeleteCandidates` / `FindMariaDbStoredDeleteCandidates`）
と対称のロジックで自動判定する。DBに存在しGitにも存在すれば「更新」、DBに存在しGitに
存在しなければ「新規」。判定は名前の存在有無のみで行い、内容差分は見ない。

新規サービス・新規画面は作らない。既存の `ModuleInfo` / `Module` 型にフィールドを1つ
追加し、既存の「DB問い合わせ→削除候補検出」の流れに「新規候補検出」を並べて追加する形の
拡張とする。フロント側は既存の `Map<string, OpType>` による選択管理はそのまま活かし、
値の決定方法だけをユーザー操作からバックエンド算出値に置き換える。

## Architecture Decisions

- **`ModuleInfo` に `IsNewCandidate: bool` を追加する（`OpType` 文字列への一本化はしない）**:
  既存の `IsDeleteCandidate` / `GitOnly` と同じ「ブール値の特性フラグ」というパターンに
  揃える。`OpType` 文字列プロパティへの統一も検討したが、`IsDeleteCandidate` を直接参照する
  既存コード（`DeployStg.tsx` の複数箇所、`ModuleQueryServiceDeleteCandidateTests.cs` 等）を
  すべて書き換える必要があり、削除判定という既存の正しく動いているロジックに触れるリスクが
  上がる。追加のみで済む設計を優先する。
- **新規判定は「DB問い合わせ結果1件ごとに対応するGitファイルの存在確認」で行う。
  削除候補検出のような全件Enumerate＋差集合は使わない**: 削除候補検出はGit側にしかない
  ファイル名を洗い出す必要があるためディレクトリ全列挙が必須だが、新規判定はDB側の各名前に
  対して「そのファイルがあるか」を1件ずつ `File.Exists` で確認すればよく、ロジックが単純になる。
  対象件数（モジュール数）が数百〜数千件規模でも `File.Exists` は十分高速。
- **`GitRepoPath` / `MariaDbGitRepoPath` が未設定の場合は新規判定を行わない
  （既存の削除候補検出と同じフォールバック方針）**: Git連携が設定されていない環境では
  従来どおり全件「更新」扱いのままにする（新規判定をスキップ＝`IsNewCandidate` は既定の
  `false` のまま）。誤って全件「新規」と表示されることを防ぐ。
- **判定処理は `ModuleInfo` を直接書き換える（ミューテーション）方式にする**:
  `FindDeleteCandidates` が新しい `ModuleInfo` のリストを返す（追加型）のに対し、新規判定は
  既存リストの各要素に対する属性確定（更新型）なので、`void` で `List<ModuleInfo> existing` を
  直接書き換えるメソッドにする。呼び出し側（`GetModulesAsync`）でのリスト取り回しが増えない。
- **呼び出しタイミングは `FindDeleteCandidates` による削除候補の `AddRange` より前**:
  削除候補（Git側にしかない＝`IsDeleteCandidate=true`）まで新規判定の対象に含めると
  無駄な `File.Exists` 呼び出しが発生するため、DB問い合わせ直後・削除候補追加前に実行する。
- **新規判定ロジックのテストは一時ディレクトリを都度作成する自己完結型にする**:
  既存の削除候補テスト（`ModuleQueryServiceDeleteCandidateTests.cs`）は `test/`
  （`.gitignore` 対象＝リポジトリに含まれない外部fixture）に依存しており、この環境には
  存在しない。新規判定のテストは `Directory.CreateTempSubdirectory()` でテスト内に
  ダミーGitフォルダを作り、外部fixtureに依存しない形にする。
- **フロント側は選択状態の型（`Map<string, OpType>`）を変えない**: `selectedModulesByDb`、
  `allConfirmModules`、`ConfirmDialog` / `SelectionSummary` / `LogViewer` / `DeployService`
  （バックエンド適用処理）はすべて `OpType` 値をそのまま利用しており、値の出どころが
  「ユーザー選択」から「モジュールの自動判定結果」に変わるだけなので、これらのコンポーネント・
  APIコントラクトには一切手を入れない。変更は `DeployStg.tsx` 内の「値の決定ロジック」と
  「UI表示（ドロップダウン→固定バッジ）」に閉じる。

## Task List

### Phase 1: Backend（新規判定ロジック）

- [x] **Task 1: `ModuleInfo` に `IsNewCandidate` プロパティを追加**
  - **Description**: `backend/Models/ModuleInfo.cs` の `ModuleInfo` クラスに
    `public bool IsNewCandidate { get; set; }` を追加する。既定値 `false`（＝更新扱い）。
  - **Acceptance criteria**:
    - [x] `IsNewCandidate` プロパティが追加され、既定値は `false`
    - [x] 既存のプロパティ・シリアライズ形式に影響しない
  - **Verification**:
    - [x] `cd backend && dotnet build` が成功する
  - **Dependencies**: None
  - **Files likely touched**: `backend/Models/ModuleInfo.cs`
  - **Estimated scope**: XS（1ファイル）

- [x] **Task 2: `ModuleQueryService` に新規判定ロジックを実装し `GetModulesAsync` に組み込む**
  - **Description**: 以下2つのメソッドを追加する。
    - `internal void MarkNewCandidates(string gitRepoPath, string folderName, string fileNamePrefix, List<ModuleInfo> existing)`
      — `FindDeleteCandidates` と同じ引数構成（`gitRepoPath` / `folderName` / `fileNamePrefix`）を踏襲。
      `gitRepoPath` が空なら何もしない。**`FindDeleteCandidates` と同様、
      `Path.Combine(gitRepoPath, folderName)` に対する `Directory.Exists` チェックを行い、
      サブフォルダ自体が存在しない場合は何もせず戻る**（該当タイプ全件が誤って
      `IsNewCandidate=true` にならないようにするための必須ガード。git は空ディレクトリを
      追跡しないため、対象タイプのフォルダがリポジトリ上にまだ無いケースが実際に起こりうる）。
      ディレクトリが存在する場合のみ、各 `existing` 要素について
      `Path.Combine(dir, $"{fileNamePrefix}{m.Name}.sql")` の存在を `File.Exists` で確認し、
      無ければ `m.IsNewCandidate = true` にする。例外は `FindDeleteCandidates` 同様 catch して
      ログ出力し、処理は継続する。
    - `internal void MarkMariaDbStoredNewCandidates(string gitRepoPath, List<ModuleInfo> existingStored, List<ModuleInfo> existingFunctions)`
      — MariaDBのStored（プロシージャ・ファンクション混在フォルダ）用。同様に
      `Path.Combine(gitRepoPath, "Stored")` の `Directory.Exists` を先頭でチェックし、
      存在しない場合は何もせず戻る。存在する場合のみ、両リストを合わせて各要素の
      `Stored/{Name}.sql` の存在を確認する（ファイル内容判定は不要。DB側で既に
      種別が確定しているため）。
    - `GetModulesAsync` 内、各DB問い合わせ結果を確定した直後・`FindDeleteCandidates` の
      `AddRange` より前に、対応する `MarkNewCandidates` / `MarkMariaDbStoredNewCandidates` 呼び出しを追加する
      （`FindDeleteCandidates` 呼び出しと同じ `gitRepoPath` / `folderName` / `fileNamePrefix` の
      組み合わせを使う）。
  - **Acceptance criteria**:
    - [x] SQL Server: StoredProcedure / Function / VIEW / Table / UserDefinedTableType の
          全種別で、Gitにファイルが無いDBモジュールに `IsNewCandidate=true` が付く
    - [x] MariaDB: Stored（Procedure/Function混在）/ MariaDbTable でも同様に判定される
    - [x] `GitRepoPath` / `MariaDbGitRepoPath` が空文字のDB設定では、全モジュールとも
          `IsNewCandidate=false` のまま（従来どおり）
    - [x] 対象タイプのGitサブフォルダ自体が存在しない場合、そのタイプの全DBモジュールも
          `IsNewCandidate=false` のまま（誤って全件「新規」にならない）。GitOnly系
          （例: `UserDefinedTableType`）・非GitOnly系（例: `StoredProcedure`）の両方で確認する。
          非GitOnly系は `DeployService.Step4_SqlConvert`（L218-242）が実際に
          `srcPath = Path.Combine(config.GitRepoPath, m.Type, "dbo.{Name}.sql")` を読みに行く
          対象であり、フォルダ丸ごと不在の場合は本ガードの有無に関わらず「新規」「更新」
          いずれの分岐でもデプロイ自体が失敗する（下記 Risks 参照）。本ガードが防ぐのは
          あくまで「ツリー画面上のバッジ誤表示」であり、デプロイ失敗そのものは防げない点に注意
    - [x] 削除候補（`IsDeleteCandidate=true`）の項目には `MarkNewCandidates` が影響しない
          （呼び出し順序により対象外になっている）
  - **Verification**:
    - [x] `dotnet build` 成功
    - [x] Task 3 のユニットテストが通る
  - **Dependencies**: Task 1
  - **Files likely touched**: `backend/Services/ModuleQueryService.cs`
  - **Estimated scope**: S（1ファイル、既存コードと対称構造なので実装難度は低い）

- [x] **Task 3: 新規判定ロジックのユニットテストを追加**
  - **Description**: `backend/Tests/Services/ModuleQueryServiceNewCandidateTests.cs` を新設し、
    一時ディレクトリで自己完結のダミーGitフォルダを作成してテストする
    （`Directory.CreateTempSubdirectory()` は当該環境の DirectoryInfo が IDisposable 非対応のため、
    `Path.GetTempPath()` + GUID で代替）。
    ケース: ①DBのみ存在（Git無し）→ `IsNewCandidate=true`、②DB・Git双方に存在→
    `IsNewCandidate=false`、③`gitRepoPath` 空文字→ 全件 `false`（判定スキップ）、
    ④MariaDB Stored（Procedure/Function混在フォルダ）でも同様に判定、⑤`dbo.`プレフィックス
    ありのSQL Serverケースでも正しく突合できる、⑥**対象タイプのサブフォルダ自体が
    存在しない場合（`gitRepoPath` は有効だが `Path.Combine(gitRepoPath, folderName)` が
    無い）→ 全件 `IsNewCandidate=false` のまま（誤って全件「新規」にならないことの確認。
    レビュー指摘1への対応）。`StoredProcedure`（非GitOnly系・Step4_SqlConvertの対象）と
    `UserDefinedTableType`（GitOnly系・ManualApplyService対象）の両方で確認する
    （レビュー指摘4への対応）**。
  - **Acceptance criteria**:
    - [x] 上記①〜⑥のケースがすべてテストとして存在し成功する
    - [x] 外部fixture（`test/`）に依存しない
  - **Verification**:
    - [x] `cd backend/Tests && dotnet test --filter ModuleQueryServiceNewCandidateTests` が成功
  - **Dependencies**: Task 2
  - **Files likely touched**: `backend/Tests/Services/ModuleQueryServiceNewCandidateTests.cs`
  - **Estimated scope**: S（1ファイル）

### Checkpoint: Backend

- [x] `dotnet build` が通る
- [x] `dotnet test`（Tests プロジェクト全体）が既存分含め通る（削除候補判定に回帰がない）
      ※ 外部fixture（`test/Kaios_MariaDB_rep`）依存の既存4件は本環境では失敗するが、
      今回追加分と fixture 非依存の既存テストはすべて合格（78/82）
- [ ] 人によるレビュー: `MarkNewCandidates` の呼び出しタイミングと `FindDeleteCandidates` との
      対称性

---

### Phase 2: Frontend（自動判定結果の反映・手動UI撤去）

- [x] **Task 4: 型定義を更新（`isNewCandidate` を追加）**
  - **Description**: `frontend/src/types.ts` の `Module` インターフェースに
    `isNewCandidate: boolean` を追加。`frontend/src/api/modules.ts` の `ApiModuleInfo` に
    `isNewCandidate: boolean` を追加し、`formatModules` でマッピングする。
  - **Acceptance criteria**:
    - [x] `Module` / `ApiModuleInfo` の両方に `isNewCandidate` が反映される
    - [x] `tsc` の型エラーが出ない
  - **Verification**:
    - [x] `cd frontend && npm run build`（`tsc && vite build`）が成功
  - **Dependencies**: Task 2（バックエンドのレスポンスに `isNewCandidate` が含まれること）
  - **Files likely touched**: `frontend/src/types.ts`, `frontend/src/api/modules.ts`
  - **Estimated scope**: XS（2ファイル）

- [x] **Task 5: `opType.ts` に `resolveOpType` ヘルパーを追加**
  - **Description**: `frontend/src/lib/opType.ts` に
    `export function resolveOpType(module: Pick<Module, 'isDeleteCandidate' | 'isNewCandidate'>): OpType`
    を追加。`isDeleteCandidate` → `'削除'`、`isNewCandidate` → `'新規'`、それ以外 → `'更新'`
    の優先順位で1つ返す。
  - **Acceptance criteria**:
    - [x] 3パターン（削除／新規／更新）を正しく返す
    - [x] `isDeleteCandidate` が最優先される（両方trueは実データ上あり得ないが安全側に倒す）
  - **Verification**:
    - [x] `npm run build`（既存にフロントのユニットテストは無いため型チェック＋ビルド）
  - **Dependencies**: Task 4
  - **Files likely touched**: `frontend/src/lib/opType.ts`
  - **Estimated scope**: XS（1ファイル）

- [x] **Task 6: `DeployStg.tsx` の手動選択UIを撤去し自動判定値を使うよう変更**
  - **Description**:
    - `toggleModule` / `selectAll` 内の `module.isDeleteCandidate ? '削除' : '更新'` を
      `resolveOpType(module)` に置き換える。
    - `setOpType` / `setOpTypeBulk` 関数と、それらを使う「操作区分を一括変更」の
      `<select>` UI（検索バー横）を削除する。
    - モジュール一覧の各行にあった `module.isDeleteCandidate ? <固定削除バッジ> : <選択可能select>`
      の分岐を撤去し、常に `resolveOpType(module)` に基づく固定バッジ（`op-badge-fixed`
      相当）を表示する。
    - `SELECTABLE_OP_TYPES` 定数を削除する（未使用化）。
  - **Acceptance criteria**:
    - [x] 操作区分は常にDB/Gitの状態から自動決定され、画面上で編集できない
    - [x] 削除候補・新規・更新の3種でバッジの色分け（`op-badge-delete` / `op-badge-new` /
          `op-badge-update`）が維持される
    - [x] 一括変更UI・単体selectが画面から消えている
    - [x] `selectAll` / `toggleModule` の選択・解除自体（操作区分ではなく選択状態）は
          従来どおり動作する
  - **Verification**:
    - [x] `cd frontend && npm run build` が成功する
    - [x] `npm run dev` でSTG適用画面を開き、モジュールを選択して操作区分バッジが
          正しく表示されること・編集UIが存在しないことを目視確認（Task 7 で確認済み）
  - **Dependencies**: Task 5
  - **Files likely touched**: `frontend/src/pages/DeployStg.tsx`
  - **Estimated scope**: M（1ファイルだが複数箇所の削除・置き換えを伴う）

### Checkpoint: Frontend

- [x] `npm run build`（`tsc && vite build`）が通る
- [x] 開発サーバーでSTG適用画面の一連の操作（DB切替・種別切替・選択・確認画面遷移）に
      回帰がない（Task 7 で確認済み）
- [ ] 人によるレビュー: バッジ表示・一括変更UI撤去後の画面レイアウト

---

### Phase 3: 通し確認・ドキュメント

- [x] **Task 7: DB×Gitの4パターンでの通し確認**
  - **Description**: 実際の（またはローカルのダミー）DB・Gitリポジトリ構成で、
    ①DBのみ（新規）②DB＋Git（更新）③Gitのみ（削除）④GitOnly系種別（Table等）の
    新規／更新、の4パターンが画面上で正しいバッジになることを確認する。
    `GitRepoPath` 未設定のDB設定がある場合は、そのDBで全件「更新」のままになることも確認する。
    加えて、**対象タイプのGitサブフォルダを丸ごと存在しない状態にしたダミー環境**で、
    ⑤該当タイプのDBモジュールが（誤って「新規」にならず）「更新」バッジで表示されること、
    ⑥そのモジュールを実際に選択してSTG適用を実行した場合に、`srcPath` 不在により
    明示的なエラーで停止し、原因が分かるログ（またはエラーメッセージ）が残ることを確認する
    （レビュー指摘3: ガードはバッジ誤表示の防止のみが目的であり、デプロイ失敗自体は
    防げないことの確認）。
  - **Acceptance criteria**:
    - [x] spec.md の Success Criteria を満たしていることを確認できる
    - [x] フォルダ丸ごと不在ケースで、バッジは「更新」のまま・実行時は明示的なエラーで
          停止することの両方を確認できる
  - **Verification**:
    - [x] 手動確認（開発サーバー、必要なら `appsettings.Development.json` にダミー設定）
  - **Dependencies**: Task 3, Task 6
  - **Files likely touched**: なし（確認のみ）
  - **Estimated scope**: S

- [x] **Task 8: ドキュメント更新**
  - **Description**: spec.md の Success Criteria のチェック状況を実績に合わせ、
    本 PLAN.md のタスクチェックも更新する。実装中に判明した仕様差分があれば spec.md に反映する。
  - **Acceptance criteria**:
    - [x] spec.md と実装内容が一致している
  - **Verification**:
    - [ ] 人によるレビュー
  - **Dependencies**: Task 7
  - **Files likely touched**: `docs/issue32/spec.md`, `docs/issue32/PLAN.md`
  - **Estimated scope**: XS

### Checkpoint: Complete

- [x] `dotnet build` / `npm run build` が通る（`dotnet test` は fixture 非依存分は合格）
- [x] spec.md の Success Criteria をすべて満たしている
- [x] レビュー可能な状態

---

## 依存関係

```
Task 1 (ModuleInfo.IsNewCandidate)
   └── Task 2 (判定ロジック + GetModulesAsync組み込み)
           ├── Task 3 (バックエンドテスト)
           └── Task 4 (フロント型定義)
                   └── Task 5 (resolveOpType)
                           └── Task 6 (DeployStg.tsx改修)

Task 3 + Task 6 ── Task 7 (通し確認) ── Task 8 (ドキュメント)
```

### 並行可能な作業

- Task 3（バックエンドテスト）と Task 4〜6（フロント）は、Task 2 完了後は互いに独立して並行可能
  （フロント側はAPIレスポンス形状さえ決まれば実装できるため、Task 2 のマージを待てば
  Task 3 と Task 4 は並行できる）

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `File.Exists` によるファイル名突合が、削除候補検出の正規化ルール（`dbo.`除去・拡張子除去）とズレて誤判定する | Med | `FindDeleteCandidates` と全く同じ `fileNamePrefix` 組み立てパターンを使い回すことでズレを防ぐ。Task 3 のテストで `dbo.` プレフィックスケースを明示的に確認する |
| MariaDB Stored（Procedure/Function混在フォルダ）で、ファイル名は一致するがDB側の種別（Procedure/Function）とファイル内容が食い違うケースを見落とす | Low | 新規判定はファイル名の存在有無のみを見るため種別不一致の影響を受けない。既存の削除候補判定（内容判定あり）とは非対称だが、spec合意通り「存在有無のみ」で判定する方針のため許容 |
| `GitRepoPath` 未設定時のフォールバック挙動を誤り、既存ユーザーの「全件更新」运用が崩れる | Med | Task 3 で `gitRepoPath` 空文字ケースを明示的にテストする。既存の `FindDeleteCandidates` と同じ空文字ガードを踏襲する |
| 対象タイプのGitサブフォルダ自体が未作成（gitは空ディレクトリを追跡しないため起こりうる）の場合に、同タイプの既存DBモジュール全件が誤って「新規」判定される（レビュー指摘1） | High→対応済み（**ただし「バッジ誤表示」の防止に限る。実際のデプロイ失敗自体は防げない点に注意。レビュー指摘3**） | `MarkNewCandidates` / `MarkMariaDbStoredNewCandidates` の先頭で `FindDeleteCandidates` と同じ `Directory.Exists` ガードを設ける（Task 2）。自動判定はユーザーが上書きできない固定表示のため、このガード漏れはUI誤表示に直結する。Task 3 でサブフォルダ未存在ケースを明示的にテストする |
| フォルダ丸ごと不在のモジュールが選択・デプロイされた場合、`DeployService.Step4_SqlConvert` が `srcPath`（Git上のファイル）を読めず失敗する。これは上記ガードの有無や「新規」「更新」どちらの判定でも変わらず発生する（レビュー指摘3） | Med | ガードは「デプロイ失敗の防止」ではなく「バッジ誤表示によるユーザーの誤認防止」が目的である旨をドキュメントに明記する。Task 7（通し確認）で、フォルダ丸ごと不在の種別を実際に選択・デプロイした際に、原因が分かる明示的なエラーで停止することを確認する |
| `DeployStg.tsx` からのUI撤去で、`SelectionSummary` / `ConfirmDialog` など他コンポーネントが暗黙に「ユーザー編集可能な `OpType`」を前提にしていた場合に影響が出る | Low | Architecture Decisionsの通り `Map<string, OpType>` の型・値の意味（決定済みのOpTypeを保持するだけ）は変えないため、他コンポーネントへの影響はない想定。Task 6 のVerificationで確認画面（ConfirmDialog）の表示も目視確認する |
| 大量モジュール（数百〜数千件）で `File.Exists` 呼び出しが増えることによる応答時間の悪化 | Low | 削除候補検出（ディレクトリ全列挙）より軽い操作。体感遅延が出た場合は、削除候補検出と同様にディレクトリ列挙結果をHashSet化して1回の走査で両方向を判定する方式に切り替える余地を残す |

## Open Questions

- なし（spec.md の Open Questions は本 Plan で決定済み）

## レビュー対応履歴

[`review.md`](./review.md)（`/code-review` による敵対的検証）の指摘を反映済み。

- 指摘1（High）: `MarkNewCandidates` / `MarkMariaDbStoredNewCandidates` に
  `Directory.Exists` ガードが無く、対象タイプのGitサブフォルダ未作成時に全件が誤って
  「新規」判定される問題 → Task 2 の実装方針・Task 3 のテストケース・Risks に反映済み。
- 指摘2（Low）: テスト配置先が spec.md（既存ファイルへ追加）と PLAN.md（新規ファイル）で
  食い違っていた問題 → spec.md 側を PLAN.md（新規ファイル `ModuleQueryServiceNewCandidateTests.cs`
  を新設し自己完結型でテストする方針）に合わせて修正済み。

### 第2回レビュー（指摘1・2の解消確認 + 新規指摘3・4）

- 指摘1・2は解消確認済み（ブロッカーなし）。
- 指摘3（Medium）: 「フォルダ丸ごと不在→更新扱い」というガードは、`DeployService.Step4_SqlConvert`
  が `srcPath`（Git上のファイル）を「新規」「更新」どちらの分岐でも読みに行くため、
  実際にそのモジュールを選択・デプロイすればガードの有無に関わらず失敗する。ガードの効果は
  「ツリー画面上のバッジ誤表示の防止」に限られ、「デプロイ失敗の防止」ではない → Task 2の
  Acceptance criteria・Risksテーブル・Task 7（通し確認）に、この限界を明記し、フォルダ丸ごと
  不在ケースを実際に選択・デプロイした際に明示的なエラーで停止することの確認項目を追加済み。
- 指摘4（Low）: 指摘1対応の具体例が GitOnly系（`UserDefinedTableType`）のみで、実際に
  ALTER→CREATE変換が走る非GitOnly系（`StoredProcedure` 等）が抜けていた → Task 2の
  Acceptance criteria・Task 3のテストケース⑥に `StoredProcedure` を明記し、両系統で
  確認する方針に修正済み。
