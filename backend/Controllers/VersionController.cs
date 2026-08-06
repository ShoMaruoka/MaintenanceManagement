using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    // アセンブリ属性は起動後に変わらないため一度だけ組み立てる。
    private static readonly VersionInfo Info = BuildInfo();

    [HttpGet]
    public IActionResult Get() => Ok(Info);

    private static VersionInfo BuildInfo()
    {
        var assembly = typeof(VersionController).Assembly;
        var informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // SourceRevisionId をビルド時に渡すと "1.1.1+<commit>" 形式になる。
        // 画面表示に使う version は "+" より前だけを取る。
        var version = informational.Split('+')[0];

        return new VersionInfo(version, informational);
    }

    public record VersionInfo(string Version, string InformationalVersion);
}
