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
    private readonly ImagePrepareService _imagePrepare;
    private readonly ManualApplyService _manualApply;
    private readonly List<DbConfig> _dbConfigs;

    public PrepareController(
        FastCopyService fastCopy,
        DatabaseService db,
        ImagePrepareService imagePrepare,
        ManualApplyService manualApply,
        IConfiguration config)
    {
        _fastCopy = fastCopy;
        _db = db;
        _imagePrepare = imagePrepare;
        _manualApply = manualApply;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
    }

    [HttpGet("files")]
    public IActionResult GetFiles()
    {
        var result = new List<PrepareDbEntry>();

        foreach (var config in _dbConfigs)
        {
            var entry = new PrepareDbEntry { DbName = config.Name };

            // 操作区分は DB 単位で 1 回だけ引き、ファイルごとには辞書引きで解決する。
            var opTypes = _db.GetLatestOpTypes(config.Name);

            entry.Files.AddRange(ReadFiles(config.DeployedPath, "deployed", "sqlserver", opTypes));
            entry.Files.AddRange(ReadFiles(config.DeployedHoldPath, "hold", "sqlserver", opTypes));
            entry.Files.AddRange(ReadFiles(config.MariaDbDeployedPath, "deployed", "mariadb", opTypes));
            entry.Files.AddRange(ReadFiles(config.MariaDbDeployedHoldPath, "hold", "mariadb", opTypes));
            entry.ImageFiles.AddRange(_imagePrepare.ListRelativeFilePaths(config));
            entry.ManualItems.AddRange(_manualApply.List(config));

            result.Add(entry);
        }

        return Ok(result);
    }

    [HttpPost("stream")]
    public async Task StreamPrepare([FromBody] PrepareRequest request, CancellationToken ct)
    {
        var executedBy = string.IsNullOrWhiteSpace(request.ExecutedBy) ? "unknown" : request.ExecutedBy;

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        await Response.Body.FlushAsync(ct);

        var channel = Channel.CreateUnbounded<LogEntry>();
        var writeTask = WriteStreamAsync(channel.Reader, ct);

        try
        {
            var (applied, held, manual, logDetail) = await _fastCopy.ExecuteAsync(
                _dbConfigs, request.Selections, request.ImageSelections, request.ManualSelections,
                channel.Writer, ct);

            channel.Writer.Complete();
            await writeTask;

            _db.InsertProductionReadyLog(executedBy, applied, held, manual, "success", logDetail);

            var doneJson = JsonSerializer.Serialize(new { type = "done", applied, held, manual }, _camelCase);
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"), ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
            await writeTask;
            _db.InsertProductionReadyLog(executedBy, 0, 0, 0, "failed", ex.Message);
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

    private static List<PrepareFileInfo> ReadFiles(
        string dir, string source, string dbType, IReadOnlyDictionary<string, string> opTypes)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.sql")
            .Select(f =>
            {
                var fileName = Path.GetFileName(f);
                return new PrepareFileInfo
                {
                    FileName = fileName,
                    Source = source,
                    DbType = dbType,
                    OpType = opTypes.TryGetValue(OpTypeResolver.FileKey(dbType, fileName), out var opType)
                        ? opType
                        : OpTypeResolver.Unknown,
                };
            })
            .ToList();
    }
}
