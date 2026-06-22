namespace MaintenanceManagement.Api.Models;

public class PrepareFileInfo
{
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";  // "deployed" | "hold"
    public string DbType { get; set; } = "";  // "sqlserver" | "mariadb"
}

public class PrepareDbEntry
{
    public string DbName { get; set; } = "";
    public List<PrepareFileInfo> Files { get; set; } = [];
}

public class PrepareRequest
{
    public List<PrepareSelection> Selections { get; set; } = [];
}

public class PrepareSelection
{
    public string DbName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public string DbType { get; set; } = "";
    public bool Apply { get; set; }
}

public class ProductionReadyLog
{
    public long LogId { get; set; }
    public string ExecutedBy { get; set; } = "";
    public string ExecutedAt { get; set; } = "";
    public int AppliedFiles { get; set; }
    public int HeldFiles { get; set; }
    public string Result { get; set; } = "";
    public string? LogDetail { get; set; }
}
