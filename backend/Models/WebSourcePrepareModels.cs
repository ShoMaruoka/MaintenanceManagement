namespace MaintenanceManagement.Api.Models;

public class WebSourcePilotTargetInfo
{
    public string Name { get; set; } = "";
    public string DestWebSourcePath { get; set; } = "";
}

public class WebSourceInfoResponse
{
    public string DbName { get; set; } = "";
    public string WebSourcePath { get; set; } = "";
    public List<WebSourcePilotTargetInfo> PilotTargets { get; set; } = [];
}

public class WebSourceDeployRequest
{
    public string ExecutedBy { get; set; } = "";

    /// <summary>実行内容。"both"（既定）/ "web"（Webソースコピーのみ）/ "sql"（SQL適用のみ）。</summary>
    public string Step { get; set; } = "both";
}
