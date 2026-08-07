namespace MaintenanceManagement.Api.Models;

public class ModuleInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ModifyDate { get; set; } = "";
    public bool GitOnly { get; set; }
    public bool IsDeleteCandidate { get; set; }
    /// <summary>
    /// DBに存在するが対応するGitファイルが無い場合 true（操作区分「新規」）。
    /// 既定は false（＝更新扱い）。
    /// </summary>
    public bool IsNewCandidate { get; set; }
}

public class ModuleListResponse
{
    public string DbName { get; set; } = "";
    public List<ModuleInfo> StoredProcedures { get; set; } = [];
    public List<ModuleInfo> Functions { get; set; } = [];
    public List<ModuleInfo> Views { get; set; } = [];
    public List<ModuleInfo> Tables { get; set; } = [];
    public List<ModuleInfo> UserDefinedTableTypes { get; set; } = [];
    public List<ModuleInfo> MariaDb { get; set; } = [];
    public List<ModuleInfo> MariaDbFunctions { get; set; } = [];
    public List<ModuleInfo> MariaDbTables { get; set; } = [];
}
