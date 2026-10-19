# Spec: Pilot環境適用の web.config 切替を pilot 側 bat 実行に変更

## 関連ドキュメント

- `docs/pilot-webconfig-file-swap/SPEC.md`（現行仕様: パイロット用ファイルの上書きコピー。本 Spec で置き換え）
- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`（Issue #25: Pilot環境適用の全体仕様）

## Objective

Webソースのフォルダ構成変更により、web.config の切替は Webソースに同梱された bat を実行するだけで済むようになった。
Pilot環境適用（Webソースコピー）の web.config 適用ステップを、「`Web.config.DC.{Name}.pilot` を `web.config` へ上書きコピー」から
「コピー先の `deploy\{Name}.DC.Pilot.bat` を実行」に変更する。

| DbConfig.Name | 実行する bat |
|---------------|--------------|
| `kaios` | `{DestWebSourcePath}\deploy\Kaios.DC.Pilot.bat` |
| `gos` | `{DestWebSourcePath}\deploy\Gos.DC.Pilot.bat` |

- **利用者**: 運用担当者（既存「Pilot環境適用」画面）
- **対象システム**: kaios / gos（現行どおり。paf・duskin は対象外）
- **成功の定義**:
  - 各 pilot のコピー後、その pilot の `DestWebSourcePath\deploy\{Name}.DC.Pilot.bat` が1回実行される
  - bat が無い、または終了コードが 0 以外なら、そのターゲットを失敗とし、以降のターゲットと SQL 適用を実行しない
  - `ApplyPilotWebConfig`（ファイル上書き）は削除され、`File.Copy` による web.config 書き換えは行わない
  - 既存の robocopy / 画像情報準備 / 共通画像 / SQL 適用の流れは維持される

## 背景・現状との差分

| 項目 | 現状（pilot-webconfig-file-swap） | 本改修 |
|------|-----------------------------------|--------|
| 切替方法 | `Web.config.DC.{Name}.pilot` → `web.config` を `File.Copy` | `deploy\{Name}.DC.Pilot.bat` を実行 |
| 実行タイミング | 各ターゲットの共通画像コピー後 | 同じ |
| bat の場所 | — | `DestWebSourcePath\deploy\`（STG の `WebSourcePath\deploy\` から robocopy で運ばれる） |
| bat の引数 | — | なし |
| 失敗判定 | `.pilot` ファイル欠落 | bat 欠落、または終了コード ≠ 0 |
| DryRun | コピー元 `WebSourcePath` で `.pilot` の存在チェック | コピー元 `WebSourcePath\deploy\` で bat の存在チェック＋ログのみ（実行しない） |
| 設定項目 | 追加なし（`Name` から導出） | 追加なし（`Name` から導出） |
| robocopy 除外 | `*.tmp` / `*.log` / `*.user`、`.vs` / `obj` / `bin\obj` | 変更なし（`deploy` は除外しない。コピー先で必要なため） |

## 設計方針

1. **bat パスは `DbConfig.Name` から導出する**
   `Path.Combine(dir, "deploy", $"{Name}.DC.Pilot.bat")`。Windows のファイル名は大文字小文字を区別しないため、`kaios` で `Kaios.DC.Pilot.bat` に一致する。設定項目は増やさない。

2. **DryRun の存在チェックはコピー元で行う**
   DryRun では robocopy が走らずコピー先に bat が無い可能性があるため、現行 `ApplyPilotWebConfig` と同じく `WebSourcePath` 側を検査する。bat は STG の Web ソースに含まれ robocopy で運ばれる（運用確認済み）。

3. **UNC の作業フォルダ対策に `pushd` を使う**
   `DestWebSourcePath` は `\\pilot1server\...` の UNC パス。`cmd.exe` は UNC を作業フォルダにできず `C:\Windows` に切り替わるため、bat 内の相対パスが別の場所を指す。
   起動コマンドを `cmd.exe /c "chcp 932 > nul && pushd "{deployDir}" && "{batPath}""` とし、`pushd` で一時ドライブを割り当ててから実行する。
   SQL 適用で使う既存 `RunDeployBatAsync` は変更しない。

4. **bat の出力は `DETAIL` ログに流す**（robocopy と同じ扱い）。

## Code Style

既存の `WebSourceDeployService` に合わせる（日本語メッセージ、`[DRY-RUN]` タグ、ターゲット名付きログ）。イメージ:

```csharp
/// <summary>
/// コピー先の deploy\{dbConfigName}.DC.Pilot.bat を実行し、pilot 用 web.config に切り替える。
/// dryRun=true の場合はコピー元（webSourcePath）側の存在チェックとログのみで、bat は実行しない。
/// </summary>
/// <returns>存在確認に使った bat のフルパス（ログ用）。</returns>
private async Task<string> RunPilotWebConfigBatAsync(
    string webSourcePath, string destWebSourcePath, string dbConfigName,
    Action<string> onOutputLine, CancellationToken ct)
```

## Testing Strategy

| レベル | 方針 |
|--------|------|
| ビルド | `cd backend && dotnet build` 成功 |
| 単体 | `WebSourceDeployServiceSqlSourceTests` の `FakeProcessRunner` で、bat の起動引数・回数・終了コード・DryRun を検証 |
| 結合（手動） | DryRun でターゲットごとに `[DRY-RUN]` の bat ログが出ること。実環境で各 pilot の `web.config` が pilot 用に切り替わること |
| 回帰 | 画像情報準備・共通画像・SQL 適用の順序と成否が変わらないこと |

## Boundaries

- **Always**
  - bat は各ターゲットの共通画像コピー（またはスキップ）の後に実行する
  - 実行するのはそのターゲットの `DestWebSourcePath\deploy\` の bat
  - DryRun では bat を実行しない
  - 失敗時は既存の `catch` でターゲット失敗・`break`
- **Ask first**
  - bat 名の命名規則変更、引数の追加
  - kaios / gos 以外への Pilot 適用拡張
  - robocopy 除外設定の変更
- **Never**
  - web.config をアプリ側で書き換えること（`File.Copy` / 文字列置換）
  - STG 側の `WebSourcePath\deploy\` の bat を実行すること
  - git commit / push（ユーザー指示があるまで）

## 処理フロー（変更後）

各 `PilotTarget` について:

1. `WebSourcePath` → `DestWebSourcePath` robocopy（`deploy\` も含む）
2. 画像情報準備 `FilesPath\{Images,news,pdf}` → `DestWebSourcePath\{同名}`
3. 共通画像 `CommonImagePath` → `DestImagePath`
4. **変更**: `DestWebSourcePath\deploy\{Name}.DC.Pilot.bat` を実行
5. 次ターゲット / 全成功後に SQL 適用（既存）

## Success Criteria

- [x] `ApplyPilotWebConfig` が削除され、bat 実行に置き換わっている
- [x] 各ターゲットで bat が1回ずつ（pilot1 → pilot2 の順に）実行される
- [x] 起動コマンドに `pushd` と bat のフルパスが含まれる
- [x] bat 欠落時はエラーで中断し、後続ターゲット・SQL 適用を実行しない
- [x] bat の終了コード ≠ 0 でエラーで中断し、後続ターゲット・SQL 適用を実行しない
- [x] DryRun 時は bat を起動せず、`[DRY-RUN]` ログのみ出る
- [x] 画面の説明文が bat 実行の内容に更新されている
- [x] `dotnet build` / `dotnet test` 成功

## Open Questions

なし（確認事項は回答済み）。

| # | 確認事項 | 決定 |
|---|----------|------|
| 1 | 実行タイミング | 各 pilot のコピー直後に毎回 |
| 2 | bat の場所 | kaios / gos とも `DestWebSourcePath\deploy\{Name}.DC.Pilot.bat` |
| 3 | 引数 | なし |
| 4 | bat の搬送 | STG の Web ソースに含まれ、robocopy で運ばれる（DryRun はコピー元を検査） |

---

**Status**: Task 1–4 実装完了（バージョン 1.6.3）。git commit / push・タグ作成は未実施。実環境での bat 動作確認は運用側で実施。
