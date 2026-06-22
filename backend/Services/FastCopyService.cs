using System.Text;
using System.Threading.Channels;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class FastCopyService
{
    private readonly bool _dryRun;
    private readonly string _fastCopyExe;
    private readonly ILogger<FastCopyService> _logger;

    public FastCopyService(IConfiguration config, ILogger<FastCopyService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _fastCopyExe = config["FastCopyPath"] ?? @"C:\Program Files\FastCopy\FastCopy.exe";
        _logger = logger;
    }

    public async Task<(int applied, int held, string log)> ExecuteAsync(
        List<DbConfig> allConfigs,
        List<PrepareSelection> selections,
        ChannelWriter<LogEntry> writer,
        CancellationToken ct)
    {
        string dryRunTag = _dryRun ? " [DRY-RUN]" : "";
        int applied = 0;
        int held = 0;
        var logLines = new StringBuilder();

        void LogLine(string level, string msg)
        {
            var entry = new LogEntry
            {
                Timestamp = $"[{DateTime.Now:HH:mm:ss}]",
                Level = level,
                Message = msg,
            };
            writer.TryWrite(entry);
            logLines.AppendLine($"{entry.Timestamp} [{level}] {msg}");
        }

        LogLine("INFO", $"本番前準備を開始します{dryRunTag}");

        var selByDb = selections.GroupBy(s => s.DbName);

        foreach (var dbGroup in selByDb)
        {
            var config = allConfigs.FirstOrDefault(c => c.Name == dbGroup.Key);
            if (config is null) continue;

            LogLine("STEP", $"▶ {config.Name}");

            var applyList = dbGroup.Where(s => s.Apply).ToList();
            var holdList = dbGroup.Where(s => !s.Apply && s.Source == "deployed").ToList();

            // SQL Server files
            var sqlApply = applyList.Where(s => s.DbType == "sqlserver").ToList();
            var sqlHold = holdList.Where(s => s.DbType == "sqlserver").ToList();

            if (sqlApply.Count > 0)
            {
                LogLine("INFO", $"  FastCopy: {sqlApply.Count} 件コピー (SQLServer){dryRunTag}");
                foreach (var sel in sqlApply)
                {
                    var srcDir = sel.Source == "hold" ? config.DeployedHoldPath : config.DeployedPath;
                    var src = Path.Combine(srcDir, sel.FileName);
                    var dest = config.Deploy2PrdPath;
                    LogLine("DETAIL", $"  → {sel.FileName}{dryRunTag}");

                    if (!_dryRun)
                        await RunFastCopyAsync(src, dest, ct);

                    // delete source after copy
                    if (!_dryRun && File.Exists(src))
                        File.Delete(src);

                    applied++;
                }
            }

            foreach (var sel in sqlHold)
            {
                var src = Path.Combine(config.DeployedPath, sel.FileName);
                var dest = Path.Combine(config.DeployedHoldPath, sel.FileName);
                LogLine("DETAIL", $"  → {sel.FileName}  [保留へ移動]{dryRunTag}");

                if (!_dryRun)
                {
                    Directory.CreateDirectory(config.DeployedHoldPath);
                    File.Move(src, dest, overwrite: true);
                }
                held++;
            }

            // MariaDB files
            var mdbApply = applyList.Where(s => s.DbType == "mariadb").ToList();
            var mdbHold = holdList.Where(s => s.DbType == "mariadb").ToList();

            if (mdbApply.Count > 0)
            {
                LogLine("INFO", $"  FastCopy: {mdbApply.Count} 件コピー (MariaDB){dryRunTag}");
                foreach (var sel in mdbApply)
                {
                    var srcDir = sel.Source == "hold" ? config.MariaDbDeployedHoldPath : config.MariaDbDeployedPath;
                    var src = Path.Combine(srcDir, sel.FileName);
                    var mariaDbDest = Path.Combine(config.Deploy2PrdPath, "MariaDB");
                    LogLine("DETAIL", $"  → MariaDB/{sel.FileName}{dryRunTag}");

                    if (!_dryRun)
                        await RunFastCopyAsync(src, mariaDbDest, ct);

                    if (!_dryRun && File.Exists(src))
                        File.Delete(src);

                    applied++;
                }
            }

            foreach (var sel in mdbHold)
            {
                var src = Path.Combine(config.MariaDbDeployedPath, sel.FileName);
                var dest = Path.Combine(config.MariaDbDeployedHoldPath, sel.FileName);
                LogLine("DETAIL", $"  → MariaDB/{sel.FileName}  [保留へ移動]{dryRunTag}");

                if (!_dryRun)
                {
                    Directory.CreateDirectory(config.MariaDbDeployedHoldPath);
                    File.Move(src, dest, overwrite: true);
                }
                held++;
            }
        }

        LogLine("OK", $"✅ 本番前準備が完了しました  適用: {applied} 件  保留: {held} 件");
        return (applied, held, logLines.ToString());
    }

    private async Task RunFastCopyAsync(string src, string destDir, CancellationToken ct)
    {
        if (!File.Exists(_fastCopyExe))
        {
            _logger.LogWarning("FastCopy not found at {Path}, using File.Copy fallback", _fastCopyExe);
            Directory.CreateDirectory(destDir);
            File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), overwrite: true);
            return;
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _fastCopyExe,
            Arguments = $"/cmd=diff /force_close \"{src}\" /to=\"{destDir}\\\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        proc.Start();
        await proc.WaitForExitAsync(ct);
    }
}
