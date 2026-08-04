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
            response.MariaDb = await QueryMariaDbAsync(config.MariaDbConnectionString, config.DevDb);
            response.MariaDbTables = await QueryMariaDbTablesAsync(config.MariaDbConnectionString, config.DevDb);
        }

        response.StoredProcedures.AddRange(FindDeleteCandidates(config.GitRepoPath, "StoredProcedure", "StoredProcedure", response.StoredProcedures, gitOnly: false));
        response.Functions.AddRange(FindDeleteCandidates(config.GitRepoPath, "Function", "Function", response.Functions, gitOnly: false));
        response.Views.AddRange(FindDeleteCandidates(config.GitRepoPath, "VIEW", "VIEW", response.Views, gitOnly: false));
        response.Tables.AddRange(FindDeleteCandidates(config.GitRepoPath, "Table", "Table", response.Tables, gitOnly: true));
        response.UserDefinedTableTypes.AddRange(FindDeleteCandidates(config.GitRepoPath, "UserDefinedTableType", "UserDefinedTableType", response.UserDefinedTableTypes, gitOnly: true));

        // MariaDB: Git上のフォルダ名は "Stored"/"Table"、ファイル名にプレフィックスは付かない
        response.MariaDb.AddRange(FindDeleteCandidates(config.MariaDbGitRepoPath, "Stored", "Stored", response.MariaDb, gitOnly: false, fileNamePrefix: ""));
        response.MariaDbTables.AddRange(FindDeleteCandidates(config.MariaDbGitRepoPath, "Table", "MariaDbTable", response.MariaDbTables, gitOnly: true, fileNamePrefix: ""));

        return response;
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

    private async Task<List<ModuleInfo>> QueryMariaDbAsync(string connectionString, string schema)
    {
        var list = new List<ModuleInfo>();
        try
        {
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ROUTINE_NAME,
                       DATE_FORMAT(LAST_ALTERED, '%Y-%m-%d %H:%i') as modify_date
                FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA = @schema AND ROUTINE_TYPE = 'PROCEDURE'
                ORDER BY ROUTINE_NAME
                """;
            cmd.Parameters.AddWithValue("@schema", schema);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ModuleInfo
                {
                    Name = reader.GetString(0),
                    Type = "Stored",
                    ModifyDate = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    GitOnly = false,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MariaDB query failed schema={Schema}", schema);
        }
        return list;
    }

    private async Task<List<ModuleInfo>> QueryMariaDbTablesAsync(string connectionString, string schema)
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
                WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;
            cmd.Parameters.AddWithValue("@schema", schema);
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
            _logger.LogError(ex, "MariaDB table query failed schema={Schema}", schema);
        }
        return list;
    }
}
