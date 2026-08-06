using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// GetLatestOpTypes（本番前準備画面向けの操作区分逆引き）の振る舞いを、
/// 一時 SQLite ファイル上の実データで固定する。
/// </summary>
public class DatabaseServiceOpTypeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceOpTypeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"optype-test-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = _dbPath })
            .Build();
        _db = new DatabaseService(configuration);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { /* 一時ファイルの掃除失敗はテスト結果に影響させない */ }
        GC.SuppressFinalize(this);
    }

    private long Deploy(string dbName, string opType, string moduleType, string moduleName, string result = "success")
    {
        var sessionId = _db.InsertDeploySession(dbName, "tester");
        _db.InsertDeployDetail(sessionId, opType, moduleType, moduleName, result);
        return sessionId;
    }

    [Fact]
    public void EnsureCreated_IsIdempotent()
    {
        // 既存 DB に対する再実行（インデックス追加を含む）で例外が出ないこと
        _db.EnsureCreated();
        _db.EnsureCreated();
    }

    [Fact]
    public void GetLatestOpTypes_ReturnsOpType_KeyedByFileName()
    {
        Deploy("kaios", "更新", "StoredProcedure", "usp_GetOrder");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("更新", map[OpTypeResolver.FileKey("sqlserver", "dbo.usp_GetOrder.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_TakesTheNewestEntry_WhenModuleDeployedMultipleTimes()
    {
        Deploy("kaios", "新規", "Stored", "SM0010GOODS_UP");
        Deploy("kaios", "更新", "Stored", "SM0010GOODS_UP");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("更新", map[OpTypeResolver.FileKey("mariadb", "SM0010GOODS_UP.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_DoesNotConfuseSqlServerAndMariaDbModulesWithTheSameName()
    {
        Deploy("kaios", "削除", "StoredProcedure", "SharedName");
        Deploy("kaios", "新規", "Stored", "SharedName");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("削除", map[OpTypeResolver.FileKey("sqlserver", "dbo.SharedName.sql")]);
        Assert.Equal("新規", map[OpTypeResolver.FileKey("mariadb", "SharedName.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_ResolvesModuleNamesRecordedWithDboPrefix()
    {
        // 実データに ModuleName="dbo.TestSP" の行が存在する
        Deploy("kaios", "更新", "StoredProcedure", "dbo.TestSP");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("更新", map[OpTypeResolver.FileKey("sqlserver", "dbo.TestSP.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_NormalizesUnexpectedOpTypeToUnknown()
    {
        // 実データに OpType="更新2" の行が存在する
        Deploy("kaios", "更新2", "VIEW", "dbo.TestView");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal(OpTypeResolver.Unknown, map[OpTypeResolver.FileKey("sqlserver", "dbo.TestView.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_ResolvesEvenWhenTheSessionFailed()
    {
        // deployed/ にファイルがある＝適用成功が保証済みのため成否では絞らない（SPEC Assumption 6）。
        // MariaDB は明細にセッション全体の成否が書かれるため、絞ると区分が引けなくなる。
        var sessionId = _db.InsertDeploySession("kaios", "tester");
        _db.InsertDeployDetail(sessionId, "削除", "StoredProcedure", "usp_Obsolete", "failed");
        _db.UpdateDeploySessionStatus(sessionId, "failed", "boom", "log");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("削除", map[OpTypeResolver.FileKey("sqlserver", "dbo.usp_Obsolete.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_IsScopedToTheRequestedDb()
    {
        Deploy("kaios", "新規", "StoredProcedure", "usp_KaiosOnly");
        Deploy("gos", "削除", "StoredProcedure", "usp_GosOnly");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.True(map.ContainsKey(OpTypeResolver.FileKey("sqlserver", "dbo.usp_KaiosOnly.sql")));
        Assert.False(map.ContainsKey(OpTypeResolver.FileKey("sqlserver", "dbo.usp_GosOnly.sql")));
    }

    [Fact]
    public void GetLatestOpTypes_OmitsUnknownFiles_SoCallerCanFallBack()
    {
        Deploy("kaios", "更新", "StoredProcedure", "usp_GetOrder");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.False(map.ContainsKey(OpTypeResolver.FileKey("sqlserver", "dbo.usp_NeverDeployed.sql")));
    }

    [Fact]
    public void GetLatestOpTypes_LastWins_WhenMariaDbProcedureAndFunctionShareAName()
    {
        // AD5: MariaDB はプロシージャと関数が別名前空間だが、Git 上はどちらも Stored/{name}.sql
        // となり deployed/ で同一ファイル名に衝突する。既知の制約として後勝ちを固定する。
        Deploy("kaios", "新規", "Stored", "AmbiguousName");
        Deploy("kaios", "削除", "MariaDbFunction", "AmbiguousName");

        var map = _db.GetLatestOpTypes("kaios");

        Assert.Equal("削除", map[OpTypeResolver.FileKey("mariadb", "AmbiguousName.sql")]);
    }

    [Fact]
    public void GetLatestOpTypes_ReturnsEmpty_WhenDbHasNoHistory()
    {
        Assert.Empty(_db.GetLatestOpTypes("duskin"));
    }
}
