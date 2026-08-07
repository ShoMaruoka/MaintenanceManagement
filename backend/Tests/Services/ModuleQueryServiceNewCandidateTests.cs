using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// 新規候補判定（MarkNewCandidates / MarkMariaDbStoredNewCandidates）の自己完結型テスト。
/// 一時ディレクトリにダミー Git フォルダを作り、外部 fixture（test/）に依存しない。
/// </summary>
public class ModuleQueryServiceNewCandidateTests
{
    private static ModuleQueryService CreateService() =>
        new(NullLogger<ModuleQueryService>.Instance);

    private static ModuleInfo Mod(string name, string type = "StoredProcedure") =>
        new() { Name = name, Type = type };

    private static string CreateTempGitRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "chinook-newcand-" + Guid.NewGuid().ToString("N"));
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
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void MarkNewCandidates_DbOnly_SetsIsNewCandidateTrue()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "StoredProcedure"));
            // Git 側にファイルは置かない

            var existing = new List<ModuleInfo> { Mod("OnlyInDb") };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "StoredProcedure", "dbo.", existing);

            Assert.True(existing[0].IsNewCandidate);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkNewCandidates_DbAndGit_SetsIsNewCandidateFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "StoredProcedure");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dbo.InBoth.sql"), "-- stub");

            var existing = new List<ModuleInfo> { Mod("InBoth") };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "StoredProcedure", "dbo.", existing);

            Assert.False(existing[0].IsNewCandidate);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkNewCandidates_EmptyGitRepoPath_LeavesAllFalse()
    {
        var existing = new List<ModuleInfo> { Mod("A"), Mod("B") };
        var service = CreateService();

        service.MarkNewCandidates("", "StoredProcedure", "dbo.", existing);

        Assert.All(existing, m => Assert.False(m.IsNewCandidate));
    }

    [Fact]
    public void MarkMariaDbStoredNewCandidates_MarksProcedureAndFunctionByFilePresence()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var storedDir = Path.Combine(tmp, "Stored");
            Directory.CreateDirectory(storedDir);
            // Procedure のみ Git に存在。Function は DB のみ → 新規
            File.WriteAllText(Path.Combine(storedDir, "ExistingProc.sql"), "-- stub");

            var procedures = new List<ModuleInfo>
            {
                Mod("ExistingProc", "Stored"),
                Mod("NewProc", "Stored"),
            };
            var functions = new List<ModuleInfo>
            {
                Mod("NewFunc", "MariaDbFunction"),
            };
            var service = CreateService();

            service.MarkMariaDbStoredNewCandidates(tmp, procedures, functions);

            Assert.False(procedures[0].IsNewCandidate);
            Assert.True(procedures[1].IsNewCandidate);
            Assert.True(functions[0].IsNewCandidate);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkNewCandidates_SqlServer_UsesDboPrefixForMatching()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "Function");
            Directory.CreateDirectory(dir);
            // dbo. プレフィックス付きで突合できること
            File.WriteAllText(Path.Combine(dir, "dbo.FnMatched.sql"), "-- stub");

            var existing = new List<ModuleInfo>
            {
                Mod("FnMatched", "Function"),
                Mod("FnMissing", "Function"),
            };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "Function", "dbo.", existing);

            Assert.False(existing[0].IsNewCandidate);
            Assert.True(existing[1].IsNewCandidate);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkNewCandidates_MissingSubfolder_StoredProcedure_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            // gitRepoPath は有効だが StoredProcedure サブフォルダは作らない

            var existing = new List<ModuleInfo> { Mod("Sp1"), Mod("Sp2") };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "StoredProcedure", "dbo.", existing);

            Assert.All(existing, m => Assert.False(m.IsNewCandidate));
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkNewCandidates_MissingSubfolder_UserDefinedTableType_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            // GitOnly 系も同様にサブフォルダ未存在なら全件 false

            var existing = new List<ModuleInfo>
            {
                Mod("Udtt1", "UserDefinedTableType"),
                Mod("Udtt2", "UserDefinedTableType"),
            };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "UserDefinedTableType", "dbo.", existing);

            Assert.All(existing, m => Assert.False(m.IsNewCandidate));
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkMariaDbStoredNewCandidates_MissingStoredFolder_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var procedures = new List<ModuleInfo> { Mod("P1", "Stored") };
            var functions = new List<ModuleInfo> { Mod("F1", "MariaDbFunction") };
            var service = CreateService();

            service.MarkMariaDbStoredNewCandidates(tmp, procedures, functions);

            Assert.All(procedures, m => Assert.False(m.IsNewCandidate));
            Assert.All(functions, m => Assert.False(m.IsNewCandidate));
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkMariaDbStoredNewCandidates_EmptyGitRepoPath_LeavesAllFalse()
    {
        var procedures = new List<ModuleInfo> { Mod("P1", "Stored") };
        var functions = new List<ModuleInfo> { Mod("F1", "MariaDbFunction") };
        var service = CreateService();

        service.MarkMariaDbStoredNewCandidates("", procedures, functions);

        Assert.All(procedures, m => Assert.False(m.IsNewCandidate));
        Assert.All(functions, m => Assert.False(m.IsNewCandidate));
    }

    [Fact]
    public void MarkNewCandidates_MariaDbTable_UsesEmptyPrefix()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "Table");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ExistingTable.sql"), "-- stub");

            var existing = new List<ModuleInfo>
            {
                Mod("ExistingTable", "MariaDbTable"),
                Mod("NewTable", "MariaDbTable"),
            };
            var service = CreateService();

            service.MarkNewCandidates(tmp, "Table", "", existing);

            Assert.False(existing[0].IsNewCandidate);
            Assert.True(existing[1].IsNewCandidate);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }

    [Fact]
    public void MarkAbsentAsNew_MarksOnlyMissingNames_CaseInsensitive()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExistsInStg" };
        var existing = new List<ModuleInfo>
        {
            Mod("ExistsInStg"),
            Mod("MissingInStg"),
            Mod("existsinstg"), // 大文字小文字違い → STG にある扱い
        };
        var service = CreateService();

        service.MarkAbsentAsNew(present, existing);

        Assert.False(existing[0].IsNewCandidate);
        Assert.True(existing[1].IsNewCandidate);
        Assert.False(existing[2].IsNewCandidate);
    }

    [Fact]
    public void MarkAbsentAsNew_EmptyExisting_IsNoOp()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
        var service = CreateService();
        service.MarkAbsentAsNew(present, []);
    }

    [Fact]
    public void TryListGitModuleNames_IsCaseInsensitive()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "StoredProcedure");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dbo.MyProc.sql"), "-- stub");

            var service = CreateService();
            var names = service.TryListGitModuleNames(tmp, "StoredProcedure", "dbo.");

            Assert.NotNull(names);
            Assert.Contains("myproc", names!);
            Assert.Contains("MYPROC", names!);
        }
        finally
        {
            DeleteTempGitRoot(tmp);
        }
    }
}
