# Jenkins + IIS + Kestrel デプロイガイド

開発環境から git push → Jenkins で自動ビルド → サーバーで自動起動する運用フロー。

---

## 全体アーキテクチャ

```
開発環境
  ↓ git push
GitHub / GitLab / オンプレ Git
  ↓ Webhook または Poll
Jenkins サーバー
  ├─ git pull
  ├─ npm run build
  ├─ dotnet publish -c Release
  └─ デプロイスクリプト実行
  ↓
Windows Server（アプリサーバー）
  ├─ IIS（ポート 80）→ リバースプロキシ
  └─ Kestrel（ポート 5254）→ バックエンド実行
```

---

## Phase 1: サーバー環境準備

### 1-1. Windows Server 環境要件

| 要件 | 用途 |
|------|------|
| .NET 8 Runtime | Kestrel ランタイム（SDK 不要） |
| IIS 10 | リバースプロキシ・静的ファイル配信 |
| Windows Server 2016 以上 | ホスト OS |

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

### 1-3. IIS サイト作成（ポート 80）

```powershell
New-Website -Name "MaintenanceManagement" `
    -PhysicalPath "D:\Apps\MaintenanceManagement\wwwroot" `
    -ApplicationPool "MaintenanceManagement" `
    -Port 80
```

### 1-4. IIS 認証設定

IIS マネージャー → **認証** で以下に設定:
- 匿名認証: **OFF**
- Windows 認証: **ON**

---

## Phase 2: IIS リバースプロキシ設定

### 2-1. URL Rewrite モジュールのインストール

IIS Manager で **役割と機能の追加** または:

```powershell
# Web Platform Installer から「URL Rewrite 2.1」をインストール
# または手動ダウンロード: https://www.iis.net/downloads/microsoft/url-rewrite
```

### 2-2. web.config にリバースプロキシルール追加

`D:\Apps\MaintenanceManagement\web.config` に以下を追加:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <!-- 静的ファイル配信 -->
      <staticContent>
        <mimeMap fileExtension=".js" mimeType="application/javascript" />
        <mimeMap fileExtension=".css" mimeType="text/css" />
        <mimeMap fileExtension=".json" mimeType="application/json" />
      </staticContent>

      <!-- URL Rewrite: /api/* → Kestrel へ -->
      <rewrite>
        <rules>
          <!-- API リクエストを Kestrel（ポート 5254）に転送 -->
          <rule name="ReverseProxyAPI" stopProcessing="true">
            <match url="^api/(.*)" />
            <action type="Rewrite" url="http://localhost:5254/api/{R:1}" />
            <serverVariables>
              <set name="HTTP_X_FORWARDED_FOR" value="{REMOTE_ADDR}" />
              <set name="HTTP_X_FORWARDED_PROTO" value="{SCHEME}" />
            </serverVariables>
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

      <!-- 認証設定 -->
      <security>
        <authentication>
          <anonymousAuthentication enabled="false" />
          <windowsAuthentication enabled="true" />
        </authentication>
      </security>
    </system.webServer>
  </location>
</configuration>
```

### 2-3. wwwroot にフロントエンドを配置

Jenkins デプロイ時に `frontend/dist` の内容を `wwwroot` にコピーされるので、ここでは手動で初期配置:

```powershell
# フォルダ構成
mkdir D:\Apps\MaintenanceManagement\wwwroot
mkdir D:\Apps\MaintenanceManagement\data
mkdir D:\Apps\MaintenanceManagement\logs
mkdir D:\Apps\MaintenanceManagement\backend
```

---

## Phase 3: Kestrel サーバー設定

### 3-1. appsettings.json を Kestrel 用に設定

`D:\Apps\MaintenanceManagement\backend\appsettings.json` を以下のように設定:

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
  "DatabasePath": "D:\\Apps\\MaintenanceManagement\\data\\maintenance.db",
  "DryRun": false,
  "FastCopyPath": "C:\\Program Files\\FastCopy\\FastCopy.exe",
  "DbConfigs": [
    {
      "Name": "kaios",
      "DevDb": "kaios_dev",
      "StgDb": "kaios",
      "DevConnectionString": "Server=<SQLServerホスト>;Database=kaios_dev;User Id=<ユーザー>;Password=<パスワード>;TrustServerCertificate=true;Connect Timeout=3;",
      "StgConnectionString": "Server=<SQLServerホスト>;Database=kaios;User Id=<ユーザー>;Password=<パスワード>;TrustServerCertificate=true;Connect Timeout=3;",
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

以下の PowerShell スクリプトで NSSM（Non-Sucking Service Manager）を使用してサービス化:

```powershell
# NSSM をダウンロード（https://nssm.cc/download）して C:\tools\nssm\ に配置

$nssm = "C:\tools\nssm\nssm.exe"
$backendPath = "D:\Apps\MaintenanceManagement\backend"
$dotnetExe = "dotnet"
$dllPath = "$backendPath\MaintenanceManagement.Api.dll"

# サービス削除（既存の場合）
& $nssm remove "MaintenanceManagement-Backend" confirm

# サービス作成
& $nssm install "MaintenanceManagement-Backend" $dotnetExe $dllPath
& $nssm set "MaintenanceManagement-Backend" AppDirectory $backendPath
& $nssm set "MaintenanceManagement-Backend" AppRotateFiles 1
& $nssm set "MaintenanceManagement-Backend" AppRotateOnline 1
& $nssm set "MaintenanceManagement-Backend" AppStdout "D:\Apps\MaintenanceManagement\logs\stdout.log"
& $nssm set "MaintenanceManagement-Backend" AppStderr "D:\Apps\MaintenanceManagement\logs\stderr.log"

# サービス開始
Start-Service "MaintenanceManagement-Backend"
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
dotnet publish -c Release -o D:\publish\MaintenanceManagement
```

#### ステップ 3: wwwroot にフロントエンドをコピー

```powershell
$source = "frontend\dist\*"
$dest = "D:\publish\MaintenanceManagement\wwwroot\"
Copy-Item -Path $source -Destination $dest -Recurse -Force
```

#### ステップ 4: サーバーへのデプロイ

```powershell
# リモートサーバーに SSH またはネットワークドライブ経由でコピー
# または、サーバー上の共有フォルダに直接コピー

$publishPath = "D:\publish\MaintenanceManagement\*"
$appPath = "D:\Apps\MaintenanceManagement\"

# ネットワークドライブの場合
Copy-Item -Path $publishPath -Destination $appPath -Recurse -Force

# または SSH の場合（plink を使用）
# plink.exe -ssh user@server "powershell -Command { ... }"
```

#### ステップ 5: Kestrel サービス再起動

```powershell
# リモートサーバー上で実行
# または Jenkins 実行ユーザーが Invoke-Command で リモート実行

Restart-Service "MaintenanceManagement-Backend" -Force
Start-Sleep -Seconds 3

# 起動確認
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
                    bat 'dotnet publish -c Release -o D:\\publish\\MaintenanceManagement'
                }
            }
        }

        stage('Deploy to Server') {
            steps {
                powershell '''
                    $src = "frontend\\dist\\*"
                    $dst = "D:\\publish\\MaintenanceManagement\\wwwroot\\"
                    Copy-Item -Path $src -Destination $dst -Recurse -Force
                    
                    $publishPath = "D:\\publish\\MaintenanceManagement\\*"
                    $appPath = "D:\\Apps\\MaintenanceManagement\\"
                    Copy-Item -Path $publishPath -Destination $appPath -Recurse -Force
                '''
            }
        }

        stage('Restart Service') {
            steps {
                powershell '''
                    Restart-Service "MaintenanceManagement-Backend" -Force
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
http://<サーバーIP>/
```

Windows 認証でログイン。

### 6-2. バックエンド確認

```
http://<サーバーIP>/api/modules?db=kaios
```

JSON が返ってきたら OK。

### 6-3. ログ確認

```powershell
# Kestrel stdout ログ
Get-Content "D:\Apps\MaintenanceManagement\logs\stdout.log" -Tail 50

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
cd D:\Apps\MaintenanceManagement\backend
dotnet MaintenanceManagement.Api.dll
```

### Jenkins から SSH/リモート実行する場合

Jenkins プラグイン **Publish Over SSH** をインストールして、リモートコマンド実行を設定:

```
SSH Server: <サーバーIP>
Remote directory: D:\Apps\MaintenanceManagement
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

- [ ] .NET 8 Runtime がサーバーにインストール済み
- [ ] IIS アプリプール・サイトを作成
- [ ] URL Rewrite モジュールをインストール
- [ ] `web.config` にリバースプロキシルール追加
- [ ] `appsettings.json` を Kestrel 用に編集
- [ ] Kestrel を Windows サービス（NSSM）で起動
- [ ] `D:\Apps\MaintenanceManagement\data/` フォルダを作成・権限設定
- [ ] Jenkins ジョブ / Jenkinsfile を作成
- [ ] ビルドステップ（npm build / dotnet publish）を設定
- [ ] デプロイステップ（ファイルコピー・サービス再起動）を設定
- [ ] ブラウザで動作確認（`http://<ホスト>/`）
- [ ] Kestrel ログ・Jenkins ビルドログを確認

---

**最終更新**: 2026-06-24  
**対象**: MaintenanceManagement + Jenkins + IIS + Kestrel
