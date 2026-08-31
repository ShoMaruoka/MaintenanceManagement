using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DatabaseServicePilotRunTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServicePilotRunTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pilot-run-test-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = _dbPath })
            .Build();
        _db = new DatabaseService(configuration);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void InsertRow(
        string runId,
        string dbName,
        string targetName,
        string mode,
        string executedBy,
        string result,
        DateTime executedAt,
        string? logDetail = null)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WebSourceDeployLog (RunId, DbName, TargetName, Mode, ExecutedBy, ExecutedAt, Result, LogDetail)
            VALUES ($runId, $dbName, $targetName, $mode, $executedBy, $executedAt, $result, $logDetail);
            """;
        cmd.Parameters.AddWithValue("$runId", runId);
        cmd.Parameters.AddWithValue("$dbName", dbName);
        cmd.Parameters.AddWithValue("$targetName", targetName);
        cmd.Parameters.AddWithValue("$mode", mode);
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", executedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$result", result);
        cmd.Parameters.AddWithValue("$logDetail", logDetail ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void GetRecentPilotRuns_AggregatesByRunId_WithoutLogDetail()
    {
        var older = Guid.NewGuid().ToString("n");
        var newer = Guid.NewGuid().ToString("n");
        var t0 = DateTime.UtcNow.AddHours(-2);
        var t1 = DateTime.UtcNow.AddHours(-1);

        InsertRow(older, "kaios", "pilot1", "both", "alice", "success", t0, "OLD LOG");
        InsertRow(older, "kaios", "pilot2", "both", "alice", "failed", t0.AddSeconds(1), "OLD LOG");
        InsertRow(newer, "gos", "pilot1", "web", "bob", "success", t1, "NEW LOG");

        var runs = _db.GetRecentPilotRuns(100);

        Assert.Equal(2, runs.Count);
        Assert.Equal(newer, runs[0].RunId);
        Assert.Equal("gos", runs[0].DbName);
        Assert.Equal("bob", runs[0].ExecutedBy);
        Assert.Equal("success", runs[0].Result);
        Assert.Equal("Webのみ", runs[0].StepLabel);
        Assert.Contains("pilot1✓", runs[0].Summary, StringComparison.Ordinal);

        Assert.Equal(older, runs[1].RunId);
        Assert.Equal("failed", runs[1].Result);
        Assert.Equal("両方", runs[1].StepLabel);
        Assert.Contains("pilot1✓", runs[1].Summary, StringComparison.Ordinal);
        Assert.Contains("pilot2✗", runs[1].Summary, StringComparison.Ordinal);

        Assert.All(runs, r => Assert.Null(r.GetType().GetProperty("LogDetail")));
    }

    [Fact]
    public void GetPilotRunById_ReturnsTargetsAndLogDetail()
    {
        var runId = Guid.NewGuid().ToString("n");
        var at = DateTime.UtcNow.AddMinutes(-5);
        InsertRow(runId, "kaios", "pilot1", "both", "alice", "success", at, "full log");
        InsertRow(runId, "kaios", "sql", "sql-skipped", "alice", "success", at.AddSeconds(1), "full log");

        var detail = _db.GetPilotRunById(runId);

        Assert.NotNull(detail);
        Assert.Equal(runId, detail.RunId);
        Assert.Equal("両方", detail.StepLabel);
        Assert.Equal("full log", detail.LogDetail);
        Assert.Equal(2, detail.Targets.Count);
        Assert.Contains(detail.Targets, t => t.TargetName == "pilot1" && t.Result == "success" && t.Mode == "both");
        Assert.Contains(detail.Targets, t => t.TargetName == "sql" && t.Mode == "sql-skipped");
        Assert.Contains("SQL–", detail.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPilotRunById_ReturnsNull_WhenMissing()
    {
        Assert.Null(_db.GetPilotRunById("does-not-exist"));
    }

    [Fact]
    public void GetRecentPilotRuns_RespectsLimitAsRunCount()
    {
        for (var i = 0; i < 3; i++)
        {
            var runId = $"run{i}";
            var at = DateTime.UtcNow.AddMinutes(-i);
            InsertRow(runId, "kaios", "pilot1", "web", "alice", "success", at);
            InsertRow(runId, "kaios", "pilot2", "web", "alice", "success", at.AddSeconds(1));
        }

        var runs = _db.GetRecentPilotRuns(2);
        Assert.Equal(2, runs.Count);
    }
}
