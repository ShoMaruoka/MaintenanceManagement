using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/image-prepare")]
public class ImagePrepareController : ControllerBase
{
    private readonly ImagePrepareService _service;
    private readonly List<DbConfig> _dbConfigs;
    private readonly ILogger<ImagePrepareController> _logger;

    public ImagePrepareController(
        ImagePrepareService service,
        IConfiguration config,
        ILogger<ImagePrepareController> logger)
    {
        _service = service;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
        _logger = logger;
    }

    [HttpGet("{dbName}/tree")]
    public IActionResult GetTree(string dbName)
    {
        var config = FindConfig(dbName);
        if (config is null)
            return NotFound(new { error = $"DB '{dbName}' not found" });

        return Ok(_service.GetTree(config));
    }

    [HttpPost("{dbName}/folders")]
    public IActionResult CreateFolder(string dbName, [FromBody] ImageCreateFolderRequest request)
    {
        var config = FindConfig(dbName);
        if (config is null)
            return NotFound(new { error = $"DB '{dbName}' not found" });

        try
        {
            var result = _service.CreateFolder(config, request.Category, request.RelativeSubPath);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{dbName}/upload")]
    [RequestSizeLimit(ImagePrepareService.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImagePrepareService.MaxUploadBytes)]
    public IActionResult Upload(
        string dbName,
        [FromForm] string category,
        [FromForm] string? relativeSubPath,
        [FromForm] bool overwrite = false,
        [FromForm] List<IFormFile>? files = null)
    {
        var config = FindConfig(dbName);
        if (config is null)
            return NotFound(new { error = $"DB '{dbName}' not found" });

        try
        {
            var result = _service.Upload(config, category, relativeSubPath, files ?? [], overwrite);
            return Ok(result);
        }
        catch (ImagePrepareConflictException ex)
        {
            return Conflict(new { error = ex.Message, conflicts = ex.Conflicts });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{dbName}/delete")]
    public IActionResult Delete(string dbName, [FromBody] ImageDeleteRequest? request)
    {
        var config = FindConfig(dbName);
        if (config is null)
            return NotFound(new { error = $"DB '{dbName}' not found" });

        try
        {
            var result = _service.Delete(config, request?.Paths ?? []);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ImagePreparePartialDeleteException ex)
        {
            _logger.LogError(
                ex,
                "Image prepare delete partially failed for DB {DbName}. Deleted={Deleted}",
                dbName,
                string.Join(", ", ex.Deleted));
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "削除中にエラーが発生しました",
                deleted = ex.Deleted,
            });
        }
    }

    private DbConfig? FindConfig(string dbName) =>
        _dbConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
}
