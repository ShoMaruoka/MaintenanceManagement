# 検証記録: PR #37 Blocking #1（A4 / B2）

対応: Issue #35 / PR #37 敵対的レビュー Blocking #1  
実施日: 2026-08-12  
実施者: 開発（コード・単体テストに基づく検証。実サーバーの `deploy.bat` 実体は本リポジトリ外のため、運用側の目視を「残作業」に記載）

## 背景

PLAN / TASKS が **必須** とした手動確認:

- **A4**: 片側空（SQL Server のみ / MariaDB のみ）での `deploy.bat` 挙動
- **B2**: `*.sql` 限定コピーにより、非 SQL 補助ファイルが運ばれなくなっていないか

Blocking #2 対応（B1）により MariaDB は別ツリー＋専用 bat になったため、A4 の前提も更新して検証した。

---

## A4. 片側空での bat 挙動

### 現行仕様（B1 後）

| 状態 | 動作 |
|------|------|
| SQL Server のみ `*.sql` あり | SQL Server Source へコピー → **SQL Server bat のみ**実行 |
| MariaDB のみ `*.sql` あり | MariaDB Source へコピー → **MariaDB bat のみ**実行 |
| 両方あり | SQL Server → MariaDB の順で各 bat |
| 両方なし | 両 bat とも起動せず `Skipped=true` |

旧懸念（単一 bat が `Source\` 直下＋空の `Source\MariaDB` を見て非0終了）は、**MariaDB を SQL Server Source 配下に載せない**ことで解消した。

### 検証結果（単体テスト）

| ケース | テスト | 結果 |
|--------|--------|------|
| SQL Server のみ | `RunSqlDeploy_SqlServerOnly_CopiesOneSide` | コピー1回・成功 |
| MariaDB のみ | `RunSqlDeploy_MariaDbOnly_CopiesToPilotMariaDbSource_AndRunsMariaBat` | MariaDB Source へコピー・**MariaDB bat のみ**起動 |
| 両方 | `RunSqlDeploy_CopiesFromDeployedPaths_NotDeploy2Prd` | コピー2・**bat 2回** |
| 両方空 | `RunSqlDeploy_BothEmpty_SkipsBeforeBat_SetsSkipped` | bat 呼出 0・Skipped |

**判定: A4 は B1 後の仕様として単体テストで担保済み。**  
実サーバー bat が「空 Source で非0」でも、当該エンジンに SQL が無いときはその bat 自体を呼ばないため、片側空が他方の成功を壊さない。

### 残作業（運用）

- 実機で「SQL Server のみ」「MariaDB のみ」を1回ずつ DryRun または実実行し、ログに該当 bat だけが出ることを目視（任意だが望ましい）。

---

## B2. 非 SQL 補助ファイル依存

### 現行仕様

Pilot SQL コピーは専用経路で次の引数のみ（共通 Web/Files コピーとは分離）:

```
"{src}" "{dest}" *.sql /E /MT:32 /R:2 /W:5 /NP /XX
```

- `Source` は実行のたびに空にしてから `*.sql` だけを入れる。
- したがって **Source 内の非 SQL ファイルは運ばれず、残しても次回初期化で消える**。

### 検証結果

| 確認 | 結果 |
|------|------|
| コード | `CopyPilotSqlFilesAsync` が `*.sql` ファイルクラス固定（`WebSourceDeployService.cs`） |
| 単体 | 引数組み立てのテスト（`BuildPilotSqlRobocopyArgs`）で `*.sql` を含むこと・共通 `BuildArguments` と分離されていることを固定（PR 指摘 #3 対応） |
| リポジトリ内 | Pilot 用 `deploy.bat` はリポ外（本システムは作成しない）。実体の依存ファイル一覧はリポからは検証不可 |

**判定: コピー層は「非 SQL を運ばない」ことがコード＋テストで確定。**  
bat が Source 内の非 SQL（実行順リスト等）に依存している場合は **壊れる／不完全適用のリスクあり**（レビュー指摘どおり）。

### 運用上の必須対応

1. 実サーバーの `PilotSqlDeployPath\deploy.bat` と `PilotMariaDbSqlDeployPath\deploy.bat` を開き、**Source 内の非 SQL を参照していないか**を確認する。
2. 補助ファイルが必要なら **`Source` の外**（bat と同じルート等）に恒久配置する。`Source` 配下に置かない。
3. 確認結果を下表に追記する。

| bat | 確認日 | 確認者 | Source 内非 SQL 依存 | 備考 |
|-----|--------|--------|---------------------|------|
| SQL Server (`PilotSqlDeployPath`) | （未） | | 未確認 | 実機確認待ち |
| MariaDB (`PilotMariaDbSqlDeployPath`) | （未） | | 未確認 | 実機確認待ち |

---

## リリースノート向け（FYI / B4）

既存ログ行の `Mode='full'` は除外 Mode 集合に入らないため、本変更以前の DryRun 実行が初回のみ「Pilot 最終適用」に載り得る。意図的受容。リリースノートに一言あると親切。

---

## PR 本文への転記用サマリ

```
### Blocking #1 検証（A4 / B2）

- A4: B1 により片側空では当該エンジンの bat のみ実行。単体テストで SQL Server のみ / MariaDB のみ / 両方 / 両方空を確認済み。
- B2: Pilot SQL コピーは *.sql のみ（専用経路）。Source は毎回合差し直し。非 SQL 補助は運ばれない。
  → 実機 bat が Source 内非 SQL に依存していないことの目視を運用で実施（表は docs/issue35/VERIFICATION.md）。
- Blocking #2: PilotMariaDbSqlDeployPath + 専用 bat で MariaDB 自動適用を実装済み。
```
