using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrepareController : ControllerBase
{
    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly FastCopyService _fastCopy;
    private readonly DatabaseService _db;
    private readonly List<DbConfig> _dbConfigs;

    public PrepareController(FastCopyService fastCopy, DatabaseService db, IConfiguration config)
    {
        _fastCopy = fastCopy;
        _db = db;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
    }

    [HttpGet("files")]
    public IActionResult GetFiles()
    {
        var result = new List<PrepareDbEntry>();

        foreach (var config in _dbConfigs)
        {
            var entry = new PrepareDbEntry { DbName = config.Name };

            entry.Files.AddRange(ReadFiles(config.DeployedPath, "deployed", "sqlserver"));
            entry.Files.AddRange(ReadFiles(config.DeployedHoldPath, "hold", "sqlserver"));
            entry.Files.AddRange(ReadFiles(config.MariaDbDeployedPath, "deployed", "mariadb"));
            entry.Files.AddRange(ReadFiles(config.MariaDbDeployedHoldPath, "hold", "mariadb"));

            result.Add(entry);
        }

        return Ok(result);
    }

    [HttpPost("stream")]
    public async Task StreamPrepare([FromBody] PrepareRequest request, CancellationToken ct)
    {
        var executedBy = User.Identity?.Name ?? "unknown";

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        await Response.Body.FlushAsync(ct);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var writeTask = WriteStreamAsync(channel.Reader, ct);

        try
        {
            var (applied, held, logDetail) = await _fastCopy.ExecuteAsync(
                _dbConfigs, request.Selections, channel.Writer, ct);

            channel.Writer.Complete();
            await writeTask;

            _db.InsertProductionReadyLog(executedBy, applied, held, "success", logDetail);

            var doneJson = JsonSerializer.Serialize(new { type = "done", applied, held }, _camelCase);
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"), ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
            await writeTask;
            _db.InsertProductionReadyLog(executedBy, 0, 0, "failed", ex.Message);
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

    private static List<PrepareFileInfo> ReadFiles(string dir, string source, string dbType)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.sql")
            .Select(f => new PrepareFileInfo
            {
                FileName = Path.GetFileName(f),
                Source = source,
                DbType = dbType,
            })
            .ToList();
    }
}
