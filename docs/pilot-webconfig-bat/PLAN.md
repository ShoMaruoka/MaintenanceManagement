# Implementation Plan: Pilot環境適用の web.config 切替を bat 実行に変更

対応する仕様: [`SPEC.md`](./SPEC.md)
置き換え対象: [`../pilot-webconfig-file-swap/SPEC.md`](../pilot-webconfig-file-swap/SPEC.md)

## Overview

`ExecuteAsync` の各ターゲット末尾で呼んでいる `ApplyPilotWebConfig`（`.pilot` ファイルの上書きコピー）を、
コピー先の `deploy\{Name}.DC.Pilot.bat` を実行するメソッドに置き換える。

設定・モデル・API・テーブルは変更しない。変更は `WebSourceDeployService` の1ステップと、そのテスト、画面説明文、ドキュメントに限る。

## Architecture Decisions

1. **bat パスは `DbConfig.Name` から導出**
   - 実行時: `Path.Combine(target.DestWebSourcePath, "deploy", $"{config.Name}.DC.Pilot.bat")`
   - DryRun 時: `Path.Combine(config.WebSourcePath, "deploy", $"{config.Name}.DC.Pilot.bat")`
   - ディレクトリの選び方は現行 `ApplyPilotWebConfig` と同じ。存在確認に使ったフルパスを返し、呼び出し側はそれをログに使う。

2. **bat 起動は専用メソッドで `pushd` を使う**
   引数: `/c "chcp 932 > nul && pushd "{deployDir}" && "{batPath}""`。作業フォルダは渡さない（`pushd` で移動するため）。
   既存 `RunDeployBatAsync` は SQL 適用（ローカルパス）用のため変更しない。

3. **失敗の扱いは例外に揃える**
   bat 欠落は `FileNotFoundException`、終了コード ≠ 0 は `InvalidOperationException` を投げ、既存の `catch` でターゲット失敗・`break` に落とす。
   `results.Add` / `break` を新たに書かない。

4. **ステップ・設定は増やさない**
   `WebSourceDeployStep` の分岐、`DbConfig`、appsettings は変更しない。

## Dependency Graph

```
Task1: bat 実行メソッド追加＋ExecuteAsync 差し替え（ApplyPilotWebConfig 削除）
   │
   ├── Task2: テスト更新・追加
   │
   ├── Task3: フロント説明文更新     （Task1 と並行可）
   └── Task4: ドキュメント更新        （Task1 完了後）
```

## Risks & Mitigations

| リスク | 影響 | 緩和 |
|--------|------|------|
| UNC を作業フォルダにできず bat の相対パスがずれる | 別フォルダの web.config を操作する | `pushd` で一時ドライブに移動してから実行 |
| `pushd` の一時ドライブ文字が枯渇 | bat 実行失敗 | `cmd /c` 終了時に解放される。失敗時は終了コード ≠ 0 でエラー検出 |
| 既存テストの `FakeProcessRunner` は cmd 呼び出しを区別しない（`OnCmd` / `CmdExitCode` 共通） | Web ステップでも `OnCmd` が走り、SQL エラーログ系テストの前提が崩れる | `Calls` の引数で bat 種別を判定する、または Web 用の終了コードを分ける形で Fake を拡張 |
| bat が対話入力（`pause` 等）を待つ | 処理が止まる | bat 側の責任。運用前に bat が非対話で終わることを確認（手動検証項目） |
| `config.Name` と bat 名の大文字小文字差 | — | Windows は区別しないため問題なし |

## Verification Checkpoints

### Checkpoint A（コア動作）

- [x] `ApplyPilotWebConfig` が削除され、bat 実行に置き換わっている
- [x] `dotnet build` / `dotnet test` 成功

### Checkpoint B（整合）

- [x] 画面説明文・ドキュメントが新方式を示す
- [x] DryRun のターゲットごとの `[DRY-RUN]` bat ログ（テストで確認。実機の手動確認は未実施）

## Out of Scope

- bat の内容（切替ロジック）の作成・検証
- 不要になった `Web.config.DC.*.pilot` の削除（残っていても影響なし）
- IIS アプリプール再起動
- git commit / push

---

**Status**: 実装完了（[`TASKS.md`](./TASKS.md)）。git commit / push は未実施。
