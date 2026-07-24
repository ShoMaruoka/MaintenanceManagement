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
    /// <summary>"mirror"（差分ミラー、削除同期あり） | "full"（全量コピー、削除同期なし）</summary>
    public string Mode { get; set; } = "mirror";
    public string ExecutedBy { get; set; } = "";
}
