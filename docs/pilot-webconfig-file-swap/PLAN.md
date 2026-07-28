# Implementation Plan: Pilot環境適用の web.config ファイル差し替え

対応する仕様: [`SPEC.md`](./SPEC.md)  
関連: [`../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`](../SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md) / [`../issue27/SPEC.md`](../issue27/SPEC.md)

## Overview

Issue #25 で実装した「コピー先 `web.config` の接続文字列部分置換」（`ReplaceConnectionStrings` + `PilotConnectionStrings`）を廃止し、
robocopy 後にコピー先の `Web.config.DC.{DbConfig.Name}.pilot` を `web.config` へ上書きする方式へ切り替える。

新規サービス・画面・API・テーブルは作らない。変更は主に `WebSourceDeployService` の最終ステップと、設定・モデルの削除、画面説明文・関連ドキュメントの整合である。

## Architecture Decisions

1. **差し替え元はコピー先（方式 A）**  
   `Path.Combine(target.DestWebSourcePath, $"Web.config.DC.{config.Name}.pilot")` をソースとし、同ディレクトリの `web.config` へ `File.Copy(..., overwrite: true)`。  
   robocopy で既に届いているファイルを使うため、STG→pilot の再転送を避ける。

2. **ファイル名は `DbConfig.Name` から導出**  
   設定項目は増やさない。`Web.config.DC.{config.Name}.pilot` の固定規則のみ。kaios / gos 以外に Pilot が付いても同じ規則で動く。

3. **置換処理は完全削除**  
   `ReplaceConnectionStrings` / `FindActiveConnectionStringLine` / `EscapeXmlAttribute` と `using System.Xml` を削除。  
   `PilotConnectionString` 型と `DbConfig.PilotConnectionStrings` も削除。部分置換の残骸を残さない。

4. **挿入位置は現行の「web.config 置換」と同じタイミング**  
   各ターゲット内: Webソース robocopy → Files → 画像 → **パイロット用 web.config 適用**。  
   失敗時の `break`（以降ターゲット・SQL スキップ）は現行どおり。専用の `WebSourceDeployStep` は増やさない。

5. **DryRun**  
   パイロット用ファイルの存在チェックは **コピー元 `WebSourcePath`** で行い、欠落なら例外。存在すればログのみで `File.Copy` しない。  
   （robocopy が DryRun 時にコピーしないため、コピー先を検査すると偽陰性になる）

6. **`ApplyPilotWebConfig` は検査に使ったフルパスを返す**  
   呼び出し側は戻り値をそのまま `File.GetLastWriteTime` に渡し、ログのファイル名は `Path.GetFileName` で得る。  
   ディレクトリ選択ロジックをメソッド内に閉じ込める。

7. **設定掃除の範囲**  
   - 必須: `appsettings_sample.json`、`DbConfig.cs`  
   - `appsettings.Development.json` / `appsettings.json` に現状キーが無いことは確認済み。あれば削除、無ければ触らない。  
   - 実サーバー上の `appsettings.json` に残っていても、未バインドの余分キーは ASP.NET Core では無視されるが、運用設定からは手動削除を推奨（ドキュメントに注記）。

8. **ドキュメント**  
   Issue #25 SPEC の §7（接続文字列置換）を「廃止・ファイル差し替えへ移行」と注記し、Issue #27 SPEC/PLAN の「web.config 置換」文言を更新する。歴史的記述は消しすぎず、現行挙動が分かるようにする。

## Dependency Graph

```
Task1: ApplyPilotWebConfig 実装
   │
   ├── Task2: ExecuteAsync 差し替え呼び出し（置換呼び出し削除）
   │
   ├── Task3: PilotConnectionStrings モデル削除
   │      └── Task4: appsettings_sample からキー削除
   │
   ├── Task5: フロント説明文更新          （Task2 と並行可）
   └── Task6: 関連 docs 更新              （Task2 完了後推奨）
```

実装順は Task1 → Task2 → Task3/4（削除は呼び出し側を先に直してから）→ Task5/6。  
Task3 を Task2 より先にやるとビルドが壊れるため、**呼び出し置換（Task2）のあとでモデル削除（Task3）**とする。  
実務上は Task1+2+3 を同一セッションでまとめてもよいが、検証しやすいよう Plan 上は分割する。

## Risks & Mitigations

| リスク | 影響 | 緩和 |
|--------|------|------|
| robocopy 除外で `Web.config.DC.*.pilot` がコピーされない | 差し替えが常に FileNotFound | 現行 `ExcludeFiles` は `*.tmp`/`*.log`/`*.user` のみ。本ファイルは対象外。検証で dest にファイルがあることを確認 |
| Windows の大文字小文字（`Web.config` vs `web.config`） | IIS は通常問題なし。上書き先は現行どおり `web.config` | Spec どおり `web.config` に統一。既存 dest の大文字ファイルは NTFS 上で同一扱い |
| `config.Name` とファイル名の不一致（大文字等） | ファイル見つからず失敗 | 実ファイルは `kaios`/`gos`（小文字）前提。Name も小文字で運用 |
| Issue #25 の「置換しないと STG 接続が残る」懸念 | パイロット用ファイル自体が STG 接続のままだと事故 | ファイル内容は運用側の責任。欠落時は失敗させる。中身のバリデーションはしない（Spec どおり） |
| Xml 置換削除後の未使用 using | ビルド警告 | `using System.Xml` を同時削除 |

## Parallel vs Sequential

| 並行可能 | 直列必須 |
|----------|----------|
| Task5（FE 文言）は Task2 と並行可 | Task1 → Task2 → Task3 → Task4 |
| Task6（docs）はコード完了後 | — |

## Verification Checkpoints

### Checkpoint A（コア動作）

- [ ] `ApplyPilotWebConfig` が存在し、欠落時に例外、DryRun で非書き込み
- [ ] `ExecuteAsync` が置換ではなく差し替えを呼ぶ
- [ ] `dotnet build` 成功
- [ ] 一時フォルダで DryRun / 実コピーの手動確認

### Checkpoint B（掃除・整合）

- [ ] `PilotConnectionStrings` がコード・sample から消滅
- [ ] FE 説明文が新方式を示す
- [ ] Issue #25 / #27 ドキュメントに本変更が追記されている

---

## Task List

### Phase 1: Backend コア

- [ ] **Task 1: `ApplyPilotWebConfig` を実装**
  - **Description**: `WebSourceDeployService` に Spec 記載の静的メソッドを追加する。ソース名は `$"Web.config.DC.{dbConfigName}.pilot"`。欠落時 `FileNotFoundException`。`dryRun=true` なら存在確認のみ。
  - **Acceptance**:
    - [ ] メソッドが public static で単体呼び出し可能
    - [ ] DryRun / 欠落 / 上書きの3経路が仕様どおり
  - **Verify**: 一時フォルダにダミー2ファイルを置き、メソッドを直接呼んで確認（または次 Task と合わせて）
  - **Files**: `backend/Services/WebSourceDeployService.cs`
  - **Deps**: None

- [ ] **Task 2: `ExecuteAsync` の最終ステップを差し替えに変更**
  - **Description**: `ReplaceConnectionStrings(...)` 呼び出しを `ApplyPilotWebConfig(target.DestWebSourcePath, config.Name, _dryRun)` に置換。ログ文言を「パイロット用 web.config を適用」系に変更（件数置換ではなくファイル名を出す）。XML 置換メソッド群と `using System.Xml` を削除。クラス／メソッドコメントの「置換」表現を更新。
  - **Acceptance**:
    - [ ] 画像コピーの直後に差し替えが走る
    - [ ] 失敗時は既存 catch でターゲット失敗・以降スキップ
    - [ ] 置換メソッドがソース上に残っていない
  - **Verify**: `dotnet build`；DryRun 実行ログに新メッセージ
  - **Files**: `backend/Services/WebSourceDeployService.cs`
  - **Deps**: Task 1

### Phase 2: 設定・モデル削除

- [ ] **Task 3: `PilotConnectionStrings` モデル削除**
  - **Description**: `DbConfig.PilotConnectionStrings` と `PilotConnectionString` クラスを削除。他参照が無いことを Grep で確認。
  - **Acceptance**:
    - [ ] 型・プロパティがリポジトリから消えている
    - [ ] `dotnet build` 成功
  - **Verify**: `dotnet build`；`PilotConnectionString` の Grep ゼロ（docs の歴史記述を除く）
  - **Files**: `backend/Models/DbConfig.cs`
  - **Deps**: Task 2

- [ ] **Task 4: `appsettings_sample.json` からキー削除**
  - **Description**: kaios / gos の `PilotConnectionStrings` 配列を削除。JSON として valid であること。
  - **Acceptance**:
    - [ ] sample に `PilotConnectionStrings` が無い
  - **Verify**: JSON パース／アプリ起動時の設定エラーなし
  - **Files**: `backend/appsettings_sample.json`（他 appsettings にキーがあれば同様）
  - **Deps**: Task 3（順序は緩くても可。ビルド非依存）

### Phase 3: UI・ドキュメント

- [ ] **Task 5: `WebSourcePrepare.tsx` 説明文更新**
  - **Description**: 「接続文字列を書き換えます」を「パイロット用 Web.config（`Web.config.DC.{システム}.pilot`）を `web.config` として適用します」等に変更。パイロット用ファイルが残ることは UI に必須ではない。
  - **Acceptance**:
    - [ ] 旧「接続文字列」表現が説明文から消えている
  - **Verify**: 画面表示の目視
  - **Files**: `frontend/src/pages/WebSourcePrepare.tsx`
  - **Deps**: None（並行可）

- [ ] **Task 6: 関連ドキュメント更新**
  - **Description**:
    - `SPEC_ISSUE25_...`: §7 を「廃止。現行はファイル差し替え（`docs/pilot-webconfig-file-swap/SPEC.md`）」と注記
    - `issue27/SPEC.md`（および PLAN の該当行）: 「web.config 置換」→「パイロット用ファイル差し替え」
    - 本 Plan / Spec の Success Criteria チェックは実装完了時に更新
  - **Acceptance**:
    - [ ] 現行挙動を読んだ人が「部分置換」と誤解しない
  - **Verify**: ドキュメントの目視
  - **Files**: `docs/SPEC_ISSUE25_WEBSOURCE_PILOT_DEPLOY.md`, `docs/issue27/SPEC.md`, `docs/issue27/PLAN.md`（必要箇所のみ）
  - **Deps**: Task 2 完了後推奨

### Checkpoint: 完了条件（Spec Success Criteria 対応）

- [ ] 差し替えロジック動作・DryRun・欠落エラー
- [ ] モデル・sample から `PilotConnectionStrings` 削除
- [ ] FE・docs 整合
- [ ] `dotnet build` 成功
- [ ] `graphify update .`（コード変更後）

---

## Out of Scope

- パイロット用ファイル内容の検証（接続先が正しいか等）
- IIS アプリプール再起動
- バックアップ作成
- 自動テストプロジェクトの新設
- git commit / push

---

**Status**: Plan / Tasks 承認後、`/build auto` により **実装完了**（[`TASKS.md`](./TASKS.md)）。git commit / push は未実施。
