using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DeployServiceStep1Tests
{
    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    [Fact]
    public async Task ExecuteAsync_SplitsStep1Output_BySqlServerAndMariaDbType()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step1-test").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step1-test-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

            // Table / MariaDbTable は Git マージのみ（Step4以降のSQL適用処理には進まない）ため、
            // Step1 の出力振り分けだけをクリーンに検証できる
            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules =
                [
                    new DeployModule { Type = "Table", Name = "SqlServerTable1", OpType = "更新" },
                    new DeployModule { Type = "MariaDbTable", Name = "tm0010catalogno", OpType = "更新" },
                ],
            };

            var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
            await foreach (var _ in reader.ReadAllAsync()) { }

            var sqlServerUpdateFile = Path.Combine(config.MergePath, "UpdateModule.txt");
            var mariaDbUpdateFile = Path.Combine(config.MariaDbMergePath, "UpdateModule.txt");

            Assert.True(File.Exists(sqlServerUpdateFile), $"not found: {sqlServerUpdateFile}");
            Assert.True(File.Exists(mariaDbUpdateFile), $"not found: {mariaDbUpdateFile}");

            var sjis = Encoding.GetEncoding("shift_jis");
            var sqlServerContent = await File.ReadAllTextAsync(sqlServerUpdateFile, sjis);
            var mariaDbContent = await File.ReadAllTextAsync(mariaDbUpdateFile, sjis);

            Assert.Contains("Table,SqlServerTable1", sqlServerContent);
            Assert.DoesNotContain("tm0010catalogno", sqlServerContent);
            Assert.Contains("MariaDbTable,tm0010catalogno", mariaDbContent);
            Assert.DoesNotContain("SqlServerTable1", mariaDbContent);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SqlServerOnlyRequest_DoesNotCreateMariaDbMergeFolder()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step1-test2").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step1-test2-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "Table", Name = "SqlServerTable1", OpType = "更新" }],
            };

            var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
            await foreach (var _ in reader.ReadAllAsync()) { }

            Assert.True(File.Exists(Path.Combine(config.MergePath, "UpdateModule.txt")));
            Assert.False(Directory.Exists(config.MariaDbMergePath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
