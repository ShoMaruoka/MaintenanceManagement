# Plan: STG 適用時の CREATE OR ALTER 変換の適用範囲拡大

SPEC: [SPEC.md](SPEC.md)

## 構成要素と依存関係

```
SqlDefinitionConverter（新規・純粋関数）
        ▲
        │ 呼び出し
DeployService.Step4_SqlConvert（分岐変更）
        ▲
        │ 既存の ExecuteAsync 経由
DeployServiceStep4SqlServerTests（結合テスト）
```

変換ロジックが先、Step 4 の配線が後。逆順だと結合テストが書けない。

## 実装順序

### Phase 1: 変換ロジック（TDD）
1. `SqlDefinitionConverterTests` を先に書き、失敗を確認する
2. `SqlDefinitionConverter.ToCreateOrAlter(string sql)` を実装する
   - 正規表現は `^[ \t]*ALTER\s+(PROC(EDURE)?|FUNCTION|VIEW)\b`（IgnoreCase | Multiline）
   - `Regex.Replace(input, evaluator, count: 1)` 相当で最初の 1 件のみ置換
   - `ALTER` の直前に `CREATE OR ` を挿入し、元の表記は保持

**チェックポイント:** 単体テスト全件成功。

### Phase 2: Step 4 への配線
1. `DeployServiceStep4SqlServerTests` を先に書く（特に「更新 → CREATE OR ALTER」が失敗することを確認）
2. `Step4_SqlConvert` を「削除」と「それ以外」の 2 分岐にし、それ以外はすべて変換する
3. `ConvertAlterToCreate` の中身を `SqlDefinitionConverter` 呼び出しに置き換える（Shift_JIS 読み書きは維持）
4. ログ文言を `[新規] / [更新] CREATE OR ALTER 変換` に変更する

**チェックポイント:** `dotnet test` 全件成功（既存の Step1/3/3b/4MariaDb/5And6MariaDb テストも含む）。

### Phase 3: 仕上げ
- SPEC の Success Criteria にチェックを付ける
- 必要ならバージョン更新（`VERSIONING_GUIDE.md` に従う。要相談）

## リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| 本体中の行頭 `ALTER VIEW` 等（動的 SQL 内など）を誤変換 | 壊れた SQL | 最初の 1 件のみ置換。テストで固定 |
| 定義文より前のブロックコメント `/* ... */` 内の行頭 `ALTER PROCEDURE` を先に拾う | 実定義が変換されず従来どおり `ALTER` のまま | 発生頻度は低いと判断し対象外。SSMS のスクリプト見出し（`/****** Object: ... ******/`）は 1 行で `ALTER` を含まないため影響なし |
| `CREATE OR` と `ALTER` の間に改行がある既存ファイル | 二重変換 | 「最初の定義文」を CREATE 系も含めて判定し、CREATE 系なら変換しない実装で解消（Task 1） |
| 更新モジュールも `CREATE OR ALTER` になることで STG の挙動が変わる | なし（既存オブジェクトに対しては ALTER と同等） | SPEC Assumption 2 |

## 並行化

規模が小さく、Phase 1 → 2 の直列で進める。
