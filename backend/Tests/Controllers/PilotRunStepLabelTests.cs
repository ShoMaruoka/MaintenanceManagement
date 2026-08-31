using MaintenanceManagement.Api.Controllers;

namespace MaintenanceManagement.Api.Tests.Controllers;

public class PilotRunStepLabelTests
{
    [Theory]
    [InlineData(new[] { "sql" }, "SQLのみ")]
    [InlineData(new[] { "sql-dryrun" }, "SQLのみ（DryRun）")]
    [InlineData(new[] { "sql-skipped" }, "SQLのみ")]
    [InlineData(new[] { "sql", "sql-skipped" }, "SQLのみ")]
    [InlineData(new[] { "web" }, "Webのみ")]
    [InlineData(new[] { "web-dryrun" }, "Webのみ（DryRun）")]
    [InlineData(new[] { "both" }, "両方")]
    [InlineData(new[] { "both", "sql" }, "両方")]
    [InlineData(new[] { "web", "sql" }, "両方")]
    [InlineData(new[] { "both-dryrun", "sql-dryrun" }, "両方（DryRun）")]
    public void PilotRunStepLabel_FromModes(string[] modes, string expected)
    {
        Assert.Equal(expected, PilotRunStepLabel.FromModes(modes));
    }
}
