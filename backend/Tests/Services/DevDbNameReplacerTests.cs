using System.Text;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DevDbNameReplacerTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("dev-dbname-replacer").FullName;

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Theory]
    [InlineData("FROM KaiosDB_dev.dbo.T", "FROM KaiosDB.dbo.T")]
    [InlineData("FROM [KaiosDB_dev].dbo.T", "FROM [KaiosDB].dbo.T")]
    [InlineData("FROM [KaiosDB_dev].[dbo].[T]", "FROM [KaiosDB].[dbo].[T]")]
    [InlineData("FROM KaiosDB_dev..T", "FROM KaiosDB..T")]
    [InlineData("FROM KaiosDB_DEV.dbo.T", "FROM KaiosDB.dbo.T")]
    [InlineData("FROM KaiosDB_dev . dbo . T", "FROM KaiosDB . dbo . T")]
    [InlineData("FROM KaiosDB_dev.dbo.TK0207サンプルデモ依頼 AS x", "FROM KaiosDB.dbo.TK0207サンプルデモ依頼 AS x")]
    [InlineData("EXEC('SELECT * FROM KaiosDB_dev.dbo.T')", "EXEC('SELECT * FROM KaiosDB.dbo.T')")]
    public void Replace_RemovesDevSuffix_FromDatabaseQualifiedNames(string input, string expected)
    {
        Assert.Equal(expected, DevDbNameReplacer.Replace(input, out var replaced));
        Assert.Single(replaced);
    }

    [Theory]
    [InlineData("SELECT t.col_dev FROM dbo.T t")]          // 2 部構成の列名
    [InlineData("SELECT x_dev.col FROM dbo.T x_dev")]      // 2 部構成のエイリアス
    [InlineData("FROM dbo.T_dev")]                         // オブジェクト名の _dev
    [InlineData("FROM KaiosDB_devX.dbo.T")]                // _dev で終わらない
    [InlineData("FROM KaiosDB.dbo.T")]
    public void Replace_LeavesNonDatabaseNamesUntouched(string input)
    {
        Assert.Equal(input, DevDbNameReplacer.Replace(input, out var replaced));
        Assert.Empty(replaced);
    }

    [Fact]
    public void Replace_ReportsDistinctOriginalNames()
    {
        var sql = "FROM KaiosDB_dev.dbo.A JOIN [KaiosDB_dev].dbo.B JOIN KaiosDB_dev.dbo.C JOIN PafDB_dev.dbo.D";
        DevDbNameReplacer.Replace(sql, out var replaced);
        Assert.Equal(["KaiosDB_dev", "[KaiosDB_dev]", "PafDB_dev"], replaced);
    }

    [Fact]
    public async Task ReplaceFileAsync_PreservesUtf8Bom_AndLineEndings()
    {
        var path = Path.Combine(_tempRoot, "utf8bom.sql");
        await File.WriteAllTextAsync(path, "CREATE VIEW dbo.V\r\nAS\r\nSELECT * FROM KaiosDB_dev.dbo.日本語\r\n", new UTF8Encoding(true));

        var replaced = await DevDbNameReplacer.ReplaceFileAsync(path);

        Assert.Single(replaced);
        var expected = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("CREATE VIEW dbo.V\r\nAS\r\nSELECT * FROM KaiosDB.dbo.日本語\r\n")).ToArray();
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ReplaceFileAsync_PreservesShiftJis()
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var path = Path.Combine(_tempRoot, "sjis.sql");
        await File.WriteAllTextAsync(path, "-- 表示\r\nSELECT * FROM [KaiosDB_dev].dbo.ソース\r\n", sjis);

        await DevDbNameReplacer.ReplaceFileAsync(path);

        Assert.Equal(sjis.GetBytes("-- 表示\r\nSELECT * FROM [KaiosDB].dbo.ソース\r\n"), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ReplaceFileAsync_DoesNotTouchFile_WhenNothingToReplace()
    {
        var path = Path.Combine(_tempRoot, "none.sql");
        await File.WriteAllTextAsync(path, "SELECT 1", Encoding.ASCII);
        var before = File.GetLastWriteTimeUtc(path);

        var replaced = await DevDbNameReplacer.ReplaceFileAsync(path);

        Assert.Empty(replaced);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task ReplaceFileAsync_Throws_WhenEncodingCannotRoundTrip()
    {
        // SJIS として往復できないバイト列（末尾に単独のリードバイト）は、壊さずに例外にする
        var path = Path.Combine(_tempRoot, "broken.sql");
        byte[] original = [.. Encoding.ASCII.GetBytes("SELECT * FROM KaiosDB_dev.dbo.T"), 0x81];
        await File.WriteAllBytesAsync(path, original);

        await Assert.ThrowsAsync<InvalidDataException>(() => DevDbNameReplacer.ReplaceFileAsync(path));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
}
