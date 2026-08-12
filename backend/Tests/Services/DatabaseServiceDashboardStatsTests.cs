using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// GetDashboardStats（ダッシュボードのサマリーカード集計）の振る舞いを、
/// 一時 SQLite ファイル上の実データで固定する。
/// </summary>
public class DatabaseServiceDashboardStatsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceDashboardStatsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stats-test-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = _dbPath })
            .Build();
        _db = new DatabaseService(configuration);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { /* 一時ファイルの掃除失敗はテスト結果に影響させない */ }
        GC.SuppressFinalize(this);
    }

    private long Session(string dbName, string status, string? executedBy = null)
    {
        var sessionId = _db.InsertDeploySession(dbName, executedBy ?? "tester");
        if (status != "running") _db.UpdateDeploySessionStatus(sessionId, status);
        return sessionId;
    }

    /// <summary>集計期間の境界を検証するため、記録済みセッションの日時を過去にずらす。</summary>
    private void BackdateSession(long sessionId, int daysAgo)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE DeploySession SET ExecutedAt = $at WHERE SessionId = $id;";
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.AddDays(-daysAgo).ToString("o"));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void GetDashboardStats_ReturnsZeros_WhenNoData()
    {
        var stats = _db.GetDashboardStats();

        Assert.Equal(30, stats.Days);
        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.SuccessSessions);
        Assert.Equal(0, stats.RunningCount);
        Assert.Null(stats.LastPrepare);
        Assert.Null(stats.RunningDbName);
    }

    [Fact]
    public void GetDashboardStats_CountsSuccessAndFailed_ExcludingRunning()
    {
        Session("kaios", "success");
        Session("kaios", "success");
        Session("gos", "failed");
        Session("paf", "running");

        var stats = _db.GetDashboardStats();

        Assert.Equal(3, stats.TotalSessions);
        Assert.Equal(2, stats.SuccessSessions);
    }

    [Fact]
    public void GetDashboardStats_ExcludesSessionsOlderThanTheWindow()
    {
        Session("kaios", "success");
        var old = Session("gos", "failed");
        BackdateSession(old, 31);

        var stats = _db.GetDashboardStats(30);

        Assert.Equal(1, stats.TotalSessions);
        Assert.Equal(1, stats.SuccessSessions);
    }

    [Fact]
    public void GetDashboardStats_CountsRunningSessions_RegardlessOfAge()
    {
        var old = Session("kaios", "running");
        BackdateSession(old, 90);
        Session("gos", "running");

        var stats = _db.GetDashboardStats(30);

        Assert.Equal(2, stats.RunningCount);
        // 最新（SessionId 最大）の実行中セッションをサブテキスト用に返す
        Assert.Equal("gos", stats.RunningDbName);
        Assert.Equal("tester", stats.RunningExecutedBy);
    }

    [Fact]
    public void GetDashboardStats_ReturnsLatestPrepareLog()
    {
        _db.InsertProductionReadyLog("alice", 1, 0, 0, "failed", "old");
        _db.InsertProductionReadyLog("bob", 12, 3, 2, "success", "new");

        var stats = _db.GetDashboardStats();

        Assert.NotNull(stats.LastPrepare);
        Assert.Equal("bob", stats.LastPrepare!.ExecutedBy);
        Assert.Equal("success", stats.LastPrepare.Result);
        Assert.Equal(12, stats.LastPrepare.AppliedFiles);
        Assert.Equal(3, stats.LastPrepare.HeldFiles);
        Assert.Equal(2, stats.LastPrepare.ManualFiles);
    }

    private void InsertPilotLog(
        string runId,
        string dbName,
        string targetName,
        string mode,
        string executedBy,
        string result,
        DateTime? executedAt = null)
    {
        var at = (executedAt ?? DateTime.UtcNow).ToString("o");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WebSourceDeployLog (RunId, DbName, TargetName, Mode, ExecutedBy, ExecutedAt, Result, LogDetail)
            VALUES ($runId, $dbName, $targetName, $mode, $executedBy, $executedAt, $result, NULL);
            """;
        cmd.Parameters.AddWithValue("$runId", runId);
        cmd.Parameters.AddWithValue("$dbName", dbName);
        cmd.Parameters.AddWithValue("$targetName", targetName);
        cmd.Parameters.AddWithValue("$mode", mode);
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", at);
        cmd.Parameters.AddWithValue("$result", result);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void GetDashboardStats_ReturnsNullPilot_WhenNoHistory()
    {
        var stats = _db.GetDashboardStats();
        Assert.Null(stats.LastPilotKaios);
        Assert.Null(stats.LastPilotGos);
    }

    [Fact]
    public void GetDashboardStats_AdoptsSuccessfulPilotRun()
    {
        var runId = Guid.NewGuid().ToString("n");
        var at = DateTime.UtcNow.AddHours(-1);
        InsertPilotLog(runId, "kaios", "pilot1", "both", "alice", "success", at);
        InsertPilotLog(runId, "kaios", "pilot2", "both", "alice", "success", at);
        InsertPilotLog(runId, "kaios", "sql", "sql", "alice", "success", at);

        var stats = _db.GetDashboardStats();

        Assert.NotNull(stats.LastPilotKaios);
        Assert.Equal("kaios", stats.LastPilotKaios!.DbName);
        Assert.Equal("alice", stats.LastPilotKaios.ExecutedBy);
        Assert.Equal(at.ToString("o"), stats.LastPilotKaios.ExecutedAt);
        Assert.Null(stats.LastPilotGos);
    }

    [Fact]
    public void GetDashboardStats_ExcludesRunWithAnyFailure()
    {
        var runId = Guid.NewGuid().ToString("n");
        InsertPilotLog(runId, "kaios", "pilot1", "both", "bob", "success");
        InsertPilotLog(runId, "kaios", "pilot2", "both", "bob", "failed");

        var stats = _db.GetDashboardStats();
        Assert.Null(stats.LastPilotKaios);
    }

    [Fact]
    public void GetDashboardStats_ExcludesAllDryRunOrSqlSkippedOnly()
    {
        var dryRun = Guid.NewGuid().ToString("n");
        InsertPilotLog(dryRun, "kaios", "pilot1", "both-dryrun", "c", "success");
        InsertPilotLog(dryRun, "kaios", "sql", "sql-dryrun", "c", "success");

        var skipOnly = Guid.NewGuid().ToString("n");
        InsertPilotLog(skipOnly, "gos", "sql", "sql-skipped", "d", "success");

        var stats = _db.GetDashboardStats();
        Assert.Null(stats.LastPilotKaios);
        Assert.Null(stats.LastPilotGos);
    }

    [Fact]
    public void GetDashboardStats_AdoptsBothWithWebSuccessAndSqlSkipped()
    {
        // A2: both + Web 成功 + sql-skipped は最終に採用
        var runId = Guid.NewGuid().ToString("n");
        var at = DateTime.UtcNow.AddMinutes(-30);
        InsertPilotLog(runId, "kaios", "pilot1", "both", "eve", "success", at);
        InsertPilotLog(runId, "kaios", "pilot2", "both", "eve", "success", at);
        InsertPilotLog(runId, "kaios", "sql", "sql-skipped", "eve", "success", at);

        var stats = _db.GetDashboardStats();

        Assert.NotNull(stats.LastPilotKaios);
        Assert.Equal("eve", stats.LastPilotKaios!.ExecutedBy);
    }

    [Fact]
    public void GetDashboardStats_PilotIsIndependentPerDb_AndUsesLatest()
    {
        var oldKaios = Guid.NewGuid().ToString("n");
        InsertPilotLog(oldKaios, "kaios", "pilot1", "web", "old", "success", DateTime.UtcNow.AddDays(-2));

        var newKaios = Guid.NewGuid().ToString("n");
        InsertPilotLog(newKaios, "kaios", "pilot1", "web", "new", "success", DateTime.UtcNow.AddHours(-1));

        var gos = Guid.NewGuid().ToString("n");
        InsertPilotLog(gos, "gos", "sql", "sql", "gos-user", "success", DateTime.UtcNow.AddHours(-3));

        var stats = _db.GetDashboardStats();

        Assert.Equal("new", stats.LastPilotKaios!.ExecutedBy);
        Assert.Equal("gos-user", stats.LastPilotGos!.ExecutedBy);
    }

    [Fact]
    public void GetDashboardStats_ExecutedBy_ComesFromLatestExecutedAtRow_NotLexicalMax()
    {
        // 同一 Run 内で ExecutedBy が異なっても、辞書順 MAX（zzz）ではなく最新 ExecutedAt 行の実行者を返す。
        var runId = Guid.NewGuid().ToString("n");
        var older = DateTime.UtcNow.AddMinutes(-20);
        var newer = DateTime.UtcNow.AddMinutes(-5);
        InsertPilotLog(runId, "kaios", "pilot1", "both", "zzz", "success", older);
        InsertPilotLog(runId, "kaios", "pilot2", "both", "zzz", "success", older);
        InsertPilotLog(runId, "kaios", "sql", "sql", "latest-user", "success", newer);

        var stats = _db.GetDashboardStats();

        Assert.NotNull(stats.LastPilotKaios);
        Assert.Equal("latest-user", stats.LastPilotKaios!.ExecutedBy);
    }
}
