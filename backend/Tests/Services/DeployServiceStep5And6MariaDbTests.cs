using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DeployServiceStep5And6MariaDbTests
{
    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    // 実際の mysql CLI 呼び出しは実機検証（Task7 Verification）で行うため、
    // ユニットテストでは RESULT: 行を出力するだけのスタブ deploy.bat を使う
    private static void CreateStubDeployBat(string forNewCreationPath, string resultLines)
    {
        Directory.CreateDirectory(forNewCreationPath);
        File.WriteAllText(
            Path.Combine(forNewCreationPath, "deploy.bat"),
            $"@echo off\r\n{resultLines}\r\nexit /b 0\r\n");
    }

    [Fact]
    public async Task Step5And6_MovesOnlySuccessfulFiles_ToDeployed()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step5-maria").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step5-maria-test-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };

            var storedDir = Path.Combine(config.MariaDbGitRepoPath, "Stored");
            Directory.CreateDirectory(storedDir);
            await File.WriteAllTextAsync(Path.Combine(storedDir, "file1.sql"), "-- proc1", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(storedDir, "file2.sql"), "-- proc2", Encoding.UTF8);

            CreateStubDeployBat(config.MariaDbForNewCreationPath, "echo RESULT:OK:file1.sql\r\necho RESULT:FAIL:file2.sql");

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules =
                [
                    new DeployModule { Type = "Stored", Name = "file1", OpType = "更新" },
                    new DeployModule { Type = "Stored", Name = "file2", OpType = "更新" },
                ],
            };

            var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
            await foreach (var _ in reader.ReadAllAsync()) { }

            Assert.True(File.Exists(Path.Combine(config.MariaDbDeployedPath, "file1.sql")), "file1 should be moved to deployed");
            Assert.False(File.Exists(Path.Combine(config.MariaDbDeploySourcePath, "file1.sql")), "file1 should no longer be in Source");

            Assert.False(File.Exists(Path.Combine(config.MariaDbDeployedPath, "file2.sql")), "file2 (failed) should not be moved to deployed");
            Assert.True(File.Exists(Path.Combine(config.MariaDbDeploySourcePath, "file2.sql")), "file2 (failed) should remain in Source");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Step5And6_TreatsFileWithoutResultMarker_AsFailed()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step5-maria2").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step5-maria-test2-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };

            var storedDir = Path.Combine(config.MariaDbGitRepoPath, "Stored");
            Directory.CreateDirectory(storedDir);
            await File.WriteAllTextAsync(Path.Combine(storedDir, "orphan.sql"), "-- proc", Encoding.UTF8);

            // RESULT 行を一切出力しないスタブ（bat が異常終了・クラッシュしたケースを模擬）
            CreateStubDeployBat(config.MariaDbForNewCreationPath, "echo something unrelated");

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "Stored", Name = "orphan", OpType = "更新" }],
            };

            var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
            await foreach (var _ in reader.ReadAllAsync()) { }

            Assert.False(File.Exists(Path.Combine(config.MariaDbDeployedPath, "orphan.sql")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
