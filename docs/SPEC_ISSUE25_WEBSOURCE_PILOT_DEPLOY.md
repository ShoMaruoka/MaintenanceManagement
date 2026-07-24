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

- 新規画面「WebSourcePrepare」（画面表示名: Pilot環境適用）から、対象システム（kaios/gosのいずれか）を選択し、STG→pilotへのWebソースコピーを実行できる
- コピー方式は選択式にせず、**常に全量コピー（robocopy `/E`、削除同期なし）のみで実行する**（差分ミラー`/MIR`は誤操作によるファイル削除のリスクがあるため、仕様として提供しない）
- コピー処理は robocopy（Windows標準）を利用し、外部ツール（FastCopy.exe）への依存をなくす
- 1回の実行で **STG→pilot1→pilot2の順に自動連続適用** する（pilot1完了後、自動的にpilot2への適用に進む）
- pilot1・pilot2それぞれの適用後、コピー先web.configの `connectionStrings` セクションを、`appsettings.json` に保持したpilot用接続文字列で自動置換する（pilot1・pilot2で同一の接続文字列値を使用）
- 進捗は既存のPrepare機能と同様にSSEでログ表示し、「pilot1適用中」「pilot2適用中」のようにどちらの対象を処理中か分かるようにする
- pilot1適用中にエラーが発生した場合はpilot2への適用を行わず処理を中断し、エラー内容をログ・履歴に記録する
- 【追加】Webソースコピー（pilot1→pilot2）が全て成功した場合、続けてSQLファイルのpilot環境への適用を自動連結実行する。`Deploy2PrdPath`のSQLファイル一式を、事前に用意された「Pilot適用用SQLフォルダ」（`PilotSqlDeployPath`。配下に`Source\`と`deploy.bat`を持つ）の`Source`へ全量コピーし、続けて`deploy.bat`（本システムは作成しない。ユーザー側で事前配置）を引数なし・作業ディレクトリ`PilotSqlDeployPath`で実行する。SQL Server（DB）はpilot1/pilot2で共有の単一インスタンスのため、pilotターゲットごとに分けず`DbConfig`単位で1パスのみ保持する。SQL適用の成否はWebソースコピー結果とは独立して記録し、互いのステータスに影響しない

---

## 3. 受け入れ条件（Acceptance Criteria）

- [x] WebSourcePrepare画面で対象システム（kaios/gosのみ選択可能）を選択し、STGのWebソースパスとpilot1・pilot2のコピー先パスが表示される
- [x] コピー方式の選択肢は設けず、常に全量コピー（robocopy `/E`、削除同期なし）のみで実行する（誤操作による削除事故を防ぐため、差分ミラー`/MIR`は提供しない）
- [x] 実行ボタン押下で、まずSTG→pilot1へrobocopyが起動し、完了後に自動でSTG→pilot2へのrobocopyが起動する
- [x] 進捗ログがSSEでリアルタイム表示され、現在pilot1/pilot2のどちらを処理中か区別できる
- [x] 全量コピーのため、削除ファイルの同期は行わず、新規・変更ファイルのみコピーする（pilot側にのみ存在する余分なファイルは削除されず残る）
- [x] robocopy実行後、コピー先（pilot1・pilot2それぞれ）web.configの`connectionStrings`内の各`name`属性に対応する`connectionString`値が、`appsettings.json`のpilot用設定値（pilot1・pilot2共通）で置換される
- [x] 置換対象外のセクション（appSettings等）は変更されない
- [x] robocopyの終了コード0〜7は成功として扱い、8以上はエラーとしてログ・履歴に記録する
- [x] pilot1適用でエラーが発生した場合、pilot2への適用は行わずに処理を中断する
- [x] 実行結果はpilot1・pilot2それぞれについて、既存の`InsertProductionReadyLog`同様に履歴テーブルへ記録される（実装上は専用の`InsertWebSourceDeployLog`／`WebSourceDeployLog`テーブルとして、同一`RunId`で束ねて記録する）
- [x] `PathSafety`を用いて、コピー元・コピー先（pilot1・pilot2それぞれ）パスの設定ミス（空文字・相対パス・src=dest一致・ドライブ/共有ルート指定）を検出し拒否する（7.3参照。ユーザー入力の相対パスをルート配下に閉じ込める従来のPathSafety用途とは異なり、信頼できる設定値に対する事故防止ガードとして用いる）
- [x] 除外対象（例: `bin/`直下の一時ファイル、`.vs`等）を設定可能にする
- [x] `DryRun=true`の場合、robocopyコピーだけでなくweb.config接続文字列の置換も実ファイルへ書き込まない（ログにのみ「置換予定」を出力する）
- [x] `PilotConnectionStrings`に定義された`name`が、コピー先web.configの`connectionStrings`内に1件も見つからない場合はエラーとして扱う（STGの接続文字列がpilotに残ったまま「成功」とならないようにする）
- [x] 【追加】`PilotSqlDeployPath`が設定されており、かつWebソースコピーが（pilot1・pilot2とも）全て成功した場合、`Deploy2PrdPath`のSQLファイル一式を`{PilotSqlDeployPath}\Source`へ全量コピーし、続けて`{PilotSqlDeployPath}\deploy.bat`を実行する
- [x] 【追加】`Source`フォルダは、前回実行分の古いSQLファイルが残らないよう、コピー前に毎回空にしてから全量コピーする
- [x] 【追加】`deploy.bat`は引数なし・作業ディレクトリ`PilotSqlDeployPath`で実行し、標準出力/標準エラーを既存のrobocopyログと同じSSEストリームでリアルタイム表示する
- [x] 【追加】`deploy.bat`が見つからない、またはエラー終了コード（0以外）の場合はSQL適用結果を失敗として記録する
- [x] 【追加】Webソースコピーが失敗（中断）した場合、SQL適用ステップは実行しない
- [x] 【追加】SQL適用の成否は、Webソースコピーの各ターゲット結果とは独立した項目として画面・履歴に記録する（互いのステータスに影響しない）
- [x] 【追加】`PilotSqlDeployPath`が未設定の場合、SQL適用ステップ自体を行わない（オプトイン）
- [x] 【追加】実行画面で実行内容（`両方` / `Webソースコピーのみ` / `SQL適用のみ`）を選択できる。片方のみ失敗した場合に、成功済みの側を再実行せず失敗した側だけを再実行できるようにするため。「SQL適用のみ」選択時はWebソースコピーの成否・実行有無を問わず無条件でSQL適用を実行する

---

## 4. 技術スタック

| レイヤー | 技術 | 備考 |
|---------|------|------|
| バックエンド | .NET（既存構成に準拠） | `Process.Start`で`robocopy.exe`を起動 |
| コピーツール | robocopy（Windows標準搭載） | FastCopy.exe依存を廃止。誤操作防止のため常に`/E`（全量コピー・削除同期なし）で実行し、`/MIR`（差分ミラー）は使用しない。`/MT:n`（マルチスレッド）、`/R:n /W:n`（リトライ）、`/XF` `/XD`（除外） |
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
| `frontend/src/pages/WebSourcePrepare.tsx`（新規） | 対象システム選択、実行、ログ表示（コピー方式の選択UIは設けない。常に全量コピー） |
| `frontend/src/api/webSourcePrepare.ts`（新規） | 新規APIのクライアント関数 |
| `frontend/src/App.tsx` 等ルーティング設定 | 新規ページへのルート追加、ナビゲーションメニュー追加 |
| `backend/Models/DbConfig.cs`（追加） | 【追加】`PilotSqlDeployPath`（SQL適用フォルダのパス）と派生パス（`PilotSqlDeploySourcePath`, `PilotSqlDeployBatPath`）を追加 |
| `backend/Services/WebSourceDeployService.cs`（追加） | 【追加】`RunSqlDeployAsync`（Source初期化→SQLコピー→`deploy.bat`実行）、`ExecuteAsync`戻り値をタプル化しWebソースコピー全成功時のみSQL適用を連結実行 |
| `backend/Controllers/WebSourcePrepareController.cs`（追加） | 【追加】SQL適用結果を`WebSourceDeployLog`へ記録（`TargetName="sql"`）、`done`イベントJSONに`sqlDeploy`を追加 |
| `frontend/src/api/webSourcePrepare.ts` / `WebSourcePrepare.tsx`（追加） | 【追加】`ApiWebSourceSqlDeployResult`型追加、完了画面にSQL適用結果を独立表示 |

---

## 6. データフロー

```
[画面表示]
GET /api/web-source-prepare/{dbName}/info   (dbName は kaios | gos のみ許可)
  → DbConfig から WebSourcePath / PilotTargets(pilot1, pilot2 の DestWebSourcePath) を取得し返却

[実行]
POST /api/web-source-prepare/{dbName}/stream
  → WebSourceDeployService.ExecuteAsync
     for each target in PilotTargets（pilot1 → pilot2 の順）:
       1. PathSafety でコピー元・コピー先パスを検証
       2. SSEで「{target.Name} 適用開始」を送信
       3. robocopy起動（常に /E /MT:8 /R:2 /W:5 /XF ... /XD ... 。/MIR は使用しない）
          コピー元: DbConfig.WebSourcePath → コピー先: target.DestWebSourcePath
       4. 標準出力をSSEでフロントへストリーミング
       5. robocopy終了コード判定（0-7:成功 / 8+:エラー）
          → エラーの場合、ここでループを中断し以降のtargetは実行しない
       6. DbConfig.FilesDeploy2PrdPath が設定されていれば、その中身（本番前準備で確定した
          Images/news/pdf等の画像・静的ファイル）を target.DestWebSourcePath 直下へ追加でコピー
          （常に /E 固定。Web アプリ本体と同じルートへコピーするため、誤って本体ファイルを
          削除しないよう /MIR は使用しない）
          → エラーの場合、ここでループを中断し以降のtargetは実行しない
       7. 成功時、target側 web.config を読み込み、
          appsettings.json の PilotConnectionStrings（pilot1/pilot2共通）を用いて
          connectionStrings/add[@name]/@connectionString を置換
       8. targetごとの結果を WebSourceDeployLog テーブルへ記録
          （stream呼び出し1回につき1つの RunId（GUID）を発行し、pilot1/pilot2両方のレコードに同一RunIdを付与して束ねる）
     【追加】pilot1・pilot2とも成功した場合のみ、続けて SQL 適用ステップを実行:
       9. PilotSqlDeployPath が未設定なら本ステップ自体をスキップ
       10. PilotSqlDeployPath\Source が存在すれば削除して空フォルダとして再作成
           （前回実行分の古い SQL ファイルを残さないため。DryRun時は初期化せずログのみ）
       11. Deploy2PrdPath → PilotSqlDeployPath\Source へ robocopy 全量コピー（/E 固定）
           → エラーの場合、SQL適用結果を失敗として記録しdeploy.batは実行しない
       12. PilotSqlDeployPath\deploy.bat（事前配置・本システムは作成しない）を
           引数なし・作業ディレクトリ PilotSqlDeployPath で実行
           標準出力/標準エラーは既存robocopyログと同じSSEストリームへリアルタイム配信（Shift-JIS対応）
       13. deploy.bat の終了コードが 0 以外の場合、SQL適用結果を失敗として記録
       14. SQL適用結果（WebSourceSqlDeployResult）を WebSourceDeployLog へ
           TargetName="sql" として同一 RunId で記録（Webソースコピーの targets 結果とは独立して扱う）
     最後にSSEで全体の完了（または中断）イベントを送信（done イベントJSONに sqlDeploy を含める）
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

### 7.2 置換時の注意点（実装確定）

実装・実ファイル検証の結果、当初想定していた`XDocument`によるロード→Save方式は不採用とした。`XDocument.Save`は自己終了タグへの空白挿入（`/>` → ` />`）やXML宣言への`encoding`属性付与など、**置換対象外の箇所まで書式を変えてしまう**ことが実ファイル検証で判明したため（7.1のサンプルで実測）。

代わりに以下の方式を採用する:

1. `XmlReader`で`connectionStrings`配下の`add`要素を走査し、対象`name`が存在する**行番号**と**現在のconnectionString値**のみを特定する（コメント内の`<add>`はXmlReaderが要素として認識しないため、7.1の性質はそのまま活かせる）
2. 元ファイルを行単位のテキストとして扱い、特定した行の`connectionString="旧値"`部分のみを文字列置換する（XML全体の再シリアライズは行わない）
3. 元ファイルのBOM有無（kaiosはBOMあり、gosはBOMなしを実ファイルで確認）を検出し、書き込み時も同じ状態を再現する

この方式により、実サンプル（`Web.config_sample_kaios`/`gos`）で検証した結果、**置換対象の2行以外は完全に元ファイルと一致**することを確認済み（コメントアウトされた逆システム向けの値も無変更）。

### 7.3 未ヒット・DryRun時の扱い（実装確定）

- `PilotConnectionStrings`に定義された`name`のうち、web.config側に有効な（コメントアウトされていない）`add`要素が1件も見つからない場合、**エラーとして例外を送出する**。STGの接続文字列がpilot側に残ったまま「成功」として処理が終わる事故を防ぐため。
- 一部の`name`はヒットし一部はヒットしない場合も、ファイルへの書き込みは行わずエラーとする（部分適用を避ける。全件ヒットした場合のみ書き込む）
- `appsettings.json`の`DryRun: true`設定時は、robocopyコピーだけでなくweb.config置換も実ファイルへ書き込まない（対象特定・存在チェックまでは行うが、書き込みはスキップしログにのみ記録する）

---

## 8. Boundaries

- **Always**: robocopyは常に`/E`（全量コピー・削除同期なし）で実行し`/MIR`は使用しない（誤操作によるファイル削除事故防止）／robocopy実行前に`PathSafety`でコピー元・コピー先パスの設定ミス（空文字・相対パス・src=dest一致・ドライブ/共有ルート指定）を検証する／実行結果を履歴に記録する／web.config書き換えはXmlReaderで対象位置を特定した上での行単位テキスト置換とし、connectionStrings以外の書式・内容を変えない／`DryRun=true`時はrobocopy・web.config置換のどちらも実ファイルへ書き込まない／キャンセル時はrobocopyプロセスをKillして残留させない／【追加】`PilotSqlDeployPath\Source`はコピー前に毎回空にしてから全量コピーする（古いSQLの残留防止）／【追加】`deploy.bat`は本システムで作成・自動生成しない（ユーザー側の事前配置を前提とする）／【追加】SQL適用結果はWebソースコピー結果と独立して記録し、Webソースコピーが失敗した場合はSQL適用ステップ自体を実行しない
- **Ask first**: 除外パターンのデフォルトリスト決定、pilotサーバーへの接続方式（UNCパス到達性の確認）、robocopyの並列数(`/MT`)のデフォルト値、【追加】`deploy.bat`の実行に必要な権限・認証情報（SQL Server接続情報等）は`deploy.bat`側で保持する前提でよいか
- **Never**: web.configの`connectionStrings`以外のセクションを自動変更しない／robocopy未インストール環境へのフォールバックとして`File.Copy`を無断で使わない（robocopyはWindows標準のため通常不要だが、非搭載環境の扱いは要確認）／`PilotConnectionStrings`のnameが1件でも未ヒットの場合は書き込みを行わない（部分適用の禁止）／【追加】`deploy.bat`の内容を本システムが生成・書き換えることはしない

---

## 9. Open Questions

1. **未解決（運用担当確認要）**: pilot1・pilot2サーバーの公開フォルダパスに、本アプリのサーバーから到達可能なUNCパスは存在するか？（`\\pilot1server\...`, `\\pilot2server\...` 形式でアクセス可能か要確認）。実サーバーでの`info`/`stream`目視確認とあわせて確認する。
2. **解決済み（Task 12）**: robocopyの除外パターン（`/XF`, `/XD`）は`appsettings.json`の`WebSourceDeploy:ExcludeFiles`/`WebSourceDeploy:ExcludeDirs`で設定可能とし、未設定時のデフォルトは`*.tmp`/`*.log`/`*.user`（ファイル）、`.vs`/`obj`/`bin\obj`（ディレクトリ）とした。
3. **未解決（運用担当確認要）**: アプリケーションプール停止・起動（pilot1・pilot2側IIS再起動）は本機能のスコープに含めるか、対象外（別途手動）か？現状の実装はrobocopy＋web.config書き換えのみで、IIS再起動は行わない。
4. **未解決（運用担当確認要）**: `PilotConnectionStrings`の値そのもの（パスワード等の機密情報）を`appsettings.json`に平文で置くことの可否。現状は既存の`DbConfigs`内の扱いに準拠し平文としている。
5. **未解決（運用担当確認要、暫定方針あり）**: pilot1適用は成功したがpilot2適用でエラーになった場合、pilot1は成功済みのまま残してよいか（ロールバックは不要という理解でよいか）？現在の実装は、pilot1失敗時にpilot2をスキップする（Task 13で動作確認済み）が、逆にpilot1成功・pilot2失敗時のpilot1側ロールバックは実装していない（部分適用状態が残る）。運用上ロールバックが必要な場合は追加タスク化が必要。
6. **未解決（運用担当確認要）**: 【追加】`deploy.bat`はユーザー側で事前に用意する前提だが、実サーバー環境での`deploy.bat`の内容・接続先DB指定方法（bat内にハードコードか、環境変数等か）の確認、および実サーバーでの動作確認は未実施（実装はrobocopyコピー＋`Process.Start`実行のみを担保）。
7. **未解決（運用担当確認要、暫定方針あり）**: 【追加】SQL適用が失敗した場合の再実行方法（画面から「Pilot環境適用」を再実行すると、成功済みのWebソースコピーも含め最初からやり直しになる）でよいか、SQL適用のみの再実行を別途用意すべきか。現状はSQL適用のみの再実行手段は用意していない。
