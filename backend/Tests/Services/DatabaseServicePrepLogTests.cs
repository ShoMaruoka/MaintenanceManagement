using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// ProductionReadyLog の一覧／詳細取得（GetRecentPrepLogs / GetPrepLogById）の振る舞いを固定する。
/// </summary>
public class DatabaseServicePrepLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServicePrepLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prep-log-test-{Guid.NewGuid():N}.db");
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

    [Fact]
    public void GetRecentPrepLogs_OmitsLogDetail()
    {
        _db.InsertProductionReadyLog("alice", 3, 1, 0, "success", "FULL LOG BODY");

        var logs = _db.GetRecentPrepLogs(20);

        Assert.Single(logs);
        Assert.Equal("alice", logs[0].ExecutedBy);
        Assert.Equal(3, logs[0].AppliedFiles);
        Assert.Null(logs[0].LogDetail);
    }

    [Fact]
    public void GetPrepLogById_ReturnsLogDetail()
    {
        var logId = _db.InsertProductionReadyLog("bob", 5, 2, 1, "success", "DETAIL LINE 1\nDETAIL LINE 2");

        var log = _db.GetPrepLogById(logId);

        Assert.NotNull(log);
        Assert.Equal(logId, log!.LogId);
        Assert.Equal("bob", log.ExecutedBy);
        Assert.Equal(5, log.AppliedFiles);
        Assert.Equal(2, log.HeldFiles);
        Assert.Equal(1, log.ManualFiles);
        Assert.Equal("DETAIL LINE 1\nDETAIL LINE 2", log.LogDetail);
    }

    [Fact]
    public void GetPrepLogById_ReturnsNull_WhenMissing()
    {
        Assert.Null(_db.GetPrepLogById(99999));
    }

    [Fact]
    public void GetPrepLogById_AllowsNullLogDetail()
    {
        var logId = _db.InsertProductionReadyLog("carol", 1, 0, 0, "success", null);

        var log = _db.GetPrepLogById(logId);

        Assert.NotNull(log);
        Assert.Null(log!.LogDetail);
    }

    [Fact]
    public void GetRecentPrepLogs_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
            _db.InsertProductionReadyLog($"user{i}", i, 0, 0, "success", $"log{i}");

        var logs = _db.GetRecentPrepLogs(2);

        Assert.Equal(2, logs.Count);
        Assert.Equal("user4", logs[0].ExecutedBy);
        Assert.Equal("user3", logs[1].ExecutedBy);
    }
}
