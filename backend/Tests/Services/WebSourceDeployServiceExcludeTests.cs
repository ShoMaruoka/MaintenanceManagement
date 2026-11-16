using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// pilot-webconfig-exclude T1: Web ソースのコピーで、コピー元ルート直下の
/// Web.config（設定次第）/ AppSettings.Local.config / connectionStrings.local.config をフルパスで除外する。
/// 画像情報準備・共通画像のコピーには除外を付けない。
/// </summary>
public class WebSourceDeployServiceExcludeTests : IDisposable
{
    private readonly string _root;

    public WebSourceDeployServiceExcludeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ws-excl-{Guid.NewGuid():N}");
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
        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName, string arguments, string? workingDirectory,
            Action<string> onOutputLine, CancellationToken ct)
        {
            Calls.Add((fileName, arguments));
            return Task.FromResult(
                string.Equals(fileName, "robocopy.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        public List<string> RobocopyArgs =>
            Calls.Where(c => string.Equals(c.FileName, "robocopy.exe", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Arguments)
                .ToList();
    }

    private static (WebSourceDeployService Svc, FakeProcessRunner Runner) CreateService(bool dryRun = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = dryRun.ToString() })
            .Build();
        var runner = new FakeProcessRunner();
        var svc = new WebSourceDeployService(configuration, NullLogger<WebSourceDeployService>.Instance, runner);
        return (svc, runner);
    }

    /// <summary>
    /// Web ソース・pilot1・画像情報準備（Images）・共通画像を持つ構成を作る。
    /// web.config 切替 bat はコピー元・コピー先の両方に置く（DryRun / 実行の両方で通るように）。
    /// </summary>
    private DbConfig CreateConfig(string webSrc)
    {
        var pilot1 = Path.Combine(_root, "pilot1");
        var deployDev2Stg = Path.Combine(_root, "Deploy_DEV2STG");
        var commonImage = Path.Combine(_root, "CommonImage");
        foreach (var dir in new[] { webSrc, pilot1, commonImage })
            Directory.CreateDirectory(dir);
        foreach (var dir in new[] { webSrc, pilot1 })
        {
            Directory.CreateDirectory(Path.Combine(dir, "deploy"));
            File.WriteAllText(Path.Combine(dir, "deploy", "kaios.DC.Pilot.bat"), "@echo off\r\n");
        }

        var config = new DbConfig
        {
            Name = "kaios",
            DeployDev2StgPath = deployDev2Stg,
            WebSourcePath = webSrc,
            CommonImagePath = commonImage,
            PilotTargets =
            [
                new PilotTarget
                {
                    Name = "pilot1",
                    DestWebSourcePath = pilot1,
                    DestImagePath = Path.Combine(pilot1, "Images", "products"),
                },
            ],
        };
        // 画像情報準備（Images）にファイルを1件置き、Files コピーも走らせる。
        Directory.CreateDirectory(Path.Combine(config.FilesPath, "Images"));
        File.WriteAllText(Path.Combine(config.FilesPath, "Images", "a.png"), "x");
        return config;
    }

    private static async Task<List<string>> RunWebOnly(
        WebSourceDeployService svc, DbConfig config, bool excludeWebConfig)
    {
        var channel = Channel.CreateUnbounded<LogEntry>();
        await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None,
            WebSourceDeployStep.WebOnly, excludeWebConfig);
        channel.Writer.Complete();
        var messages = new List<string>();
        await foreach (var e in channel.Reader.ReadAllAsync())
            messages.Add(e.Message);
        return messages;
    }

    private static string Quoted(string dir, string file) => $"\"{Path.Combine(dir, file)}\"";

    [Fact]
    public void BuildWebSourceRootExcludes_WithoutWebConfig_ReturnsLocalConfigsOnly()
    {
        var excludes = WebSourceDeployService.BuildWebSourceRootExcludes(excludeWebConfig: false);

        Assert.Equal(["AppSettings.Local.config", "connectionStrings.local.config"], excludes);
    }

    [Fact]
    public void BuildWebSourceRootExcludes_WithWebConfig_AddsWebConfig()
    {
        var excludes = WebSourceDeployService.BuildWebSourceRootExcludes(excludeWebConfig: true);

        Assert.Equal(["Web.config", "AppSettings.Local.config", "connectionStrings.local.config"], excludes);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludeWebConfigOn_WebSourceCopyExcludesThreeRootFilesByFullPath()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, runner) = CreateService();

        await RunWebOnly(svc, config, excludeWebConfig: true);

        var webArgs = runner.RobocopyArgs.First();
        Assert.StartsWith($"\"{webSrc}\" \"{config.PilotTargets[0].DestWebSourcePath}\"", webArgs, StringComparison.Ordinal);
        Assert.Contains(Quoted(webSrc, "Web.config"), webArgs, StringComparison.Ordinal);
        Assert.Contains(Quoted(webSrc, "AppSettings.Local.config"), webArgs, StringComparison.Ordinal);
        Assert.Contains(Quoted(webSrc, "connectionStrings.local.config"), webArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludeWebConfigOff_WebSourceCopyExcludesLocalConfigsOnly()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, runner) = CreateService();

        await RunWebOnly(svc, config, excludeWebConfig: false);

        var webArgs = runner.RobocopyArgs.First();
        Assert.DoesNotContain("Web.config", webArgs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Quoted(webSrc, "AppSettings.Local.config"), webArgs, StringComparison.Ordinal);
        Assert.Contains(Quoted(webSrc, "connectionStrings.local.config"), webArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_FilesAndCommonImageCopies_HaveNoRootExcludes()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, runner) = CreateService();

        await RunWebOnly(svc, config, excludeWebConfig: true);

        // Web ソース・画像情報準備 Images・共通画像 の3回。
        var args = runner.RobocopyArgs;
        Assert.Equal(3, args.Count);
        foreach (var other in args.Skip(1))
        {
            Assert.DoesNotContain("Web.config", other, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Local.config", other, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WebSourcePathWithTrailingSeparator_StillBuildsSingleSeparatorFullPath()
    {
        var webSrcNoSep = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrcNoSep);
        config.WebSourcePath = webSrcNoSep + Path.DirectorySeparatorChar;
        var (svc, runner) = CreateService();

        await RunWebOnly(svc, config, excludeWebConfig: true);

        var webArgs = runner.RobocopyArgs.First();
        Assert.Contains(Quoted(webSrcNoSep, "Web.config"), webArgs, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}Web.config", webArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_LogsRobocopyArgsWithExcludes()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, runner) = CreateService(dryRun: true);

        var messages = await RunWebOnly(svc, config, excludeWebConfig: true);

        Assert.Empty(runner.Calls);
        Assert.Contains(messages, m =>
            m.StartsWith("[DRY-RUN] robocopy", StringComparison.Ordinal)
            && m.Contains(Quoted(webSrc, "Web.config"), StringComparison.Ordinal)
            && m.Contains(Quoted(webSrc, "AppSettings.Local.config"), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "Web.config コピー: しない（設定: ON）")]
    [InlineData(false, "Web.config コピー: する（設定: OFF）")]
    public async Task ExecuteAsync_LogsWebConfigSettingAtStart(bool excludeWebConfig, string expected)
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, _) = CreateService();

        var messages = await RunWebOnly(svc, config, excludeWebConfig);

        Assert.Contains(expected, messages);
    }

    [Fact]
    public async Task ExecuteAsync_LogsExcludedFilesPerTarget()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, _) = CreateService();

        var messages = await RunWebOnly(svc, config, excludeWebConfig: true);

        Assert.Contains(
            "pilot1: コピー除外 Web.config, AppSettings.Local.config, connectionStrings.local.config",
            messages);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultArgument_ExcludesWebConfig()
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var config = CreateConfig(webSrc);
        var (svc, runner) = CreateService();
        var channel = Channel.CreateUnbounded<LogEntry>();

        await svc.ExecuteAsync(config, channel.Writer, CancellationToken.None, WebSourceDeployStep.WebOnly);

        Assert.Contains(Quoted(webSrc, "Web.config"), runner.RobocopyArgs.First(), StringComparison.Ordinal);
    }
}
