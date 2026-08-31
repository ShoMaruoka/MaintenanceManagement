namespace MaintenanceManagement.Api.Controllers;

/// <summary>WebSourceDeployLog.Mode 集合から実行履歴用の stepLabel / summary を決める。</summary>
public static class PilotRunStepLabel
{
    public static string FromModes(IEnumerable<string> modes)
    {
        var list = modes
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        if (list.Count == 0)
            return "両方";

        var allDryRun = list.All(IsDryRun);
        var label = list.All(IsSqlFamily) ? "SQLのみ"
            : list.All(IsWebFamily) ? "Webのみ"
            : "両方";

        return allDryRun ? label + "（DryRun）" : label;
    }

    public static string BuildSummary(IEnumerable<(string TargetName, string Result, string Mode)> rows) =>
        string.Join("  ", rows.Select(r =>
        {
            var name = r.TargetName.Equals("sql", StringComparison.OrdinalIgnoreCase) ? "SQL" : r.TargetName;
            var mark = r.Mode.Equals("sql-skipped", StringComparison.Ordinal)
                ? "–"
                : r.Result.Equals("success", StringComparison.Ordinal) ? "✓" : "✗";
            return name + mark;
        }));

    private static bool IsDryRun(string mode) =>
        mode.EndsWith("-dryrun", StringComparison.Ordinal);

    private static bool IsSqlFamily(string mode) =>
        mode is "sql" or "sql-dryrun" or "sql-skipped";

    private static bool IsWebFamily(string mode) =>
        mode is "web" or "web-dryrun";
}
