using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeployController : ControllerBase
{
    private readonly DeployService _deployService;
    private readonly DatabaseService _db;
    private readonly List<DbConfig> _dbConfigs;

    public DeployController(DeployService deployService, DatabaseService db, IConfiguration config)
    {
        _deployService = deployService;
        _db = db;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
    }

    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [HttpPost("stream")]
    public async Task StreamDeploy([FromBody] DeployRequest request, CancellationToken ct)
    {
        var dbConfig = _dbConfigs.FirstOrDefault(c => c.Name.Equals(request.DbName, StringComparison.OrdinalIgnoreCase));
        if (dbConfig is null)
        {
            Response.StatusCode = 400;
            return;
        }

        var executedBy = string.IsNullOrWhiteSpace(request.ExecutedBy) ? "unknown" : request.ExecutedBy;
        var sessionId = _db.InsertDeploySession(request.DbName, executedBy);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        await Response.Body.FlushAsync(ct);

        var logLines = new StringBuilder();
        var success = true;
        string? errorMessage = null;

        try
        {
            var reader = _deployService.ExecuteAsync(dbConfig, request, executedBy, ct);
            await foreach (var entry in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(entry, _camelCase);
                var data = $"data: {json}\n\n";
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(data), ct);
                await Response.Body.FlushAsync(ct);

                logLines.AppendLine($"{entry.Timestamp} [{entry.Level}] {entry.Message}");

                if (entry.Level == "ERROR")
                {
                    success = false;
                    errorMessage = entry.Message;
                }
            }
        }
        catch (OperationCanceledException)
        {
            success = false;
            errorMessage = "中断されました";
        }

        var status = success ? "success" : "failed";
        _db.UpdateDeploySessionStatus(sessionId, status, errorMessage);

        foreach (var m in request.Modules)
        {
            _db.InsertDeployDetail(sessionId, m.OpType, m.Type, m.Name, success ? "success" : "failed");
        }

        var doneJson = JsonSerializer.Serialize(new { type = "done", sessionId }, _camelCase);
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"), ct);
        await Response.Body.FlushAsync(ct);
    }
}
