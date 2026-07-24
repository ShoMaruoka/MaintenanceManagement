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
                ErrorMessage TEXT,
                LogDetail    TEXT
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
            CREATE TABLE IF NOT EXISTS AppUser (
                UserId      INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName    TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS WebSourceDeployLog (
                LogId      INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId      TEXT NOT NULL,
                DbName     TEXT NOT NULL,
                TargetName TEXT NOT NULL,
                Mode       TEXT NOT NULL,
                ExecutedBy TEXT NOT NULL,
                ExecutedAt TEXT NOT NULL,
                Result     TEXT NOT NULL,
                LogDetail  TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_WebSourceDeployLog_DbName_ExecutedAt
                ON WebSourceDeployLog (DbName, ExecutedAt DESC);
            """;
        cmd.ExecuteNonQuery();

        // Role カラムが存在しない場合は追加（既存 DB の後方互換）
        try
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE AppUser ADD COLUMN Role TEXT NOT NULL DEFAULT 'user';";
            alter.ExecuteNonQuery();
        }
        catch { /* 既にカラムが存在する場合は無視 */ }

        // LogDetail カラムが存在しない場合は追加（既存 DB の後方互換）
        try
        {
            using var alterLog = conn.CreateCommand();
            alterLog.CommandText = "ALTER TABLE DeploySession ADD COLUMN LogDetail TEXT;";
            alterLog.ExecuteNonQuery();
        }
        catch { /* 既にカラムが存在する場合は無視 */ }
    }

    public List<AppUser> GetAllUsers()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, DisplayName, Role FROM AppUser ORDER BY UserId;";
        var users = new List<AppUser>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new AppUser
            {
                UserId      = reader.GetInt64(0),
                UserName    = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Role        = reader.GetString(3),
            });
        }
        return users;
    }

    public AppUser AddUser(string userName, string displayName, string role = "user")
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppUser (UserName, DisplayName, Role) VALUES ($userName, $displayName, $role);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$userName", userName);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$role", role);
        var id = (long)(cmd.ExecuteScalar() ?? 0);
        return new AppUser { UserId = id, UserName = userName, DisplayName = displayName, Role = role };
    }

    public bool DeleteUser(string userName)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AppUser WHERE UserName = $userName;";
        cmd.Parameters.AddWithValue("$userName", userName);
        return cmd.ExecuteNonQuery() > 0;
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

    public void UpdateDeploySessionStatus(long sessionId, string status, string? errorMessage = null, string? logDetail = null)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE DeploySession SET Status = $status, ErrorMessage = $errorMessage, LogDetail = $logDetail
            WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$logDetail", logDetail ?? (object)DBNull.Value);
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

    public long InsertWebSourceDeployLog(string runId, string dbName, string targetName, string mode, string executedBy, string result, string? logDetail)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WebSourceDeployLog (RunId, DbName, TargetName, Mode, ExecutedBy, ExecutedAt, Result, LogDetail)
            VALUES ($runId, $dbName, $targetName, $mode, $executedBy, $executedAt, $result, $logDetail);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$runId", runId);
        cmd.Parameters.AddWithValue("$dbName", dbName);
        cmd.Parameters.AddWithValue("$targetName", targetName);
        cmd.Parameters.AddWithValue("$mode", mode);
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$result", result);
        cmd.Parameters.AddWithValue("$logDetail", logDetail ?? (object)DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0);
    }

    public List<DeploySession> GetRecentSessions(int limit = 50)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ds.SessionId, ds.DbName, ds.ExecutedBy, ds.ExecutedAt, ds.Status, ds.ErrorMessage,
                   COUNT(dsd.DetailId) as ModuleCount,
                   GROUP_CONCAT(dsd.OpType || ':' || dsd.ModuleType || ':' || dsd.ModuleName || ':' || dsd.Result, '|') as ModuleSummary
            FROM DeploySession ds
            LEFT JOIN DeploySessionDetail dsd ON ds.SessionId = dsd.SessionId
            GROUP BY ds.SessionId, ds.DbName, ds.ExecutedBy, ds.ExecutedAt, ds.Status, ds.ErrorMessage
            ORDER BY ds.SessionId DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var sessions = new List<DeploySession>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var moduleCount = reader.IsDBNull(6) ? 0 : (int)reader.GetInt64(6);
            var moduleSummary = reader.IsDBNull(7) ? null : reader.GetString(7);
            var details = BuildDetailsFromSummary(reader.GetInt64(0), moduleSummary);

            sessions.Add(new DeploySession
            {
                SessionId    = reader.GetInt64(0),
                DbName       = reader.GetString(1),
                ExecutedBy   = reader.GetString(2),
                ExecutedAt   = reader.GetString(3),
                Status       = reader.GetString(4),
                ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
                Details      = details,
            });
        }
        return sessions;
    }

    private static List<DeploySessionDetail> BuildDetailsFromSummary(long sessionId, string? summary)
    {
        if (string.IsNullOrEmpty(summary)) return [];
        return summary.Split('|')
            .Select(part =>
            {
                var pieces = part.Split(':', 4);
                return new DeploySessionDetail
                {
                    SessionId  = sessionId,
                    OpType     = pieces.Length > 0 ? pieces[0] : "",
                    ModuleType = pieces.Length > 1 ? pieces[1] : "",
                    ModuleName = pieces.Length > 2 ? pieces[2] : "",
                    Result     = pieces.Length > 3 ? pieces[3] : "success",
                };
            })
            .ToList();
    }

    public DeploySession? GetSessionById(long sessionId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SessionId, DbName, ExecutedBy, ExecutedAt, Status, ErrorMessage, LogDetail
            FROM DeploySession WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new DeploySession
        {
            SessionId    = reader.GetInt64(0),
            DbName       = reader.GetString(1),
            ExecutedBy   = reader.GetString(2),
            ExecutedAt   = reader.GetString(3),
            Status       = reader.GetString(4),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            LogDetail    = reader.IsDBNull(6) ? null : reader.GetString(6),
        };
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
