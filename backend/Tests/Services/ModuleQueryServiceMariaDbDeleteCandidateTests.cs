using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ModuleQueryServiceMariaDbDeleteCandidateTests
{
    [Fact]
    public async Task GetModulesAsync_DetectsMariaDbStoredAndTable_DeleteCandidates()
    {
        var tempRoot = Directory.CreateTempSubdirectory("modulequery-maria-delete").FullName;
        try
        {
            var mariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo");
            var storedDir = Path.Combine(mariaDbGitRepoPath, "Stored");
            var tableDir = Path.Combine(mariaDbGitRepoPath, "Table");
            Directory.CreateDirectory(storedDir);
            Directory.CreateDirectory(tableDir);
            await File.WriteAllTextAsync(Path.Combine(storedDir, "orphanProc.sql"), "-- proc", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(tableDir, "orphanTable.sql"), "-- table", Encoding.UTF8);

            var config = new DbConfig
            {
                Name = "kaios",
                DevDb = "kaios_dev",
                DevConnectionString = "", // SQL Server 接続なし（早期return）
                MariaDbConnectionString = "", // MariaDB 接続なし（DBクエリはスキップされるが削除候補検出はGitRepoPathのみに依存する想定）
                GitRepoPath = "",
                MariaDbGitRepoPath = mariaDbGitRepoPath,
            };

            var service = new ModuleQueryService(NullLogger<ModuleQueryService>.Instance);
            var response = await service.GetModulesAsync(config);

            Assert.Contains(response.MariaDb, m => m.Name == "orphanProc" && m.IsDeleteCandidate);
            Assert.Contains(response.MariaDbTables, m => m.Name == "orphanTable" && m.IsDeleteCandidate && m.GitOnly);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
