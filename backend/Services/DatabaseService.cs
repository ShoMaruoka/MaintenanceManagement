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
            CREATE INDEX IF NOT EXISTS IX_DeploySessionDetail_SessionId
                ON DeploySessionDetail (SessionId);
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

        // ManualFiles カラムが存在しない場合は追加（既存 DB の後方互換）
        try
        {
            using var alterManual = conn.CreateCommand();
            alterManual.CommandText =
                "ALTER TABLE ProductionReadyLog ADD COLUMN ManualFiles INTEGER NOT NULL DEFAULT 0;";
            alterManual.ExecuteNonQuery();
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

    /// <summary>
    /// 指定 DB の「逆引きキー → 最新の操作区分（新規／更新／削除）」の辞書を返す。
    /// 本番前準備画面で deployed/ の SQL ファイルに区分を表示するために使う。
    ///
    /// deployed/ にファイルがある＝適用が成功している（Step6 は成功時のみ移動する）ため、
    /// セッション・明細の成否では絞らない。MariaDB は明細にセッション全体の成否が
    /// 書かれるため、絞ると正しい区分が引けなくなる。
    ///
    /// 重複排除の粒度は ModuleType 単位ではなく「DB 種別＋モジュール名」単位。
    /// 分類ルールを SQL と C# に二重定義しないよう、SQL は素直に古い順で返し、
    /// C# 側で OpTypeResolver を通しながら後勝ちで畳み込む。
    /// </summary>
    public Dictionary<string, string> GetLatestOpTypes(string dbName)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.ModuleType, d.ModuleName, d.OpType
            FROM DeploySessionDetail d
            JOIN DeploySession s ON s.SessionId = d.SessionId
            WHERE s.DbName = $dbName
            ORDER BY d.DetailId ASC;
            """;
        cmd.Parameters.AddWithValue("$dbName", dbName);

        var map = new Dictionary<string, string>(OpTypeResolver.KeyComparer);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = OpTypeResolver.ModuleKey(reader.GetString(0), reader.GetString(1));
            map[key] = OpTypeResolver.NormalizeOpType(reader.GetString(2));
        }
        return map;
    }

    public long InsertProductionReadyLog(
        string executedBy, int appliedFiles, int heldFiles, int manualFiles, string result, string? logDetail)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ProductionReadyLog (ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, ManualFiles, Result, LogDetail)
            VALUES ($executedBy, $executedAt, $appliedFiles, $heldFiles, $manualFiles, $result, $logDetail);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$executedBy", executedBy);
        cmd.Parameters.AddWithValue("$executedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$appliedFiles", appliedFiles);
        cmd.Parameters.AddWithValue("$heldFiles", heldFiles);
        cmd.Parameters.AddWithValue("$manualFiles", manualFiles);
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
            SELECT LogId, ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, LogDetail, ManualFiles
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
                ManualFiles  = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            });
        }
        return logs;
    }

    /// <summary>
    /// ダッシュボードのサマリーカード用の集計を 1 コネクションでまとめて取得する。
    /// ExecutedAt は ISO 8601 (UTC) 文字列なので、辞書順比較で期間絞り込みができる。
    /// </summary>
    public DashboardStats GetDashboardStats(int days = 30)
    {
        using var conn = OpenConnection();
        var stats = new DashboardStats { Days = days };
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("o");

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*), SUM(CASE WHEN Status = 'success' THEN 1 ELSE 0 END)
                FROM DeploySession
                WHERE ExecutedAt >= $cutoff AND Status IN ('success', 'failed');
                """;
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                stats.TotalSessions   = (int)reader.GetInt64(0);
                stats.SuccessSessions = reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM DeploySession WHERE Status = 'running';
                """;
            stats.RunningCount = (int)(long)(cmd.ExecuteScalar() ?? 0L);
        }

        if (stats.RunningCount > 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DbName, ExecutedBy FROM DeploySession
                WHERE Status = 'running' ORDER BY SessionId DESC LIMIT 1;
                """;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                stats.RunningDbName     = reader.GetString(0);
                stats.RunningExecutedBy = reader.GetString(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT LogId, ExecutedBy, ExecutedAt, AppliedFiles, HeldFiles, Result, ManualFiles
                FROM ProductionReadyLog ORDER BY LogId DESC LIMIT 1;
                """;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                // LogDetail はサマリー表示に不要なため取得しない（ログ本文が大きくなりうる）。
                stats.LastPrepare = new ProductionReadyLog
                {
                    LogId        = reader.GetInt64(0),
                    ExecutedBy   = reader.GetString(1),
                    ExecutedAt   = reader.GetString(2),
                    AppliedFiles = reader.GetInt32(3),
                    HeldFiles    = reader.GetInt32(4),
                    Result       = reader.GetString(5),
                    ManualFiles  = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                };
            }
        }

        stats.LastPilotKaios = GetLastSuccessfulPilotDeploy(conn, "kaios");
        stats.LastPilotGos = GetLastSuccessfulPilotDeploy(conn, "gos");

        return stats;
    }

    /// <summary>
    /// Pilot 最終成功（Issue #35）:
    /// 同一 RunId の全行が success かつ、全行が除外 Mode 集合に属するわけではない Run のうち、
    /// 最も新しい ExecutedAt を返す。履歴なしは null。
    /// </summary>
    private static PilotDeploySummary? GetLastSuccessfulPilotDeploy(SqliteConnection conn, string dbName)
    {
        using var cmd = conn.CreateCommand();
        // 除外 Mode は有限リストの完全一致 IN（E1）。部分一致は使わない。
        cmd.CommandText = """
            SELECT t.ExecutedAt, t.ExecutedBy
            FROM (
                SELECT
                    RunId,
                    MAX(ExecutedAt) AS ExecutedAt,
                    COUNT(*) AS TotalRows,
                    SUM(CASE WHEN Result = 'success' THEN 1 ELSE 0 END) AS SuccessRows,
                    SUM(CASE WHEN Mode IN (
                        'both-dryrun', 'web-dryrun', 'sql-dryrun', 'sql-skipped'
                    ) THEN 1 ELSE 0 END) AS ExcludedModeRows
                FROM WebSourceDeployLog
                WHERE DbName = $dbName
                GROUP BY RunId
            ) AS r
            -- ExecutedBy は辞書順 MAX ではなく、その Run の最新 ExecutedAt 行から取る（PR #37 #5）
            INNER JOIN WebSourceDeployLog AS t
                ON t.RunId = r.RunId
               AND t.DbName = $dbName
               AND t.ExecutedAt = r.ExecutedAt
            WHERE r.SuccessRows = r.TotalRows
              AND r.ExcludedModeRows < r.TotalRows
            ORDER BY t.ExecutedAt DESC, t.TargetName
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$dbName", dbName);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new PilotDeploySummary
        {
            DbName = dbName,
            ExecutedAt = reader.GetString(0),
            ExecutedBy = reader.GetString(1),
        };
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
