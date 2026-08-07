namespace MaintenanceManagement.Api.Models;

public class ModuleInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ModifyDate { get; set; } = "";
    public bool GitOnly { get; set; }
    public bool IsDeleteCandidate { get; set; }
    /// <summary>
    /// 操作区分「新規」候補。
    /// 優先: STG DB にオブジェクトが無い（<see cref="DbConfig.StgConnectionString"/> 利用時）。
    /// フォールバック: Dev DB にはあるが対応する Git ファイルが無い（Git は STG 存在の代理指標）。
    /// 既定は false（＝更新扱い）。例外発生時は以降のモジュールが未判定のまま更新扱いで残る。
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
