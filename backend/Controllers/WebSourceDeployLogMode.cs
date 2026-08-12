using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

/// <summary>WebSourceDeployLog.Mode の行種別（SPEC 2.1）。</summary>
public enum WebSourceLogRowKind
{
    Web,
    Sql,
    Exception,
}

/// <summary>
/// Mode 文字列決定の純関数（Issue #35 / D4）。
/// Controller が InsertWebSourceDeployLog に渡す値だけを返す。
/// </summary>
public static class WebSourceDeployLogMode
{
    /// <summary>
    /// SPEC 2.1 の行別対応表どおりに Mode を返す。
    /// DryRun と空スキップが同時のときは DryRun 優先（E2 → sql-dryrun）。
    /// </summary>
    public static string ResolveLogMode(
        WebSourceDeployStep step,
        bool dryRun,
        bool skipped,
        WebSourceLogRowKind rowKind)
    {
        return rowKind switch
        {
            WebSourceLogRowKind.Web => ResolveWebMode(step, dryRun),
            WebSourceLogRowKind.Sql => ResolveSqlMode(dryRun, skipped),
            WebSourceLogRowKind.Exception => StepToMode(step),
            _ => StepToMode(step),
        };
    }

    private static string ResolveWebMode(WebSourceDeployStep step, bool dryRun) => step switch
    {
        WebSourceDeployStep.WebOnly => dryRun ? "web-dryrun" : "web",
        // Both（および想定外の SqlOnly で Web 行を書く場合）は both 系
        _ => dryRun ? "both-dryrun" : "both",
    };

    private static string ResolveSqlMode(bool dryRun, bool skipped)
    {
        // E2: DryRun ＋ 空スキップ同時 → DryRun 優先
        if (dryRun) return "sql-dryrun";
        if (skipped) return "sql-skipped";
        return "sql";
    }

    private static string StepToMode(WebSourceDeployStep step) => step switch
    {
        WebSourceDeployStep.WebOnly => "web",
        WebSourceDeployStep.SqlOnly => "sql",
        _ => "both",
    };
}
