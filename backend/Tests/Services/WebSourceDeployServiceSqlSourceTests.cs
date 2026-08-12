using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// Issue #35: Pilot SQL コピー元を DeployedPath / MariaDbDeployedPath に切替え、
/// *.sql 専用経路・空スキップ・注入による呼出検証を固定する。
/// </summary>
public class WebSourceDeployServiceSqlSourceTests : IDisposable
{
    private readonly string _root;

    public WebSourceDeployServiceSqlSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ws-sql-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { /* 一時ディレクトリの掃除失敗は無視 */ }
        GC.SuppressFinalize(this);
    }

    private static WebSourceDeployService CreateService(bool dryRun = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = dryRun.ToString() })
            .Build();
        return new WebSourceDeployService(configuration, NullLogger<WebSourceDeployService>.Instance);
    }

    private DbConfig CreateConfig()
    {
        var deployDev2Stg = Path.Combine(_root, "Deploy_DEV2STG");
        var pilotSql = Path.Combine(_root, "PilotSql");
        Directory.CreateDirectory(deployDev2Stg);
        Directory.CreateDirectory(pilotSql);
        File.WriteAllText(Path.Combine(pilotSql, "deploy.bat"), "@echo off\r\nexit /b 0\r\n");

        return new DbConfig
        {
            Name = "kaios",
            DeployDev2StgPath = deployDev2Stg,
            PilotSqlDeployPath = pilotSql,
            PilotSqlDbNameReplacements = [],
        };
    }

    private static void WriteSql(string dir, string fileName, string body = "SELECT 1;")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), body);
    }

    [Fact]
    public async Task RunSqlDeploy_CopiesFromDeployedPaths_NotDeploy2Prd()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        WriteSql(config.MariaDbDeployedPath, "b.sql");
        // Deploy2Prd に置いても参照されないこと
        Directory.CreateDirectory(Path.Combine(_root, "Deploy2Prd"));
        config.Deploy2PrdPath = Path.Combine(_root, "Deploy2Prd");
        WriteSql(config.Deploy2PrdPath, "prd-only.sql");

        var copies = new List<(string Src, string Dest)>();
        var batCalls = 0;
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (src, dest, _, _) =>
        {
            copies.Add((src, dest));
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) =>
        {
            batCalls++;
            return Task.FromResult(0);
        };

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.False(result.Skipped);
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.DeployedPath)
            && Path.GetFullPath(c.Dest) == Path.GetFullPath(config.PilotSqlDeploySourcePath));
        Assert.Contains(copies, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.MariaDbDeployedPath)
            && Path.GetFullPath(c.Dest) == Path.GetFullPath(Path.Combine(config.PilotSqlDeploySourcePath, "MariaDB")));
        Assert.DoesNotContain(copies, c => Path.GetFullPath(c.Src) == Path.GetFullPath(config.Deploy2PrdPath));
        Assert.Equal(1, batCalls);
    }

    [Fact]
    public async Task RunSqlDeploy_BothEmpty_SkipsBeforeBat_SetsSkipped()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        Directory.CreateDirectory(config.MariaDbDeployedPath);
        // 非 SQL のみ → 空扱い
        File.WriteAllText(Path.Combine(config.DeployedPath, "readme.txt"), "x");

        var copies = 0;
        var batCalls = 0;
        var logs = new List<string>();
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) =>
        {
            copies++;
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) =>
        {
            batCalls++;
            return Task.FromResult(0);
        };

        var result = await svc.RunSqlDeployAsync(config, line => logs.Add(line), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.True(result.Skipped);
        Assert.Equal(0, copies);
        Assert.Equal(0, batCalls);
        Assert.Contains(logs, l => l.Contains("適用対象 SQL なし", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_SqlServerOnly_CopiesOneSide()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "only-ss.sql");
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var copies = new List<string>();
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (src, _, _, _) =>
        {
            copies.Add(Path.GetFullPath(src));
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.False(result.Skipped);
        Assert.Single(copies);
        Assert.Equal(Path.GetFullPath(config.DeployedPath), copies[0]);
    }

    [Fact]
    public async Task RunSqlDeploy_MariaDbOnly_CopiesToMariaDbSubfolder()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        WriteSql(config.MariaDbDeployedPath, "only-mdb.sql");

        var copies = new List<(string Src, string Dest)>();
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (src, dest, _, _) =>
        {
            copies.Add((Path.GetFullPath(src), Path.GetFullPath(dest)));
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Single(copies);
        Assert.Equal(Path.GetFullPath(config.MariaDbDeployedPath), copies[0].Src);
        Assert.Equal(Path.GetFullPath(Path.Combine(config.PilotSqlDeploySourcePath, "MariaDB")), copies[0].Dest);
    }

    [Fact]
    public async Task RunSqlDeploy_UnsetDeployDev2StgPath_ThrowsBeforeSkip()
    {
        var config = CreateConfig();
        config.DeployDev2StgPath = ""; // DeployedPath / MariaDbDeployedPath が相対になる

        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) => Task.FromResult(1);
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task RunSqlDeploy_DryRun_StillInvokesCopyOverride()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");

        var copies = 0;
        var svc = CreateService(dryRun: true);
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) =>
        {
            copies++;
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Equal(1, copies);
    }

    [Fact]
    public async Task RunSqlDeploy_DoesNotCopyHoldOrManual()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "ok.sql");
        WriteSql(config.DeployedHoldPath, "hold.sql");
        WriteSql(config.DeployedManualPath, "manual.sql");

        var sources = new List<string>();
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (src, _, _, _) =>
        {
            sources.Add(Path.GetFullPath(src));
            return Task.FromResult(1);
        };
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.All(sources, s =>
        {
            Assert.NotEqual(Path.GetFullPath(config.DeployedHoldPath), s);
            Assert.NotEqual(Path.GetFullPath(config.DeployedManualPath), s);
        });
        Assert.Contains(Path.GetFullPath(config.DeployedPath), sources);
    }

    [Fact]
    public async Task RunSqlDeploy_DryRun_ViewReplaceScansBothSources()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "v1.sql", "CREATE VIEW dbo.V AS SELECT 1 FROM KaiosDB.dbo.T;");
        WriteSql(config.MariaDbDeployedPath, "v2.sql", "CREATE VIEW dbo.V2 AS SELECT 1 FROM KaiosDB.dbo.T;");
        config.PilotSqlDbNameReplacements =
        [
            new PilotDbNameReplacement { From = "KaiosDB", To = "KaiosDB_pilot" },
        ];

        var svc = CreateService(dryRun: true);
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) => Task.FromResult(1);
        var logs = new List<string>();
        var result = await svc.RunSqlDeployAsync(config, line => logs.Add(line), CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Contains(logs, l => l.Contains(config.DeployedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Contains(config.MariaDbDeployedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_SqlOnly_Skipped_LogsSkipMessage()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var svc = CreateService();
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) => Task.FromResult(1);
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var (_, sql) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.SqlOnly);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(sql!.Skipped);
        Assert.Contains(messages, m => m.Contains("SQL適用: スキップ（適用対象 SQL なし）", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m == "SQL適用: 完了しました");
    }

    [Fact]
    public async Task ExecuteAsync_Both_Skipped_LogsSkipMessage()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var pilot1 = Path.Combine(_root, "pilot1");
        Directory.CreateDirectory(webSrc);
        Directory.CreateDirectory(pilot1);
        File.WriteAllText(Path.Combine(webSrc, "Web.config.DC.kaios.pilot"), "<configuration />");

        var config = CreateConfig();
        config.WebSourcePath = webSrc;
        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];
        Directory.CreateDirectory(config.DeployedPath);
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var svc = CreateService(dryRun: true);
        svc.RunRobocopyOverride = (_, _, _, _) => Task.FromResult(1);
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) => Task.FromResult(1);
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var (_, sql) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.Both);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(sql!.Skipped);
        Assert.Contains(messages, m => m.Contains("SQL適用: スキップ（適用対象 SQL なし）", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_Files_UsesFilesPath_SkipsWhenEmpty()
    {
        var webSrc = Path.Combine(_root, "WebSrc2");
        var pilot1 = Path.Combine(_root, "pilot1b");
        Directory.CreateDirectory(webSrc);
        Directory.CreateDirectory(pilot1);
        File.WriteAllText(Path.Combine(webSrc, "Web.config.DC.kaios.pilot"), "<configuration />");

        var config = CreateConfig();
        config.WebSourcePath = webSrc;
        config.FilesDeploy2PrdPath = Path.Combine(_root, "should-not-use");
        Directory.CreateDirectory(config.FilesDeploy2PrdPath);
        File.WriteAllText(Path.Combine(config.FilesDeploy2PrdPath, "x.png"), "x");
        // FilesPath は空（ディレクトリのみ）
        Directory.CreateDirectory(config.FilesPath);

        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];
        WriteSql(config.DeployedPath, "a.sql"); // SQL は成功経路へ

        var robocopyCalls = new List<(string Src, string Dest)>();
        var channel = Channel.CreateUnbounded<LogEntry>();
        var svc = CreateService(dryRun: true);
        svc.RunRobocopyOverride = (src, dest, _, _) =>
        {
            robocopyCalls.Add((Path.GetFullPath(src), Path.GetFullPath(dest)));
            return Task.FromResult(1);
        };
        svc.CopyPilotSqlFilesOverride = (_, _, _, _) => Task.FromResult(1);
        svc.RunDeployBatOverride = (_, _, _, _) => Task.FromResult(0);

        var (targets, _) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);
        channel.Writer.Complete();

        Assert.True(targets.All(t => t.Success));
        Assert.DoesNotContain(robocopyCalls, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.FilesDeploy2PrdPath));
        Assert.DoesNotContain(robocopyCalls, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.FilesPath));
    }

    [Fact]
    public async Task ExecuteAsync_Files_CopiesWhenFilesExist()
    {
        var webSrc = Path.Combine(_root, "WebSrc3");
        var pilot1 = Path.Combine(_root, "pilot1c");
        Directory.CreateDirectory(webSrc);
        Directory.CreateDirectory(pilot1);
        File.WriteAllText(Path.Combine(webSrc, "Web.config.DC.kaios.pilot"), "<configuration />");

        var config = CreateConfig();
        config.WebSourcePath = webSrc;
        Directory.CreateDirectory(Path.Combine(config.FilesPath, "Images"));
        File.WriteAllText(Path.Combine(config.FilesPath, "Images", "a.png"), "x");
        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];

        var robocopyCalls = new List<string>();
        var channel = Channel.CreateUnbounded<LogEntry>();
        var svc = CreateService(dryRun: true);
        svc.RunRobocopyOverride = (src, _, _, _) =>
        {
            robocopyCalls.Add(Path.GetFullPath(src));
            return Task.FromResult(1);
        };

        var (targets, _) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);
        channel.Writer.Complete();

        Assert.True(targets.Single().Success);
        Assert.Contains(Path.GetFullPath(config.FilesPath), robocopyCalls);
    }
}
