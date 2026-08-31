namespace MaintenanceManagement.Api.Models;

/// <summary>実行履歴一覧用。同一 RunId を1件に束ねた Pilot 適用。</summary>
public class PilotRunSummary
{
    public string RunId { get; set; } = "";
    public string DbName { get; set; } = "";
    public string ExecutedAt { get; set; } = "";
    public string ExecutedBy { get; set; } = "";
    public string StepLabel { get; set; } = "";
    public string Result { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// <summary>実行履歴詳細。一覧項目にターゲット行とログ全文を足す。</summary>
public class PilotRunDetail : PilotRunSummary
{
    public List<PilotRunTarget> Targets { get; set; } = [];
    public string? LogDetail { get; set; }
}

public class PilotRunTarget
{
    public string TargetName { get; set; } = "";
    public string Result { get; set; } = "";
    public string Mode { get; set; } = "";
}
