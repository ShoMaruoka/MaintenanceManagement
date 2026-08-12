using MaintenanceManagement.Api.Controllers;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Controllers;

public class ResolveLogModeTests
{
    [Theory]
    [InlineData(WebSourceDeployStep.Both, false, "both")]
    [InlineData(WebSourceDeployStep.Both, true, "both-dryrun")]
    [InlineData(WebSourceDeployStep.WebOnly, false, "web")]
    [InlineData(WebSourceDeployStep.WebOnly, true, "web-dryrun")]
    public void ResolveLogMode_WebRows(WebSourceDeployStep step, bool dryRun, string expected)
    {
        var mode = WebSourceDeployLogMode.ResolveLogMode(step, dryRun, skipped: false, WebSourceLogRowKind.Web);
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(false, false, "sql")]
    [InlineData(true, false, "sql-dryrun")]
    [InlineData(false, true, "sql-skipped")]
    [InlineData(true, true, "sql-dryrun")] // E2: DryRun 優先
    public void ResolveLogMode_SqlRows(bool dryRun, bool skipped, string expected)
    {
        var mode = WebSourceDeployLogMode.ResolveLogMode(
            WebSourceDeployStep.Both, dryRun, skipped, WebSourceLogRowKind.Sql);
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(WebSourceDeployStep.Both, "both")]
    [InlineData(WebSourceDeployStep.WebOnly, "web")]
    [InlineData(WebSourceDeployStep.SqlOnly, "sql")]
    public void ResolveLogMode_ExceptionRows_UseStep(WebSourceDeployStep step, string expected)
    {
        var mode = WebSourceDeployLogMode.ResolveLogMode(step, dryRun: true, skipped: true, WebSourceLogRowKind.Exception);
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void ResolveLogMode_WebRow_NeverReturnsSqlSkipped()
    {
        var mode = WebSourceDeployLogMode.ResolveLogMode(
            WebSourceDeployStep.Both, dryRun: false, skipped: true, WebSourceLogRowKind.Web);
        Assert.NotEqual("sql-skipped", mode);
        Assert.Equal("both", mode);
    }
}
