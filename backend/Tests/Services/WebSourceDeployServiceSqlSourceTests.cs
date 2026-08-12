using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// Issue #35: Pilot SQL コピー元を DeployedPath / MariaDbDeployedPath に切替え、
/// *.sql 専用経路・空スキップ・IProcessRunner 注入による呼出検証を固定する。
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

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<(string FileName, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName,
            string arguments,
            string? workingDirectory,
            Action<string> onOutputLine,
            CancellationToken ct)
        {
            Calls.Add((fileName, arguments, workingDirectory));
            // robocopy 成功範囲の代表値 1、bat/cmd は 0
            return Task.FromResult(
                string.Equals(fileName, "robocopy.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        public IEnumerable<(string Src, string Dest)> RobocopyCopies =>
            Calls
                .Where(c => string.Equals(c.FileName, "robocopy.exe", StringComparison.OrdinalIgnoreCase))
                .Select(c => ParseRobocopyPaths(c.Arguments))
                .Where(p => p is not null)
                .Select(p => p!.Value);

        public int BatCallCount =>
            Calls.Count(c => string.Equals(c.FileName, "cmd.exe", StringComparison.OrdinalIgnoreCase));

        public bool RanBat(string batPath, string workingDirectory) =>
            Calls.Any(c =>
                string.Equals(c.FileName, "cmd.exe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase)
                && c.Arguments.Contains(batPath, StringComparison.OrdinalIgnoreCase));

        private static (string Src, string Dest)? ParseRobocopyPaths(string args)
        {
            var matches = Regex.Matches(args, "\"([^\"]+)\"");
            if (matches.Count < 2) return null;
            return (matches[0].Groups[1].Value, matches[1].Groups[1].Value);
        }
    }

    private static (WebSourceDeployService Svc, FakeProcessRunner Runner) CreateService(bool dryRun = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = dryRun.ToString() })
            .Build();
        var runner = new FakeProcessRunner();
        var svc = new WebSourceDeployService(
            configuration,
            NullLogger<WebSourceDeployService>.Instance,
            runner);
        return (svc, runner);
    }

    private DbConfig CreateConfig()
    {
        var deployDev2Stg = Path.Combine(_root, "Deploy_DEV2STG");
        var pilotSql = Path.Combine(_root, "PilotSql");
        var pilotMaria = Path.Combine(_root, "PilotMariaDb");
        Directory.CreateDirectory(deployDev2Stg);
        Directory.CreateDirectory(pilotSql);
        Directory.CreateDirectory(pilotMaria);
        File.WriteAllText(Path.Combine(pilotSql, "deploy.bat"), "@echo off\r\nexit /b 0\r\n");
        File.WriteAllText(Path.Combine(pilotMaria, "deploy.bat"), "@echo off\r\nexit /b 0\r\n");

        return new DbConfig
        {
            Name = "kaios",
            DeployDev2StgPath = deployDev2Stg,
            PilotSqlDeployPath = pilotSql,
            PilotMariaDbSqlDeployPath = pilotMaria,
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
        Directory.CreateDirectory(Path.Combine(_root, "Deploy2Prd"));
        config.Deploy2PrdPath = Path.Combine(_root, "Deploy2Prd");
        WriteSql(config.Deploy2PrdPath, "prd-only.sql");

        var (svc, runner) = CreateService();
        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.False(result.Skipped);
        var copies = runner.RobocopyCopies.ToList();
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.DeployedPath)
            && Path.GetFullPath(c.Dest) == Path.GetFullPath(config.PilotSqlDeploySourcePath));
        Assert.Contains(copies, c =>
            Path.GetFullPath(c.Src) == Path.GetFullPath(config.MariaDbDeployedPath)
            && Path.GetFullPath(c.Dest) == Path.GetFullPath(config.PilotMariaDbSqlDeploySourcePath));
        Assert.DoesNotContain(copies, c => Path.GetFullPath(c.Src) == Path.GetFullPath(config.Deploy2PrdPath));
        Assert.Equal(2, runner.BatCallCount);
    }

    [Fact]
    public async Task RunSqlDeploy_BothEmpty_SkipsBeforeBat_SetsSkipped()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var (svc, runner) = CreateService();
        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.True(result.Skipped);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RunSqlDeploy_SqlServerOnly_CopiesOneSide()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "only-ss.sql");
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var (svc, runner) = CreateService();
        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        var copies = runner.RobocopyCopies.ToList();
        Assert.Single(copies);
        Assert.Equal(Path.GetFullPath(config.DeployedPath), Path.GetFullPath(copies[0].Src));
        Assert.Equal(1, runner.BatCallCount);
        Assert.True(runner.RanBat(config.PilotSqlDeployBatPath, config.PilotSqlDeployPath));
    }

    [Fact]
    public async Task RunSqlDeploy_MariaDbOnly_CopiesToPilotMariaDbSource_AndRunsMariaBat()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        WriteSql(config.MariaDbDeployedPath, "only-mdb.sql");

        var (svc, runner) = CreateService();
        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        var copies = runner.RobocopyCopies.ToList();
        Assert.Single(copies);
        Assert.Equal(Path.GetFullPath(config.MariaDbDeployedPath), Path.GetFullPath(copies[0].Src));
        Assert.Equal(Path.GetFullPath(config.PilotMariaDbSqlDeploySourcePath), Path.GetFullPath(copies[0].Dest));
        Assert.Equal(1, runner.BatCallCount);
        Assert.True(runner.RanBat(config.PilotMariaDbSqlDeployBatPath, config.PilotMariaDbSqlDeployPath));
    }

    [Fact]
    public async Task RunSqlDeploy_MariaDbFiles_WithoutPilotMariaPath_Throws()
    {
        var config = CreateConfig();
        config.PilotMariaDbSqlDeployPath = "";
        WriteSql(config.MariaDbDeployedPath, "mdb.sql");

        var (svc, _) = CreateService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None));
        Assert.Contains("PilotMariaDbSqlDeployPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSqlDeploy_UnsetDeployDev2StgPath_ThrowsBeforeSkip()
    {
        var config = CreateConfig();
        config.DeployDev2StgPath = "";

        var (svc, _) = CreateService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None));
        Assert.Contains("DeployDev2StgPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSqlDeploy_RelativePilotSqlDeployPath_ThrowsBeforeSourceDelete()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");

        var relName = $"mm-sentinel-ss-{Guid.NewGuid():N}";
        var absRoot = Path.Combine(Directory.GetCurrentDirectory(), relName);
        var sourceDir = Path.Combine(absRoot, "Source");
        Directory.CreateDirectory(sourceDir);
        var sentinel = Path.Combine(sourceDir, "keep-me.txt");
        File.WriteAllText(sentinel, "sentinel");

        try
        {
            config.PilotSqlDeployPath = relName;
            var (svc, runner) = CreateService();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None));

            Assert.Contains("PilotSqlDeployPath", ex.Message, StringComparison.Ordinal);
            Assert.Contains("設定を確認してください", ex.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Calls);
            // ガードが Delete より前であることの直接検証（間に移動した回帰を捕まえる）
            Assert.True(File.Exists(sentinel), "Source 初期化（再帰削除）がガードより先に走ってはならない");
        }
        finally
        {
            if (Directory.Exists(absRoot))
                Directory.Delete(absRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunSqlDeploy_RelativePilotMariaDbPath_ThrowsBeforeSourceDelete()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        WriteSql(config.MariaDbDeployedPath, "mdb.sql");

        var relName = $"mm-sentinel-mdb-{Guid.NewGuid():N}";
        var absRoot = Path.Combine(Directory.GetCurrentDirectory(), relName);
        var sourceDir = Path.Combine(absRoot, "Source");
        Directory.CreateDirectory(sourceDir);
        var sentinel = Path.Combine(sourceDir, "keep-me.txt");
        File.WriteAllText(sentinel, "sentinel");

        try
        {
            config.PilotMariaDbSqlDeployPath = relName;
            var (svc, runner) = CreateService();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None));

            Assert.Contains("PilotMariaDbSqlDeployPath", ex.Message, StringComparison.Ordinal);
            Assert.Contains("設定を確認してください", ex.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Calls);
            Assert.True(File.Exists(sentinel), "Source 初期化（再帰削除）がガードより先に走ってはならない");
        }
        finally
        {
            if (Directory.Exists(absRoot))
                Directory.Delete(absRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunSqlDeploy_Success_ExitCodeIsNull()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");

        var (svc, _) = CreateService();
        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task RunSqlDeploy_DryRun_DoesNotInvokeProcessRunner_ButLogsRobocopyArgs()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");

        var logs = new List<string>();
        var (svc, runner) = CreateService(dryRun: true);
        var result = await svc.RunSqlDeployAsync(config, line => logs.Add(line), CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Empty(runner.Calls);
        Assert.Contains(logs, l =>
            l.Contains("[DRY-RUN] robocopy", StringComparison.Ordinal)
            && l.Contains(config.DeployedPath, StringComparison.OrdinalIgnoreCase)
            && l.Contains("*.sql", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_DoesNotCopyHoldOrManual()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "ok.sql");
        WriteSql(config.DeployedHoldPath, "hold.sql");
        WriteSql(config.DeployedManualPath, "manual.sql");

        var (svc, runner) = CreateService();
        await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        var sources = runner.RobocopyCopies.Select(c => Path.GetFullPath(c.Src)).ToList();
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

        var (svc, _) = CreateService(dryRun: true);
        var logs = new List<string>();
        var result = await svc.RunSqlDeployAsync(config, line => logs.Add(line), CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Contains(logs, l => l.Contains(config.DeployedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Contains(config.MariaDbDeployedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunSqlDeploy_DryRun_SqlServerOnly_DoesNotWarnMissingMariaDbDir()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "v1.sql", "CREATE VIEW dbo.V AS SELECT 1 FROM KaiosDB.dbo.T;");
        // MariaDB deployed は作らない（無い側は走査しない）
        config.PilotSqlDbNameReplacements =
        [
            new PilotDbNameReplacement { From = "KaiosDB", To = "KaiosDB_pilot" },
        ];

        var (svc, _) = CreateService(dryRun: true);
        var logs = new List<string>();
        await svc.RunSqlDeployAsync(config, line => logs.Add(line), CancellationToken.None);

        Assert.DoesNotContain(logs, l =>
            l.Contains("走査先ディレクトリが存在しません", StringComparison.Ordinal)
            && l.Contains("MariaDB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_SqlOnly_Skipped_LogsSkipMessage()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.DeployedPath);
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var (svc, _) = CreateService();

        var (_, sql) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.SqlOnly);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(sql!.Skipped);
        Assert.Contains(messages, m => m.Contains("SQL適用: スキップ（適用対象 SQL なし）", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m == "SQL適用: 完了しました");
        Assert.Contains(messages, m => m.Contains("スキップしました（適用対象なし）", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m == "✅ Pilot環境適用が完了しました");
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
        var (svc, _) = CreateService(dryRun: true);

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
        Directory.CreateDirectory(config.FilesPath);

        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];
        WriteSql(config.DeployedPath, "a.sql");

        var channel = Channel.CreateUnbounded<LogEntry>();
        var (svc, _) = CreateService(dryRun: true);

        var (targets, _) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(targets.All(t => t.Success));
        Assert.DoesNotContain(messages, m =>
            m.Contains("[DRY-RUN] robocopy", StringComparison.Ordinal)
            && m.Contains(config.FilesDeploy2PrdPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m =>
            m.Contains("[DRY-RUN] robocopy", StringComparison.Ordinal)
            && m.Contains(config.FilesPath, StringComparison.OrdinalIgnoreCase));
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

        var channel = Channel.CreateUnbounded<LogEntry>();
        var (svc, _) = CreateService(dryRun: true);

        var (targets, _) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(targets.Single().Success);
        Assert.Contains(messages, m =>
            m.Contains("[DRY-RUN] robocopy", StringComparison.Ordinal)
            && m.Contains(config.FilesPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPilotSqlRobocopyArgs_IncludesSqlFileClass_AndNotSharedWithWebArgs()
    {
        var src = @"D:\src";
        var dest = @"D:\dest";
        var args = WebSourceDeployService.BuildPilotSqlRobocopyArgs(src, dest);

        Assert.Contains("*.sql", args, StringComparison.Ordinal);
        Assert.Contains("/E", args, StringComparison.Ordinal);
        Assert.Contains("/MT:32", args, StringComparison.Ordinal);
        Assert.Contains($"\"{src}\"", args, StringComparison.Ordinal);
        Assert.Contains($"\"{dest}\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("/XF", args, StringComparison.Ordinal);
        Assert.DoesNotContain("/XD", args, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatOverallCompletionMessage_SkippedOnly_IsNotCompleted()
    {
        Assert.Equal(
            "⏭ Pilot環境適用をスキップしました（適用対象なし）",
            WebSourceDeployService.FormatOverallCompletionMessage(failed: false, skippedOnly: true));
        Assert.Equal(
            "✅ Pilot環境適用が完了しました",
            WebSourceDeployService.FormatOverallCompletionMessage(failed: false, skippedOnly: false));
    }
}
