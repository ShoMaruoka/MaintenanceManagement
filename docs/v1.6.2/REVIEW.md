# Review: v1.6.2 Pilot SQL Server 適用のエラーログ検知

- 対象コミット: `344a3a11092d77d71a9329da5d20b03a6de7defb`
- レビュー日: 2026-10-02
- 観点: 正しさ・読みやすさ・設計・セキュリティ・性能（敵対的検証）

## 判定

**条件付き承認（Request changes）**。下の「要対応」2 件を直してからマージしてください。

主目的の「`deploy.bat` が終了コード 0 でも、今回増えたエラーログがあれば失敗にする」は達成できています。「両方」実行の最終行の修正も正しく、コントローラの `sqlOk` 経路で履歴と `done.success` も失敗になります。

### 確認したこと

- コミット時点で `dotnet test` を実行し、187 件すべて合格
- 疑わしい点は、コミット時点のコードを別ディレクトリに展開して再現テストを書き、実際の挙動を確かめた（下の各指摘に再現コードあり）

---

## 要対応（Important）

### 1. 古いログがロックされているだけで、SQL 適用が失敗になる

**場所**: `backend/Services/WebSourceDeployService.cs` の `SqlDeployErrorLog.ReadNewContent`（`File.ReadAllBytes` の try/catch）

**問題**: ファイルが変わったかどうかを調べる前に `File.ReadAllBytes` を実行し、読み込みに失敗すると即 `CouldNotRead`（`HasNewError = true`）を返している。

**起きること**: 同日の古い `deployerror_yyyyMMdd.log` を誰かが Excel などで開いていてロックされていると、今回のバッチがログに触れていなくても SQL 適用が失敗になる。

**仕様との矛盾**: SPEC の Boundaries「同日に残っている過去のエラーログだけで失敗にしない」に反する。

**再現**:

```csharp
var p = Path.GetTempFileName();
File.WriteAllText(p, "old error", Sjis);
File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-5));   // 5 時間前の同日ログ
var before = SqlDeployErrorLog.Capture(p);
using var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None); // 他プロセスのロック
var r = SqlDeployErrorLog.ReadNewContent(p, before, DateTime.UtcNow, Sjis);
// 結果: HasNewError = True, Unreadable = True  （期待: None）
```

**修正案**: 先に `FileInfo` でサイズと更新時刻を取り、`created / grew / touched` のどれにも当たらなければ `None` を返す。本文の読み込みはその後に行う。メタデータの取得はロック中でも成功する。

### 2. 上書きされて前より長くなったログの本文が、途中から欠ける

**場所**: 同メソッドの `var slice = grew && before.Exists ? bytes.AsSpan((int)before.Length) : bytes.AsSpan();`

**問題**: サイズが増えていれば、無条件で「起動前のサイズ」の位置から後ろだけを切り出している。バッチが `>` や `sqlcmd -o` でログを作り直し、前回より長く書いた場合も「追記」とみなされる。

**起きること**:

- 今回のエラーの先頭部分が消える
- 切り出し位置が Shift-JIS の 2 バイト文字の途中に当たると復号に失敗し、「エラーログを読めませんでした」だけが出て本文が一切残らない
- 失敗という判定は正しく出るが、運用者がログから原因を追えなくなる

**再現**:

```csharp
var p = Path.GetTempFileName();
File.WriteAllText(p, "前回のエラー: 列名 'X' が無効です。\r\n", Sjis);
File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-5));
var before = SqlDeployErrorLog.Capture(p);
var start = DateTime.UtcNow;
// バッチが > で作り直し、前回より長く書いた
File.WriteAllText(p, "今回のエラー: プロシージャ usp_Foo の作成に失敗しました。列名 'Y' が無効です。\r\n", Sjis);
var r = SqlDeployErrorLog.ReadNewContent(p, before, start, Sjis);
// 結果の Content: 「作成に失敗しました。列名 'Y' が無効です。」
// 先頭の「今回のエラー: プロシージャ usp_Foo の」が欠落
```

**背景**: SPEC 自体が「サイズが増えた＝追記」と仮定している。まず Pilot サーバーの実際の `deploy.bat` が追記（`>>`）か上書き（`>` / `sqlcmd -o`）かを確認すること。

**修正案**: `Capture` で起動前の内容の先頭バイト（またはファイル全体のハッシュ。ログは小さいので全体でも可）を控える。起動後のファイルの先頭が起動前の内容と一致するときだけ追記とみなして末尾を切り出し、一致しなければファイル全体を今回分とする。

---

## 軽微（Nit）

### 3. 省略していないのに「（以降省略）」が出る

**場所**: `EmitSqlServerErrorLogs` の `Content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)`

**問題**: 本文が改行で終わると、`Split` の結果の最後に空文字列の要素が 1 つ付く。

**起きること**:

- ちょうど 50 行で末尾が改行のログでは、51 番目の空要素が行数上限に当たり、何も省略していないのに `（以降省略） {パス}` が出る
- 50 行未満のときも、空の DETAIL 行が 1 行余分に出る

**修正案**: 末尾の空要素を取り除いてから出力する（例: `Content.TrimEnd('\r', '\n')` してから `Split`）。

### 4. `LocalNow` が本番クラスの書き換え可能なプロパティになっている

テスト差し替え用の `internal Func<DateTime> LocalNow { get; set; }` がサービスに載っている。`internal` なので実害はない。既存の `IProcessRunner` のような差し込み方に揃えるなら、.NET 8 標準の `TimeProvider` をコンストラクタで受け取る形が素直。対応は任意。

---

## FYI（今回の変更が原因ではないもの）

### 同じ DB の Pilot 適用の同時実行を防いでいない

コントローラにもサービスにも、二重実行を止める仕組みが見当たらない。2 人が同じ DB の Pilot 適用を同時に実行すると、相手側のエラーを自分の失敗として拾う。今回の検知はファイル差分に頼るため、この問題が表に出やすくなる。別 Issue として扱うことを推奨。

### 疑ったが問題なかった点

| 疑い | 結果 |
|------|------|
| 空の追記（`2>> log`、`type nul >> log`）で更新時刻が変わり、同日の古いログを毎回誤検知する | Windows 上で実際に試し、更新時刻は変わらないことを確認。誤検知しない |
| `static readonly` の Shift-JIS 取得が、エンコーディング登録前に走って例外になる | 本番は `Program.cs` 冒頭、テストは `ModuleInitializer` で登録済み。問題なし |
| 日跨ぎで、終了日側のファイルを「起動前は無し」とみなすのは誤り | 起動前に翌日付のファイルが存在することは通常ない。妥当 |
| 「両方」でスキップ（`Success = true`）が失敗扱いになる | `sqlDeployResult is { Success: false }` なのでスキップは完了のまま。正しい |

---

## 追加してほしいテスト

| テスト | 対応する指摘 |
|--------|-------------|
| ロックされた未変更の同日ログがあっても `RunSqlDeployAsync` が成功する | 1 |
| 前回より長く上書きされたログで、今回分としてファイル全体が出る | 2 |
| 上書きで 2 バイト文字の途中から切り出される位置でも「読めませんでした」にならない | 2 |
| ちょうど 50 行・末尾改行のログで「（以降省略）」が出ない | 3 |

## マージ前のチェック

- [x] 指摘 1 を修正し、回帰テストを追加
- [x] 指摘 2 を修正（実 `deploy.bat` はリポジトリ外で追記か上書きかは未確認。先頭一致のときだけ追記とみなす）
- [x] 指摘 3（任意だが推奨）
- [x] `dotnet test` が通る。本対応はフロントを変えていない

## 対応記録（2026-10-02）

1. `ReadNewContent` はサイズと更新時刻が変わっていなければ本文を読まない。ロックされた未変更ログは成功のまま。
2. `Capture` が起動前全文の SHA-256 を控える。起動後の先頭が一致するときだけ増分を切り出す。一致しなければファイル全体を今回分とする。
3. ログ出力の前に末尾改行を除く。ちょうど 50 行で末尾改行があっても「（以降省略）」は出ない。
4. `LocalNow` の `TimeProvider` 化は任意のため見送り。
5. 同時実行の二重起動防止は今回の対象外（FYI のまま）。
