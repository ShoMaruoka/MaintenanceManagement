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
        MarkNewCandidates(config.GitRepoPath, "StoredProcedure", "dbo.", response.StoredProcedures);
        MarkNewCandidates(config.GitRepoPath, "Function", "dbo.", response.Functions);
        MarkNewCandidates(config.GitRepoPath, "VIEW", "dbo.", response.Views);
        MarkNewCandidates(config.GitRepoPath, "Table", "dbo.", response.Tables);
        MarkNewCandidates(config.GitRepoPath, "UserDefinedTableType", "dbo.", response.UserDefinedTableTypes);
        MarkMariaDbStoredNewCandidates(config.MariaDbGitRepoPath, response.MariaDb, response.MariaDbFunctions);
        MarkNewCandidates(config.MariaDbGitRepoPath, "Table", "", response.MariaDbTables);

        response.StoredProcedures.AddRange(FindDeleteCandidates(config.GitRepoPath, "StoredProcedure", "StoredProcedure", response.StoredProcedures, gitOnly: false));
        response.Functions.AddRange(FindDeleteCandidates(config.GitRepoPath, "Function", "Function", response.Functions, gitOnly: false));
        response.Views.AddRange(FindDeleteCandidates(config.GitRepoPath, "VIEW", "VIEW", response.Views, gitOnly: false));
        response.Tables.AddRange(FindDeleteCandidates(config.GitRepoPath, "Table", "Table", response.Tables, gitOnly: true));
        response.UserDefinedTableTypes.AddRange(FindDeleteCandidates(config.GitRepoPath, "UserDefinedTableType", "UserDefinedTableType", response.UserDefinedTableTypes, gitOnly: true));

        // MariaDB: ストアドプロシージャとファンクションは Git 上で同じ "Stored" フォルダに混在するため、
        // 汎用の FindDeleteCandidates（1フォルダ=1種別前提）は使えず、ファイル内容から種別を判定する専用ロジックを使う。
        var (mariaDbStoredCandidates, mariaDbFunctionCandidates) = FindMariaDbStoredDeleteCandidates(
            config.MariaDbGitRepoPath, response.MariaDb, response.MariaDbFunctions);
        response.MariaDb.AddRange(mariaDbStoredCandidates);
        response.MariaDbFunctions.AddRange(mariaDbFunctionCandidates);

        response.MariaDbTables.AddRange(FindDeleteCandidates(config.MariaDbGitRepoPath, "Table", "MariaDbTable", response.MariaDbTables, gitOnly: true, fileNamePrefix: ""));

        return response;
    }

    /// <summary>
    /// DB に存在するモジュールについて、対応する Git ファイルが無ければ新規候補（IsNewCandidate=true）にする。
    /// 対象タイプのサブフォルダ自体が無い場合は何もしない（誤って全件「新規」にしないため）。
    /// </summary>
    internal void MarkNewCandidates(string gitRepoPath, string folderName, string fileNamePrefix, List<ModuleInfo> existing)
    {
        if (string.IsNullOrEmpty(gitRepoPath) || existing.Count == 0) return;

        var dir = Path.Combine(gitRepoPath, folderName);
        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (var m in existing)
            {
                var path = Path.Combine(dir, $"{fileNamePrefix}{m.Name}.sql");
                if (!File.Exists(path))
                    m.IsNewCandidate = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "New candidate detection failed for folder={Folder}", folderName);
        }
    }

    /// <summary>
    /// MariaDB Stored フォルダ（PROCEDURE / FUNCTION 混在）向けの新規候補判定。
    /// ファイル内容の種別判定は不要（DB 側で種別は確定済み）。存在有無のみ見る。
    /// </summary>
    internal void MarkMariaDbStoredNewCandidates(
        string gitRepoPath, List<ModuleInfo> existingStored, List<ModuleInfo> existingFunctions)
    {
        if (string.IsNullOrEmpty(gitRepoPath)) return;

        var dir = Path.Combine(gitRepoPath, "Stored");
        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (var m in existingStored.Concat(existingFunctions))
            {
                var path = Path.Combine(dir, $"{m.Name}.sql");
                if (!File.Exists(path))
                    m.IsNewCandidate = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB Stored new candidate detection failed");
        }
    }

    /// <summary>
    /// MariaDB の Stored フォルダ（PROCEDURE と FUNCTION が混在）から削除候補を検出する。
    /// フォルダを1回だけ走査し、DB未取得（存在しない＝削除候補）のファイルを
    /// ファイル内容（<c>CREATE DEFINER=... FUNCTION/PROCEDURE</c>）から種別判定して振り分ける。
    /// </summary>
    private (List<ModuleInfo> Stored, List<ModuleInfo> Functions) FindMariaDbStoredDeleteCandidates(
        string gitRepoPath, List<ModuleInfo> existingStored, List<ModuleInfo> existingFunctions)
    {
        var storedCandidates = new List<ModuleInfo>();
        var functionCandidates = new List<ModuleInfo>();
        if (string.IsNullOrEmpty(gitRepoPath)) return (storedCandidates, functionCandidates);

        var dir = Path.Combine(gitRepoPath, "Stored");
        if (!Directory.Exists(dir)) return (storedCandidates, functionCandidates);

        var existingNames = new HashSet<string>(
            existingStored.Select(m => m.Name).Concat(existingFunctions.Select(m => m.Name)),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.sql"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB Stored delete candidate detection failed");
        }

        return (storedCandidates, functionCandidates);
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
        var candidates = new List<ModuleInfo>();
        if (string.IsNullOrEmpty(gitRepoPath)) return candidates;

        var dir = Path.Combine(gitRepoPath, folderName);
        if (!Directory.Exists(dir)) return candidates;

        var existingNames = new HashSet<string>(existing.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, $"{fileNamePrefix}*.sql"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var name = fileNamePrefix.Length > 0 && fileName.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase)
                    ? fileName[fileNamePrefix.Length..]
                    : fileName;

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete candidate detection failed for type={Type}", moduleType);
        }

        return candidates;
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
