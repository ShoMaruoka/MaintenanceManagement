# SPEC: 実行履歴の機能強化（Issue #3）

## 1. Objective（目的）

実行履歴の展開行に、適用したモジュールの内訳（種別・モジュール名・操作区分・結果）を
テーブル形式で表示する。現在は「SP×3（新規1・更新2）」のような集計テキストしか見えず、
何のモジュールが適用されたか個別に確認できない。

---

## 2. 対象ユーザー

メンテナンス管理 Web アプリの全ユーザー（admin / user）

---

## 3. As Is / To Be

### As Is（現状）

**履歴一覧（折りたたみ状態）**
| 日時 | DB | モジュール | 実行者 | 結果 |
|------|----|-----------|--------|------|
| 06-29 10:00 | kaios | SP×3（新規1・更新2） | yamada | 成功 |

**展開時**
```
セッション詳細
3 モジュール実行
SP×3（新規1・更新2）
```
→ 個別モジュール名・結果が不明

### To Be（目標）

**履歴一覧（折りたたみ状態）** ← 変更なし
| 日時 | DB | モジュール | 実行者 | 結果 |
|------|----|-----------|--------|------|
| 06-29 10:00 | kaios | SP×3（新規1・更新2） | yamada | 成功 |

**展開時** ← ここを改修
```
セッション詳細  3 モジュール

  種別   | モジュール名                  | 区分 | 結果
---------|-------------------------------|------|-----
  SP     | dbo.SK0300アカウントSEL      | 更新 | 成功
  SP     | dbo.SK0410注文SEL            | 新規 | 成功
  Func   | dbo.FN_CalcPrice             | 削除 | 成功
```

---

## 4. Core Features（機能要件）

| ID | 要件 | 詳細 |
|----|------|------|
| F1 | 内訳テーブル表示 | 展開行にモジュール一覧をテーブル形式で表示する |
| F2 | 表示カラム | 種別・モジュール名・操作区分・結果の4カラム |
| F3 | 遅延ロード | 初回展開時のみ `GET /api/history/sessions/{id}` を呼び出し、以降はキャッシュを使用 |
| F4 | 結果バッジ | `success` → 緑「成功」、`failed` → 赤「失敗」、`skipped` → グレー「スキップ」 |
| F5 | 種別略称 | StoredProcedure→SP、Function→Func、VIEW→View、Table→Table、MariaDB→MariaDB |
| F6 | 一覧行は変更なし | 履歴一覧の「モジュール」列（集計テキスト）は現状維持 |

---

## 5. 受け入れ条件（Acceptance Criteria）

- [ ] 行クリックで展開すると、モジュール内訳テーブルが表示される
- [ ] テーブルに「種別」「モジュール名」「区分」「結果」の4カラムが存在する
- [ ] 2回目以降の展開では API を再呼び出しせずキャッシュを使う
- [ ] 失敗モジュールがある場合、「結果」列が赤い失敗バッジで表示される
- [ ] モジュールが0件のセッションでは「データなし」などの空状態メッセージを表示する
- [ ] 一覧行の「モジュール」列の集計テキストは変わらない

---

## 6. 技術スタック

| レイヤー | 技術 | 備考 |
|---------|------|------|
| フロントエンド | React 18 + TypeScript | 既存のまま |
| バックエンド | ASP.NET Core 8 Web API | 既存のまま |
| API | `GET /api/history/sessions/{id}` | 既存エンドポイントを利用 |

---

## 7. 実装スコープ（変更ファイル）

### フロントエンド（3ファイル）

| ファイル | 変更内容 |
|---------|---------|
| `frontend/src/types.ts` | `DeploySessionDetail` 型を追加。`DeploySession` に `details?: DeploySessionDetail[]` を追加 |
| `frontend/src/api/history.ts` | `formatSession` が `details` を含めて返すよう修正 |
| `frontend/src/pages/History.tsx` | 展開行にモジュール内訳テーブルを追加。`SessionWithDetail` の一時型を削除 |

### CSSの追加（1ファイル）

| ファイル | 変更内容 |
|---------|---------|
| `frontend/src/index.css` | `.log-detail-table` / `.log-detail-table td` スタイル追加（既存の `.log-session-detail` 内に配置） |

### バックエンド

変更なし（`GET /api/history/sessions/{id}` は既に `details` を含んだレスポンスを返している）

---

## 8. データフロー

```
[画面ロード時]
GET /api/history/sessions?limit=100
→ sessions[] (details は GROUP_CONCAT で含まれる)
→ formatSession() で modules 集計テキストと details[] を両方保持

[行クリック時]
- details がキャッシュ済み → そのまま展開表示
- 未キャッシュ → GET /api/history/sessions/{id} → details を追記して展開表示
```

> Note: `GetRecentSessions` の SQL クエリは GROUP_CONCAT で details を返しているが、
> Result が "success" ハードコードになっている。個別取得 (`GetSessionDetails`) は
> DB から正確な Result を取得するため、展開時は個別取得結果を優先する。

---

## 9. UI 詳細

### 展開行レイアウト

```
┌─ セッション詳細 ──────────── 3 モジュール ──────┐
│  種別   │ モジュール名                │ 区分 │ 結果 │
│---------|--------------------------|------|------|
│  SP     │ dbo.SK0300アカウントSEL   │ 更新 │ ✓成功│
│  SP     │ dbo.SK0410注文SEL         │ 新規 │ ✓成功│
│  Func   │ dbo.FN_CalcPrice          │ 削除 │ ✗失敗│
└──────────────────────────────────────────────────┘
```

### 結果バッジ仕様

| 値 | 表示 | 色 |
|---|------|-----|
| success | 成功 | 緑（#1a7a4a） |
| failed | 失敗 | 赤（#c5283d） |
| skipped | スキップ | グレー（#8a9099） |

---

## 10. Boundaries（制約）

### Always（必ず守る）
- 一覧行の「モジュール」列（集計テキスト）は変更しない
- キャッシュ済みの details がある場合は API を再呼び出ししない
- 既存の CSS クラス（`.log-session-detail`, `.log-detail-title`）を引き続き使用

### Never（やらない）
- バックエンド API の変更（既存エンドポイントで十分）
- ページネーションや検索フィルターの追加（本 issue のスコープ外）
- 本番前準備ログ（ProductionReadyLog）の詳細表示（別 issue で対応）
