using System.Text;
using System.Threading.Channels;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class FastCopyService
{
    private readonly bool _dryRun;
    private readonly string _fastCopyExe;
    private readonly ImagePrepareService _imagePrepare;
    private readonly ManualApplyService _manualApply;
    private readonly ILogger<FastCopyService> _logger;

    public FastCopyService(
        IConfiguration config,
        ImagePrepareService imagePrepare,
        ManualApplyService manualApply,
        ILogger<FastCopyService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _fastCopyExe = config["FastCopyPath"] ?? @"C:\Program Files\FastCopy\FastCopy.exe";
        _imagePrepare = imagePrepare;
        _manualApply = manualApply;
        _logger = logger;
    }

    public async Task<(int applied, int held, int manual, string log)> ExecuteAsync(
        List<DbConfig> allConfigs,
        List<PrepareSelection> selections,
        List<PrepareImageSelection> imageSelections,
        List<PrepareManualSelection> manualSelections,
        ChannelWriter<LogEntry> writer,
        CancellationToken ct)
    {
        string dryRunTag = _dryRun ? " [DRY-RUN]" : "";
        int applied = 0;
        int held = 0;
        int manual = 0;
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

        imageSelections ??= [];
        manualSelections ??= [];

        var dbNames = selections.Select(s => s.DbName)
            .Concat(imageSelections.Select(s => s.DbName))
            .Concat(manualSelections.Select(s => s.DbName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var dbName in dbNames)
        {
            var config = allConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
            if (config is null) continue;

            LogLine("STEP", $"▶ {config.Name}");

            var dbSelections = selections
                .Where(s => s.DbName.Equals(dbName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var applyList = dbSelections.Where(s => s.Apply).ToList();
            var holdList = dbSelections.Where(s => !s.Apply && s.Source == "deployed").ToList();

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

            // 画像・静的ファイル（Files → FilesDeploy2PrdPath、相対パス維持で移動）
            var imageApply = imageSelections
                .Where(s => s.DbName.Equals(dbName, StringComparison.OrdinalIgnoreCase) && s.Apply)
                .ToList();

            if (imageApply.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(config.FilesDeploy2PrdPath))
                {
                    LogLine("ERROR", $"  FilesDeploy2PrdPath が未設定です ({config.Name})");
                    throw new InvalidOperationException(
                        $"FilesDeploy2PrdPath is not configured for DB '{config.Name}'");
                }

                LogLine("INFO", $"  画像移動: {imageApply.Count} 件 → FilesDeploy2PrdPath{dryRunTag}");
                foreach (var sel in imageApply)
                {
                    if (!_imagePrepare.TryResolveRelativeFile(config, sel.RelativePath, out var src, out var resolveError))
                    {
                        LogLine("ERROR", $"  → {sel.RelativePath}  パス不正: {resolveError}");
                        throw new InvalidOperationException(resolveError);
                    }

                    if (!_dryRun && !File.Exists(src))
                    {
                        LogLine("ERROR", $"  → {sel.RelativePath}  元ファイルがありません");
                        throw new FileNotFoundException($"Image file not found: {sel.RelativePath}", src);
                    }

                    var dest = ResolveFilesDeployPath(config.FilesDeploy2PrdPath, sel.RelativePath);
                    LogLine("DETAIL", $"  → {sel.RelativePath}{dryRunTag}");

                    if (!_dryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(src, dest, overwrite: true);
                        File.Delete(src);
                        RemoveEmptyParentDirectories(
                            Path.GetDirectoryName(src)!,
                            config.FilesPath,
                            relative => LogLine("DETAIL", $"  → 空フォルダ削除: {relative}"));
                    }

                    applied++;
                }
            }

            // 手動適用（Table / UDTT）の消化。未選択の項目は次回まで待機リストに残す。
            var dbManualSelections = manualSelections
                .Where(s => s.DbName.Equals(dbName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (dbManualSelections.Any(s => s.Apply))
            {
                var consumed = _manualApply.Consume(config, dbManualSelections, LogLine);
                if (consumed > 0)
                    LogLine("INFO", $"  手動適用 確認済み: {consumed} 件 → ManualApply{dryRunTag}");
                manual += consumed;
            }
        }

        var pendingManual = CountPendingManual(allConfigs, manualSelections);

        LogLine("OK", $"✅ 本番前準備が完了しました  適用: {applied} 件  保留: {held} 件  手動適用確認: {manual} 件");
        if (pendingManual > 0)
            LogLine("WARN", $"⚠ 手動適用が未確認のまま残っています: {pendingManual} 件（Table / UDTT）");

        return (applied, held, manual, logLines.ToString());
    }

    /// <summary>未確認のまま持ち越された手動適用項目の件数。DRY-RUN では消化が発生しないため 0 を返す。</summary>
    private int CountPendingManual(
        List<DbConfig> allConfigs,
        List<PrepareManualSelection> manualSelections)
    {
        if (_dryRun) return 0;

        var total = 0;
        foreach (var dbName in manualSelections.Select(s => s.DbName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var config = allConfigs.FirstOrDefault(c => c.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
            if (config is null) continue;
            total += _manualApply.List(config).Count;
        }
        return total;
    }

    /// <summary>
    /// ファイル削除後に空になったサブフォルダのみ削除する。
    /// Images / news / pdf などのカテゴリルートと Files 自体は残す。
    /// </summary>
    private static void RemoveEmptyParentDirectories(
        string startDirectory,
        string filesRoot,
        Action<string>? onRemoved = null)
    {
        var rootFull = Path.GetFullPath(filesRoot);
        var current = Path.GetFullPath(startDirectory);

        while (true)
        {
            if (!PathSafety.IsUnderRoot(rootFull, current))
                break;

            // Files ルート自体は削除しない
            if (PathSafety.AreSamePath(current, rootFull))
                break;

            // カテゴリルート（Files\Images 等）は削除しない
            var parent = Directory.GetParent(current);
            if (parent is null)
                break;
            if (PathSafety.AreSamePath(parent.FullName, rootFull))
                break;

            if (!Directory.Exists(current))
                break;

            if (Directory.EnumerateFileSystemEntries(current).Any())
                break;

            var relative = Path.GetRelativePath(rootFull, current).Replace('\\', '/');
            Directory.Delete(current);
            onRemoved?.Invoke(relative);

            current = parent.FullName;
        }
    }

    private static string ResolveFilesDeployPath(string filesDeploy2PrdPath, string relativePath)
    {
        var segments = relativePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return PathSafety.CombineUnderRoot(
            filesDeploy2PrdPath,
            segments,
            $"FilesDeploy2PrdPath 外への書き込みは拒否しました: {relativePath}");
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
