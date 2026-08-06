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
}
