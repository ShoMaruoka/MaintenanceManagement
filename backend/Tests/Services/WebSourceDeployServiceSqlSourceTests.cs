using System.Text;
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

        /// <summary>cmd.exe の終了コード。既定 0。</summary>
        public int CmdExitCode { get; set; }

        /// <summary>cmd.exe 起動時、終了コードを返す直前に呼ぶ。既定は何もしない。</summary>
        public Action<string?>? OnCmd { get; set; }

        public Task<int> RunAsync(
            string fileName,
            string arguments,
            string? workingDirectory,
            Action<string> onOutputLine,
            CancellationToken ct)
        {
            Calls.Add((fileName, arguments, workingDirectory));
            if (string.Equals(fileName, "cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                OnCmd?.Invoke(workingDirectory);
                return Task.FromResult(CmdExitCode);
            }

            // robocopy 成功範囲の代表値 1
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
        Assert.Equal("✅ Pilot環境適用が完了しました", messages[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_Both_SqlErrorLog_FinalLineIsInterrupted()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var pilot1 = Path.Combine(_root, "pilot1");
        Directory.CreateDirectory(webSrc);
        Directory.CreateDirectory(pilot1);
        // Fake はファイルを運ばない。非 DryRun の web.config 適用はコピー先を見る。
        File.WriteAllText(Path.Combine(pilot1, "Web.config.DC.kaios.pilot"), "<configuration />");

        var config = CreateConfig();
        config.WebSourcePath = webSrc;
        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];
        WriteSql(config.DeployedPath, "a.sql");
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, "列名が無効です");

        var channel = Channel.CreateUnbounded<LogEntry>();
        var (targets, sql) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.Both);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.True(targets.Single().Success);
        Assert.False(sql!.Success);
        var failIndex = messages.FindIndex(m => m.Contains("SQL適用: 失敗しました", StringComparison.Ordinal));
        Assert.True(failIndex >= 0);
        Assert.Equal("❌ Pilot環境適用が中断されました", messages[^1]);
        Assert.True(failIndex < messages.Count - 1);
    }

    [Fact]
    public async Task ExecuteAsync_SqlOnly_ErrorLog_FinalLineIsInterrupted()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        Directory.CreateDirectory(config.MariaDbDeployedPath);

        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, "列名が無効です");

        var channel = Channel.CreateUnbounded<LogEntry>();
        var (_, sql) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.SqlOnly);
        channel.Writer.Complete();

        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);

        Assert.False(sql!.Success);
        Assert.Contains(messages, m => m.Contains("SQL適用: 失敗しました", StringComparison.Ordinal));
        Assert.Equal("❌ Pilot環境適用が中断されました", messages[^1]);
    }

    private static List<(string Src, string Dest)> ParseDryRunCopies(IEnumerable<string> messages)
    {
        var result = new List<(string, string)>();
        foreach (var m in messages)
        {
            if (!m.Contains("[DRY-RUN] robocopy", StringComparison.Ordinal))
                continue;
            var matches = Regex.Matches(m, "\"([^\"]+)\"");
            if (matches.Count < 2)
                continue;
            result.Add((matches[0].Groups[1].Value, matches[1].Groups[1].Value));
        }
        return result;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private async Task<(List<string> Messages, IReadOnlyList<WebSourceDeployTargetResult> Targets)> RunWebOnlyDryRun(
        DbConfig config)
    {
        var channel = Channel.CreateUnbounded<LogEntry>();
        var (svc, _) = CreateService(dryRun: true);
        var (targets, _) = await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);
        channel.Writer.Complete();
        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);
        return (messages, targets);
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

        var (messages, targets) = await RunWebOnlyDryRun(config);
        var copies = ParseDryRunCopies(messages);

        Assert.True(targets.All(t => t.Success));
        Assert.DoesNotContain(copies, c =>
            c.Src.Contains(config.FilesDeploy2PrdPath, StringComparison.OrdinalIgnoreCase)
            || c.Dest.Contains(config.FilesDeploy2PrdPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(copies, c => SamePath(c.Src, config.FilesPath));
        Assert.Contains(messages, m => m.Contains("画像情報準備の適用対象なし", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ImagePrepare_CopiesCategoryToDestWebSourcePath()
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
        Directory.CreateDirectory(Path.Combine(config.FilesPath, "news"));
        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = "" },
        ];

        var (messages, targets) = await RunWebOnlyDryRun(config);
        var copies = ParseDryRunCopies(messages);

        Assert.True(targets.Single().Success);
        Assert.Contains(copies, c =>
            SamePath(c.Src, Path.Combine(config.FilesPath, "Images"))
            && SamePath(c.Dest, Path.Combine(pilot1, "Images")));
        Assert.DoesNotContain(copies, c => SamePath(c.Src, config.FilesPath));
        Assert.DoesNotContain(copies, c =>
            SamePath(c.Src, Path.Combine(config.FilesPath, "news"))
            || SamePath(c.Src, Path.Combine(config.FilesPath, "pdf")));
        Assert.Contains(messages, m =>
            m.Contains("news", StringComparison.Ordinal) && m.Contains("スキップ", StringComparison.Ordinal));
        Assert.Contains(messages, m =>
            m.Contains("pdf", StringComparison.Ordinal) && m.Contains("スキップ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ImagePrepare_KeepsCommonImageCopyUnchanged()
    {
        var webSrc = Path.Combine(_root, "WebSrc4");
        var pilot1 = Path.Combine(_root, "pilot1d");
        var common = Path.Combine(_root, "Common_Image");
        var destImage = Path.Combine(pilot1, "Images", "products");
        Directory.CreateDirectory(webSrc);
        Directory.CreateDirectory(pilot1);
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(webSrc, "Web.config.DC.kaios.pilot"), "<configuration />");
        File.WriteAllText(Path.Combine(common, "shared.png"), "x");

        var config = CreateConfig();
        config.WebSourcePath = webSrc;
        config.CommonImagePath = common;
        Directory.CreateDirectory(Path.Combine(config.FilesPath, "Images"));
        File.WriteAllText(Path.Combine(config.FilesPath, "Images", "a.png"), "x");
        config.PilotTargets =
        [
            new PilotTarget { Name = "pilot1", DestWebSourcePath = pilot1, DestImagePath = destImage },
        ];

        var (messages, targets) = await RunWebOnlyDryRun(config);
        var copies = ParseDryRunCopies(messages);

        Assert.True(targets.Single().Success);
        Assert.Contains(copies, c => SamePath(c.Src, common) && SamePath(c.Dest, destImage));
        Assert.Contains(copies, c =>
            SamePath(c.Src, Path.Combine(config.FilesPath, "Images"))
            && SamePath(c.Dest, Path.Combine(pilot1, "Images")));
        Assert.Contains(messages, m => m.Contains("画像コピー開始", StringComparison.Ordinal));
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

    private static readonly Encoding Sjis = Encoding.GetEncoding("shift_jis");

    private string ErrorLogPath() => Path.Combine(_root, $"deployerror-{Guid.NewGuid():N}.log");

    private static SqlDeployErrorLogReadResult ReadErrorLog(
        string path, SqlDeployErrorLogSnapshot before, DateTime startedAtUtc) =>
        SqlDeployErrorLog.ReadNewContent(path, before, startedAtUtc, Sjis);

    [Fact]
    public void ReadNewContent_NewFileWithText_IsErrorWithWholeFile()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "Msg 207 列名が無効です", Sjis);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);

        var result = ReadErrorLog(path, SqlDeployErrorLogSnapshot.Absent, startedAt);

        Assert.True(result.HasNewError);
        Assert.False(result.Unreadable);
        Assert.Equal("Msg 207 列名が無効です", result.Content);
    }

    [Fact]
    public void ReadNewContent_AppendedBytes_ReturnsOnlyTheNewRange()
    {
        var path = ErrorLogPath();
        var prefix = Sjis.GetBytes("previous\r\n");
        var added = Sjis.GetBytes("列名 'Unknown' が無効です");
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.WriteAllBytes(path, prefix);
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-1));
        var before = SqlDeployErrorLog.Capture(path);
        File.WriteAllBytes(path, prefix.Concat(added).ToArray());

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.Equal("列名 'Unknown' が無効です", result.Content);
    }

    [Fact]
    public void ReadNewContent_UnchangedFile_IsNotError()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "yesterday's error", Sjis);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        var writtenAt = startedAt.AddHours(-5);
        File.SetLastWriteTimeUtc(path, writtenAt);
        var before = SqlDeployErrorLog.Capture(path);

        var result = ReadErrorLog(path, before, startedAt);

        Assert.False(result.HasNewError);
        Assert.Null(result.Content);
    }

    [Fact]
    public void ReadNewContent_EmptyFile_IsNotError()
    {
        var path = ErrorLogPath();
        File.WriteAllBytes(path, []);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);

        var result = ReadErrorLog(path, SqlDeployErrorLogSnapshot.Absent, startedAt);

        Assert.False(result.HasNewError);
    }

    [Fact]
    public void ReadNewContent_WhitespaceOnlyAppend_IsNotError()
    {
        var path = ErrorLogPath();
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(path, "ABC", Sjis);
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-1));
        var before = SqlDeployErrorLog.Capture(path);
        File.AppendAllText(path, "   \r\n", Sjis);

        var result = ReadErrorLog(path, before, startedAt);

        Assert.False(result.HasNewError);
        Assert.Null(result.Content);
    }

    [Fact]
    public void ReadNewContent_SameSizeTouchedWithinSkew_ReturnsWholeFile()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "NEW!", Sjis);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, startedAt.AddSeconds(-1));
        var before = new SqlDeployErrorLogSnapshot(true, 4, startedAt.AddHours(-1));

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.Equal("NEW!", result.Content);
    }

    [Fact]
    public void ReadNewContent_ShrunkAndTouched_ReturnsWholeFile()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "ERR", Sjis);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, startedAt);
        var before = new SqlDeployErrorLogSnapshot(true, 10, startedAt.AddHours(-1));

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.Equal("ERR", result.Content);
    }

    [Fact]
    public void ReadNewContent_OldTimestampWithoutGrowth_IsNotError()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "OLD!", Sjis);
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, startedAt.AddSeconds(-3));
        var before = new SqlDeployErrorLogSnapshot(true, 4, startedAt.AddHours(-1));

        var result = ReadErrorLog(path, before, startedAt);

        Assert.False(result.HasNewError);
    }

    [Fact]
    public void ReadNewContent_InvalidShiftJisAppend_IsUnreadableError()
    {
        var path = ErrorLogPath();
        var prefix = Sjis.GetBytes("OK\r\n");
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.WriteAllBytes(path, prefix);
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-1));
        var before = SqlDeployErrorLog.Capture(path);
        // 0x81 は Shift-JIS の先行バイト。単体では文字にならず復号できない。
        File.WriteAllBytes(path, prefix.Concat(new byte[] { 0x81 }).ToArray());

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.True(result.Unreadable);
        Assert.Null(result.Content);
    }

    [Fact]
    public void ReadNewContent_LockedUnchangedFile_IsNotError()
    {
        var path = ErrorLogPath();
        File.WriteAllText(path, "old error", Sjis);
        var startedAt = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-5));
        var before = SqlDeployErrorLog.Capture(path);

        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = ReadErrorLog(path, before, startedAt);

        Assert.False(result.HasNewError);
        Assert.False(result.Unreadable);
    }

    [Fact]
    public void ReadNewContent_RewrittenLongerFile_ReturnsWholeFile()
    {
        var path = ErrorLogPath();
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(path, "前回のエラー: 列名 'X' が無効です。\r\n", Sjis);
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-5));
        var before = SqlDeployErrorLog.Capture(path);
        var rewritten = "今回のエラー: プロシージャ usp_Foo の作成に失敗しました。列名 'Y' が無効です。\r\n";
        File.WriteAllText(path, rewritten, Sjis);

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.Equal(rewritten, result.Content);
    }

    [Fact]
    public void ReadNewContent_RewriteCuttingMultibyteChar_IsReadableWholeFile()
    {
        var path = ErrorLogPath();
        var startedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(path, "x", Sjis);
        File.SetLastWriteTimeUtc(path, startedAt.AddHours(-5));
        var before = SqlDeployErrorLog.Capture(path);
        // 0x81 0x81 は 1 文字。1 バイト目で切ると先行バイトだけが残り、復号できない。
        var rewrittenBytes = new byte[] { 0x81, 0x81 };
        var rewritten = Sjis.GetString(rewrittenBytes);
        File.WriteAllBytes(path, rewrittenBytes);

        var strict = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        Assert.Throws<DecoderFallbackException>(() => strict.GetString(rewrittenBytes.AsSpan(1)));

        var result = ReadErrorLog(path, before, startedAt);

        Assert.True(result.HasNewError);
        Assert.False(result.Unreadable);
        Assert.Equal(rewritten, result.Content);
    }

    private static void PinClock(WebSourceDeployService svc, DateTime local) =>
        svc.LocalNow = () => local;

    private static void WriteErrorLog(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Sjis);
    }

    [Fact]
    public async Task RunSqlDeploy_ErrorLogCreatedDuringBat_FailsAndOmitsBodyFromMessage()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, "列名が無効です");
        var logs = new List<string>();

        var result = await svc.RunSqlDeployAsync(config, logs.Add, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Contains("終了コード 0", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(logPath, result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("列名が無効です", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(logs, l => l.Contains("列名が無効です", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_ErrorLogOverLineLimit_TruncatesAndNotesPath()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var body = string.Join("\r\n", Enumerable.Range(0, 60).Select(i => $"err-line-{i:00}"));
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, body);
        var logs = new List<string>();

        var result = await svc.RunSqlDeployAsync(config, logs.Add, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Contains(logs, l => l == "err-line-00");
        Assert.Contains(logs, l => l == "err-line-49");
        Assert.DoesNotContain(logs, l => l == "err-line-50");
        Assert.Contains(logs, l => l.Contains("以降省略", StringComparison.Ordinal) && l.Contains(logPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_ErrorLogOverCharLimit_TruncatesAndNotesPath()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var body = new string('A', 9000);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, body);
        var logs = new List<string>();

        var result = await svc.RunSqlDeployAsync(config, logs.Add, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.DoesNotContain(logs, l => l.Length == 9000);
        Assert.Contains(logs, l => l.Length == 8000 && l.All(c => c == 'A'));
        Assert.Contains(logs, l => l.Contains("以降省略", StringComparison.Ordinal) && l.Contains(logPath, StringComparison.Ordinal));
        Assert.DoesNotContain("AAAA", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSqlDeploy_UnchangedErrorLog_Succeeds()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        WriteErrorLog(logPath, "yesterday error");
        File.SetLastWriteTimeUtc(logPath, now.ToUniversalTime().AddHours(-5));
        var (svc, _) = CreateService();
        PinClock(svc, now);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
    }

    [Fact]
    public async Task RunSqlDeploy_LockedUnchangedErrorLog_Succeeds()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        WriteErrorLog(logPath, "yesterday error");
        File.SetLastWriteTimeUtc(logPath, now.ToUniversalTime().AddHours(-5));
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        FileStream? locked = null;
        runner.OnCmd = _ => locked = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);
            Assert.True(result!.Success);
        }
        finally
        {
            locked?.Dispose();
        }
    }

    [Fact]
    public async Task RunSqlDeploy_ExactlyFiftyLinesWithTrailingNewline_DoesNotSayTruncated()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var body = string.Join("\r\n", Enumerable.Range(0, 50).Select(i => $"err-line-{i:00}")) + "\r\n";
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, body);
        var logs = new List<string>();

        var result = await svc.RunSqlDeployAsync(config, logs.Add, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Contains(logs, l => l == "err-line-00");
        Assert.Contains(logs, l => l == "err-line-49");
        Assert.DoesNotContain(logs, l => l.Length == 0);
        Assert.DoesNotContain(logs, l => l.Contains("以降省略", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_FiftyLinesWithLeadingNewline_DoesNotEmitBlankOrSayTruncated()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var body = "\r\n" + string.Join("\r\n", Enumerable.Range(0, 50).Select(i => $"err-line-{i:00}")) + "\r\n";
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, body);
        var logs = new List<string>();

        var result = await svc.RunSqlDeployAsync(config, logs.Add, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Contains(logs, l => l == "err-line-00");
        Assert.Contains(logs, l => l == "err-line-49");
        Assert.DoesNotContain(logs, l => l.Length == 0);
        Assert.DoesNotContain(logs, l => l.Contains("以降省略", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSqlDeploy_EmptyErrorLog_Succeeds()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, "");

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
    }

    [Fact]
    public async Task RunSqlDeploy_WhitespaceErrorLogAppend_Succeeds()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        WriteErrorLog(logPath, "ABC");
        File.SetLastWriteTimeUtc(logPath, now.ToUniversalTime().AddHours(-5));
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => File.AppendAllText(logPath, "   \r\n", Sjis);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
    }

    [Fact]
    public async Task RunSqlDeploy_BatExitCodeNonZero_WithoutNewErrorLog_KeepsExitMessage()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var (svc, runner) = CreateService();
        runner.CmdExitCode = 1;

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Equal("SQL Server deploy.bat がエラー終了しました (exit code 1)", result.ErrorMessage);
    }

    [Fact]
    public async Task RunSqlDeploy_BatExitCodeNonZero_WithErrorLog_AppendsPath()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.CmdExitCode = 1;
        runner.OnCmd = _ => WriteErrorLog(logPath, "syntax error");

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Equal(
            $"SQL Server deploy.bat がエラー終了しました (exit code 1)。エラーログ: {logPath}",
            result.ErrorMessage);
    }

    [Fact]
    public async Task RunSqlDeploy_SqlServerErrorLog_DoesNotRunMariaDbBat()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        WriteSql(config.MariaDbDeployedPath, "b.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        var (svc, runner) = CreateService();
        PinClock(svc, now);
        runner.OnCmd = _ => WriteErrorLog(logPath, "sql server failed");

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Equal(1, runner.BatCallCount);
        Assert.True(runner.RanBat(config.PilotSqlDeployBatPath, config.PilotSqlDeployPath));
        Assert.False(runner.RanBat(config.PilotMariaDbSqlDeployBatPath, config.PilotMariaDbSqlDeployPath));
    }

    [Fact]
    public async Task RunSqlDeploy_DryRun_IgnoresExistingErrorLog()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var now = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Local);
        var logPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, now);
        WriteErrorLog(logPath, "still failing");
        var (svc, runner) = CreateService(dryRun: true);
        PinClock(svc, now);

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Equal(0, runner.BatCallCount);
    }

    [Fact]
    public async Task RunSqlDeploy_CrossMidnightErrorLog_Fails()
    {
        var config = CreateConfig();
        WriteSql(config.DeployedPath, "a.sql");
        var start = new DateTime(2026, 10, 1, 23, 50, 0, DateTimeKind.Local);
        var end = new DateTime(2026, 10, 2, 0, 10, 0, DateTimeKind.Local);
        var endPath = SqlDeployErrorLog.PathFor(config.PilotSqlDeployPath, end);
        var ticks = new Queue<DateTime>([start, end]);
        var (svc, runner) = CreateService();
        svc.LocalNow = () => ticks.Dequeue();
        runner.OnCmd = _ => WriteErrorLog(endPath, "midnight error");

        var result = await svc.RunSqlDeployAsync(config, _ => { }, CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Contains(endPath, result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("deployerror_20261001.log", result.ErrorMessage, StringComparison.Ordinal);
    }
}
