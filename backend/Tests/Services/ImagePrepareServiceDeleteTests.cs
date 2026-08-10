using Microsoft.Extensions.Configuration;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ImagePrepareServiceDeleteTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _deployRoot;
    private readonly DbConfig _config;

    public ImagePrepareServiceDeleteTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "imgprep-delete-" + Guid.NewGuid().ToString("N"));
        _deployRoot = Path.Combine(_tempRoot, "Deploy_DEV2STG");
        Directory.CreateDirectory(Path.Combine(_deployRoot, "Files", "Images"));
        Directory.CreateDirectory(Path.Combine(_deployRoot, "Files", "news"));
        Directory.CreateDirectory(Path.Combine(_deployRoot, "Files", "pdf"));

        _config = new DbConfig
        {
            Name = "test-db",
            DeployDev2StgPath = _deployRoot,
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static ImagePrepareService CreateService(bool dryRun)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DryRun"] = dryRun.ToString() })
            .Build();
        return new ImagePrepareService(configuration);
    }

    private string FilesPath(params string[] parts) =>
        Path.Combine(new[] { _config.FilesPath }.Concat(parts).ToArray());

    [Fact]
    public void Delete_RemovesFile_WhenDryRunFalse()
    {
        var file = FilesPath("Images", "a.png");
        File.WriteAllText(file, "x");
        var service = CreateService(dryRun: false);

        var result = service.Delete(_config, ["Images/a.png"]);

        Assert.False(result.DryRun);
        Assert.Equal("test-db", result.DbName);
        Assert.Equal(["Images/a.png"], result.Deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Delete_RemovesEmptyDirectory()
    {
        var dir = FilesPath("Images", "empty");
        Directory.CreateDirectory(dir);
        var service = CreateService(dryRun: false);

        var result = service.Delete(_config, ["Images/empty"]);

        Assert.Equal(["Images/empty"], result.Deleted);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Delete_RejectsNonEmptyDirectory_WithoutDeleting()
    {
        var dir = FilesPath("Images", "filled");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "x");
        var service = CreateService(dryRun: false);

        var ex = Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images/filled"]));

        Assert.Contains("空でない", ex.Message);
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(dir, "keep.txt")));
    }

    [Fact]
    public void Delete_RejectsCategoryRoot()
    {
        var service = CreateService(dryRun: false);

        var ex = Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images"]));

        Assert.Contains("カテゴリ", ex.Message);
        Assert.True(Directory.Exists(FilesPath("Images")));
    }

    [Fact]
    public void Delete_RejectsTraversal()
    {
        var service = CreateService(dryRun: false);

        Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images/../news"]));
    }

    [Fact]
    public void Delete_RejectsMissingPath_WithoutDeletingOthers()
    {
        var file = FilesPath("Images", "keep.png");
        File.WriteAllText(file, "x");
        var service = CreateService(dryRun: false);

        Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images/keep.png", "Images/missing.png"]));

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Delete_DryRun_DoesNotRemoveFile()
    {
        var file = FilesPath("Images", "dry.png");
        File.WriteAllText(file, "x");
        var service = CreateService(dryRun: true);

        var result = service.Delete(_config, ["Images/dry.png"]);

        Assert.True(result.DryRun);
        Assert.Equal(["Images/dry.png"], result.Deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Delete_ParentAndChild_DeletesBoth_DeepestFirst()
    {
        var parent = FilesPath("Images", "flash");
        var child = FilesPath("Images", "flash", "img");
        Directory.CreateDirectory(child);
        var service = CreateService(dryRun: false);

        var result = service.Delete(_config, ["Images/flash", "Images/flash/img"]);

        Assert.Equal(2, result.Deleted.Count);
        Assert.Equal("Images/flash/img", result.Deleted[0]);
        Assert.Equal("Images/flash", result.Deleted[1]);
        Assert.False(Directory.Exists(parent));
        Assert.False(Directory.Exists(child));
    }

    [Fact]
    public void Delete_DedupesPaths_CaseInsensitive()
    {
        var file = FilesPath("Images", "dup.png");
        File.WriteAllText(file, "x");
        var service = CreateService(dryRun: false);

        var result = service.Delete(_config, ["Images/dup.png", "Images/DUP.png"]);

        Assert.Single(result.Deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Delete_RejectsEmptyPaths()
    {
        var service = CreateService(dryRun: false);
        Assert.Throws<ArgumentException>(() => service.Delete(_config, []));
    }
}
