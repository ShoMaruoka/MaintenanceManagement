using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ModuleQueryServiceTests
{
    private static ModuleQueryService CreateService() =>
        new(NullLogger<ModuleQueryService>.Instance);

    [Fact]
    public async Task GetModulesAsync_ReturnsEmptyMariaDbLists_WhenConnectionStringEmpty()
    {
        var service = CreateService();
        var config = new DbConfig
        {
            Name = "kaios",
            DevDb = "kaios_dev",
            DevConnectionString = "",
            MariaDbConnectionString = "",
            GitRepoPath = "",
            MariaDbGitRepoPath = "",
        };

        var response = await service.GetModulesAsync(config);

        Assert.Empty(response.MariaDb);
        Assert.Empty(response.MariaDbTables);
    }
}
