# Review: Issue #32 操作区分（新規／更新）の自動判定 — spec.md / PLAN.md 敵対的検証

対象: [`spec.md`](./spec.md), [`PLAN.md`](./PLAN.md)

## Summary

`/code-review` による敵対的検証で、実装前に解消すべき指摘が2件見つかった。

| # | 種別 | 深刻度 | 対象 |
|---|------|--------|------|
| 1 | 設計漏れ（ロジック不整合） | High | PLAN.md Task 2 (`MarkNewCandidates`) |
| 2 | ドキュメント不整合 | Low | spec.md / PLAN.md（テスト配置先） |

---

## 指摘1: `MarkNewCandidates` にディレクトリ未存在ガードが無い

**対象箇所**: PLAN.md L71（Task 2: `MarkNewCandidates` の実装方針）

**問題**:
既存の `FindDeleteCandidates` は、対象タイプのGitサブフォルダ自体が存在しない場合
`Directory.Exists(dir)` チェックにより早期リターンし、削除候補を1件も出さない。

一方、PLAN.md が規定する `MarkNewCandidates` の実装方針にはこれに対応するガードがなく、
DBの各モジュール名について直接 `File.Exists(Path.Combine(gitRepoPath, folderName, ...))`
を呼ぶだけになっている。

**発生しうる不具合**:
あるDB設定で `GitRepoPath` は設定済みだが、特定タイプ（例: `UserDefinedTableType`）の
サブフォルダがリポジトリ上にまだ作られていない（gitは空ディレクトリを追跡しないため
起こりうる）場合:

- `FindDeleteCandidates` は `Directory.Exists(dir)` が false のため正しく「削除候補なし」を返す
- しかし `MarkNewCandidates` にはこのガードがないため、同タイプの**既存DBモジュール全件**に
  ついて `File.Exists` が false を返し、すべて `IsNewCandidate=true`（＝「新規」バッジ）に
  誤判定されてしまう

spec.md L43-44 の仕様により、この自動判定バッジは**ユーザーが手動で上書きできない固定表示**
のため、UI側での訂正手段が存在しない。誤って「新規」と表示されたモジュールをSTGに適用すると、
実際には更新のはずのモジュールが新規適用として扱われるリスクがある。

**推奨対応**:
`MarkNewCandidates` の先頭で `FindDeleteCandidates` と同様に
`Path.Combine(gitRepoPath, folderName)` に対する `Directory.Exists` チェックを行い、
存在しない場合は何もせず処理を継続する（＝該当タイプの全モジュールは `IsNewCandidate=false`
のまま＝「更新」表示）よう PLAN.md Task 2 の記述を修正する。

併せて Task 3 のテストケースに「サブフォルダ自体が存在しない場合」のケースを追加する
（現状の①〜⑤にこのケースが含まれていないため、実装しても検知できない）。

---

## 指摘2: テスト配置先が spec.md と PLAN.md で食い違っている

**対象箇所**: spec.md L59-61（影響範囲） / PLAN.md L100-101（Task 3）

**問題**:
- spec.md の「影響範囲」セクションは、新規／更新判定のテストを既存の
  `ModuleQueryServiceDeleteCandidateTests.cs` / `ModuleQueryServiceMariaDbDeleteCandidateTests.cs`
  に追加する想定で書かれている。
- PLAN.md Task 3 は、新規ファイル `ModuleQueryServiceNewCandidateTests.cs` を新設する方針
  になっている（既存の削除候補テストが `test/` 外部fixtureに依存しているため、自己完結型
  にする理由がPLAN.md側にのみ記載されている）。

**影響**:
実行時の不具合ではないが、spec.md だけを見た実装者・レビュアーが既存ファイルにテストを
探しに行って見つからない、あるいは両ドキュメントの整合性チェックで無用な差分と誤認される
おそれがある。PLAN.md の Architecture Decisions（L41-45）に記載されている「新設する理由」
は正当なため、**spec.md 側を PLAN.md に合わせて修正する**のが妥当。

**推奨対応**:
spec.md L59-61 の記述を「新規ファイル `ModuleQueryServiceNewCandidateTests.cs` を新設し、
自己完結型の一時ディレクトリでテストする」旨に更新する。

---

## 結論（第1回）

- 指摘1は実装前に PLAN.md を修正すべき（設計上のガード漏れ、UIでの訂正手段がないため実害あり）。
- 指摘2は spec.md の記述を PLAN.md に合わせて修正すれば解消（ドキュメント整合性のみ）。
- 上記2点を反映した後、Task List自体の妥当性（依存関係・並行可否）には問題なし。

---

## 第2回検証（PLAN.md修正版に対する再敵対的検証）

### 指摘1・2の解消状況

- **指摘1（Directory.Existsガード）**: PLAN.md Task 2（L73-87）に反映済み。`FindDeleteCandidates`
  と同様の `Directory.Exists` ガードが追記され、対応するテストケース⑥（L116-119）、
  Acceptance criteria（L98-100）、Risksテーブル（L261）にも反映されている。文書として一貫性は
  確保されている。
- **指摘2（テスト配置先）**: spec.md L59-64 を確認したところ、PLAN.md の方針（新規ファイル
  `ModuleQueryServiceNewCandidateTests.cs` を新設し自己完結型でテストする）に合わせて
  修正済み。矛盾は解消されている。

### 新規指摘3（Medium）: 「フォルダ不在→更新扱い」というガードのフォールバック先が、実際のデプロイ処理から見て安全側とは限らない

**対象箇所**: PLAN.md L73-87（Task 2）、L261（Risksテーブル）

**問題**:
指摘1のガードは「対象タイプのGitサブフォルダが丸ごと存在しない場合、該当DBモジュールを
`IsNewCandidate=false`（＝『更新』）のまま据え置く」という設計になっている。これは
`FindDeleteCandidates`（ディレクトリが無ければ『削除候補ゼロ』を返す＝常に安全な既定値）
との表面的な対称性を意図したものだが、実際の `backend/Services/DeployService.cs` の
デプロイ処理を確認すると、次の非対称性がある。

- `Step4_SqlConvert`（DeployService.cs L218-242）は `OpType == "新規"` の場合のみ
  `ConvertAlterToCreate` を呼び、それ以外（＝『更新』）は `File.Copy(srcPath, destPath)` で
  Gitファイルをそのままコピーする。
- いずれの分岐も `srcPath = Path.Combine(config.GitRepoPath, m.Type, ...)` というGit上の
  ファイルを読みに行く。対象タイプのサブフォルダが丸ごと存在しない状況でこのモジュールが
  実際に選択・デプロイされた場合、`srcPath` はそもそも存在しないため、`File.Copy`（『更新』側）
  も `ConvertAlterToCreate`（『新規』側）も同様に `FileNotFoundException` 相当で失敗する
  （どちらの分岐に倒しても、選択・実行すれば同じく落ちる）。

つまり、フォルダ不在ケースを「更新」に倒しても「新規」に倒しても、実際にそのモジュールを
選んでデプロイを実行すれば同じく失敗する。したがって「フォルダ不在時は安全のため更新扱いに
フォールバックする」という指摘1への対応の"安全"という位置づけは、実行時の失敗そのものを
防げているわけではない、という点は正確に理解しておく必要がある。実質的な効果は「ツリー表示上の
バッジが『新規』ではなく『更新』になる」という、UI表示上の誤解を防ぐ効果に留まる。

**発生しうる不具合**:
運用担当者がまだGitに一度もエクスポートされていない種別を含むDBモジュールをツリーから選択して
STG適用を実行すると、Step4でファイルが見つからず例外が発生しデプロイが失敗する。これは
ガードの有無に関わらず起こる（ガードなしでも「新規」側の `ConvertAlterToCreate` が同様に
`srcPath` 不在で失敗するため）。ただし、バッジが「更新」と表示されることで、ユーザーは
「既存オブジェクトのALTER」であるかのように誤認して選択してしまう可能性がある点は変わらない
（「新規」表示であれば「まだGitに取り込まれていない＝デプロイ対象外」という違和感に
気づきやすいが、「更新」表示だと通常運用の一部に見えてしまう）。

**推奨対応**:
- PLAN.md / spec.md に「Gitサブフォルダが丸ごと存在しない場合、当該タイプのモジュールは
  （バッジ表示に関わらず）実際にはデプロイ不可能な状態である」旨を明記し、Task 7（通し確認）で
  このケースを実際に選択・デプロイした場合の挙動（明示的なエラーで止まり、原因が分かる
  ログが出ること）も確認項目に加えることを推奨する。
- Risksテーブル（L261）の「High→対応済み」という評価は、あくまで「UIバッジの誤表示」という
  観点では妥当だが、「デプロイ失敗を防げる」という意味ではない点を明記した方が、後続の
  実装者・レビュアーの誤解を防げる。

### 新規指摘4（Low）: Risksテーブル・Acceptance criteriaの具体例（`UserDefinedTableType`）が、実際にはこの懸念が最も軽微なケースである

**対象箇所**: PLAN.md L98-100（Task 2 Acceptance criteria）、L261（Risksテーブル）

**問題**:
指摘1への対応例として挙げられている `UserDefinedTableType` は、`DeployService.cs` の
`GitOnlyTypes` 判定（`ManualApplyService.ManualApplyTypes` 由来）に含まれるGitOnly系種別であり、
`Step4_SqlConvert`（ALTER→CREATE変換）を経由せず、手動適用待ちへの登録に回るだけの種別と見られる。
そのため、この種別で誤ってバッジが「更新」になっても実害は「手動適用一覧のラベルが変わる」
程度に留まる可能性が高い。

一方、指摘3で述べたALTER→CREATE変換が実際に走る種別（StoredProcedure / Function / VIEW /
MariaDBのStored）については、Acceptance criteria・Risksテーブルのどちらにも具体例として
挙げられていない。低リスクな例のみを根拠に「対応済み」と結論づけていると、実装後のレビュー
（Checkpoint: Backendの「人によるレビュー」）でも非GitOnly系種別側の実害が見落とされる懸念がある。

**推奨対応**:
Task 2 の Acceptance criteria に、StoredProcedure等の非GitOnly系種別でもフォルダ不在ケースを
明示的に含める（現状 `UserDefinedTableType` のみが例示されているため、GitOnly系・非GitOnly系
の両方を明記する形に差し替える）。

## 結論（第2回）

- 指摘1・2はドキュメント間の整合性としては解消済み。
- 新規指摘3・4は、実装前に必須で直すブロッカーではない（フォルダ丸ごと不在という状況自体が
  レアケースであり、いずれにせよデプロイは失敗して止まる設計になっているため）が、「フォルダ
  不在時のフォールバックが実行時の安全を保証するものではない」という誤解を招く記述であるため、
  Task 7（通し確認）の確認項目、および Risksテーブルの評価コメントに補足を加えることを推奨する。
