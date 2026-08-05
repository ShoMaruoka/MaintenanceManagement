using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ModuleQueryServiceDeleteCandidateTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "test", "Kaios_MariaDB_rep")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("test/Kaios_MariaDB_rep が見つかりません（リポジトリ直下から実行してください）");
    }

    [Fact]
    public void FindDeleteCandidates_MariaDbTable_ResolvesWithoutDboPrefix()
    {
        var repoRoot = FindRepoRoot();
        var mariaDbGitRepoPath = Path.Combine(repoRoot, "test", "Kaios_MariaDB_rep");
        var service = new ModuleQueryService(NullLogger<ModuleQueryService>.Instance);

        // DB上には存在しない（existing が空）前提で、Git 側の実ファイルが削除候補として検出されることを確認
        var candidates = service.FindDeleteCandidates(
            mariaDbGitRepoPath, "Table", "MariaDbTable", [], gitOnly: true, fileNamePrefix: "");

        Assert.Contains(candidates, c => c.Name == "tm0010catalogno");
        var candidate = candidates.Single(c => c.Name == "tm0010catalogno");
        Assert.Equal("MariaDbTable", candidate.Type);
        Assert.True(candidate.GitOnly);
        Assert.True(candidate.IsDeleteCandidate);
    }

    [Fact]
    public void FindDeleteCandidates_SqlServerTable_StillStripsDboPrefix()
    {
        var repoRoot = FindRepoRoot();
        // SQL Server用の実リポジトリは無いため、既存挙動（dbo.プレフィックス除去）自体をロジックとして検証する
        var mariaDbGitRepoPath = Path.Combine(repoRoot, "test", "Kaios_MariaDB_rep");
        var service = new ModuleQueryService(NullLogger<ModuleQueryService>.Instance);

        // Stored フォルダには "dbo." プレフィックスのファイルは無いため、SQL Server 用呼び出し（プレフィックスあり）は
        // 何も検出しないはず＝挙動が変わっていないことの確認
        var candidates = service.FindDeleteCandidates(
            mariaDbGitRepoPath, "Stored", "StoredProcedure", [], gitOnly: false, fileNamePrefix: "dbo.");

        Assert.Empty(candidates);
    }
}
