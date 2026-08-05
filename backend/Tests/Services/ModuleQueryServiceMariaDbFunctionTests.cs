using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

public class ModuleQueryServiceMariaDbFunctionTests
{
    [Fact]
    public async Task GetModulesAsync_ClassifiesOrphanFiles_AsStoredOrMariaDbFunction_ByFileContent()
    {
        var tempRoot = Directory.CreateTempSubdirectory("modulequery-maria-func").FullName;
        try
        {
            var mariaDbGitRepoPath = Path.Combine(tempRoot, "MariaDbGitRepo");
            var storedDir = Path.Combine(mariaDbGitRepoPath, "Stored");
            Directory.CreateDirectory(storedDir);

            // Export.py が生成する実際の形式（DROP + CREATE DEFINER=... FUNCTION/PROCEDURE）
            await File.WriteAllTextAsync(Path.Combine(storedDir, "orphanProc.sql"),
                "DROP PROCEDURE IF EXISTS `orphanProc`;\r\nDELIMITER ;;\r\nCREATE DEFINER=`root`@`%` PROCEDURE `orphanProc`()\r\nBEGIN\r\nEND ;;", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(storedDir, "orphanFunc.sql"),
                "DROP FUNCTION IF EXISTS `orphanFunc`;\r\nDELIMITER ;;\r\nCREATE DEFINER=`root`@`%` FUNCTION `orphanFunc`() RETURNS varchar(8)\r\nBEGIN\r\nEND ;;", Encoding.UTF8);

            var config = new DbConfig
            {
                Name = "kaios",
                DevDb = "kaios_dev",
                DevConnectionString = "",
                MariaDbConnectionString = "",
                GitRepoPath = "",
                MariaDbGitRepoPath = mariaDbGitRepoPath,
            };

            var service = new ModuleQueryService(NullLogger<ModuleQueryService>.Instance);
            var response = await service.GetModulesAsync(config);

            Assert.Contains(response.MariaDb, m => m.Name == "orphanProc" && m.IsDeleteCandidate);
            Assert.DoesNotContain(response.MariaDbFunctions, m => m.Name == "orphanProc");

            Assert.Contains(response.MariaDbFunctions, m => m.Name == "orphanFunc" && m.IsDeleteCandidate);
            Assert.DoesNotContain(response.MariaDb, m => m.Name == "orphanFunc");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
