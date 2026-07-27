using System.Text.Json.Serialization;

namespace MaintenanceManagement.Api.Models;

public class PrepareFileInfo
{
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";  // "deployed" | "hold"
    public string DbType { get; set; } = "";  // "sqlserver" | "mariadb"
}

/// <summary>
/// Table / UserDefinedTableType は自動デプロイしない運用のため、STG 適用時に
/// 手動適用待ちとして記録し、本番前準備で確認・消化する対象。
/// </summary>
public class ManualApplyItem
{
    /// <summary>Table | UserDefinedTableType</summary>
    public string ModuleType { get; set; } = "";
    public string ModuleName { get; set; } = "";
    /// <summary>新規 | 更新 | 削除</summary>
    public string OpType { get; set; } = "";
    /// <summary>STG 適用（Git マージ）を実行した日時（ローカル時刻の ISO 8601）。</summary>
    public string StgAppliedAt { get; set; } = "";
    public string StgAppliedBy { get; set; } = "";
    /// <summary>deployed_manual 配下の SQL ファイル名。Git に定義が無かった場合は空。</summary>
    public string FileName { get; set; } = "";

    /// <summary>選択・重複判定用のキー。</summary>
    [JsonIgnore]
    public string Key => $"{ModuleType}:{ModuleName}";
}

public class PrepareDbEntry
{
    public string DbName { get; set; } = "";
    public List<PrepareFileInfo> Files { get; set; } = [];
    /// <summary>Files 配下の相対パス（例: Images/flash/img/a.png）。無ければ空。</summary>
    public List<string> ImageFiles { get; set; } = [];
    /// <summary>手動適用待ちの Table / UserDefinedTableType。</summary>
    public List<ManualApplyItem> ManualItems { get; set; } = [];
}

public class PrepareRequest
{
    public string ExecutedBy { get; set; } = "";
    public List<PrepareSelection> Selections { get; set; } = [];
    public List<PrepareImageSelection> ImageSelections { get; set; } = [];
    public List<PrepareManualSelection> ManualSelections { get; set; } = [];
}

/// <summary>本番前準備で「本番へ手動適用したことを確認済み」にする Table / UDTT。</summary>
public class PrepareManualSelection
{
    public string DbName { get; set; } = "";
    public string ModuleType { get; set; } = "";
    public string ModuleName { get; set; } = "";
    /// <summary>true = 確認済みとして消化する。false = 次回まで持ち越す。</summary>
    public bool Apply { get; set; }
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
    /// <summary>手動適用（Table / UDTT）を確認済みとして消化した件数。</summary>
    public int ManualFiles { get; set; }
    public string Result { get; set; } = "";
    public string? LogDetail { get; set; }
}
