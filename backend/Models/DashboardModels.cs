namespace MaintenanceManagement.Api.Models;

/// <summary>Pilot 最終適用（成功 Run）の要約。kaios / gos 用。</summary>
public class PilotDeploySummary
{
    /// <summary>対象 DB 名（kaios | gos）。</summary>
    public string DbName { get; set; } = "";

    /// <summary>最終成功 Run の実行日時（ISO 8601）。</summary>
    public string ExecutedAt { get; set; } = "";

    /// <summary>最終成功 Run の実行者。</summary>
    public string ExecutedBy { get; set; } = "";
}

/// <summary>ダッシュボード上部のサマリーカード用の集計値。</summary>
public class DashboardStats
{
    /// <summary>直近の本番前準備ログ。1件も無い場合は null。</summary>
    public ProductionReadyLog? LastPrepare { get; set; }

    /// <summary>kaios の Pilot 最終成功。履歴が無ければ null（集計は T5b）。</summary>
    public PilotDeploySummary? LastPilotKaios { get; set; }

    /// <summary>gos の Pilot 最終成功。履歴が無ければ null（集計は T5b）。</summary>
    public PilotDeploySummary? LastPilotGos { get; set; }

    /// <summary>成功率の集計対象期間（日数）。</summary>
    public int Days { get; set; }

    /// <summary>集計期間内で完了した（running を除く）セッション数。</summary>
    public int TotalSessions { get; set; }

    /// <summary>集計期間内で成功したセッション数。</summary>
    public int SuccessSessions { get; set; }

    /// <summary>実行中（Status = 'running'）のセッション数。期間で絞らない。</summary>
    public int RunningCount { get; set; }

    /// <summary>最新の実行中セッションの対象 DB。実行中が無ければ null。</summary>
    public string? RunningDbName { get; set; }

    /// <summary>最新の実行中セッションの実行者。実行中が無ければ null。</summary>
    public string? RunningExecutedBy { get; set; }
}
