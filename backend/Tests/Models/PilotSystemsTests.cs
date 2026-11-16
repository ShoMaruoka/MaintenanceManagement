using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Tests.Models;

/// <summary>pilot-webconfig-exclude T5: Pilot環境適用の対象システム一覧（PAF / Duskin 追加）。</summary>
public class PilotSystemsTests
{
    [Fact]
    public void DbNames_AreFourSystemsInDisplayOrder()
    {
        Assert.Equal(["kaios", "gos", "paf", "duskin"], PilotSystems.DbNames);
    }

    [Theory]
    [InlineData("kaios")]
    [InlineData("gos")]
    [InlineData("paf")]
    [InlineData("duskin")]
    [InlineData("PAF")]
    [InlineData("Duskin")]
    public void IsSupported_KnownSystems_CaseInsensitive(string dbName)
    {
        Assert.True(PilotSystems.IsSupported(dbName));
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_Unknown_ReturnsFalse(string? dbName)
    {
        Assert.False(PilotSystems.IsSupported(dbName));
    }
}
