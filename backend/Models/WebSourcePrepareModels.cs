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
    public List<WebSourcePilotTargetInfo> PilotTargets { get; set; } = [];
}

public class WebSourceDeployRequest
{
    public string ExecutedBy { get; set; } = "";

    /// <summary>実行内容。"both"（既定）/ "web"（Webソースコピーのみ）/ "sql"（SQL適用のみ）。</summary>
    public string Step { get; set; } = "both";
}
