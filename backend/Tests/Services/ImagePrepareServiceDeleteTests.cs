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
    public void Delete_RejectsParentWithChildEvenWhenChildAlsoListed()
    {
        var dir = FilesPath("Images", "junk");
        Directory.CreateDirectory(dir);
        var child = Path.Combine(dir, "a.png");
        File.WriteAllText(child, "x");
        var service = CreateService(dryRun: false);

        var ex = Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images/junk", "Images/junk/a.png"]));

        Assert.Contains("空でない", ex.Message);
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(child));
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
    public void Delete_ParentAndChildTogether_Rejected_ThenSequentialDeleteWorks()
    {
        var parent = FilesPath("Images", "flash");
        var child = FilesPath("Images", "flash", "img");
        Directory.CreateDirectory(child);
        var service = CreateService(dryRun: false);

        Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, ["Images/flash", "Images/flash/img"]));
        Assert.True(Directory.Exists(child));

        var childResult = service.Delete(_config, ["Images/flash/img"]);
        Assert.Equal(["Images/flash/img"], childResult.Deleted);

        var parentResult = service.Delete(_config, ["Images/flash"]);
        Assert.Equal(["Images/flash"], parentResult.Deleted);
        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public void Delete_RemovesDeepLegacyFile()
    {
        var deep = FilesPath("Images", "a", "b", "c", "old.png");
        Directory.CreateDirectory(Path.GetDirectoryName(deep)!);
        File.WriteAllText(deep, "x");
        var service = CreateService(dryRun: false);

        var result = service.Delete(_config, ["Images/a/b/c/old.png"]);

        Assert.Equal(["Images/a/b/c/old.png"], result.Deleted);
        Assert.False(File.Exists(deep));
    }

    [Fact]
    public void TryResolveRelativeFile_AllowsDeepLegacyPath()
    {
        var deep = FilesPath("Images", "a", "b", "c", "old.png");
        Directory.CreateDirectory(Path.GetDirectoryName(deep)!);
        File.WriteAllText(deep, "x");
        var service = CreateService(dryRun: true);

        Assert.True(service.TryResolveRelativeFile(_config, "Images/a/b/c/old.png", out var full, out var error));
        Assert.Equal(Path.GetFullPath(deep), Path.GetFullPath(full));
        Assert.Equal("", error);
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

    [Fact]
    public void Delete_PartialFailure_ReportsDeletedPaths()
    {
        var first = FilesPath("Images", "ok.png");
        var second = FilesPath("Images", "locked.png");
        File.WriteAllText(first, "x");
        File.WriteAllText(second, "y");
        var service = CreateService(dryRun: false);

        using var lockStream = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None);
        var ex = Assert.Throws<ImagePreparePartialDeleteException>(() =>
            service.Delete(_config, ["Images/ok.png", "Images/locked.png"]));

        Assert.Equal(["Images/ok.png"], ex.Deleted.ToList());
        Assert.Equal("削除中にエラーが発生しました", ex.Message);
        Assert.NotNull(ex.InnerException);
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void Delete_RejectsPathTooLong_AsArgumentException()
    {
        var service = CreateService(dryRun: false);
        var tooManySegments = "Images/" + string.Join('/', Enumerable.Repeat("aaaaaaaaaa", 5000));

        var ex = Assert.Throws<ArgumentException>(() =>
            service.Delete(_config, [tooManySegments]));

        Assert.DoesNotContain(":\\", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PathTooLong", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCombineUnderRoot_RejectsPathTooLong_WithoutThrowing()
    {
        var root = _config.FilesPath;
        var segments = new[] { "Images" }.Concat(Enumerable.Repeat("aaaaaaaaaa", 5000));

        var ok = PathSafety.TryCombineUnderRoot(root, segments, out var fullPath, out var error, "Files 配下以外のパスは指定できません");

        Assert.False(ok);
        Assert.Equal("", fullPath);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
