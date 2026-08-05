using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DeployServiceStep4MariaDbTests
{
    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    private static DbConfig CreateConfig(string tempRoot) => new()
    {
        Name = "step4-maria-test-" + Guid.NewGuid(),
        SourceControlPath = tempRoot,
        GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
        MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
        DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
    };

    private static async Task RunAsync(DbConfig config, DeployRequest request)
    {
        var configuration = DryRunFalseConfig();
        var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
        var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);
        var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
        await foreach (var _ in reader.ReadAllAsync()) { }
    }

    [Fact]
    public async Task Step4_CopiesMariaDbStoredFile_AsIs_ForUpdate()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step4-maria").FullName;
        try
        {
            var config = CreateConfig(tempRoot);
            var storedDir = Path.Combine(config.MariaDbGitRepoPath, "Stored");
            Directory.CreateDirectory(storedDir);
            var content = "DROP PROCEDURE IF EXISTS `getTodayStr`;\r\nCREATE DEFINER=`root`@`%` FUNCTION `getTodayStr`() ...";
            await File.WriteAllTextAsync(Path.Combine(storedDir, "getTodayStr.sql"), content, Encoding.UTF8);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "Stored", Name = "getTodayStr", OpType = "更新" }],
            };

            await RunAsync(config, request);

            var destPath = Path.Combine(config.MariaDbDeploySourcePath, "getTodayStr.sql");
            Assert.True(File.Exists(destPath), $"not found: {destPath}");
            Assert.Equal(content, await File.ReadAllTextAsync(destPath, Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Step4_GeneratesDropSql_ForDelete()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step4-maria2").FullName;
        try
        {
            var config = CreateConfig(tempRoot);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "Stored", Name = "OldProc", OpType = "削除" }],
            };

            await RunAsync(config, request);

            var destPath = Path.Combine(config.MariaDbDeploySourcePath, "OldProc.sql");
            Assert.True(File.Exists(destPath), $"not found: {destPath}");
            var sql = await File.ReadAllTextAsync(destPath, Encoding.UTF8);
            Assert.Contains("DROP PROCEDURE IF EXISTS `OldProc`", sql);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Step4_GeneratesDropFunctionSql_ForMariaDbFunctionDelete()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step4-maria3").FullName;
        try
        {
            var config = CreateConfig(tempRoot);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "MariaDbFunction", Name = "OldFunc", OpType = "削除" }],
            };

            await RunAsync(config, request);

            var destPath = Path.Combine(config.MariaDbDeploySourcePath, "OldFunc.sql");
            Assert.True(File.Exists(destPath), $"not found: {destPath}");
            var sql = await File.ReadAllTextAsync(destPath, Encoding.UTF8);
            Assert.Contains("DROP FUNCTION IF EXISTS `OldFunc`", sql);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
