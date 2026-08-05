using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ManualApplyServiceTests
{
    private static ManualApplyService CreateService(bool dryRun = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = dryRun.ToString() })
            .Build();
        return new ManualApplyService(configuration, NullLogger<ManualApplyService>.Instance);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "test", "Kaios_MariaDB_rep")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("test/Kaios_MariaDB_rep が見つかりません（リポジトリ直下から実行してください）");
    }

    [Fact]
    public void Register_ResolvesMariaDbTablePath_WithoutDboPrefix()
    {
        var repoRoot = FindRepoRoot();
        var config = new DbConfig
        {
            Name = "kaios",
            MariaDbGitRepoPath = Path.Combine(repoRoot, "test", "Kaios_MariaDB_rep"),
            DeployDev2StgPath = Path.Combine(Path.GetTempPath(), "manualapply-test"),
        };
        var service = CreateService(dryRun: true);
        var modules = new List<DeployModule> { new() { Type = "MariaDbTable", Name = "tm0010catalogno", OpType = "更新" } };
        var logs = new List<(string Level, string Message)>();

        var result = service.Register(config, modules, "tester", (level, message) => logs.Add((level, message)));

        Assert.Single(result);
        Assert.Equal("tm0010catalogno.sql", result[0].FileName);
        Assert.DoesNotContain(logs, l => l.Level == "WARN");
    }

    [Fact]
    public void Register_StillResolvesSqlServerTablePath_WithDboPrefix()
    {
        var repoRoot = FindRepoRoot();
        var config = new DbConfig
        {
            Name = "kaios",
            GitRepoPath = Path.Combine(repoRoot, "test", "Kaios_MariaDB_rep"), // ダミー: 実SQLServerリポジトリ不要のため同フォルダを使い回す
            DeployDev2StgPath = Path.Combine(Path.GetTempPath(), "manualapply-test"),
        };
        var service = CreateService(dryRun: true);
        // GitRepoPath 配下に "Table\dbo.xxx.sql" は存在しないため見つからない = 警告になることを確認（＝挙動が変わらないことの確認）
        var modules = new List<DeployModule> { new() { Type = "Table", Name = "tm0010catalogno", OpType = "更新" } };
        var logs = new List<(string Level, string Message)>();

        var result = service.Register(config, modules, "tester", (level, message) => logs.Add((level, message)));

        Assert.Single(result);
        Assert.Contains(logs, l => l.Level == "WARN" && l.Message.Contains("dbo.tm0010catalogno.sql"));
    }

    [Fact]
    public void ManualApplyTypes_IncludesMariaDbTable()
    {
        Assert.Contains("MariaDbTable", ManualApplyService.ManualApplyTypes);
        Assert.True(ManualApplyService.IsManualApplyType("MariaDbTable"));
    }
}
