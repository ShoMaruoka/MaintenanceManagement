# SPEC: STG → pilot サーバーへのWebソース配布機能（Issue #25）

## 1. Objective（目的）

STGサーバー上のIISアプリ公開フォルダ一式を、Git使用禁止のpilotサーバーへコピーする機能を本システムに実装する。
現在は運用担当が手動でFastCopyを使ってコピーしているが、これをWeb画面から実行できるようにし、
コピー後に必要となるweb.configの接続文字列書き換えまで自動化することで、手作業によるミス・手間を削減する。

- **利用者**: 運用担当者（既存のPrepareForPrd等を使っているユーザーと同一想定）
- **成功の定義**: 画面操作だけでSTG→pilotへのWebソース転送とweb.config接続文字列の付け替えが完了し、pilotサーバー上でアプリが正しく動作すること
- **対象システム**: pilot環境が存在するのは **kaios** と **gos** の2システムのみ（paf・duskinはpilot環境なし、対象外）
- **対象サーバー**: pilotサーバーは **pilot1・pilot2の2台** 存在し、STGから両方へ適用する必要がある（STG → pilot1 → pilot2 の順に適用）

---

## 2. As Is / To Be

### As Is（現状）

- STGサーバーのWebソース（IISアプリ公開フォルダ）をpilotサーバーへコピーする仕組みは本システムに存在しない
- 運用担当者が手動でFastCopyツールを起動し、コピー後にweb.configの接続文字列を手作業で書き換えている
- 本システムには `FastCopyService`（DBファイル・画像ファイル用）、`ImagePrepareService`、`PrepareController` があるが、いずれもWebソース（アプリ公開フォルダ全体）を対象としていない

### To Be（目標）

- 新規画面「WebSourcePrepare」から、対象システム（kaios/gosのいずれか）を選択し、STG→pilotへのWebソースコピーを実行できる
- コピー方式は「差分ミラー」「全量コピー」を画面上で選択できる
- コピー処理は robocopy（Windows標準）を利用し、外部ツール（FastCopy.exe）への依存をなくす
- 1回の実行で **STG→pilot1→pilot2の順に自動連続適用** する（pilot1完了後、自動的にpilot2への適用に進む）
- pilot1・pilot2それぞれの適用後、コピー先web.configの `connectionStrings` セクションを、`appsettings.json` に保持したpilot用接続文字列で自動置換する（pilot1・pilot2で同一の接続文字列値を使用）
- 進捗は既存のPrepare機能と同様にSSEでログ表示し、「pilot1適用中」「pilot2適用中」のようにどちらの対象を処理中か分かるようにする
- pilot1適用中にエラーが発生した場合はpilot2への適用を行わず処理を中断し、エラー内容をログ・履歴に記録する

---

## 3. 受け入れ条件（Acceptance Criteria）

- [ ] WebSourcePrepare画面で対象システム（kaios/gosのみ選択可能）を選択し、STGのWebソースパスとpilot1・pilot2のコピー先パスが表示される
- [ ] 「差分ミラー」「全量コピー」をラジオボタン等で選択できる
- [ ] 実行ボタン押下で、まずSTG→pilot1へrobocopyが起動し、完了後に自動でSTG→pilot2へのrobocopyが起動する
- [ ] 進捗ログがSSEでリアルタイム表示され、現在pilot1/pilot2のどちらを処理中か区別できる
- [ ] 差分ミラー選択時、STG側で削除されたファイルはpilot1・pilot2側でも削除される（robocopy `/MIR`）
- [ ] 全量コピー選択時、削除ファイルの同期は行わず単純上書きコピーのみ行う（robocopy `/E`）
- [ ] robocopy実行後、コピー先（pilot1・pilot2それぞれ）web.configの`connectionStrings`内の各`name`属性に対応する`connectionString`値が、`appsettings.json`のpilot用設定値（pilot1・pilot2共通）で置換される
- [ ] 置換対象外のセクション（appSettings等）は変更されない
- [ ] robocopyの終了コード0〜7は成功として扱い、8以上はエラーとしてログ・履歴に記録する
- [ ] pilot1適用でエラーが発生した場合、pilot2への適用は行わずに処理を中断する
- [ ] 実行結果はpilot1・pilot2それぞれについて、既存の`InsertProductionReadyLog`同様に履歴テーブルへ記録される
- [ ] `PathSafety`を用いて、コピー元・コピー先（pilot1・pilot2それぞれ）パスが設定されたルート配下であることを検証する
- [ ] 除外対象（例: `bin/`直下の一時ファイル、`.vs`等）を設定可能にする

---

## 4. 技術スタック

| レイヤー | 技術 | 備考 |
|---------|------|------|
| バックエンド | .NET（既存構成に準拠） | `Process.Start`で`robocopy.exe`を起動 |
| コピーツール | robocopy（Windows標準搭載） | FastCopy.exe依存を廃止。`/MIR`（差分ミラー）、`/E`（全量コピー）、`/MT:n`（マルチスレッド）、`/R:n /W:n`（リトライ）、`/XF` `/XD`（除外） |
| フロントエンド | React 18 + TypeScript（既存構成に準拠） | 新規ページ `WebSourcePrepare.tsx` |
| 進捗通知 | SSE（既存の`PrepareController`と同様のパターン） | |
| 設定管理 | `appsettings.json` の `DbConfigs` 配列に項目追加 | |

---

## 5. 実装スコープ（変更・新規ファイル）

| ファイル | 変更内容 |
|---------|---------|
| `backend/appsettings_sample.json` | kaios・gosの`DbConfigs`要素に `WebSourcePath`（STG側公開フォルダ）、`PilotTargets`（pilot1・pilot2それぞれの`Name`と`DestWebSourcePath`を持つ配列）、`PilotConnectionStrings`（name→接続文字列のマップ、pilot1・pilot2共通）を追加 |
| `backend/Models/DbConfig.cs` | 上記追加設定に対応するプロパティ・派生パスを追加。`PilotTargets`は`List<PilotTarget>`（`Name`, `DestWebSourcePath`。STG側`DbConfig.WebSourcePath`との混同を避けるため`Dest`を付与） |
| `backend/Services/WebSourceDeployService.cs`（新規） | `PilotTargets`を順番（pilot1→pilot2）に処理し、各ターゲットごとにrobocopy起動、進捗パース、web.config接続文字列置換処理を実行。途中でエラーが出たら後続ターゲットをスキップ |
| `backend/Controllers/WebSourcePrepareController.cs`（新規） | `GET /api/web-source-prepare/{dbName}/info`（対象パス情報取得）、`POST /api/web-source-prepare/{dbName}/stream`（SSEで実行） |
| `backend/Services/PathSafety.cs` | 既存メソッドをそのまま利用（変更なしを想定、必要なら関数追加） |
| `frontend/src/pages/WebSourcePrepare.tsx`（新規） | 対象システム選択、コピー方式（差分/全量）選択、実行、ログ表示 |
| `frontend/src/api/webSourcePrepare.ts`（新規） | 新規APIのクライアント関数 |
| `frontend/src/App.tsx` 等ルーティング設定 | 新規ページへのルート追加、ナビゲーションメニュー追加 |

---

## 6. データフロー

```
[画面表示]
GET /api/web-source-prepare/{dbName}/info   (dbName は kaios | gos のみ許可)
  → DbConfig から WebSourcePath / PilotTargets(pilot1, pilot2 の DestWebSourcePath) を取得し返却

[実行]
POST /api/web-source-prepare/{dbName}/stream?mode=mirror|full
  → WebSourceDeployService.ExecuteAsync
     for each target in PilotTargets（pilot1 → pilot2 の順）:
       1. PathSafety でコピー元・コピー先パスを検証
       2. SSEで「{target.Name} 適用開始」を送信
       3. robocopy起動
          - mode=mirror: /MIR /MT:8 /R:2 /W:5 /XF ... /XD ...
          - mode=full:   /E   /MT:8 /R:2 /W:5 /XF ... /XD ...
       4. 標準出力をSSEでフロントへストリーミング
       5. robocopy終了コード判定（0-7:成功 / 8+:エラー）
          → エラーの場合、ここでループを中断し以降のtargetは実行しない
       6. 成功時、target側 web.config を読み込み、
          appsettings.json の PilotConnectionStrings（pilot1/pilot2共通）を用いて
          connectionStrings/add[@name]/@connectionString を置換
       7. targetごとの結果を WebSourceDeployLog テーブルへ記録
          （stream呼び出し1回につき1つの RunId（GUID）を発行し、pilot1/pilot2両方のレコードに同一RunIdを付与して束ねる）
     最後にSSEで全体の完了（または中断）イベントを送信
```

---

## 7. web.config接続文字列の置換仕様

- `appsettings.json` の `DbConfigs[].PilotConnectionStrings` に `{ "name": "接続文字列名", "connectionString": "pilot用の値" }` の配列を保持する
- コピー完了後、pilot側web.configをXMLとして読み込み、`connectionStrings/add`要素を`name`属性で照合し、一致する要素の`connectionString`属性のみを置換する
- `PilotConnectionStrings`に定義がない`name`はそのまま（STGの値）を維持する
- 置換対象は`connectionStrings`セクションのみ。`appSettings`等は変更しない（Issue上の要件どおり）

### 7.1 実ファイル（kaios/gos）での検証結果

`docs/Web.config_sample_kaios` / `docs/Web.config_sample_gos` を確認した結果、両ファイルとも `connectionStrings` 内に `name="ConnectionString"` と `name="ConnectionStringMySQL"` の `<add>` 要素が次の2パターンで存在する：

- **有効な要素**（コメントアウトなし）: システムごとの実際の接続先（kaiosなら`KaiosDB_dev`等、gosなら`GosDB`等）
- **無効な要素**（`<!-- ... -->` でコメントアウト）: 逆側システム向けの値の残骸

kaiosとgosで「どちらがコメントアウトされているか」が入れ替わっているが、**`XDocument`でXMLパースする場合、コメント内の`<add>`はXML要素として認識されず`XComment`（コメントノード）として扱われる**。そのため `connectionStrings/add[@name='...']` による属性検索は、コメントアウトされていない有効な要素のみに自動的にヒットする。

→ **kaios/gosでコメント位置が異なっていても、name属性による照合ロジックは共通のまま両方に対応可能**。個別分岐は不要。

### 7.2 置換時の注意点

- `XDocument`で読み込み・保存すると、元ファイルのインデント（タブ/スペース混在箇所がある）や改行コードがわずかに変化する可能性がある
- 置換対象（`connectionStrings/add`の`connectionString`属性）以外の箇所に意図しない差分が出ないよう、実装後に置換前後のdiffを確認する（Plan Task 6の検証手順に追加）

---

## 8. Boundaries

- **Always**: robocopy実行前に`PathSafety`でコピー元・コピー先の安全性を検証する／実行結果を履歴に記録する／web.config書き換えはXML解析で行い文字列置換は行わない
- **Ask first**: 除外パターンのデフォルトリスト決定、pilotサーバーへの接続方式（UNCパス到達性の確認）、robocopyの並列数(`/MT`)のデフォルト値
- **Never**: web.configの`connectionStrings`以外のセクションを自動変更しない／robocopy未インストール環境へのフォールバックとして`File.Copy`を無断で使わない（robocopyはWindows標準のため通常不要だが、非搭載環境の扱いは要確認）

---

## 9. Open Questions

1. pilot1・pilot2サーバーの公開フォルダパスに、本アプリのサーバーから到達可能なUNCパスは存在するか？（`\\pilot1server\...`, `\\pilot2server\...` 形式でアクセス可能か要確認）
2. robocopyの除外パターン（`/XF`, `/XD`）のデフォルト値（`.vs`, `obj/`, ログファイル等）は何にするか？
3. アプリケーションプール停止・起動（pilot1・pilot2側IIS再起動）は本機能のスコープに含めるか、対象外（別途手動）か？
4. `PilotConnectionStrings`の値そのもの（パスワード等の機密情報）を`appsettings.json`に平文で置くことの可否。既存の`DbConfigs`内の扱いに準拠するか、Secret管理を別途検討するか？
5. pilot1適用は成功したがpilot2適用でエラーになった場合、pilot1は成功済みのまま残してよいか（ロールバックは不要という理解でよいか）？
