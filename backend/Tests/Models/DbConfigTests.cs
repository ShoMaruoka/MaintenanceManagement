using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Tests.Models;

public class DbConfigTests
{
    private static DbConfig CreateConfig() => new()
    {
        Name = "kaios",
        SourceControlPath = @"D:\Tools\SourceControl",
        DeployDev2StgPath = @"D:\Tools\SourceControl\Deploy_DEV2STG",
        MariaDbGitRepoPath = @"D:\STGENV\Kaios_MariaDB_rep",
    };

    [Fact]
    public void MariaDbGitRepoPath_IsSettable()
    {
        var config = CreateConfig();
        Assert.Equal(@"D:\STGENV\Kaios_MariaDB_rep", config.MariaDbGitRepoPath);
    }

    [Fact]
    public void MariaDbMergePath_IsSiblingOfMergePath()
    {
        var config = CreateConfig();
        Assert.Equal(Path.Combine(config.SourceControlPath, "merge_MariaDB"), config.MariaDbMergePath);
    }

    [Fact]
    public void MariaDbForNewCreationPath_IsUnderMariaDbSourcePath()
    {
        var config = CreateConfig();
        Assert.Equal(Path.Combine(config.MariaDbSourcePath, "ForNewCreation"), config.MariaDbForNewCreationPath);
    }

    [Fact]
    public void MariaDbDeploySourcePath_IsUnderForNewCreationPath()
    {
        var config = CreateConfig();
        Assert.Equal(Path.Combine(config.MariaDbForNewCreationPath, "Source"), config.MariaDbDeploySourcePath);
    }

    [Fact]
    public void MariaDbDeployBatPath_IsUnderForNewCreationPath()
    {
        var config = CreateConfig();
        Assert.Equal(Path.Combine(config.MariaDbForNewCreationPath, "deploy.bat"), config.MariaDbDeployBatPath);
    }
}
