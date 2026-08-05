using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DeployServiceStep3Tests
{
    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    // pause 等を含む実運用の git_merge.bat を直接叩くとテストがハングしうるため、
    // 「実行されたら自分のフォルダにマーカーファイルを作る」だけのスタブ bat を使う
    private static void CreateStubGitMergeBat(string mergePath)
    {
        Directory.CreateDirectory(mergePath);
        File.WriteAllText(
            Path.Combine(mergePath, "git_merge.bat"),
            "@echo off\r\necho ran> \"%~dp0ran.marker\"\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_RunsBothGitMergeBats_WhenSqlServerAndMariaDbMixed()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3-test").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step3-test-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };
            CreateStubGitMergeBat(config.MergePath);
            CreateStubGitMergeBat(config.MariaDbMergePath);

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

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

            Assert.True(File.Exists(Path.Combine(config.MergePath, "ran.marker")));
            Assert.True(File.Exists(Path.Combine(config.MariaDbMergePath, "ran.marker")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRunMariaDbGitMergeBat_WhenSqlServerOnly()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3-test2").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step3-test2-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };
            CreateStubGitMergeBat(config.MergePath);
            CreateStubGitMergeBat(config.MariaDbMergePath);

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

            Assert.True(File.Exists(Path.Combine(config.MergePath, "ran.marker")));
            Assert.False(File.Exists(Path.Combine(config.MariaDbMergePath, "ran.marker")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
