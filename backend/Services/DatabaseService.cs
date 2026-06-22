using Microsoft.Data.Sqlite;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration config)
    {
        var dbPath = config["DatabasePath"] ?? "maintenance.db";
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={dbPath}";
    }

    public void EnsureCreated()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS DeploySession (
                SessionId    INTEGER PRIMARY KEY AUTOINCREMENT,
                DbName       TEXT NOT NULL,
                ExecutedBy   TEXT NOT NULL,
                ExecutedAt   TEXT NOT NULL,
                Status       TEXT NOT NULL,
                ErrorMessage TEXT
            );
            CREATE TABLE IF NOT EXISTS DeploySessionDetail (
                DetailId     INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId    INTEGER NOT NULL REFERENCES DeploySession(SessionId),
                OpType       TEXT    NOT NULL,
                ModuleType   TEXT    NOT NULL,
                ModuleName   TEXT    NOT NULL,
                Result       TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ProductionReadyLog (
                LogId        INTEGER PRIMARY KEY AUTOINCREMENT,
                ExecutedBy   TEXT NOT NULL,
                ExecutedAt   TEXT NOT NULL,
                AppliedFiles INTEGER NOT NULL,
                HeldFiles    INTEGER NOT NULL,
                Result       TEXT NOT NULL,
                LogDetail    TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public long InsertDeploySession(string dbName, string executedBy)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DeploySession (DbName, ExecutedBy, ExecutedAt, Status)
            VALUES ($dbName, $executedBy, $executedAt, 'running');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$dbName", dbName);
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", DateTime.UtcNow.ToString("o"));
        return (long)(cmd.ExecuteScalar() ?? 0);
    }

    public void UpdateDeploySessionStatus(long sessionId, string status, string? errorMessage = null)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE DeploySession SET Status = $status, ErrorMessage = $errorMessage
            WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.ExecuteNonQuery();
    }

    public void InsertDeployDetail(long sessionId, string opType, string moduleType, string moduleName, string result)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DeploySessionDetail (SessionId, OpType, ModuleType, ModuleName, Result)
            VALUES ($sessionId, $opType, $moduleType, $moduleName, $result);
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$opType", opType);
        cmd.Parameters.AddWithValue("$moduleType", moduleType);
        cmd.Parameters.AddWithValue("$moduleName", moduleName);
        cmd.Parameters.AddWithValue("$result", result);
        cmd.ExecuteNonQuery();
    }

    public long InsertProductionReadyLog(string executedBy, int appliedFiles, int heldFiles, string result, string? logDetail)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ProductionReadyLog (ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, LogDetail)
            VALUES ($executedBy, $executedAt, $appliedFiles, $heldFiles, $result, $logDetail);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$appliedFiles", appliedFiles);
        cmd.Parameters.AddWithValue("$heldFiles", heldFiles);
        cmd.Parameters.AddWithValue("$result", result);
        cmd.Parameters.AddWithValue("$logDetail", logDetail ?? (object)DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0);
    }

    public List<DeploySession> GetRecentSessions(int limit = 50)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SessionId, DbName, ExecutedBy, ExecutedAt, Status, ErrorMessage
            FROM DeploySession ORDER BY SessionId DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var sessions = new List<DeploySession>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new DeploySession
            {
                SessionId    = reader.GetInt64(0),
                DbName       = reader.GetString(1),
                ExecutedBy   = reader.GetString(2),
                ExecutedAt   = reader.GetString(3),
                Status       = reader.GetString(4),
                ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return sessions;
    }

    public List<DeploySessionDetail> GetSessionDetails(long sessionId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DetailId, SessionId, OpType, ModuleType, ModuleName, Result
            FROM DeploySessionDetail WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        var details = new List<DeploySessionDetail>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            details.Add(new DeploySessionDetail
            {
                DetailId   = reader.GetInt64(0),
                SessionId  = reader.GetInt64(1),
                OpType     = reader.GetString(2),
                ModuleType = reader.GetString(3),
                ModuleName = reader.GetString(4),
                Result     = reader.GetString(5),
            });
        }
        return details;
    }

    public List<ProductionReadyLog> GetRecentPrepLogs(int limit = 20)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT LogId, ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, LogDetail
            FROM ProductionReadyLog ORDER BY LogId DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var logs = new List<ProductionReadyLog>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(new ProductionReadyLog
            {
                LogId        = reader.GetInt64(0),
                ExecutedBy   = reader.GetString(1),
                ExecutedAt   = reader.GetString(2),
                AppliedFiles = reader.GetInt32(3),
                HeldFiles    = reader.GetInt32(4),
                Result       = reader.GetString(5),
                LogDetail    = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }
        return logs;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
