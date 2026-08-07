using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class ModuleQueryService
{
    private readonly ILogger<ModuleQueryService> _logger;

    public ModuleQueryService(ILogger<ModuleQueryService> logger)
    {
        _logger = logger;
    }

    public async Task<ModuleListResponse> GetModulesAsync(DbConfig config)
    {
        var response = new ModuleListResponse { DbName = config.Name };

        var sqlTasks = new[]
        {
            QuerySqlServerAsync(config.DevConnectionString, """
                SELECT name, CONVERT(varchar(16), modify_date, 120) as modify_date
                FROM sys.procedures WHERE is_ms_shipped = 0 ORDER BY name
                """, "StoredProcedure", false),
            QuerySqlServerAsync(config.DevConnectionString, """
                SELECT name, CONVERT(varchar(16), modify_date, 120) as modify_date
                FROM sys.objects WHERE type IN ('FN','TF','IF') AND is_ms_shipped = 0 ORDER BY name
                """, "Function", false),
            QuerySqlServerAsync(config.DevConnectionString, """
                SELECT name, CONVERT(varchar(16), modify_date, 120) as modify_date
                FROM sys.views WHERE is_ms_shipped = 0 ORDER BY name
                """, "VIEW", false),
            QuerySqlServerAsync(config.DevConnectionString, """
                SELECT name, CONVERT(varchar(16), modify_date, 120) as modify_date
                FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name
                """, "Table", true),
            QuerySqlServerAsync(config.DevConnectionString, """
                SELECT name, NULL as modify_date
                FROM sys.types WHERE is_user_defined = 1 AND is_table_type = 1 ORDER BY name
                """, "UserDefinedTableType", true),
        };

        var results = await Task.WhenAll(sqlTasks);
        response.StoredProcedures = results[0];
        response.Functions = results[1];
        response.Views = results[2];
        response.Tables = results[3];
        response.UserDefinedTableTypes = results[4];

        if (!string.IsNullOrEmpty(config.MariaDbConnectionString))
        {
            var (procedures, functions) = await QueryMariaDbRoutinesAsync(config.MariaDbConnectionString);
            response.MariaDb = procedures;
            response.MariaDbFunctions = functions;
            response.MariaDbTables = await QueryMariaDbTablesAsync(config.MariaDbConnectionString);
        }

        // 新規候補判定は削除候補の AddRange より前に行う（削除候補＝Gitのみは判定対象外にするため）
        // SQL Server: STG DB 存在判定を優先し、未設定／照会失敗時のみ Git を代理指標にする
        var stgResolvedTypes = await MarkSqlServerNewCandidatesAsync(config, response);

        // Git フォルダは種別ごとに 1 回だけ列挙し、削除候補検出と（フォールバック時の）新規判定で共有する
        ApplyGitScan(config.GitRepoPath, "StoredProcedure", "StoredProcedure", "dbo.", response.StoredProcedures, gitOnly: false, stgResolvedTypes);
        ApplyGitScan(config.GitRepoPath, "Function", "Function", "dbo.", response.Functions, gitOnly: false, stgResolvedTypes);
        ApplyGitScan(config.GitRepoPath, "VIEW", "VIEW", "dbo.", response.Views, gitOnly: false, stgResolvedTypes);
        ApplyGitScan(config.GitRepoPath, "Table", "Table", "dbo.", response.Tables, gitOnly: true, stgResolvedTypes);
        ApplyGitScan(config.GitRepoPath, "UserDefinedTableType", "UserDefinedTableType", "dbo.", response.UserDefinedTableTypes, gitOnly: true, stgResolvedTypes);

        // MariaDB: Stored は Procedure/Function 混在。Git 列挙を 1 回で新規＋削除に使う
        ApplyMariaDbStoredGitScan(config.MariaDbGitRepoPath, response.MariaDb, response.MariaDbFunctions);
        ApplyGitScan(config.MariaDbGitRepoPath, "Table", "MariaDbTable", "", response.MariaDbTables, gitOnly: true, stgResolvedTypes);

        return response;
    }

    /// <summary>
    /// SQL Server 系の新規候補判定。
    /// <see cref="DbConfig.StgConnectionString"/> があれば STG 上の存在を権威ある判定とし、
    /// 未設定または照会失敗時は Git ファイル存在（ApplyGitScan 側）にフォールバックする。
    /// </summary>
    /// <returns>STG 判定に成功した moduleType の集合（Git 新規判定をスキップするため）。</returns>
    private async Task<HashSet<string>> MarkSqlServerNewCandidatesAsync(DbConfig config, ModuleListResponse response)
    {
        var stgResolvedTypes = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(config.StgConnectionString)) return stgResolvedTypes;

        var queries = new (string Sql, List<ModuleInfo> Target, string Label)[]
        {
            ("""
                SELECT name FROM sys.procedures WHERE is_ms_shipped = 0
                """, response.StoredProcedures, "StoredProcedure"),
            ("""
                SELECT name FROM sys.objects WHERE type IN ('FN','TF','IF') AND is_ms_shipped = 0
                """, response.Functions, "Function"),
            ("""
                SELECT name FROM sys.views WHERE is_ms_shipped = 0
                """, response.Views, "VIEW"),
            ("""
                SELECT name FROM sys.tables WHERE is_ms_shipped = 0
                """, response.Tables, "Table"),
            ("""
                SELECT name FROM sys.types WHERE is_user_defined = 1 AND is_table_type = 1
                """, response.UserDefinedTableTypes, "UserDefinedTableType"),
        };

        var tasks = queries.Select(q => QuerySqlServerNamesAsync(config.StgConnectionString, q.Sql, q.Label)).ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < queries.Length; i++)
        {
            // null = 照会失敗 → 当該種別は Git フォールバック（ApplyGitScan）に委ねる
            if (results[i] == null) continue;
            MarkAbsentAsNew(results[i]!, queries[i].Target);
            stgResolvedTypes.Add(queries[i].Label);
        }

        return stgResolvedTypes;
    }

    /// <summary>
    /// presentNames に含まれない existing 要素を新規候補にする（STG / Git 共通）。
    /// </summary>
    internal void MarkAbsentAsNew(HashSet<string> presentNames, List<ModuleInfo> existing)
    {
        if (existing.Count == 0) return;
        foreach (var m in existing)
        {
            if (!presentNames.Contains(m.Name))
                m.IsNewCandidate = true;
        }
    }

    /// <summary>
    /// Git フォルダを 1 回列挙し、必要なら新規候補を付けたうえで削除候補を AddRange する。
    /// </summary>
    private void ApplyGitScan(
        string gitRepoPath, string folderName, string moduleType, string fileNamePrefix,
        List<ModuleInfo> existing, bool gitOnly, HashSet<string> stgResolvedTypes)
    {
        var gitNames = TryListGitModuleNames(gitRepoPath, folderName, fileNamePrefix);
        // STG 判定済みでなければ Git を代理指標として新規判定する
        if (!stgResolvedTypes.Contains(moduleType))
            MarkNewCandidatesFromNames(gitNames, existing);
        existing.AddRange(BuildDeleteCandidates(gitNames, moduleType, existing, gitOnly));
    }

    private void ApplyMariaDbStoredGitScan(
        string gitRepoPath, List<ModuleInfo> existingStored, List<ModuleInfo> existingFunctions)
    {
        // MariaDB に STG 接続は無いため常に Git。フォルダを 1 回だけ走査して新規＋削除を同時に処理する。
        if (string.IsNullOrEmpty(gitRepoPath)) return;

        var dir = Path.Combine(gitRepoPath, "Stored");
        if (!Directory.Exists(dir)) return;

        try
        {
            var gitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingNames = new HashSet<string>(
                existingStored.Select(m => m.Name).Concat(existingFunctions.Select(m => m.Name)),
                StringComparer.OrdinalIgnoreCase);
            var storedCandidates = new List<ModuleInfo>();
            var functionCandidates = new List<ModuleInfo>();

            foreach (var file in Directory.EnumerateFiles(dir, "*.sql"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                gitNames.Add(name);
                if (existingNames.Contains(name)) continue;

                var isFunction = IsMariaDbFunctionFile(file);
                var candidate = new ModuleInfo
                {
                    Name = name,
                    Type = isFunction ? "MariaDbFunction" : "Stored",
                    ModifyDate = "",
                    GitOnly = false,
                    IsDeleteCandidate = true,
                };
                (isFunction ? functionCandidates : storedCandidates).Add(candidate);
                existingNames.Add(name);
            }

            if (existingStored.Count > 0) MarkAbsentAsNew(gitNames, existingStored);
            if (existingFunctions.Count > 0) MarkAbsentAsNew(gitNames, existingFunctions);
            existingStored.AddRange(storedCandidates);
            existingFunctions.AddRange(functionCandidates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB Stored git scan (new+delete) failed");
        }
    }

    /// <summary>
    /// Git サブフォルダ内のモジュール名一覧を返す。
    /// path 空・サブフォルダ未存在・列挙失敗時は null（新規判定をスキップ＝更新扱いのまま）。
    /// 比較は <see cref="StringComparer.OrdinalIgnoreCase"/>（削除候補判定と同じ）。
    /// </summary>
    internal HashSet<string>? TryListGitModuleNames(string gitRepoPath, string folderName, string fileNamePrefix)
    {
        if (string.IsNullOrEmpty(gitRepoPath)) return null;

        var dir = Path.Combine(gitRepoPath, folderName);
        if (!Directory.Exists(dir)) return null;

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(dir, $"{fileNamePrefix}*.sql"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var name = fileNamePrefix.Length > 0 && fileName.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase)
                    ? fileName[fileNamePrefix.Length..]
                    : fileName;
                names.Add(name);
            }
            return names;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git module name listing failed for folder={Folder}", folderName);
            return null;
        }
    }

    /// <summary>
    /// Git 名一覧に無い existing を新規候補にする。
    /// gitNames が null のときは何もしない（サブフォルダ未存在・path 空・列挙失敗＝更新扱い）。
    /// 例外で途中停止した場合も未処理分は更新扱いで残る。
    /// </summary>
    internal void MarkNewCandidatesFromNames(HashSet<string>? gitNames, List<ModuleInfo> existing)
    {
        if (gitNames == null || existing.Count == 0) return;
        MarkAbsentAsNew(gitNames, existing);
    }

    /// <summary>
    /// DB に存在するモジュールについて、対応する Git ファイルが無ければ新規候補にする（Git フォールバック用）。
    /// 対象タイプのサブフォルダ自体が無い場合は何もしない（誤って全件「新規」にしないため）。
    /// </summary>
    internal void MarkNewCandidates(string gitRepoPath, string folderName, string fileNamePrefix, List<ModuleInfo> existing)
    {
        var gitNames = TryListGitModuleNames(gitRepoPath, folderName, fileNamePrefix);
        MarkNewCandidatesFromNames(gitNames, existing);
    }

    /// <summary>
    /// MariaDB Stored フォルダ（PROCEDURE / FUNCTION 混在）向けの新規候補判定。
    /// ファイル内容の種別判定は不要（DB 側で種別は確定済み）。存在有無のみ見る。
    /// 例外時は以降のモジュールが未判定＝更新扱いで残る。
    /// </summary>
    internal void MarkMariaDbStoredNewCandidates(
        string gitRepoPath, List<ModuleInfo> existingStored, List<ModuleInfo> existingFunctions)
    {
        if (existingStored.Count == 0 && existingFunctions.Count == 0) return;

        var gitNames = TryListGitModuleNames(gitRepoPath, "Stored", "");
        if (gitNames == null) return;

        MarkAbsentAsNew(gitNames, existingStored);
        MarkAbsentAsNew(gitNames, existingFunctions);
    }

    private static List<ModuleInfo> BuildDeleteCandidates(
        HashSet<string>? gitNames, string moduleType, List<ModuleInfo> existing, bool gitOnly)
    {
        var candidates = new List<ModuleInfo>();
        if (gitNames == null) return candidates;

        var existingNames = new HashSet<string>(existing.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in gitNames)
        {
            if (existingNames.Contains(name)) continue;
            candidates.Add(new ModuleInfo
            {
                Name = name,
                Type = moduleType,
                ModifyDate = "",
                GitOnly = gitOnly,
                IsDeleteCandidate = true,
            });
            existingNames.Add(name);
        }
        return candidates;
    }

    /// <summary>Export.py と同じ検出方法（CREATE DEFINER=... FUNCTION/PROCEDURE）でファイルの種別を判定する。</summary>
    private static bool IsMariaDbFunctionFile(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            var match = Regex.Match(content, @"CREATE\s+DEFINER=\S+\s+(FUNCTION|PROCEDURE)", RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups[1].Value, "FUNCTION", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// DB上には存在せず Git リポジトリにのみ残っているファイル（削除候補）を検出する。
    /// SQL Server 系は GitRepoPath 配下・"dbo."プレフィックス付きファイル名が前提だが、
    /// MariaDB 系（folderName と moduleType が一致しない場合がある）はプレフィックスなしのため
    /// <paramref name="fileNamePrefix"/> で切り替えられるようにしている。
    /// </summary>
    /// <param name="gitRepoPath">Git リポジトリのルートパス（SQL Server: GitRepoPath / MariaDB: MariaDbGitRepoPath）。</param>
    /// <param name="folderName">Git リポジトリ内の実フォルダ名（例: MariaDbTable の実フォルダ名は "Table"）。</param>
    /// <param name="moduleType">検出結果の ModuleInfo.Type に設定する内部種別値。</param>
    /// <param name="fileNamePrefix">ファイル名プレフィックス（SQL Server: "dbo." / MariaDB: ""）。</param>
    internal List<ModuleInfo> FindDeleteCandidates(
        string gitRepoPath, string folderName, string moduleType, List<ModuleInfo> existing,
        bool gitOnly, string fileNamePrefix = "dbo.")
    {
        var gitNames = TryListGitModuleNames(gitRepoPath, folderName, fileNamePrefix);
        return BuildDeleteCandidates(gitNames, moduleType, existing, gitOnly);
    }

    /// <summary>
    /// SQL Server 上のオブジェクト名一覧を取得する。失敗時は null（呼び出し側で Git フォールバック）。
    /// </summary>
    private async Task<HashSet<string>?> QuerySqlServerNamesAsync(string connectionString, string sql, string label)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
            return names;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STG SQL Server name query failed for type={Type}; falling back to Git", label);
            return null;
        }
    }

    private async Task<List<ModuleInfo>> QuerySqlServerAsync(string connectionString, string sql, string type, bool gitOnly)
    {
        var list = new List<ModuleInfo>();
        if (string.IsNullOrEmpty(connectionString)) return list;

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ModuleInfo
                {
                    Name = reader.GetString(0),
                    Type = type,
                    ModifyDate = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    GitOnly = gitOnly,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL Server query failed for type={Type}", type);
        }
        return list;
    }

    /// <summary>
    /// 対象スキーマは接続文字列（<c>MariaDbConnectionString</c> の <c>Database=</c>）で既に確定しているため、
    /// アプリ側の設定値（DevDb 等）とは無関係に MySQL 自身の DATABASE() 関数（接続時の既定スキーマ）で絞り込む。
    /// これにより環境（テスト/本番）ごとにDB名が異なっても接続文字列を変えるだけで正しく動作する。
    /// PROCEDURE と FUNCTION は Git 上では同じ "Stored" フォルダに混在するが、DB上は ROUTINE_TYPE で
    /// 区別できるため、一覧表示・削除候補判定では別種別（Stored / MariaDbFunction）として扱う。
    /// </summary>
    private async Task<(List<ModuleInfo> Procedures, List<ModuleInfo> Functions)> QueryMariaDbRoutinesAsync(string connectionString)
    {
        var procedures = new List<ModuleInfo>();
        var functions = new List<ModuleInfo>();
        try
        {
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ROUTINE_NAME, ROUTINE_TYPE,
                       DATE_FORMAT(LAST_ALTERED, '%Y-%m-%d %H:%i') as modify_date
                FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION')
                ORDER BY ROUTINE_NAME
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var isFunction = string.Equals(reader.GetString(1), "FUNCTION", StringComparison.OrdinalIgnoreCase);
                var info = new ModuleInfo
                {
                    Name = reader.GetString(0),
                    Type = isFunction ? "MariaDbFunction" : "Stored",
                    ModifyDate = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    GitOnly = false,
                };
                (isFunction ? functions : procedures).Add(info);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB routines query failed");
        }
        return (procedures, functions);
    }

    private async Task<List<ModuleInfo>> QueryMariaDbTablesAsync(string connectionString)
    {
        var list = new List<ModuleInfo>();
        try
        {
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TABLE_NAME,
                       DATE_FORMAT(UPDATE_TIME, '%Y-%m-%d %H:%i') as modify_date
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ModuleInfo
                {
                    Name = reader.GetString(0),
                    Type = "MariaDbTable",
                    ModifyDate = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    GitOnly = true,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB table query failed");
        }
        return list;
    }
}
