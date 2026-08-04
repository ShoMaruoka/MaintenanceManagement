using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ParseMariaDbDeployResultsTests
{
    [Fact]
    public void ParsesOkAndFailMarkers()
    {
        var lines = new List<string> { "RESULT:OK:file1.sql", "RESULT:FAIL:file2.sql", "unrelated log line" };
        var modules = new List<DeployModule>
        {
            new() { Name = "file1", Type = "Stored", OpType = "更新" },
            new() { Name = "file2", Type = "Stored", OpType = "更新" },
        };

        var results = DeployService.ParseMariaDbDeployResults(lines, modules);

        Assert.True(results["file1"]);
        Assert.False(results["file2"]);
    }

    [Fact]
    public void TreatsModuleWithoutMarker_AsFailed()
    {
        var lines = new List<string> { "RESULT:OK:file1.sql" };
        var modules = new List<DeployModule>
        {
            new() { Name = "file1", Type = "Stored", OpType = "更新" },
            new() { Name = "missingMarker", Type = "Stored", OpType = "更新" },
        };

        var results = DeployService.ParseMariaDbDeployResults(lines, modules);

        Assert.True(results["file1"]);
        Assert.False(results["missingMarker"]);
    }
}
