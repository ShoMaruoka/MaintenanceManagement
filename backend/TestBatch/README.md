# テスト用ドライランバッチ

`DryRun: true` 設定時に使用するテスト用バッチファイル。
実際のgit操作・SQL適用は行わず、ログ出力のみを行います。

## 使い方

`appsettings.json` で `"DryRun": true` に設定すると、
DeployService はバッチを呼び出さずにシミュレーションログを返します。

実環境に切り替える場合は `"DryRun": false` に変更してください。
