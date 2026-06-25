using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModulesController : ControllerBase
{
    private readonly ModuleQueryService _queryService;
    private readonly List<DbConfig> _dbConfigs;

    public ModulesController(ModuleQueryService queryService, IConfiguration config)
    {
        _queryService = queryService;
        _dbConfigs = config.GetSection("DbConfigs").Get<List<DbConfig>>() ?? [];
    }

    [HttpGet("{dbName}")]
    public async Task<IActionResult> GetModules(string dbName)
    {
        var config = _dbConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
        if (config is null) return NotFound(new { error = $"DB '{dbName}' not found" });

        var result = await _queryService.GetModulesAsync(config);
        return Ok(result);
    }

    [HttpGet]
    public IActionResult GetDbList()
    {
        var list = _dbConfigs.Select(c => new { c.Name, c.DevDb, c.StgDb, c.PrdDb });
        return Ok(list);
    }
}
