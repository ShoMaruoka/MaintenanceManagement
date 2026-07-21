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
        }

        response.StoredProcedures.AddRange(FindDeleteCandidates(config.GitRepoPath, "StoredProcedure", response.StoredProcedures));
        response.Functions.AddRange(FindDeleteCandidates(config.GitRepoPath, "Function", response.Functions));
        response.Views.AddRange(FindDeleteCandidates(config.GitRepoPath, "VIEW", response.Views));
        response.Tables.AddRange(FindDeleteCandidates(config.GitRepoPath, "Table", response.Tables));
        response.UserDefinedTableTypes.AddRange(FindDeleteCandidates(config.GitRepoPath, "UserDefinedTableType", response.UserDefinedTableTypes));

        return response;
    }

    private List<ModuleInfo> FindDeleteCandidates(string gitRepoPath, string type, List<ModuleInfo> existing)
    {
        var candidates = new List<ModuleInfo>();
        if (string.IsNullOrEmpty(gitRepoPath)) return candidates;

        var dir = Path.Combine(gitRepoPath, type);
        if (!Directory.Exists(dir)) return candidates;

        var existingNames = new HashSet<string>(existing.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "dbo.*.sql"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var name = fileName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase)
                    ? fileName["dbo.".Length..]
                    : fileName;

                if (existingNames.Contains(name)) continue;

                candidates.Add(new ModuleInfo
                {
                    Name = name,
                    Type = type,
                    ModifyDate = "",
                    GitOnly = type is "Table" or "UserDefinedTableType",
                    IsDeleteCandidate = true,
                });
                existingNames.Add(name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete candidate detection failed for type={Type}", type);
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
                    Type = "MariaDB",
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
}
