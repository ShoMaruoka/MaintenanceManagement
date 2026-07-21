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
    /// <summary>Files 配下の相対パス（例: Images/flash/img/a.png）。無ければ空。</summary>
    public List<string> ImageFiles { get; set; } = [];
}

public class PrepareRequest
{
    public string ExecutedBy { get; set; } = "";
    public List<PrepareSelection> Selections { get; set; } = [];
    public List<PrepareImageSelection> ImageSelections { get; set; } = [];
}

public class PrepareSelection
{
    public string DbName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public string DbType { get; set; } = "";
    public bool Apply { get; set; }
}

/// <summary>本番前準備で移動する画像・静的ファイル（Files 相対パス）。</summary>
public class PrepareImageSelection
{
    public string DbName { get; set; } = "";
    /// <summary>例: Images/flash/img/a.png</summary>
    public string RelativePath { get; set; } = "";
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
