using Microsoft.Extensions.Logging.Abstractions;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Tests.Services;

/// <summary>
/// 新規候補判定の本番経路（ApplyGitScan / ApplyMariaDbStoredGitScan / MarkAbsentAsNew）の自己完結型テスト。
/// 一時ディレクトリにダミー Git フォルダを作り、外部 fixture（test/）に依存しない。
/// </summary>
public class ModuleQueryServiceNewCandidateTests
{
    private static ModuleQueryService CreateService() =>
        new(NullLogger<ModuleQueryService>.Instance);

    private static ModuleInfo Mod(string name, string type = "StoredProcedure") =>
        new() { Name = name, Type = type };

    private static HashSet<string> NoStg() => new(StringComparer.Ordinal);

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
    public void ApplyGitScan_DbOnly_SetsIsNewCandidateTrue()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "StoredProcedure"));
            var existing = new List<ModuleInfo> { Mod("OnlyInDb") };
            CreateService().ApplyGitScan(tmp, "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());
            Assert.True(existing[0].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_DbAndGit_SetsIsNewCandidateFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "StoredProcedure");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dbo.InBoth.sql"), "-- stub");
            var existing = new List<ModuleInfo> { Mod("InBoth") };
            CreateService().ApplyGitScan(tmp, "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());
            Assert.False(existing[0].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_EmptyGitRepoPath_LeavesAllFalse()
    {
        var existing = new List<ModuleInfo> { Mod("A"), Mod("B") };
        CreateService().ApplyGitScan("", "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());
        Assert.All(existing, m => Assert.False(m.IsNewCandidate));
    }

    [Fact]
    public void ApplyMariaDbStoredGitScan_MarksProcedureAndFunctionByFilePresence()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var storedDir = Path.Combine(tmp, "Stored");
            Directory.CreateDirectory(storedDir);
            File.WriteAllText(Path.Combine(storedDir, "ExistingProc.sql"), "-- stub");

            var procedures = new List<ModuleInfo>
            {
                Mod("ExistingProc", "Stored"),
                Mod("NewProc", "Stored"),
            };
            var functions = new List<ModuleInfo> { Mod("NewFunc", "MariaDbFunction") };

            CreateService().ApplyMariaDbStoredGitScan(tmp, procedures, functions);

            Assert.False(procedures[0].IsNewCandidate);
            Assert.True(procedures[1].IsNewCandidate);
            Assert.True(functions[0].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_SqlServer_UsesDboPrefixForMatching()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "Function");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dbo.FnMatched.sql"), "-- stub");

            var existing = new List<ModuleInfo>
            {
                Mod("FnMatched", "Function"),
                Mod("FnMissing", "Function"),
            };
            CreateService().ApplyGitScan(tmp, "Function", "Function", "dbo.", existing, gitOnly: false, NoStg());

            Assert.False(existing[0].IsNewCandidate);
            Assert.True(existing[1].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_MissingSubfolder_StoredProcedure_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var existing = new List<ModuleInfo> { Mod("Sp1"), Mod("Sp2") };
            CreateService().ApplyGitScan(tmp, "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());
            Assert.All(existing, m => Assert.False(m.IsNewCandidate));
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_MissingSubfolder_UserDefinedTableType_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var existing = new List<ModuleInfo>
            {
                Mod("Udtt1", "UserDefinedTableType"),
                Mod("Udtt2", "UserDefinedTableType"),
            };
            CreateService().ApplyGitScan(tmp, "UserDefinedTableType", "UserDefinedTableType", "dbo.", existing, gitOnly: true, NoStg());
            Assert.All(existing, m => Assert.False(m.IsNewCandidate));
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyMariaDbStoredGitScan_MissingStoredFolder_LeavesAllFalse()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var procedures = new List<ModuleInfo> { Mod("P1", "Stored") };
            var functions = new List<ModuleInfo> { Mod("F1", "MariaDbFunction") };
            CreateService().ApplyMariaDbStoredGitScan(tmp, procedures, functions);
            Assert.All(procedures, m => Assert.False(m.IsNewCandidate));
            Assert.All(functions, m => Assert.False(m.IsNewCandidate));
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyMariaDbStoredGitScan_EmptyGitRepoPath_LeavesAllFalse()
    {
        var procedures = new List<ModuleInfo> { Mod("P1", "Stored") };
        var functions = new List<ModuleInfo> { Mod("F1", "MariaDbFunction") };
        CreateService().ApplyMariaDbStoredGitScan("", procedures, functions);
        Assert.All(procedures, m => Assert.False(m.IsNewCandidate));
        Assert.All(functions, m => Assert.False(m.IsNewCandidate));
    }

    [Fact]
    public void ApplyGitScan_MariaDbTable_UsesEmptyPrefix()
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
            CreateService().ApplyGitScan(tmp, "Table", "MariaDbTable", "", existing, gitOnly: true, NoStg());

            Assert.False(existing[0].IsNewCandidate);
            Assert.True(existing[1].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_SkipsNewMarking_WhenModuleTypeAlreadyResolvedByStg()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            // Git にファイルが無くても、STG 判定済みなら新規マークしない
            Directory.CreateDirectory(Path.Combine(tmp, "StoredProcedure"));
            var existing = new List<ModuleInfo> { Mod("OnlyInDb") };
            var stgResolved = new HashSet<string>(StringComparer.Ordinal) { "StoredProcedure" };

            CreateService().ApplyGitScan(tmp, "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, stgResolved);

            Assert.False(existing[0].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_MariaDbTable_DoesNotSkip_WhenOnlySqlServerTableIsStgResolved()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            // folderName は同じ "Table" でも moduleType が違うので SQL Server の STG 結果を流用しない
            Directory.CreateDirectory(Path.Combine(tmp, "Table"));
            var existing = new List<ModuleInfo> { Mod("NewTable", "MariaDbTable") };
            var stgResolved = new HashSet<string>(StringComparer.Ordinal) { "Table" }; // SQL Server Table のみ

            CreateService().ApplyGitScan(tmp, "Table", "MariaDbTable", "", existing, gitOnly: true, stgResolved);

            Assert.True(existing[0].IsNewCandidate);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void ApplyGitScan_AddsDeleteCandidates_InNameOrder()
    {
        var tmp = CreateTempGitRoot();
        try
        {
            var dir = Path.Combine(tmp, "StoredProcedure");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dbo.Zebra.sql"), "-- stub");
            File.WriteAllText(Path.Combine(dir, "dbo.Alpha.sql"), "-- stub");
            File.WriteAllText(Path.Combine(dir, "dbo.Middle.sql"), "-- stub");

            var existing = new List<ModuleInfo>();
            CreateService().ApplyGitScan(tmp, "StoredProcedure", "StoredProcedure", "dbo.", existing, gitOnly: false, NoStg());

            var deletes = existing.Where(m => m.IsDeleteCandidate).Select(m => m.Name).ToList();
            Assert.Equal(["Alpha", "Middle", "Zebra"], deletes);
        }
        finally { DeleteTempGitRoot(tmp); }
    }

    [Fact]
    public void MarkAbsentAsNew_MarksOnlyMissingNames_CaseInsensitive()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExistsInStg" };
        var existing = new List<ModuleInfo>
        {
            Mod("ExistsInStg"),
            Mod("MissingInStg"),
            Mod("existsinstg"),
        };
        CreateService().MarkAbsentAsNew(present, existing);

        Assert.False(existing[0].IsNewCandidate);
        Assert.True(existing[1].IsNewCandidate);
        Assert.False(existing[2].IsNewCandidate);
    }

    [Fact]
    public void IsAuthoritativeStgResult_RejectsEmptyStgWhenDevHasModules()
    {
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ModuleQueryService.IsAuthoritativeStgResult(empty, existingDevCount: 3));
        Assert.True(ModuleQueryService.IsAuthoritativeStgResult(empty, existingDevCount: 0));
        Assert.True(ModuleQueryService.IsAuthoritativeStgResult(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" }, existingDevCount: 3));
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

            var names = CreateService().TryListGitModuleNames(tmp, "StoredProcedure", "dbo.");

            Assert.NotNull(names);
            Assert.Contains("myproc", names!);
            Assert.Contains("MYPROC", names!);
        }
        finally { DeleteTempGitRoot(tmp); }
    }
}
