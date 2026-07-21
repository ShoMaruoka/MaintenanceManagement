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

    public ImagePrepareController(ImagePrepareService service, IConfiguration config)
    {
        _service = service;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
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

    private DbConfig? FindConfig(string dbName) =>
        _dbConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
}
