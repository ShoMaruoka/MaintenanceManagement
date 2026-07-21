# Spec: STG適用画面「削除」モジュールの検出方式見直し (issue #9)

## Objective

STG適用画面（`DeployStg.tsx`）のモジュールツリーは、`ModuleQueryService` が開発環境（`{db}_dev`）へ直接クエリした結果のみを表示している。
このため、**削除したいモジュール（＝開発環境からは既に削除済みのモジュール）が一覧に表示されず、「削除」区分で選択すること自体ができない**という課題がある。

削除の実行フロー自体（`DeployService` の `DeleteModule.txt` 出力・`GenerateDropSql` によるDROP文生成等）は現状のままで問題ないことを確認済み。今回はあくまで「削除したいモジュールをどうやってツリー上で選択可能にするか」という検出・表示の課題を解決する。

- 対象ユーザー: STG適用を行う開発者・運用担当者
- 成功条件: 開発環境から既に削除済みのモジュールが「削除候補」としてツリーに表示され、ユーザーがチェックを入れることで通常の削除フロー（DROP文生成 → deploy.bat）に乗せられる

### 現状仕様の整理（調査済み・変更しない部分）

| 項目 | 現状 | 今回の方針 |
|------|------|-----------|
| DROP文生成（StoredProcedure/Function/VIEW） | `GenerateDropSql` で `DROP {TYPE} IF EXISTS [dbo].[{Name}]` を生成 | 変更なし |
| MariaDB削除時の `DROP OBJECT`（無効なSQLになる既知バグ） | 対象外タイプのため不正なSQL生成 | 今回は対象外・現状維持（別issueで対応） |
| Table / UserDefinedTableType の削除 | `GitOnlyTypes` のためDROP文は生成されず、Gitマージのみ実行 | DROP文生成ロジックは変更しない（現状通り） |
| 削除確認ダイアログの警告表示 | 操作区分バッジ（赤）＋定型の注意文言のみ | 変更なし（追加ガード不要） |

## Tech Stack

- バックエンド: ASP.NET Core 8 Web API（`backend/Services/ModuleQueryService.cs`, `backend/Models/ModuleInfo.cs`, `backend/Models/DbConfig.cs`）
- フロントエンド: React 18 + TypeScript + Vite（`frontend/src/pages/DeployStg.tsx`, `frontend/src/api/modules.ts`, `frontend/src/types.ts`）
- 既存の自動テスト基盤なし（xUnit/Vitest ともに未導入。`docs/SPEC.md` のテスト戦略は将来計画であり現状未実装）

## Commands

```
Backend Build: cd backend && dotnet build
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build   (tsc の型チェック含む)
```

## Project Structure

```
backend/Models/ModuleInfo.cs           → ModuleInfo に削除候補フラグを追加
backend/Models/DbConfig.cs             → GitRepoPath（既存プロパティ、変更不要）
backend/Services/ModuleQueryService.cs → DevDB結果とGitRepoPathファイル一覧の差分検出を追加
frontend/src/types.ts                  → Module 型に削除候補フラグを追加
frontend/src/api/modules.ts            → ApiModuleInfo / formatModules にフラグを追加
frontend/src/pages/DeployStg.tsx       → ツリー行の表示・操作区分固定・一括変更/全選択の除外処理
frontend/src/components/ConfirmDialog.tsx → （必要であれば）削除候補バッジの表示
```

## 実装方針

### 1. バックエンド: 削除候補の検出

`GitConfig.GitRepoPath`（例: `D:\STGENV\{DB}_rep`）配下の `{Type}\dbo.{Name}.sql` は、STG側Gitミラーに存在する＝過去にデプロイされた実体のスナップショットである。
`ModuleQueryService.GetModulesAsync` にて、DevDBクエリ結果と `GitRepoPath\{Type}\*.sql` のファイル一覧を突き合わせ、**GitRepoPathには存在するがDevDBクエリ結果には存在しない名前**を「削除候補」として各リストに追加する。

対象タイプ: `StoredProcedure` / `Function` / `VIEW` / `Table` / `UserDefinedTableType`
対象外: `MariaDB`（GitRepoPath配下に対応フォルダがなく、今回のスコープ外）

```csharp
private static List<ModuleInfo> DetectDeleteCandidates(
    string gitRepoPath, string type, IEnumerable<string> existingNames)
{
    var dir = Path.Combine(gitRepoPath, type);
    if (!Directory.Exists(dir)) return [];

    var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    var candidates = new List<ModuleInfo>();

    foreach (var file in Directory.GetFiles(dir, "dbo.*.sql"))
    {
        var name = Path.GetFileNameWithoutExtension(file).Replace("dbo.", "", StringComparison.OrdinalIgnoreCase);
        if (existing.Contains(name)) continue;

        candidates.Add(new ModuleInfo
        {
            Name = name,
            Type = type,
            ModifyDate = "",
            GitOnly = type is "Table" or "UserDefinedTableType",
            IsDeleteCandidate = true,
        });
    }
    return candidates;
}
```

`GetModulesAsync` 側で、各タイプのクエリ結果に対して上記の差分結果を `AddRange` する（DBごとの `GitRepoPath` は `DbConfig` から取得済み）。

### 2. `ModuleInfo` へのフラグ追加

```csharp
public class ModuleInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ModifyDate { get; set; } = "";
    public bool GitOnly { get; set; }
    public bool IsDeleteCandidate { get; set; }   // 追加
}
```

### 3. フロントエンド: 型・APIクライアント

`frontend/src/types.ts` の `Module` と `frontend/src/api/modules.ts` の `ApiModuleInfo` / `formatModules` に `isDeleteCandidate: boolean` を追加して素通しする。

### 4. フロントエンド: ツリー表示・操作区分の固定

`DeployStg.tsx` の `module-list` 行レンダリング部分（既存の `module.type === 'Table' || 'UserDefinedTableType'` で "Git マージのみ" バッジを出している箇所と同様のパターン）に対応:

- 各種別タブの中に、通常モジュールと**混在**させて表示する（別タブ・別セクションは設けない）
- `module.isDeleteCandidate === true` の行には「削除候補」バッジを表示する
- 削除候補モジュールをチェックした場合、操作区分は **「削除」固定**とし、既存の `<select>`（操作区分プルダウン）は表示しない、または disabled にして変更不可にする
- チェックボックスの初期状態は他モジュールと同様に**未チェック**（自動選択しない）
- `toggleModule` で削除候補モジュールをチェックする際は `'削除'` を初期値としてセットする（通常モジュールは `'更新'` が初期値のまま）
- `selectAll()` / `setOpTypeBulk()`（一括変更、issue #10 で追加済み）は、削除候補モジュールの操作区分を `'削除'` から変更しないようにする（対象から除外、またはガードを入れる）

## Code Style

既存コードのスタイルに合わせる（関数コンポーネント、`Map` ベースの選択状態管理、`op-badge-*` のクラス命名規則を踏襲）。

```tsx
{module.isDeleteCandidate && (
  <span className="module-delete-candidate-badge">削除候補</span>
)}
...
{isSelected ? (
  module.isDeleteCandidate
    ? <span className="op-badge op-badge-delete">削除</span>
    : <select /* 既存の操作区分プルダウン */ />
) : (
  <span className="module-item-unselected">未選択</span>
)}
```

## Testing Strategy

- 自動テストなし（プロジェクトに既存のテスト基盤なし）
- 手動確認（`dotnet build` / `npm run build` の型チェック通過に加え、ローカルSTG環境で以下を確認）
  - 開発環境から手動でストアドプロシージャを1件DROPし、Gitリポジトリ側（`GitRepoPath\StoredProcedure\`）にはファイルが残っている状態を用意する
  - STG適用画面の該当種別タブを開き、そのモジュールが「削除候補」バッジ付きで表示されることを確認
  - チェックを入れると操作区分が「削除」固定で表示され、変更できないことを確認
  - 一括変更・すべて選択を使っても削除候補モジュールの区分が「削除」のまま維持されることを確認
  - 実行内容の確認ダイアログ・実行後のログ・実行履歴で、通常の削除同様に処理されることを確認
  - 通常のDevDB存在モジュール（新規/更新/削除）の挙動に影響がないことを確認（既存機能のデグレがないこと）

## Boundaries

- Always: 既存のDROP文生成ロジック（`GenerateDropSql`）・`GitOnlyTypes` の扱い・確認ダイアログの警告表示は変更しない。型チェック（`npm run build`）・バックエンドビルド（`dotnet build`）を通す。
- Ask first: MariaDB削除時の `DROP OBJECT` 不正SQLの修正（今回スコープ外、別issueとして切り出す）、Table/UserDefinedTableTypeの実DROP対応（今回は現状の「Gitマージのみ」を維持）
- Never: `GitRepoPath` 配下のファイルをこの検出処理から削除・変更しない（読み取り専用の突き合わせのみ）。DevDBクエリ結果に存在するモジュールの既存の選択・操作区分ロジック（新規/更新/削除を自由に選べる挙動）を壊さない。

## Success Criteria

1. 開発環境から削除済みだが `GitRepoPath` に実体が残っているモジュールが、対応する種別タブのツリーに「削除候補」バッジ付きで表示される
2. 削除候補モジュールをチェックすると操作区分が「削除」に固定され、他の区分へは変更できない
3. 削除候補モジュールの初期チェック状態は未チェック
4. 一括変更・すべて選択操作を行っても、削除候補モジュールの操作区分は「削除」のまま変わらない
5. MariaDB・（DROP文を生成しない）Table/UserDefinedTableTypeの既存の削除挙動に変更がない
6. 通常モジュール（DevDBに存在するもの）の新規/更新/削除の選択・変更挙動に影響がない
7. `dotnet build` / `npm run build` がエラーなく通る

## Open Questions

- `GitRepoPath` は `git_Live Updates.bat` / `git_merge.bat`（Step2/3）実行後に最新化される想定だが、実際の更新タイミング・運用は外部スクリプト依存のため、STG適用直後に確実に最新状態になっているかは運用側で要確認。
- ファイル名からモジュール名を抽出する命名規則は `dbo.{Name}.sql` 固定と仮定している（現行コードの `Step4_SqlConvert` と同じパターン）。`dbo` 以外のスキーマを使うケースがあれば別途対応が必要。
