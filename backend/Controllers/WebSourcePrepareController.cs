using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

/// <summary>STG → pilot サーバーへの Web ソース配布（Issue #25）。</summary>
[ApiController]
[Route("api/web-source-prepare")]
public class WebSourcePrepareController : ControllerBase
{
    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>pilot環境が存在するのは kaios/gos のみ（SPEC 参照）。</summary>
    private static readonly string[] AllowedDbNames = ["kaios", "gos"];

    private readonly WebSourceDeployService _deployService;
    private readonly DatabaseService _db;
    private readonly List<DbConfig> _dbConfigs;

    public WebSourcePrepareController(
        WebSourceDeployService deployService,
        DatabaseService db,
        IConfiguration config)
    {
        _deployService = deployService;
        _db = db;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
    }

    [HttpGet("{dbName}/info")]
    public IActionResult GetInfo(string dbName)
    {
        if (!IsAllowedDbName(dbName))
            return NotFound(new { message = $"pilot環境が存在しないシステムです: {dbName}" });

        var config = FindConfig(dbName);
        if (config is null)
            return NotFound(new { message = $"設定が見つかりません: {dbName}" });

        var response = new WebSourceInfoResponse
        {
            DbName = config.Name,
            WebSourcePath = config.WebSourcePath,
            PilotTargets = config.PilotTargets
                .Select(t => new WebSourcePilotTargetInfo { Name = t.Name, DestWebSourcePath = t.DestWebSourcePath })
                .ToList(),
        };
        return Ok(response);
    }

    [HttpPost("{dbName}/stream")]
    public async Task StreamDeploy(string dbName, [FromBody] WebSourceDeployRequest request, CancellationToken ct)
    {
        if (!IsAllowedDbName(dbName))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var config = FindConfig(dbName);
        if (config is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var executedBy = string.IsNullOrWhiteSpace(request.ExecutedBy) ? "unknown" : request.ExecutedBy;
        var runId = Guid.NewGuid().ToString("n");
        var step = ParseStep(request.Step);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        await Response.Body.FlushAsync(ct);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var writeTask = WriteStreamAsync(channel.Reader, ct);

        try
        {
            var (results, sqlDeploy) = await _deployService.ExecuteAsync(config, channel.Writer, ct, step);

            channel.Writer.Complete();
            await writeTask;

            foreach (var r in results)
            {
                _db.InsertWebSourceDeployLog(
                    runId, config.Name, r.TargetName, "full", executedBy,
                    r.Success ? "success" : "failed", r.ErrorMessage);
            }

            if (sqlDeploy is not null)
            {
                _db.InsertWebSourceDeployLog(
                    runId, config.Name, "sql", "full", executedBy,
                    sqlDeploy.Success ? "success" : "failed", sqlDeploy.ErrorMessage);
            }

            var overallSuccess = results.Count > 0 && results.All(r => r.Success) && (sqlDeploy is null || sqlDeploy.Success);
            var doneJson = JsonSerializer.Serialize(new
            {
                type = "done",
                runId,
                success = overallSuccess,
                targets = results,
                sqlDeploy,
            }, _camelCase);
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"), ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
            await writeTask;
            _db.InsertWebSourceDeployLog(runId, config.Name, "-", "full", executedBy, "failed", ex.Message);
        }
    }

    private async Task WriteStreamAsync(ChannelReader<LogEntry> reader, CancellationToken ct)
    {
        await foreach (var entry in reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(entry, _camelCase);
            var data = $"data: {json}\n\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(data), ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>リクエストの "step" 文字列を解析する。未指定・不正値は "both" として扱う。</summary>
    private static WebSourceDeployStep ParseStep(string? step) => step?.ToLowerInvariant() switch
    {
        "web" => WebSourceDeployStep.WebOnly,
        "sql" => WebSourceDeployStep.SqlOnly,
        _ => WebSourceDeployStep.Both,
    };

    private static bool IsAllowedDbName(string dbName) =>
        AllowedDbNames.Contains(dbName, StringComparer.OrdinalIgnoreCase);

    private DbConfig? FindConfig(string dbName) =>
        _dbConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
}
