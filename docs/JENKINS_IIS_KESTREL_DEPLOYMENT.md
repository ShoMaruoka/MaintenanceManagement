# Jenkins + IIS + Kestrel デプロイガイド

開発環境から git push → Jenkins で自動ビルド → サーバーで自動起動する運用フロー。

---

## 全体アーキテクチャ

```
開発環境
  ↓ git push
GitHub / GitLab / オンプレ Git
  ↓ Webhook または Poll SCM
Jenkins サーバー
  ├─ [ワークスペース] git pull（ソースコード管理）
  ├─ npm run build
  ├─ dotnet publish -c Release -o D:\publish\MaintenanceManagement
  └─ D:\Tools\MaintenanceManagement\ へコピー（成果物のみ）
       ↓
Windows Server（アプリサーバー）
  ├─ D:\Tools\MaintenanceManagement\wwwroot\  ← フロントエンド静的ファイル
  ├─ D:\Tools\MaintenanceManagement\backend\  ← .NET 成果物（DLL など）
  ├─ IIS（ポート 57010）→ wwwroot を配信 + /api/* を Kestrel へ転送
  └─ Kestrel（ポート 5254）→ backend\MaintenanceManagement.Api.dll を実行
```

---

## Phase 1: サーバー環境準備

### 1-1. Windows Server 環境要件

| 要件 | 用途 |
|------|------|
| .NET 8 **SDK** | `dotnet publish` によるビルド＋Kestrel 実行（Runtime を内包） |
| IIS 10 | リバースプロキシ・静的ファイル配信 |
| Windows Server 2016 以上 | ホスト OS |

> **注意**: Runtime のみではビルド（`dotnet publish`）ができません。Jenkins でビルドするサーバーには必ず **SDK** をインストールしてください。

### 1-2. IIS アプリケーションプール作成

IIS マネージャー、または PowerShell:

```powershell
Import-Module WebAdministration

# アプリプール作成（Kestrel バックアップ用）
New-WebAppPool -Name "MaintenanceManagement"
Set-ItemProperty IIS:\AppPools\MaintenanceManagement managedRuntimeVersion ""

# 実行ユーザー（例: ローカルシステム、または サービスアカウント）
Set-ItemProperty IIS:\AppPools\MaintenanceManagement -name processModel -value @{
    identitytype=0  # 0=LocalSystem, 3=SpecificUser
}
```

### 1-3. IIS サイト作成（ポート 57010）

```powershell
New-Website -Name "MaintenanceManagement" `
    -PhysicalPath "D:\Tools\MaintenanceManagement\wwwroot" `
    -ApplicationPool "MaintenanceManagement" `
    -Port 57010
```

### 1-4. IIS 認証設定

認証設定は **IIS マネージャーで直接** 行います（web.config への記述は不可）。

IIS マネージャー → サイト「MaintenanceManagement」選択 → **認証** で以下に設定:
- 匿名認証: **有効**
- Windows 認証: **無効**（表示されない場合はスキップ）

> **web.config に認証設定を書かない理由**: IIS はデフォルトで認証セクションをサーバーレベルでロックしています。web.config から設定しようとすると「この構成セクションをこのパスで使用できません」エラーが発生します。

---

## Phase 2: IIS リバースプロキシ設定

### 2-1. URL Rewrite モジュールのインストール

IIS Manager で **役割と機能の追加** または:

```powershell
# Web Platform Installer から「URL Rewrite 2.1」をインストール
# または手動ダウンロード: https://www.iis.net/downloads/microsoft/url-rewrite
```

### 2-2. web.config にリバースプロキシルール追加

`D:\Tools\MaintenanceManagement\web.config` に以下を追加:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <rewrite>
        <rules>
          <!-- API リクエストを Kestrel（ポート 5254）に転送 -->
          <rule name="ReverseProxyAPI" stopProcessing="true">
            <match url="^api/(.*)" />
            <action type="Rewrite" url="http://localhost:5254/api/{R:1}" />
          </rule>

          <!-- SPA フォールバック（静的ファイル以外は index.html へ） -->
          <rule name="SPA Fallback" stopProcessing="true">
            <match url=".*" />
            <conditions logicalGrouping="MatchAll">
              <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
              <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
              <add input="{REQUEST_URI}" pattern="^/api/" negate="true" />
            </conditions>
            <action type="Rewrite" url="/index.html" />
          </rule>
        </rules>
      </rewrite>
    </system.webServer>
  </location>
</configuration>
```

> **注意（mimeMap）**: `<staticContent>` に `.js` などを記述すると IIS サーバーレベルとの重複エラーが発生します。IIS がデフォルトで処理するため記述不要です。
> **注意（serverVariables）**: `HTTP_X_FORWARDED_FOR` など独自サーバー変数を使う場合は IIS の許可リストへの追加が別途必要なため、シンプルな構成では省略します。

> **注意**: `<security><authentication>` を web.config に記述すると「この構成セクションをこのパスで使用できません」エラーが発生します。IIS はデフォルトで認証セクションをサーバーレベルでロックしているため、web.config からは設定できません。認証は IIS マネージャーで直接設定してください（後述 1-4 参照）。

> **補足**: `dotnet publish` 実行時にバックエンド用の `web.config` が自動生成されます（`hostingModel="inprocess"` を含む）。このファイルは `D:\Tools\MaintenanceManagement\` 直下に配置されますが、IIS サイトの物理パスは `wwwroot\` を向いているため、上記の `wwwroot\web.config`（URL Rewrite ルール）とは別のファイルとして共存します。

### 2-3. デプロイ先フォルダの準備

`D:\Tools\MaintenanceManagement` は **コンパイル済み成果物の配置先** です。ソースコードは置きません。

**既に git clone している場合は事前に削除してください:**

```powershell
# 既存の git clone を削除
Remove-Item -Path "D:\Tools\MaintenanceManagement" -Recurse -Force
```

**フォルダを再作成:**

```powershell
mkdir D:\Tools\MaintenanceManagement\wwwroot
mkdir D:\Tools\MaintenanceManagement\data
mkdir D:\Tools\MaintenanceManagement\logs
mkdir D:\Tools\MaintenanceManagement\backend
```

> **フォルダ役割**:
> - `wwwroot\` → IIS が配信するフロントエンド静的ファイル（`frontend/dist` の内容）
> - `backend\` → Kestrel が実行する .NET 成果物（`dotnet publish` の出力）
> - `data\` → SQLite データベースファイル
> - `logs\` → Kestrel の stdout/stderr ログ

---

## Phase 3: Kestrel サーバー設定

### 3-1. appsettings.json を Kestrel 用に設定

`D:\Tools\MaintenanceManagement\backend\appsettings.json` を以下のように設定:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5254"
      }
    }
  },
  "AllowedHosts": "*",
  "AllowedOrigins": [],
  "DatabasePath": "D:\\Tools\\MaintenanceManagement\\data\\maintenance.db",
  "DryRun": false,
  "DbConfigs": [
    {
      "Name": "kaios",
      "DevDb": "kaios_dev",
      "StgDb": "kaios",
      "PrdDb": "",
      "DevConnectionString": "Server=<SQLServerホスト>;Database=kaios_dev;User Id=<ユーザー>;Password=<パスワード>;TrustServerCertificate=true;Connect Timeout=3;",
      "StgConnectionString": "Server=<SQLServerホスト>;Database=kaios;User Id=<ユーザー>;Password=<パスワード>;TrustServerCertificate=true;Connect Timeout=3;",
      "PrdConnectionString": "",
      "MariaDbConnectionString": "Server=<MariaDBホスト>;Port=3306;Database=kaios_dev;User Id=<ユーザー>;Password=<パスワード>;",
      "SourceControlPath": "D:\\Tools\\SourceControl",
      "GitRepoPath": "D:\\STGENV\\KaiosDB_rep",
      "DeployDev2StgPath": "D:\\Tools\\SourceControl\\Deploy_DEV2STG",
      "Deploy2PrdPath": "D:\\Tools\\UseAtProductionUpdate\\2_Deploy_STG2PRD"
    }
  ]
}
```

> **重要**: `AllowedOrigins` は空配列にする（IIS と同一オリジン）

### 3-2. Kestrel を Windows サービスとして起動（推奨）

> **前提**: Phase 4 の Jenkins デプロイを先に実行し、`D:\Tools\MaintenanceManagement\backend\MaintenanceManagement.Api.dll` が存在することを確認してから以下を実行してください。

以下の PowerShell スクリプトで NSSM（Non-Sucking Service Manager）を使用してサービス化:

```powershell
# NSSM をダウンロード（https://nssm.cc/download）して C:\tools\nssm\ に配置

$nssm = "C:\tools\nssm\nssm.exe"
$backendPath = "D:\Tools\MaintenanceManagement\backend"
$dotnetExe = "dotnet"
$dllPath = "$backendPath\MaintenanceManagement.Api.dll"

# サービス削除（既存の場合）
& $nssm remove "MaintenanceManagement-Backend" confirm

# サービス作成
& $nssm install "MaintenanceManagement-Backend" $dotnetExe $dllPath
& $nssm set "MaintenanceManagement-Backend" AppDirectory $backendPath
& $nssm set "MaintenanceManagement-Backend" AppRotateFiles 1
& $nssm set "MaintenanceManagement-Backend" AppRotateOnline 1
& $nssm set "MaintenanceManagement-Backend" AppStdout "D:\Tools\MaintenanceManagement\logs\stdout.log"
& $nssm set "MaintenanceManagement-Backend" AppStderr "D:\Tools\MaintenanceManagement\logs\stderr.log"

# サービス開始
Start-Service "MaintenanceManagement-Backend"

# Jenkins（SYSTEM アカウント）がサービスを制御できるよう権限を付与
sc.exe sdset "MaintenanceManagement-Backend" "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"
```

> **NSSM がない場合の代替**: Jenkins デプロイジョブで `Start-Job -FilePath ...` で Kestrel を起動することも可能（ただしサーバー再起動時の自動起動がない）

### 3-3. サービス起動確認

```powershell
Get-Service "MaintenanceManagement-Backend" | Select-Object Status, Name
# Status: Running
```

確認コマンド:

```powershell
# ポート 5254 でリッスン中か確認
netstat -ano | findstr :5254
```

---

## Phase 4: Jenkins パイプライン設定

### 4-1. Jenkins ジョブの作成

Jenkins で新規ジョブを作成:

1. **ジョブ種別**: フリースタイル（または Pipeline）
2. **ジョブ名**: `MaintenanceManagement-AutoDeploy`

### 4-2. ソース管理設定

**Git** セクション:
```
リポジトリ URL: <Git リポジトリ URL>
ブランチ: main
```

### 4-3. ビルド トリガー

以下のいずれか:

**オプション A: Push イベント時自動実行**
- Git リポジトリの Webhook を設定
- Jenkins URL: `http://<Jenkins-IP>:8080/github-webhook/`

**オプション B: 定期実行**
```
ビルド周期: H H * * *  # 毎日 1 回
```

### 4-4. ビルド ステップ

> **前提**: 各ステップは Jenkins のワークスペース（例: `D:\Jenkins\workspace\MaintenanceManagement-AutoDeploy\`）を起点として実行されます。4-2 の Git チェックアウトでリポジトリ全体がここに展開されるため、`cd frontend` や `cd backend` は相対パスで動作します。

#### ステップ 1: フロントエンドビルド

**実行シェル**（または PowerShell）:
```bash
cd frontend
npm install
npm run build
```

#### ステップ 2: バックエンドビルド・Publish

```bash
cd backend
dotnet publish -c Release -o D:\publish\MaintenanceManagement\backend
```

#### ステップ 3: Kestrel サービス停止

> DLL がサービスに掴まれているため、コピー前に必ずサービスを停止します。

```powershell
$service = Get-Service "MaintenanceManagement-Backend" -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq "Running") {
    Stop-Service "MaintenanceManagement-Backend" -Force
    Start-Sleep -Seconds 2
    Write-Host "✅ Service stopped"
}
```

#### ステップ 4: デプロイ先にコピー

> **注意**: vite のビルド出力先は `frontend/dist` ではなく `backend/wwwroot/` です。`dotnet publish` 実行時に wwwroot も一緒にパブリッシュされるため、フロントエンドの個別コピーは不要です。

```powershell
# バックエンド成果物（wwwroot 含む）をデプロイ先にコピー
$publishPath = "D:\publish\MaintenanceManagement\backend\*"
$appPath = "D:\Tools\MaintenanceManagement\backend\"
Copy-Item -Path $publishPath -Destination $appPath -Recurse -Force

# wwwroot を IIS 配信フォルダにもコピー
$wwwSrc = "D:\publish\MaintenanceManagement\backend\wwwroot\*"
$wwwDst = "D:\Tools\MaintenanceManagement\wwwroot\"
Copy-Item -Path $wwwSrc -Destination $wwwDst -Recurse -Force

# IIS URL Rewrite 用 web.config は frontend/public/web.config として
# リポジトリで管理され、vite ビルド → dotnet publish → コピーで自動配置される
```

#### ステップ 5: Kestrel サービス起動

> **初回のみ**: サービスは Phase 3-2 で NSSM を使って手動登録が必要です。登録後は Jenkins がサービスを起動します。

```powershell
Start-Service "MaintenanceManagement-Backend"
Start-Sleep -Seconds 3

$service = Get-Service "MaintenanceManagement-Backend" -ErrorAction SilentlyContinue
if ($service.Status -eq "Running") {
    Write-Host "✅ MaintenanceManagement-Backend is running"
} else {
    Write-Error "❌ Service failed to start"
    exit 1
}
```

#### ステップ 6: IIS キャッシュクリア（オプション）

```powershell
iisreset /restart
```

### 4-5. ビルド後の処理

**ビルドログ保存**（オプション）:
```
ビルドログを保存: D:\Jenkins\logs\MaintenanceManagement\
```

---

## Phase 5: デプロイスクリプト統合（Jenkins パイプライン版）

複雑な場合は `Jenkinsfile` を使用した Pipeline 構成:

```groovy
pipeline {
    agent any

    stages {
        stage('Checkout') {
            steps {
                git branch: 'main', url: '<Git-URL>'
            }
        }

        stage('Build Frontend') {
            steps {
                dir('frontend') {
                    bat 'npm install && npm run build'
                }
            }
        }

        stage('Build Backend') {
            steps {
                dir('backend') {
                    bat 'dotnet publish -c Release -o D:\\publish\\MaintenanceManagement\\backend'
                }
            }
        }

        stage('Stop Service') {
            steps {
                powershell '''
                    $service = Get-Service "MaintenanceManagement-Backend" -ErrorAction SilentlyContinue
                    if ($service -and $service.Status -eq "Running") {
                        Stop-Service "MaintenanceManagement-Backend" -Force
                        Start-Sleep -Seconds 2
                        Write-Host "✅ Service stopped"
                    }
                '''
            }
        }

        stage('Deploy to Server') {
            steps {
                powershell '''
                    # バックエンド成果物（wwwroot 含む）をデプロイ先にコピー
                    $publishPath = "D:\\publish\\MaintenanceManagement\\backend\\*"
                    $appPath = "D:\\Tools\\MaintenanceManagement\\backend\\"
                    Copy-Item -Path $publishPath -Destination $appPath -Recurse -Force

                    # wwwroot を IIS 配信フォルダにもコピー
                    $wwwSrc = "D:\\publish\\MaintenanceManagement\\backend\\wwwroot\\*"
                    $wwwDst = "D:\\Tools\\MaintenanceManagement\\wwwroot\\"
                    Copy-Item -Path $wwwSrc -Destination $wwwDst -Recurse -Force

                    # IIS URL Rewrite 用 web.config は frontend/public/web.config として
                    # リポジトリで管理され、vite ビルド → dotnet publish → コピーで自動配置される
                '''
            }
        }

        stage('Start Service') {
            steps {
                powershell '''
                    Start-Service "MaintenanceManagement-Backend"
                    Start-Sleep -Seconds 3

                    $service = Get-Service "MaintenanceManagement-Backend"
                    if ($service.Status -eq "Running") {
                        Write-Host "✅ Service is running"
                    } else {
                        exit 1
                    }
                '''
            }
        }
    }

    post {
        success {
            echo "✅ Deployment successful"
        }
        failure {
            echo "❌ Deployment failed"
        }
    }
}
```

この `Jenkinsfile` をリポジトリのルートに配置して、Jenkins で **Pipeline script from SCM** を選択。

---

## Phase 6: 動作確認

### 6-1. フロントエンド確認

ブラウザで:
```
http://<サーバーIP>:57010/
```

### 6-2. バックエンド確認

```
http://<サーバーIP>:57010/api/modules?db=kaios
```

JSON が返ってきたら OK。

### 6-3. ログ確認

```powershell
# Kestrel stdout ログ
Get-Content "D:\Tools\MaintenanceManagement\logs\stdout.log" -Tail 50

# Jenkins ビルドログ
Get-Content "D:\Jenkins\logs\MaintenanceManagement\build.log"
```

---

## Phase 7: トラブルシューティング

| 症状 | 確認ポイント |
|------|------------|
| IIS で 404 エラー | `web.config` の URL Rewrite ルールが正しいか。`wwwroot/index.html` が存在するか |
| Kestrel に接続できない | ポート 5254 でリッスン中か確認。ファイアウォール設定 |
| Jenkins ビルド失敗 | Node.js / .NET SDK のパスが環境変数に登録されているか確認 |
| サービス起動失敗 | `stdout.log` / `stderr.log` でエラー内容確認 |
| 権限エラー | Jenkins 実行ユーザー・Kestrel サービスユーザーが対象フォルダへのアクセス権を持つか確認 |

---

## トラブル対応例

### Kestrel が起動しない場合

```powershell
# マニュアル起動してエラーを確認
cd D:\Tools\MaintenanceManagement\backend
dotnet MaintenanceManagement.Api.dll
```

### Jenkins から SSH/リモート実行する場合

Jenkins プラグイン **Publish Over SSH** をインストールして、リモートコマンド実行を設定:

```
SSH Server: <サーバーIP>
Remote directory: D:\Tools\MaintenanceManagement
Command: 
  powershell -Command { 
    Restart-Service "MaintenanceManagement-Backend" -Force 
  }
```

---

## 運用フロー（日々の更新）

1. 開発環境で機能開発
2. `git push` → main ブランチへマージ
3. Jenkins 自動トリガー（Push Hook）
4. ビルド・デプロイ・サービス再起動が自動実行
5. サーバー上のアプリが更新される

**所要時間**: 約 3〜5 分（ビルド時間による）

---

## チェックリスト

- [ ] .NET 8 **SDK** がサーバーにインストール済み（`dotnet --version` で確認）
- [ ] IIS アプリプール・サイトを作成
- [ ] URL Rewrite モジュールをインストール
- [ ] `web.config` にリバースプロキシルール追加
- [ ] `appsettings.json` を Kestrel 用に編集
- [ ] Kestrel を Windows サービス（NSSM）で起動
- [ ] `D:\Tools\MaintenanceManagement\data` フォルダを作成・権限設定
- [ ] Jenkins ジョブ / Jenkinsfile を作成
- [ ] ビルドステップ（npm build / dotnet publish）を設定
- [ ] デプロイステップ（ファイルコピー・サービス再起動）を設定
- [ ] ブラウザで動作確認（`http://<ホスト>:57010/`）
- [ ] Kestrel ログ・Jenkins ビルドログを確認

---

**最終更新**: 2026-06-25  
**対象**: MaintenanceManagement + Jenkins + IIS + Kestrel
