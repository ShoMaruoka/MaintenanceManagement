using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>pilot-webconfig-exclude T2: AppSetting テーブル・Pilot の Web.config 除外設定・admin 判定。</summary>
public class DatabaseServiceAppSettingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceAppSettingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"app-setting-test-{Guid.NewGuid():N}.db");
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
    public void GetSetting_Missing_ReturnsNull()
    {
        Assert.Null(_db.GetSetting("NoSuchKey"));
    }

    [Fact]
    public void UpsertSetting_ThenGet_ReturnsValueAndUpdater()
    {
        _db.UpsertSetting("K", "v1", "alice");

        var s = _db.GetSetting("K");

        Assert.NotNull(s);
        Assert.Equal("v1", s!.Value);
        Assert.Equal("alice", s.UpdatedBy);
        Assert.False(string.IsNullOrEmpty(s.UpdatedAt));
    }

    [Fact]
    public void UpsertSetting_Twice_Overwrites()
    {
        _db.UpsertSetting("K", "v1", "alice");
        var first = _db.GetSetting("K")!;

        _db.UpsertSetting("K", "v2", "bob");
        var second = _db.GetSetting("K")!;

        Assert.Equal("v2", second.Value);
        Assert.Equal("bob", second.UpdatedBy);
        Assert.True(string.CompareOrdinal(second.UpdatedAt, first.UpdatedAt) >= 0);
    }

    [Fact]
    public void GetPilotExcludeWebConfig_NotSet_DefaultsToTrueWithNullUpdater()
    {
        var s = _db.GetPilotExcludeWebConfig("kaios");

        Assert.True(s.ExcludeWebConfig);
        Assert.Null(s.UpdatedBy);
        Assert.Null(s.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetPilotExcludeWebConfig_RoundTrips(bool value)
    {
        _db.SetPilotExcludeWebConfig("kaios", value, "admin1");

        var s = _db.GetPilotExcludeWebConfig("kaios");

        Assert.Equal(value, s.ExcludeWebConfig);
        Assert.Equal("admin1", s.UpdatedBy);
        Assert.NotNull(s.UpdatedAt);
    }

    [Fact]
    public void SetPilotExcludeWebConfig_IsPerSystem()
    {
        _db.SetPilotExcludeWebConfig("kaios", false, "admin1");

        Assert.False(_db.GetPilotExcludeWebConfig("kaios").ExcludeWebConfig);
        Assert.True(_db.GetPilotExcludeWebConfig("gos").ExcludeWebConfig);
    }

    [Fact]
    public void PilotExcludeWebConfig_KeyIsCaseInsensitiveOnDbName()
    {
        _db.SetPilotExcludeWebConfig("Kaios", false, "admin1");

        Assert.False(_db.GetPilotExcludeWebConfig("kaios").ExcludeWebConfig);
        Assert.Equal("false", _db.GetSetting("PilotExcludeWebConfig.kaios")!.Value);
    }

    [Fact]
    public void IsAdmin_ReturnsTrueOnlyForAdminRole()
    {
        _db.AddUser("adm", "管理者", "admin");
        _db.AddUser("usr", "一般", "user");

        Assert.True(_db.IsAdmin("adm"));
        Assert.False(_db.IsAdmin("usr"));
        Assert.False(_db.IsAdmin("nobody"));
        Assert.False(_db.IsAdmin(""));
        Assert.False(_db.IsAdmin(null));
    }

    [Fact]
    public void EnsureCreated_Twice_KeepsSettings()
    {
        _db.UpsertSetting("K", "v1", "alice");

        _db.EnsureCreated();

        Assert.Equal("v1", _db.GetSetting("K")!.Value);
    }
}
