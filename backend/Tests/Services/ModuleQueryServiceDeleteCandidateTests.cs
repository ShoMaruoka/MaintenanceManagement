using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// 削除候補検出は本番経路 ApplyGitScan 経由で検証する。
/// 外部 fixture（test/）に依存しない自己完結型。
/// </summary>
public class ModuleQueryServiceDeleteCandidateTests
{
    private static ModuleQueryService CreateService() =>
        new(NullLogger<ModuleQueryService>.Instance);

    private static HashSet<string> NoStg() => new(StringComparer.Ordinal);

    private static string CreateTempGitRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "chinook-delcand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempGitRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort */ }
    }

    [Fact]
    public void ApplyGitScan_MariaDbTable_ResolvesWithoutDboPrefix()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "Table");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "tm0010catalogno.sql"), "-- stub");

            var existing = new List<ModuleInfo>();
            CreateService().ApplyGitScan(tmp, "Table", "MariaDbTable", "", existing, gitOnly: true, NoStg());

            Assert.Contains(existing, c => c.Name == "tm0010catalogno");
            var candidate = existing.Single(c => c.Name == "tm0010catalogno");
            Assert.Equal("MariaDbTable", candidate.Type);
            Assert.True(candidate.GitOnly);
            Assert.True(candidate.IsDeleteCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_SqlServerPrefix_DoesNotMatchUnprefixedFiles()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            // MariaDB 風のプレフィックス無しファイルは、SQL Server 用 dbo. プレフィックスでは検出しない
            var dir = Path.Combine(tmp, "Stored");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SomeProc.sql"), "-- stub");

            var existing = new List<ModuleInfo>();
            CreateService().ApplyGitScan(tmp, "Stored", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());

            Assert.Empty(existing);
        }
        finally { DeleteTempGitRoot(tmp); }
    }
}
