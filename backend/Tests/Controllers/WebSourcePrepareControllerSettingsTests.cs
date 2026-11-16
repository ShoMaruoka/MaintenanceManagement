using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Controllers;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Controllers;

/// <summary>
/// pilot-webconfig-exclude T3: info への除外設定追加、PUT settings（admin のみ）、
/// 実行時に DB の除外設定を ExecuteAsync へ渡すこと。
/// </summary>
public class WebSourcePrepareControllerSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public WebSourcePrepareControllerSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"wsp-ctrl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        _db = new DatabaseService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = _dbPath })
            .Build());
        _db.EnsureCreated();
        _db.AddUser("adm", "管理者", "admin");
        _db.AddUser("usr", "一般", "user");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class NoProcessRunner : IProcessRunner
    {
        public Task<int> RunAsync(string fileName, string arguments, string? workingDirectory,
            Action<string> onOutputLine, CancellationToken ct) =>
            throw new InvalidOperationException("DryRun のためプロセスは起動しない想定");
    }

    /// <summary>
    /// DbConfigs に kaios（Web ソース・pilot1 あり）を持つ DryRun 構成で Controller を作る。
    /// DryRun の web.config 切替 bat はコピー元を検査するため、WebSourcePath\deploy に置く。
    /// </summary>
    private WebSourcePrepareController CreateController(string dbName = "kaios", string batFileName = "kaios.DC.Pilot.bat")
    {
        var webSrc = Path.Combine(_root, "WebSrc");
        var pilot1 = Path.Combine(_root, "pilot1");
        Directory.CreateDirectory(Path.Combine(webSrc, "deploy"));
        Directory.CreateDirectory(pilot1);
        File.WriteAllText(Path.Combine(webSrc, "deploy", batFileName), "@echo off\r\n");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DryRun"] = "true",
                ["DbConfigs:0:Name"] = dbName,
                ["DbConfigs:0:WebSourcePath"] = webSrc,
                ["DbConfigs:0:DeployDev2StgPath"] = Path.Combine(_root, "Deploy_DEV2STG"),
                ["DbConfigs:0:PilotTargets:0:Name"] = "pilot1",
                ["DbConfigs:0:PilotTargets:0:DestWebSourcePath"] = pilot1,
                ["DbConfigs:0:PilotTargets:0:DestImagePath"] = "",
            })
            .Build();
        var svc = new WebSourceDeployService(config, NullLogger<WebSourceDeployService>.Instance, new NoProcessRunner());
        return new WebSourcePrepareController(svc, _db, config)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public void GetInfo_NotSet_ReturnsDefaultExcludeTrueWithNullUpdater()
    {
        var ctrl = CreateController();

        var ok = Assert.IsType<OkObjectResult>(ctrl.GetInfo("kaios"));
        var info = Assert.IsType<WebSourceInfoResponse>(ok.Value);

        Assert.True(info.ExcludeWebConfig);
        Assert.Null(info.ExcludeWebConfigUpdatedBy);
        Assert.Null(info.ExcludeWebConfigUpdatedAt);
    }

    [Fact]
    public void GetInfo_AfterSet_ReturnsSavedValueAndUpdater()
    {
        _db.SetPilotExcludeWebConfig("kaios", false, "adm");
        var ctrl = CreateController();

        var info = Assert.IsType<WebSourceInfoResponse>(
            Assert.IsType<OkObjectResult>(ctrl.GetInfo("kaios")).Value);

        Assert.False(info.ExcludeWebConfig);
        Assert.Equal("adm", info.ExcludeWebConfigUpdatedBy);
        Assert.NotNull(info.ExcludeWebConfigUpdatedAt);
    }

    [Fact]
    public void UpdateSettings_Admin_SavesAndReturnsSetting()
    {
        var ctrl = CreateController();

        var result = ctrl.UpdateSettings("kaios",
            new WebSourceSettingsRequest { ExcludeWebConfig = false, UpdatedBy = "adm" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<WebSourceSettingsResponse>(ok.Value);
        Assert.False(body.ExcludeWebConfig);
        Assert.Equal("adm", body.UpdatedBy);
        Assert.NotNull(body.UpdatedAt);
        Assert.False(_db.GetPilotExcludeWebConfig("kaios").ExcludeWebConfig);
    }

    [Theory]
    [InlineData("usr")]
    [InlineData("nobody")]
    [InlineData("")]
    public void UpdateSettings_NonAdmin_Returns403AndDoesNotSave(string updatedBy)
    {
        var ctrl = CreateController();

        var result = ctrl.UpdateSettings("kaios",
            new WebSourceSettingsRequest { ExcludeWebConfig = false, UpdatedBy = updatedBy });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Null(_db.GetSetting("PilotExcludeWebConfig.kaios"));
    }

    [Theory]
    [InlineData("foo")]   // 許可されていないシステム
    [InlineData("gos")]   // 許可されているが DbConfigs に無い
    public void UpdateSettings_UnknownSystem_Returns404(string dbName)
    {
        var ctrl = CreateController();

        var result = ctrl.UpdateSettings(dbName,
            new WebSourceSettingsRequest { ExcludeWebConfig = false, UpdatedBy = "adm" });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Null(_db.GetSetting($"PilotExcludeWebConfig.{dbName}"));
    }

    /// <summary>SSE の各 data 行（JSON。日本語は \uXXXX エスケープされる）から message を取り出す。</summary>
    private static async Task<List<string>> RunStream(WebSourcePrepareController ctrl, string dbName = "kaios")
    {
        var body = new MemoryStream();
        ctrl.HttpContext.Response.Body = body;
        await ctrl.StreamDeploy(dbName,
            new WebSourceDeployRequest { ExecutedBy = "usr", Step = "web" }, CancellationToken.None);
        return Encoding.UTF8.GetString(body.ToArray())
            .Split('\n')
            .Where(l => l.StartsWith("data: ", StringComparison.Ordinal))
            .Select(l => JsonDocument.Parse(l["data: ".Length..]).RootElement)
            .Where(e => e.TryGetProperty("message", out _))
            .Select(e => e.GetProperty("message").GetString() ?? "")
            .ToList();
    }

    [Fact]
    public async Task StreamDeploy_UsesDbSettingOff_WebConfigIsCopied()
    {
        _db.SetPilotExcludeWebConfig("kaios", false, "adm");
        var ctrl = CreateController();

        var sse = await RunStream(ctrl);

        Assert.Contains("Web.config コピー: する（設定: OFF）", sse);
        Assert.Contains("pilot1: コピー除外 AppSettings.Local.config, connectionStrings.local.config", sse);
    }

    // ── T5: PAF / Duskin ─────────────────────────────

    [Theory]
    [InlineData("paf")]
    [InlineData("duskin")]
    public void GetInfo_PafAndDuskin_AreAllowed(string dbName)
    {
        var ctrl = CreateController(dbName, $"{dbName}.DC.Pilot.bat");

        var info = Assert.IsType<WebSourceInfoResponse>(
            Assert.IsType<OkObjectResult>(ctrl.GetInfo(dbName)).Value);

        Assert.Equal(dbName, info.DbName);
    }

    [Fact]
    public void GetInfo_UnknownSystem_Returns404()
    {
        var ctrl = CreateController();

        Assert.IsType<NotFoundObjectResult>(ctrl.GetInfo("foo"));
    }

    [Theory]
    [InlineData("paf", "Paf.DC.Pilot.bat")]
    [InlineData("duskin", "Duskin.DC.Pilot.bat")]
    public async Task StreamDeploy_PafAndDuskin_DryRunChecksBrandBat(string dbName, string batFileName)
    {
        var ctrl = CreateController(dbName, batFileName);

        var messages = await RunStream(ctrl, dbName);

        Assert.Contains(messages, m =>
            m.StartsWith("[DRY-RUN] web.config 切替 bat 実行", StringComparison.Ordinal)
            && m.EndsWith(batFileName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("✅ Pilot環境適用が完了しました", messages);
    }

    [Fact]
    public async Task StreamDeploy_NotSet_DefaultsToExcludeWebConfig()
    {
        var ctrl = CreateController();

        var sse = await RunStream(ctrl);

        Assert.Contains("Web.config コピー: しない（設定: ON）", sse);
    }
}
