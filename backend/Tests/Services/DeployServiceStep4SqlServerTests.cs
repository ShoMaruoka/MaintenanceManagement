using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// SQL Server モジュールの Step 4（SQL 変換）。deploy.bat が無い環境では Step 5 が警告のみで通過し、
/// Step 6 で deployed/ へ移動されるため、検証は deployed/ 上のファイルに対して行う。
/// </summary>
public class DeployServiceStep4SqlServerTests : IDisposable
{
    private static readonly Encoding Sjis = Encoding.GetEncoding("shift_jis");

    private readonly string _tempRoot = Directory.CreateTempSubdirectory("deploy-step4-sqlserver").FullName;

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    private DbConfig CreateConfig() => new()
    {
        Name = "step4-sqlserver-test-" + Guid.NewGuid(),
        SourceControlPath = _tempRoot,
        GitRepoPath = Path.Combine(_tempRoot, "GitRepo"),
        MariaDbGitRepoPath = Path.Combine(_tempRoot, "MariaDbGitRepo"),
        DeployDev2StgPath = Path.Combine(_tempRoot, "Deploy_DEV2STG"),
    };

    private static async Task<List<LogEntry>> RunAsync(DbConfig config, params DeployModule[] modules)
    {
        var configuration = DryRunFalseConfig();
        var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
        var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);
        var request = new DeployRequest { DbName = config.Name, ExecutedBy = "tester", Modules = [.. modules] };

        var logs = new List<LogEntry>();
        await foreach (var entry in deployService.ExecuteAsync(config, request, "tester", CancellationToken.None).ReadAllAsync())
            logs.Add(entry);
        return logs;
    }

    private static async Task WriteGitFileAsync(DbConfig config, string type, string name, string content)
    {
        var dir = Path.Combine(config.GitRepoPath, type);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, $"dbo.{name}.sql"), content, Sjis);
    }

    private static Task<string> ReadDeployedAsync(DbConfig config, string name) =>
        File.ReadAllTextAsync(Path.Combine(config.DeployedPath, $"dbo.{name}.sql"), Sjis);

    [Theory]
    [InlineData("新規")]
    [InlineData("更新")]
    public async Task Step4_ConvertsStoredProcedure_ToCreateOrAlter(string opType)
    {
        var config = CreateConfig();
        await WriteGitFileAsync(config, "StoredProcedure", "usp_X", "ALTER PROCEDURE [dbo].[usp_X]\r\nAS\r\nSELECT 1\r\n");

        await RunAsync(config, new DeployModule { Type = "StoredProcedure", Name = "usp_X", OpType = opType });

        Assert.Equal("CREATE OR ALTER PROCEDURE [dbo].[usp_X]\r\nAS\r\nSELECT 1\r\n", await ReadDeployedAsync(config, "usp_X"));
    }

    [Fact]
    public async Task Step4_NewThenUpdate_KeepsCreateOrAlter_InDeployed()
    {
        // Pilot 適用周期内に「新規 → 更新」と STG 適用されても、deployed/ が ALTER で上書きされないこと
        var config = CreateConfig();
        await WriteGitFileAsync(config, "StoredProcedure", "usp_X", "ALTER PROCEDURE [dbo].[usp_X]\r\nAS\r\nSELECT 1\r\n");
        await RunAsync(config, new DeployModule { Type = "StoredProcedure", Name = "usp_X", OpType = "新規" });

        await WriteGitFileAsync(config, "StoredProcedure", "usp_X", "ALTER PROCEDURE [dbo].[usp_X]\r\nAS\r\nSELECT 2\r\n");
        await RunAsync(config, new DeployModule { Type = "StoredProcedure", Name = "usp_X", OpType = "更新" });

        Assert.Equal("CREATE OR ALTER PROCEDURE [dbo].[usp_X]\r\nAS\r\nSELECT 2\r\n", await ReadDeployedAsync(config, "usp_X"));
    }

    [Theory]
    [InlineData("Function", "ALTER FUNCTION [dbo].[fn_X]()", "CREATE OR ALTER FUNCTION [dbo].[fn_X]()")]
    [InlineData("VIEW", "ALTER VIEW [dbo].[v_X]", "CREATE OR ALTER VIEW [dbo].[v_X]")]
    public async Task Step4_ConvertsFunctionAndView_ToCreateOrAlter(string type, string input, string expected)
    {
        var config = CreateConfig();
        var name = type == "VIEW" ? "v_X" : "fn_X";
        await WriteGitFileAsync(config, type, name, input);

        await RunAsync(config, new DeployModule { Type = type, Name = name, OpType = "新規" });

        Assert.Equal(expected, await ReadDeployedAsync(config, name));
    }

    [Fact]
    public async Task Step4_PreservesJapanese_InShiftJis()
    {
        var config = CreateConfig();
        const string name = "SK0300アカウントSEL";
        await WriteGitFileAsync(config, "StoredProcedure", name, $"-- 日本語コメント\r\nALTER PROCEDURE [dbo].[{name}]\r\n");

        await RunAsync(config, new DeployModule { Type = "StoredProcedure", Name = name, OpType = "更新" });

        Assert.Equal($"-- 日本語コメント\r\nCREATE OR ALTER PROCEDURE [dbo].[{name}]\r\n", await ReadDeployedAsync(config, name));
    }

    [Fact]
    public async Task Step4_GeneratesDropSql_ForDelete()
    {
        var config = CreateConfig();

        await RunAsync(config, new DeployModule { Type = "VIEW", Name = "v_Old", OpType = "削除" });

        Assert.Equal("DROP VIEW IF EXISTS [dbo].[v_Old]\r\nGO\r\n", await ReadDeployedAsync(config, "v_Old"));
    }

    [Theory]
    [InlineData("新規")]
    [InlineData("更新")]
    public async Task Step4_LogsCreateOrAlterConversion(string opType)
    {
        var config = CreateConfig();
        await WriteGitFileAsync(config, "StoredProcedure", "usp_X", "ALTER PROCEDURE [dbo].[usp_X]\r\n");

        var logs = await RunAsync(config, new DeployModule { Type = "StoredProcedure", Name = "usp_X", OpType = opType });

        Assert.Contains(logs, l => l.Message.StartsWith($"→ dbo.usp_X.sql  [{opType}] CREATE OR ALTER 変換"));
    }
}
