using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class OpTypeResolverTests
{
    // --- DbType 判定 ---

    [Theory]
    [InlineData("Stored")]           // MariaDB のストアド（Issue #22 で "MariaDB" から改名）
    [InlineData("MariaDbFunction")]
    [InlineData("MariaDbTable")]
    [InlineData("MariaDB")]          // Issue #22 以前の旧値（既存履歴に残りうる）
    [InlineData("mariadbtable")]     // 大文字小文字を問わない
    public void ToDbType_ReturnsMariaDb_ForMariaDbModuleTypes(string moduleType)
    {
        Assert.Equal("mariadb", OpTypeResolver.ToDbType(moduleType));
    }

    [Theory]
    [InlineData("StoredProcedure")]  // SQL Server のストアド。MariaDB の "Stored" とは別値
    [InlineData("Function")]
    [InlineData("VIEW")]
    [InlineData("Table")]
    [InlineData("UserDefinedTableType")]
    [InlineData("")]
    [InlineData("UnknownFutureType")] // 未知の値は SQL Server 側へフォールバック
    public void ToDbType_ReturnsSqlServer_ForOtherModuleTypes(string moduleType)
    {
        Assert.Equal("sqlserver", OpTypeResolver.ToDbType(moduleType));
    }

    // --- ファイル名の正規化 ---

    [Theory]
    [InlineData("dbo.usp_GetOrder.sql", "usp_GetOrder")]
    [InlineData("SM0010GOODS_UP.sql", "SM0010GOODS_UP")]      // MariaDB は dbo. なし
    [InlineData("usp_NoPrefix.sql", "usp_NoPrefix")]          // 想定外だが安全側に倒す
    [InlineData("dbo.VK0010県.sql", "VK0010県")]              // 日本語モジュール名
    public void NormalizeFileName_StripsExtensionAndDboPrefix(string fileName, string expected)
    {
        Assert.Equal(expected, OpTypeResolver.NormalizeFileName(fileName));
    }

    [Fact]
    public void NormalizeFileName_KeepsDotsInsideTheName()
    {
        // Path.GetFileNameWithoutExtension だと "a.b" に切り詰められてしまうため、
        // 末尾の ".sql" のみを取り除くこと。
        Assert.Equal("a.b.c", OpTypeResolver.NormalizeFileName("a.b.c.sql"));
    }

    // --- モジュール名の正規化 ---

    [Theory]
    [InlineData("dbo.TestSP", "TestSP")]   // 実データに dbo. 付きで記録された行が存在する
    [InlineData("usp_GetOrder", "usp_GetOrder")]
    public void NormalizeModuleName_StripsDboPrefix(string moduleName, string expected)
    {
        Assert.Equal(expected, OpTypeResolver.NormalizeModuleName(moduleName));
    }

    [Fact]
    public void NormalizeModuleName_DoesNotStripExtensionLikeSuffix()
    {
        // モジュール名は拡張子を持たない。末尾が .sql に見えても切らない。
        Assert.Equal("Weird.sql", OpTypeResolver.NormalizeModuleName("Weird.sql"));
    }

    // --- キー生成: ファイル名側とモジュール名側が同じキーに落ちること ---

    [Fact]
    public void FileKeyAndModuleKey_Match_ForSqlServerModule()
    {
        Assert.Equal(
            OpTypeResolver.ModuleKey("StoredProcedure", "usp_GetOrder"),
            OpTypeResolver.FileKey("sqlserver", "dbo.usp_GetOrder.sql"));
    }

    [Fact]
    public void FileKeyAndModuleKey_Match_WhenModuleNameHasDboPrefix()
    {
        // 実データ: ModuleType=StoredProcedure, ModuleName=dbo.TestSP
        Assert.Equal(
            OpTypeResolver.ModuleKey("StoredProcedure", "dbo.TestSP"),
            OpTypeResolver.FileKey("sqlserver", "dbo.TestSP.sql"));
    }

    [Fact]
    public void FileKeyAndModuleKey_Match_ForMariaDbModule()
    {
        Assert.Equal(
            OpTypeResolver.ModuleKey("Stored", "SM0010GOODS_UP"),
            OpTypeResolver.FileKey("mariadb", "SM0010GOODS_UP.sql"));
    }

    [Fact]
    public void Keys_Differ_BetweenSqlServerAndMariaDb_ForSameModuleName()
    {
        // 同名モジュールが両エンジンに存在しても取り違えないこと
        Assert.NotEqual(
            OpTypeResolver.ModuleKey("StoredProcedure", "SharedName"),
            OpTypeResolver.ModuleKey("Stored", "SharedName"));
    }

    // --- OpType の正規化 ---

    [Theory]
    [InlineData("新規")]
    [InlineData("更新")]
    [InlineData("削除")]
    public void NormalizeOpType_KeepsKnownValues(string opType)
    {
        Assert.Equal(opType, OpTypeResolver.NormalizeOpType(opType));
    }

    [Theory]
    [InlineData("更新2")]   // 実データに存在する不正値
    [InlineData("")]
    [InlineData(null)]
    [InlineData("なにか")]
    public void NormalizeOpType_FallsBackToUnknown_ForUnexpectedValues(string? opType)
    {
        Assert.Equal(OpTypeResolver.Unknown, OpTypeResolver.NormalizeOpType(opType));
    }
}
