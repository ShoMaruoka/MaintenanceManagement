using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class DeployServiceStep3bMariaDbTableTests
{
    private static IConfiguration DryRunFalseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = "false" })
            .Build();

    [Fact]
    public async Task ExecuteAsync_MariaDbTableOnly_RegistersManualApply_AndSkipsMysqlDeploy()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3b-mariatable").FullName;
        try
        {
            var config = new DbConfig
            {
                Name = "step3b-mariatable-test-" + Guid.NewGuid(),
                SourceControlPath = tempRoot,
                GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
                MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
                DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
            };

            // Git マージ後に存在するはずの Table 定義ファイル（プレフィックスなし）を用意
            var tableDir = Path.Combine(config.MariaDbGitRepoPath, "Table");
            Directory.CreateDirectory(tableDir);
            await File.WriteAllTextAsync(Path.Combine(tableDir, "tm0010catalogno.sql"), "CREATE TABLE ...", Encoding.UTF8);

            // MariaDB merge フォルダにスタブ git_merge.bat（実git操作は不要、実行痕跡だけ残す）
            Directory.CreateDirectory(config.MariaDbMergePath);
            File.WriteAllText(Path.Combine(config.MariaDbMergePath, "git_merge.bat"), "@echo off\r\nexit /b 0\r\n");

            var configuration = DryRunFalseConfig();
            var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
            var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);

            var request = new DeployRequest
            {
                DbName = config.Name,
                ExecutedBy = "tester",
                Modules = [new DeployModule { Type = "MariaDbTable", Name = "tm0010catalogno", OpType = "更新" }],
            };

            var reader = deployService.ExecuteAsync(config, request, "tester", CancellationToken.None);
            await foreach (var _ in reader.ReadAllAsync()) { }

            // 手動適用待ちに登録されていること
            Assert.True(File.Exists(config.DeployedManualManifestPath), "manifest not created");
            var manifestLine = (await File.ReadAllLinesAsync(config.DeployedManualManifestPath)).Single();
            using var doc = JsonDocument.Parse(manifestLine);
            Assert.Equal("MariaDbTable", doc.RootElement.GetProperty("moduleType").GetString());
            Assert.Equal("tm0010catalogno", doc.RootElement.GetProperty("moduleName").GetString());
            Assert.Equal("tm0010catalogno.sql", doc.RootElement.GetProperty("fileName").GetString());

            var copiedFile = Path.Combine(config.DeployedManualPath, "tm0010catalogno.sql");
            Assert.True(File.Exists(copiedFile));

            // mysql CLI（Step4/5/6 MariaDB）はまったく起動されない = MariaDbDeploySourcePath が作られない
            Assert.False(Directory.Exists(config.MariaDbDeploySourcePath),
                "MariaDbTable only request should not touch the mysql deploy pipeline");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
