# Spec: Pilot環境適用の web.config をパイロット用ファイル差し替えに変更

## 関連ドキュメント

- `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`（Issue #25: 接続文字列置換の現行仕様）
- `docs/issue27/SPEC.md`（Issue #27: 画像コピー・View DB名置換。web.config 手順への言及あり）

## Objective

Pilot環境適用（Webソースコピー）において、現状の「コピー先 `web.config` の `connectionStrings` を `appsettings.json` の値で部分置換する」方式をやめ、
`WebSourcePath` に事前配置されたパイロット専用設定ファイルを `web.config` として上書きする方式に変更する。

| DbConfig.Name | パイロット用ファイル名 |
|---------------|------------------------|
| `kaios` | `Web.config.DC.kaios.pilot` |
| `gos` | `Web.config.DC.gos.pilot` |

- **利用者**: 運用担当者（既存「Pilot環境適用」画面）
- **対象システム**: kaios / gos（現行どおり。paf・duskin は対象外）
- **成功の定義**:
  - 各 pilot 適用後、コピー先の `web.config` が上記パイロット用ファイルと同一内容になっている
  - パイロット用ファイル自体はコピー先に残る
  - `PilotConnectionStrings` による部分置換は行われない（設定・コードとも削除）
  - 既存の robocopy / Files / 画像コピー / SQL 適用の流れは維持される

## 背景・現状

| 項目 | 現状 | 本改修の方針 |
|------|------|--------------|
| Webソースコピー | `WebSourcePath` → `DestWebSourcePath` へ robocopy | 変更なし（パイロット用ファイルも同梱でコピーされる） |
| web.config 適用 | `ReplaceConnectionStrings` で接続文字列のみ置換 | **廃止**。パイロット用ファイルを `web.config` へ上書きコピー |
| `PilotConnectionStrings` | appsettings に保持し置換に使用 | **削除**（モデル・設定・コード） |
| パイロット用ファイル | STG の `WebSourcePath` に事前配置済み | 運用前提として存在必須。欠落時はエラー |

## 方式選定（効率）

差し替え元の候補:

| 案 | 内容 | 評価 |
|----|------|------|
| **A（採用）** | コピー先 `DestWebSourcePath\Web.config.DC.{name}.pilot` → 同ディレクトリの `web.config` へ上書き | robocopy 済みのファイルを同一共有内でコピーするだけ。STG→pilot の再転送が不要 |
| B | STG の `WebSourcePath\...` から直接 dest の `web.config` へコピー | 結果は同じだが、小さなファイルでも STG→pilot のネットワーク転送がもう一度走る |

**採用: A**。結果は同等だが、追加のネットワーク転送がない A の方が効率的。

## Tech Stack

Issue #25 / #27 と同一。追加の依存関係なし。

- バックエンド: ASP.NET Core（`WebSourceDeployService` / `DbConfig`）
- フロントエンド: 画面説明文の更新のみ（処理ロジック変更なし想定）
- 設定: `appsettings.json` / `appsettings_sample.json` / `appsettings.Development.json`

## Commands

```
Backend Build:  cd backend && dotnet build
Backend Run:    cd backend && dotnet run
Frontend Dev:   cd frontend && npm run dev
Frontend Build: cd frontend && npm run build
```

自動テスト基盤は未導入のため、検証は `dotnet build` と DryRun / ローカルダミーフォルダでの手動確認とする。

## Project Structure

```
backend/
  Models/DbConfig.cs                    # PilotConnectionStrings / PilotConnectionString 削除
  Services/WebSourceDeployService.cs    # 置換 → ファイル差し替え
  appsettings_sample.json               # PilotConnectionStrings 削除
  appsettings.Development.json          # 同上（ローカル設定）
  appsettings.json                      # 同上（実環境設定。秘密情報を含む場合は手元のみ）
frontend/
  src/pages/WebSourcePrepare.tsx        # 説明文の更新
docs/
  SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md  # 参照整合（必要箇所を追記/注記）
  issue27/SPEC.md                         # web.config 手順の記述を更新
  pilot-webconfig-file-swap/SPEC.md       # 本仕様（本ファイル）
```

## Code Style

既存の `WebSourceDeployService` に合わせる。例（差し替え処理のイメージ）:

```csharp
/// <summary>
/// コピー先のパイロット用 Web.config を web.config として上書きする。
/// ファイル名は Web.config.DC.{dbConfigName}.pilot（例: kaios → Web.config.DC.kaios.pilot）。
/// dryRun=true の場合は存在チェックのみ行い、上書きしない。
/// </summary>
public static void ApplyPilotWebConfig(string destWebSourcePath, string dbConfigName, bool dryRun)
{
    var sourceName = $"Web.config.DC.{dbConfigName}.pilot";
    var sourcePath = Path.Combine(destWebSourcePath, sourceName);
    var destPath = Path.Combine(destWebSourcePath, "web.config");

    if (!File.Exists(sourcePath))
        throw new FileNotFoundException($"パイロット用 web.config が見つかりません: {sourcePath}", sourcePath);

    if (dryRun)
        return;

    File.Copy(sourcePath, destPath, overwrite: true);
}
```

命名・ログは既存どおり（日本語メッセージ、`[DRY-RUN]` タグ、ターゲット名付き）。

## Testing Strategy

| レベル | 方針 |
|--------|------|
| ビルド | `dotnet build` が成功すること（`PilotConnectionString` 参照切れがないこと） |
| 単体相当（手動） | 一時フォルダに `Web.config.DC.kaios.pilot` とダミー `web.config` を置き、差し替えメソッドを DryRun / 本番相当で確認 |
| 結合（手動） | DryRun=true でログに差し替え予定が出ること。実コピー環境では dest の `web.config` がパイロット用と一致し、`Web.config.DC.*.pilot` が残ること |
| 回帰 | Files コピー・画像コピー・SQL 適用の順序と成否が変わらないこと |

## Boundaries

- **Always**
  - 差し替えは robocopy（および Files / 画像コピー）完了後に実行する
  - 差し替え元はコピー先内の `Web.config.DC.{DbConfig.Name}.pilot`
  - 差し替え先ファイル名は `web.config`（上書きのみ。バックアップ不要）
  - パイロット用ファイルは削除しない（残す）
  - 欠落時は例外を送出し、当該ターゲットを失敗・以降スキップ（現行の置換失敗時と同じ）
  - `DryRun=true` 時は実ファイルを書き換えない（存在チェックとログのみ）
- **Ask first**
  - パイロット用ファイル名の命名規則変更
  - kaios/gos 以外への Pilot 適用拡張
  - IIS アプリプール再起動の自動化
- **Never**
  - `web.config` の部分文字列置換に戻すこと（本改修後）
  - STG 側の `Web.config.DC.*.pilot` や `web.config` を書き換え・削除すること
  - シークレットを Spec / コミットに載せること
  - git commit / push（ユーザー指示があるまで）

## 処理フロー（変更後）

各 `PilotTarget` について:

1. `WebSourcePath` → `DestWebSourcePath` robocopy
2. （任意）`FilesDeploy2PrdPath` → dest
3. （任意）`CommonImagePath` → `DestImagePath`
4. **NEW**: `DestWebSourcePath\Web.config.DC.{Name}.pilot` → `DestWebSourcePath\web.config` 上書き
5. 次ターゲット / 全成功後に SQL 適用（既存）

## Success Criteria

- [x] `ReplaceConnectionStrings` / `FindActiveConnectionStringLine` / `EscapeXmlAttribute`（置換専用）が削除または未使用になっている
- [x] `PilotConnectionString` 型および `DbConfig.PilotConnectionStrings` が削除されている
- [x] `appsettings_sample.json`（および開発用設定）から `PilotConnectionStrings` が削除されている
- [x] kaios 適用後、dest の `web.config` が `Web.config.DC.kaios.pilot` と同一内容（Checkpoint A で同等検証）
- [x] gos 適用後、dest の `web.config` が `Web.config.DC.gos.pilot` と同一内容（同一ロジック・`DbConfig.Name` 導出）
- [x] dest に `Web.config.DC.*.pilot` が残っている（Checkpoint A 確認）
- [x] パイロット用ファイル欠落時はエラーで中断し、後続ターゲットを実行しない
- [x] `DryRun=true` 時は `web.config` が書き換わらない（Checkpoint A 確認）
- [x] SSE ログに差し替え完了（または DryRun）が分かるメッセージが出る
- [x] フロントの説明文が「接続文字列の書き換え」から「パイロット用 web.config の適用」に更新されている
- [x] `dotnet build` 成功
- [x] Issue #25 / #27 の関連ドキュメントに本変更が反映または注記されている

## Open Questions

なし（確認事項は回答済み）。

| # | 確認事項 | 決定 |
|---|----------|------|
| 1 | 差し替え元 | **A**: コピー先内のファイル（効率優先） |
| 2 | パイロット用ファイルの残留 | **残す** |
| 3 | `PilotConnectionStrings` | **削除** |
| 4 | 最終ファイル名 | `web.config` |
| 5 | 既存 web.config | **上書きのみ**（バックアップ不要） |

---

**Status**: Spec / Plan / Tasks 承認後、`/build auto` により **実装完了**（2026-07-28）。git commit / push は未実施。
