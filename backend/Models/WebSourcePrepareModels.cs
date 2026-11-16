namespace MaintenanceManagement.Api.Models;

public class WebSourcePilotTargetInfo
{
    public string Name { get; set; } = "";
    public string DestWebSourcePath { get; set; } = "";
    /// <summary>共通画像のコピー先。未設定時は空文字。</summary>
    public string DestImagePath { get; set; } = "";
}

public class WebSourceInfoResponse
{
    public string DbName { get; set; } = "";
    public string WebSourcePath { get; set; } = "";
    /// <summary>STG 側の共通画像フォルダ。未設定時は空文字。</summary>
    public string CommonImagePath { get; set; } = "";
    /// <summary>STG 適用後の SQL Server deployed（Issue #35）。</summary>
    public string DeployedPath { get; set; } = "";
    /// <summary>STG 適用後の MariaDB deployed（Issue #35）。</summary>
    public string MariaDbDeployedPath { get; set; } = "";
    /// <summary>STG 側静的ファイル（DeployDev2StgPath\Files）（Issue #35）。</summary>
    public string FilesPath { get; set; } = "";
    /// <summary>Pilot SQL Server 適用ルート（未設定時は空）。</summary>
    public string PilotSqlDeployPath { get; set; } = "";
    /// <summary>Pilot MariaDB 適用ルート（未設定時は空。Issue #35 B1）。</summary>
    public string PilotMariaDbSqlDeployPath { get; set; } = "";
    public List<WebSourcePilotTargetInfo> PilotTargets { get; set; } = [];

    /// <summary>Webソースコピーで Web.config をコピーしないか（DB 設定。未設定時 true。pilot-webconfig-exclude）。</summary>
    public bool ExcludeWebConfig { get; set; } = true;
    /// <summary>除外設定の最終更新者（未設定時 null）。</summary>
    public string? ExcludeWebConfigUpdatedBy { get; set; }
    /// <summary>除外設定の最終更新日時（UTC ISO 8601。未設定時 null）。</summary>
    public string? ExcludeWebConfigUpdatedAt { get; set; }
}

/// <summary>PUT /api/web-source-prepare/{dbName}/settings のボディ（admin のみ）。</summary>
public class WebSourceSettingsRequest
{
    public bool ExcludeWebConfig { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
}

/// <summary>PUT settings の応答（保存後の値）。</summary>
public class WebSourceSettingsResponse
{
    public bool ExcludeWebConfig { get; set; }
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }
}

public class WebSourceDeployRequest
{
    public string ExecutedBy { get; set; } = "";

    /// <summary>実行内容。"both"（既定）/ "web"（Webソースコピーのみ）/ "sql"（SQL適用のみ）。</summary>
    public string Step { get; set; } = "both";
}
