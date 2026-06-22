namespace MaintenanceManagement.Api.Models;

public class DeployRequest
{
    public string DbName { get; set; } = "";
    public List<DeployModule> Modules { get; set; } = [];
}

public class DeployModule
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string OpType { get; set; } = "";
}

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Step { get; set; }
}

public class DeploySession
{
    public long SessionId { get; set; }
    public string DbName { get; set; } = "";
    public string ExecutedBy { get; set; } = "";
    public string ExecutedAt { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<DeploySessionDetail> Details { get; set; } = [];
}

public class DeploySessionDetail
{
    public long DetailId { get; set; }
    public long SessionId { get; set; }
    public string OpType { get; set; } = "";
    public string ModuleType { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string Result { get; set; } = "";
}
