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

    private static async Task<List<LogEntry>> RunAsync(DbConfig config, params DeployModule[] modules)
    {
        var configuration = DryRunFalseConfig();
        var manualApply = new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
        var deployService = new DeployService(configuration, manualApply, NullLogger<DeployService>.Instance);
        var request = new DeployRequest { DbName = config.Name, ExecutedBy = "tester", Modules = [.. modules] };
        var logs = new List<LogEntry>();
        await foreach (var e in deployService.ExecuteAsync(config, request, "tester", CancellationToken.None).ReadAllAsync())
            logs.Add(e);
        return logs;
    }

    private static DbConfig CreateConfig(string tempRoot) => new()
    {
        Name = "step3-replace-test-" + Guid.NewGuid(),
        SourceControlPath = tempRoot,
        GitRepoPath = Path.Combine(tempRoot, "GitRepo"),
        MariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo"),
        DeployDev2StgPath = Path.Combine(tempRoot, "Deploy_DEV2STG"),
    };

    [Fact]
    public async Task ExecuteAsync_ReplacesDevDbNames_BetweenMergeAndPush_WhenGitPushBatExists()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3-replace").FullName;
        try
        {
            var config = CreateConfig(tempRoot);
            var viewPath = Path.Combine(config.GitRepoPath, "VIEW", "dbo.V1.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(viewPath)!);
            Directory.CreateDirectory(config.MergePath);

            // git_merge.bat: 引数を記録し、DEV からの checkout を模して _dev 付きのファイルを書き出す
            File.WriteAllText(Path.Combine(config.MergePath, "git_merge.bat"),
                "@echo off\r\n" +
                "echo args=%~1> \"%~dp0merge_args.marker\"\r\n" +
                $"echo SELECT * FROM KaiosDB_dev.dbo.T> \"{viewPath}\"\r\n");
            // git_push.bat: 実行時点のファイル内容を控える（= commit される内容）
            File.WriteAllText(Path.Combine(config.MergePath, "git_push.bat"),
                $"@echo off\r\ncopy /y \"{viewPath}\" \"%~dp0pushed.marker\" > nul\r\n");

            await RunAsync(config, new DeployModule { Type = "VIEW", Name = "V1", OpType = "更新" });

            Assert.Equal("args=NOPUSH", File.ReadAllText(Path.Combine(config.MergePath, "merge_args.marker")).Trim());
            Assert.Equal("SELECT * FROM KaiosDB.dbo.T", File.ReadAllText(Path.Combine(config.MergePath, "pushed.marker")).Trim());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RunsLegacyMerge_WithoutReplacing_WhenGitPushBatMissing()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3-legacy").FullName;
        try
        {
            var config = CreateConfig(tempRoot);
            var viewPath = Path.Combine(config.GitRepoPath, "VIEW", "dbo.V1.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(viewPath)!);
            Directory.CreateDirectory(config.MergePath);
            File.WriteAllText(Path.Combine(config.MergePath, "git_merge.bat"),
                "@echo off\r\n" +
                "echo args=%~1> \"%~dp0merge_args.marker\"\r\n" +
                $"echo SELECT * FROM KaiosDB_dev.dbo.T> \"{viewPath}\"\r\n");

            var logs = await RunAsync(config, new DeployModule { Type = "VIEW", Name = "V1", OpType = "更新" });

            Assert.Equal("args=", File.ReadAllText(Path.Combine(config.MergePath, "merge_args.marker")).Trim());
            Assert.Equal("SELECT * FROM KaiosDB_dev.dbo.T", File.ReadAllText(viewPath).Trim());
            Assert.Contains(logs, l => l.Level == "WARN" && l.Message.Contains("git_push.bat"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPush_WhenReplacementFails()
    {
        var tempRoot = Directory.CreateTempSubdirectory("deploy-step3-replace-fail").FullName;
        try
        {
            var config = CreateConfig(tempRoot);
            var viewPath = Path.Combine(config.GitRepoPath, "VIEW", "dbo.V1.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(viewPath)!);
            // SJIS として往復できないバイト列 → 置換エラー
            File.WriteAllBytes(viewPath, [.. System.Text.Encoding.ASCII.GetBytes("SELECT * FROM KaiosDB_dev.dbo.T"), 0x81]);
            Directory.CreateDirectory(config.MergePath);
            File.WriteAllText(Path.Combine(config.MergePath, "git_merge.bat"), "@echo off\r\n");
            File.WriteAllText(Path.Combine(config.MergePath, "git_push.bat"),
                "@echo off\r\necho ran> \"%~dp0pushed.marker\"\r\n");

            var logs = await RunAsync(config, new DeployModule { Type = "VIEW", Name = "V1", OpType = "更新" });

            Assert.False(File.Exists(Path.Combine(config.MergePath, "pushed.marker")));
            Assert.Contains(logs, l => l.Level == "ERROR");
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
