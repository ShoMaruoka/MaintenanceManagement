using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class SqlDefinitionConverterTests
{
    // --- 変換対象 ---

    [Theory]
    [InlineData("ALTER PROCEDURE [dbo].[X]", "CREATE OR ALTER PROCEDURE [dbo].[X]")]
    [InlineData("ALTER PROC [dbo].[X]", "CREATE OR ALTER PROC [dbo].[X]")]
    [InlineData("ALTER FUNCTION [dbo].[X]", "CREATE OR ALTER FUNCTION [dbo].[X]")]
    [InlineData("ALTER VIEW [dbo].[X]", "CREATE OR ALTER VIEW [dbo].[X]")]
    public void ToCreateOrAlter_ConvertsAlterDefinition(string input, string expected)
    {
        Assert.Equal(expected, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    [Theory]
    [InlineData("alter procedure [dbo].[X]", "CREATE OR alter procedure [dbo].[X]")]   // 元の表記は保持する
    [InlineData("ALTER   PROCEDURE [dbo].[X]", "CREATE OR ALTER   PROCEDURE [dbo].[X]")]
    [InlineData("ALTER\tVIEW [dbo].[X]", "CREATE OR ALTER\tVIEW [dbo].[X]")]
    [InlineData("ALTER\r\nFUNCTION [dbo].[X]", "CREATE OR ALTER\r\nFUNCTION [dbo].[X]")]
    [InlineData("  ALTER PROCEDURE [dbo].[X]", "  CREATE OR ALTER PROCEDURE [dbo].[X]")]
    public void ToCreateOrAlter_ToleratesCaseAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    [Fact]
    public void ToCreateOrAlter_ConvertsDefinitionAfterSsmsHeader()
    {
        var input =
            "SET ANSI_NULLS ON\r\nGO\r\n" +
            "/****** Object:  StoredProcedure [dbo].[X] ******/\r\n" +
            "ALTER PROCEDURE [dbo].[X]\r\nAS\r\nSELECT 1\r\n";

        var expected = input.Replace("ALTER PROCEDURE", "CREATE OR ALTER PROCEDURE");

        Assert.Equal(expected, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    // --- 変換対象外 ---

    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE [dbo].[X]")]   // 二重変換しない
    [InlineData("create or alter view [dbo].[X]")]
    [InlineData("CREATE PROCEDURE [dbo].[X]")]            // SPEC Decision 1: 変換しない
    [InlineData("ALTER TABLE [dbo].[T] ADD C int")]
    [InlineData("ALTER PROCEDURES")]                      // 単語境界
    [InlineData("SELECT 1 -- ALTER PROCEDURE は行頭ではない")]
    [InlineData("")]
    public void ToCreateOrAlter_LeavesNonTargetUnchanged(string input)
    {
        Assert.Equal(input, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    [Fact]
    public void ToCreateOrAlter_IgnoresLineComment_AndConvertsRealDefinition()
    {
        var input = "-- ALTER PROCEDURE [dbo].[Old]\r\nALTER PROCEDURE [dbo].[X]\r\n";
        var expected = "-- ALTER PROCEDURE [dbo].[Old]\r\nCREATE OR ALTER PROCEDURE [dbo].[X]\r\n";

        Assert.Equal(expected, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    [Fact]
    public void ToCreateOrAlter_ConvertsOnlyFirstDefinition()
    {
        // 本体中（動的 SQL 等）に行頭の ALTER VIEW が現れても書き換えない
        var input = "ALTER PROCEDURE [dbo].[X]\r\nAS\r\nEXEC('\r\nALTER VIEW [dbo].[V] AS SELECT 1')\r\n";
        var expected = "CREATE OR ALTER PROCEDURE [dbo].[X]\r\nAS\r\nEXEC('\r\nALTER VIEW [dbo].[V] AS SELECT 1')\r\n";

        Assert.Equal(expected, SqlDefinitionConverter.ToCreateOrAlter(input));
    }

    [Fact]
    public void ToCreateOrAlter_DoesNotTouchAlterAfterCreateOrAlter()
    {
        var input = "CREATE OR ALTER PROCEDURE [dbo].[X]\r\nAS\r\nEXEC('\r\nALTER VIEW [dbo].[V] AS SELECT 1')\r\n";

        // 定義文が既に CREATE OR ALTER の場合でも、後続の行頭 ALTER を誤って変換しない
        Assert.Equal(input, SqlDefinitionConverter.ToCreateOrAlter(input));
    }
}
