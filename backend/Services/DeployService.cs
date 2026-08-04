using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class DeployService
{
    private readonly bool _dryRun;
    private readonly ILogger<DeployService> _logger;
    private readonly ManualApplyService _manualApply;

    private static readonly HashSet<string> GitOnlyTypes =
        new(ManualApplyService.ManualApplyTypes, StringComparer.OrdinalIgnoreCase);

    /// <summary>MariaDB 系のモジュール種別（ストアド・Table）。Step1 の出力先振り分けに使用する。</summary>
    private static readonly HashSet<string> MariaDbTypes =
        new(["Stored", "MariaDbTable"], StringComparer.OrdinalIgnoreCase);

    // DB ごとの実行中フラグ（重複リクエスト防止）
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _dbLocks = new();

    public DeployService(IConfiguration config, ManualApplyService manualApply, ILogger<DeployService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _manualApply = manualApply;
        _logger = logger;
    }

    public ChannelReader<LogEntry> ExecuteAsync(DbConfig dbConfig, DeployRequest request, string executedBy, CancellationToken ct)
    {
        var semaphore = _dbLocks.GetOrAdd(dbConfig.Name, _ => new SemaphoreSlim(1, 1));
        var channel = Channel.CreateUnbounded<LogEntry>();

        _ = Task.Run(async () =>
        {
            // 同一 DB の並列実行を拒否（React StrictMode の 2重リクエスト等を防止）
            if (!semaphore.Wait(0))
            {
                await Log(channel.Writer, "WARN", "このDBは既にデプロイ実行中です。重複リクエストをスキップします。");
                channel.Writer.Complete();
                return;
            }
            try
            {
                await RunPipelineAsync(channel.Writer, dbConfig, request, executedBy, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }, ct);

        return channel.Reader;
    }

    private async Task RunPipelineAsync(
        ChannelWriter<LogEntry> writer,
        DbConfig dbConfig,
        DeployRequest request,
        string executedBy,
        CancellationToken ct)
    {
        string dryRunTag = _dryRun ? " [DRY-RUN]" : "";
        var deployModules = request.Modules.Where(m => !GitOnlyTypes.Contains(m.Type)).ToList();
        var gitOnlyModules = request.Modules.Where(m => GitOnlyTypes.Contains(m.Type)).ToList();
        var sqlServerModules = request.Modules.Where(m => !MariaDbTypes.Contains(m.Type)).ToList();
        var mariaDbModules = request.Modules.Where(m => MariaDbTypes.Contains(m.Type)).ToList();
        var sqlServerDeployModules = deployModules.Where(m => !MariaDbTypes.Contains(m.Type)).ToList();
        var mariaDbDeployModules = deployModules.Where(m => MariaDbTypes.Contains(m.Type)).ToList();

        try
        {
            await Log(writer, "INFO", $"セッション開始{dryRunTag}  db={dbConfig.Name}  user={executedBy}");

            // Step 1: Generate module txt files（SQL Server / MariaDB で出力先を分ける）
            await Log(writer, "STEP", "1/6 UpdateModule.txt / DeleteModule.txt を生成 (SJIS/CP932)", "generate");
            await Step1_GenerateModuleFiles(writer, dbConfig.MergePath, sqlServerModules, dryRunTag);
            if (mariaDbModules.Count > 0)
                await Step1_GenerateModuleFiles(writer, dbConfig.MariaDbMergePath, mariaDbModules, dryRunTag);
            await Log(writer, "OK", "生成完了", stepDone: "generate");

            // Step 2: git Live Updates
            await Log(writer, "STEP", "2/6 git_Live Updates.bat 実行", "git-update");
            await Step2_GitLiveUpdates(writer, dbConfig, dryRunTag, ct);
            await Log(writer, "OK", "Live Updates 完了", stepDone: "git-update");

            // Step 3: git merge（SQL Server / MariaDB それぞれの merge フォルダで実行）
            await Log(writer, "STEP", "3/6 git_merge.bat 実行", "merge");
            if (gitOnlyModules.Count > 0)
                await Log(writer, "INFO", $"Git マージのみ対象: {string.Join(", ", gitOnlyModules.Select(m => m.Name))}");
            await Step3_GitMerge(writer, dbConfig.MergePath, dryRunTag, ct);
            if (mariaDbModules.Count > 0)
                await Step3_GitMerge(writer, dbConfig.MariaDbMergePath, dryRunTag, ct);
            if (gitOnlyModules.Count > 0)
                await Step3b_RegisterManualApply(writer, dbConfig, gitOnlyModules, executedBy, dryRunTag);
            await Log(writer, "OK", $"merge 完了  ({request.Modules.Count} files changed)", stepDone: "merge");

            // Step 4: SQL convert（SQL Server / MariaDB で変換ロジックを分ける）
            await Log(writer, "STEP", "4/6 SQL ファイルをコピー・変換", "sql-convert");
            if (deployModules.Count == 0)
            {
                await Log(writer, "INFO", "SQL 変換対象なし（全モジュールが Git マージのみ）");
            }
            else
            {
                if (sqlServerDeployModules.Count > 0)
                    await Step4_SqlConvert(writer, dbConfig, sqlServerDeployModules, dryRunTag);
                if (mariaDbDeployModules.Count > 0)
                    await Step4_SqlConvertMariaDb(writer, dbConfig, mariaDbDeployModules, dryRunTag);
            }
            await Log(writer, "OK", "SQL 変換完了", stepDone: "sql-convert");

            // Step 5: deploy.bat（SQL Server / MariaDB で実行方式が異なる）
            await Log(writer, "STEP", "5/6 deploy.bat 実行中…", "deploy");
            await Step5_Deploy(writer, dbConfig, sqlServerDeployModules, dryRunTag, ct);
            Dictionary<string, bool>? mariaDbDeployResults = null;
            if (mariaDbDeployModules.Count > 0)
                mariaDbDeployResults = await Step5_DeployMariaDb(writer, dbConfig, mariaDbDeployModules, dryRunTag, ct);

            // Step 6: move to deployed/
            await Log(writer, "STEP", "6/6 適用済みファイルを deployed/ へ移動", "record");
            await Step6_MoveToDeployed(writer, dbConfig, sqlServerDeployModules, dryRunTag);
            if (mariaDbDeployModules.Count > 0)
                await Step6_MoveToDeployedMariaDb(writer, dbConfig, mariaDbDeployModules, mariaDbDeployResults!, dryRunTag);
            await Log(writer, "OK", "移動完了");
            await Log(writer, "INFO", "実行結果を DB に記録中");
            await Log(writer, "OK", "✅ STG 適用が完了しました", stepDone: "record");
        }
        catch (OperationCanceledException)
        {
            await Log(writer, "WARN", "実行が中断されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deploy failed for db={DbName}", dbConfig.Name);
            await Log(writer, "ERROR", $"エラーが発生しました: {ex.Message}");
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task Step1_GenerateModuleFiles(ChannelWriter<LogEntry> w, string mergePath, List<DeployModule> modules, string tag)
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var updateModules = modules.Where(m => m.OpType != "削除").ToList();
        var deleteModules = modules.Where(m => m.OpType == "削除").ToList();

        var updatePath = Path.Combine(mergePath, "UpdateModule.txt");
        var deletePath = Path.Combine(mergePath, "DeleteModule.txt");

        await Log(w, "DETAIL", $"→ {updatePath}  ({updateModules.Count} modules){tag}");
        if (deleteModules.Count > 0)
            await Log(w, "DETAIL", $"→ {deletePath}  ({deleteModules.Count} modules){tag}");

        if (!_dryRun)
        {
            Directory.CreateDirectory(mergePath);
            await File.WriteAllTextAsync(updatePath, string.Join("\r\n", updateModules.Select(m => $"{m.Type},{m.Name}")), sjis);
            await File.WriteAllTextAsync(deletePath, string.Join("\r\n", deleteModules.Select(m => $"{m.Type},{m.Name}")), sjis);
        }
    }

    private async Task Step2_GitLiveUpdates(ChannelWriter<LogEntry> w, DbConfig config, string tag, CancellationToken ct)
    {
        var batPath = Path.Combine(config.SourceControlPath, "git_Live Updates.bat");
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(300, ct);
            await Log(w, "DETAIL", "[DRY-RUN] Already up to date. (simulated)");
            return;
        }
        await RunBatAsync(w, batPath, config.SourceControlPath, ct);
    }

    private async Task Step3_GitMerge(ChannelWriter<LogEntry> w, string mergePath, string tag, CancellationToken ct)
    {
        var batPath = Path.Combine(mergePath, "git_merge.bat");
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(400, ct);
            await Log(w, "DETAIL", "[DRY-RUN] Merge simulated (no actual git operation)");
            return;
        }
        await RunBatAsync(w, batPath, mergePath, ct);
    }

    /// <summary>
    /// Git マージのみのモジュール（Table / UDTT）を手動適用待ちとして登録する。
    /// 自動デプロイはしないが、本番前準備画面に確認対象として残すことで適用漏れを防ぐ。
    /// </summary>
    private async Task Step3b_RegisterManualApply(
        ChannelWriter<LogEntry> w,
        DbConfig config,
        List<DeployModule> gitOnlyModules,
        string executedBy,
        string tag)
    {
        var logs = new List<(string Level, string Message)>();
        var items = _manualApply.Register(
            config, gitOnlyModules, executedBy, (level, message) => logs.Add((level, message)));

        foreach (var (level, message) in logs)
            await Log(w, level, message);

        await Log(w, "INFO", $"手動適用待ちに登録: {items.Count} 件（本番前準備画面で確認）{tag}");
        foreach (var item in items)
            await Log(w, "DETAIL", $"→ {item.ModuleType}/{item.ModuleName}  [{item.OpType}] deployed_manual/{tag}");
    }

    private async Task Step4_SqlConvert(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> modules, string tag)
    {
        Directory.CreateDirectory(config.DeploySourcePath);
        foreach (var m in modules)
        {
            var srcPath = Path.Combine(config.GitRepoPath, m.Type, $"dbo.{m.Name}.sql");
            var destPath = Path.Combine(config.DeploySourcePath, $"dbo.{m.Name}.sql");

            if (m.OpType == "新規")
            {
                await Log(w, "DETAIL", $"→ dbo.{m.Name}.sql  [新規] ALTER→CREATE 置換{tag}");
                if (!_dryRun) await ConvertAlterToCreate(srcPath, destPath);
            }
            else if (m.OpType == "削除")
            {
                await Log(w, "DETAIL", $"→ dbo.{m.Name}.sql  [削除] DROP 文を生成{tag}");
                if (!_dryRun) await GenerateDropSql(m, destPath);
            }
            else
            {
                await Log(w, "DETAIL", $"→ dbo.{m.Name}.sql  [更新] copy{tag}");
                if (!_dryRun) File.Copy(srcPath, destPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// MariaDB ストアドプロシージャの SQL 変換。Git 上のファイルは既に
    /// DROP IF EXISTS + CREATE の完成形（Export.py 生成）のため、新規/更新はそのままコピーする。
    /// 削除のみ DROP 単独 SQL を生成する（SJIS ではなく UTF-8。Export.py 出力に合わせる）。
    /// </summary>
    private async Task Step4_SqlConvertMariaDb(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> modules, string tag)
    {
        Directory.CreateDirectory(config.MariaDbDeploySourcePath);
        foreach (var m in modules)
        {
            var srcPath = Path.Combine(config.MariaDbGitRepoPath, "Stored", $"{m.Name}.sql");
            var destPath = Path.Combine(config.MariaDbDeploySourcePath, $"{m.Name}.sql");

            if (m.OpType == "削除")
            {
                await Log(w, "DETAIL", $"→ {m.Name}.sql  [削除] DROP 文を生成{tag}");
                if (!_dryRun) await GenerateMariaDbDropSql(m, destPath);
            }
            else
            {
                await Log(w, "DETAIL", $"→ {m.Name}.sql  [{m.OpType}] copy{tag}");
                if (!_dryRun) File.Copy(srcPath, destPath, overwrite: true);
            }
        }
    }

    private static async Task GenerateMariaDbDropSql(DeployModule m, string destPath)
    {
        var sql = $"DROP PROCEDURE IF EXISTS `{m.Name}`;";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await File.WriteAllTextAsync(destPath, sql + "\r\n", Encoding.UTF8);
    }

    private async Task Step5_Deploy(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> deployModules, string tag, CancellationToken ct)
    {
        if (deployModules.Count == 0)
        {
            await Log(w, "INFO", $"deploy.bat スキップ（SQL 変換対象なし）{tag}", stepDone: "deploy");
            return;
        }

        var batPath = Path.Combine(config.ForNewCreationPath, "deploy.bat");
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(600, ct);
            await Log(w, "INFO", $"[DRY-RUN] deploy.bat スキップ (exit code 0 simulated)", stepDone: "deploy");
            return;
        }
        await RunBatAsync(w, batPath, config.ForNewCreationPath, ct);
        await Log(w, "OK", "deploy.bat 完了 (exit code 0)", stepDone: "deploy");
    }

    /// <summary>
    /// MariaDB 用 deploy.bat を実行する。MariaDB の DDL はトランザクション非対応のため
    /// DB レベルの自動ロールバックはできない（SPEC.md Assumption 5）。deploy.bat は1ファイルずつ
    /// mysql CLI を実行し、"RESULT:OK:xxx.sql" / "RESULT:FAIL:xxx.sql" 形式で成否を標準出力する
    /// 前提で、その行を解析してモジュール名→成否のマップを返す。
    /// </summary>
    private async Task<Dictionary<string, bool>> Step5_DeployMariaDb(
        ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> mariaDbDeployModules, string tag, CancellationToken ct)
    {
        var batPath = config.MariaDbDeployBatPath;
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(600, ct);
            await Log(w, "INFO", "[DRY-RUN] MariaDB deploy.bat スキップ (exit code 0 simulated)");
            return mariaDbDeployModules.ToDictionary(m => m.Name, _ => true, StringComparer.OrdinalIgnoreCase);
        }

        var lines = await RunBatAsync(w, batPath, config.MariaDbForNewCreationPath, ct);
        var results = ParseMariaDbDeployResults(lines, mariaDbDeployModules);

        var failed = results.Where(r => !r.Value).Select(r => r.Key).ToList();
        if (failed.Count > 0)
            await Log(w, "WARN", $"MariaDB 適用失敗: {string.Join(", ", failed)}{tag}");
        await Log(w, "OK", $"MariaDB deploy.bat 完了（成功 {results.Count(r => r.Value)}/{results.Count}）");

        return results;
    }

    /// <summary>
    /// "RESULT:OK:xxx.sql" / "RESULT:FAIL:xxx.sql" 形式の行を解析する。
    /// マーカーが出力されなかったファイル（bat のクラッシュ等）はフェイルセーフとして失敗扱いにする。
    /// </summary>
    internal static Dictionary<string, bool> ParseMariaDbDeployResults(List<string> lines, List<DeployModule> modules)
    {
        var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var parts = line.Split(':', 3);
            if (parts.Length < 3 || !string.Equals(parts[0], "RESULT", StringComparison.OrdinalIgnoreCase))
                continue;

            var moduleName = Path.GetFileNameWithoutExtension(parts[2].Trim());
            results[moduleName] = string.Equals(parts[1], "OK", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var m in modules)
            results.TryAdd(m.Name, false);

        return results;
    }

    private async Task Step6_MoveToDeployedMariaDb(
        ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> mariaDbDeployModules,
        Dictionary<string, bool> results, string tag)
    {
        if (!_dryRun) Directory.CreateDirectory(config.MariaDbDeployedPath);
        foreach (var m in mariaDbDeployModules)
        {
            var success = results.TryGetValue(m.Name, out var ok) && ok;
            if (!success)
            {
                await Log(w, "WARN", $"適用失敗のため deployed へ移動しません: {m.Name}.sql{tag}");
                continue;
            }

            var src = Path.Combine(config.MariaDbDeploySourcePath, $"{m.Name}.sql");
            var dest = Path.Combine(config.MariaDbDeployedPath, $"{m.Name}.sql");
            await Log(w, "DETAIL", $"→ {m.Name}.sql → MariaDB/deployed/{tag}");

            if (!_dryRun) File.Move(src, dest, overwrite: true);
        }
    }

    private async Task Step6_MoveToDeployed(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> deployModules, string tag)
    {
        if (deployModules.Count == 0)
        {
            await Log(w, "INFO", $"移動対象なし{tag}");
            return;
        }

        var sourceDir = config.DeploySourcePath;
        var deployedDir = config.DeployedPath;

        if (!_dryRun) Directory.CreateDirectory(deployedDir);
        foreach (var m in deployModules)
        {
            var src = Path.Combine(sourceDir, $"dbo.{m.Name}.sql");
            var dest = Path.Combine(deployedDir, $"dbo.{m.Name}.sql");
            await Log(w, "DETAIL", $"→ dbo.{m.Name}.sql → deployed/{tag}");

            if (!_dryRun) File.Move(src, dest, overwrite: true);
        }
    }

    private static async Task ConvertAlterToCreate(string srcPath, string destPath)
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var content = await File.ReadAllTextAsync(srcPath, sjis);
        content = content.Replace("ALTER PROCEDURE", "CREATE OR ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase);
        content = content.Replace("ALTER FUNCTION", "CREATE OR ALTER FUNCTION", StringComparison.OrdinalIgnoreCase);
        await File.WriteAllTextAsync(destPath, content, sjis);
    }

    private static async Task GenerateDropSql(DeployModule m, string destPath)
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var sql = m.Type switch
        {
            "StoredProcedure" => $"DROP PROCEDURE IF EXISTS [dbo].[{m.Name}]",
            "Function" => $"DROP FUNCTION IF EXISTS [dbo].[{m.Name}]",
            "VIEW" => $"DROP VIEW IF EXISTS [dbo].[{m.Name}]",
            _ => $"DROP OBJECT [dbo].[{m.Name}]"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await File.WriteAllTextAsync(destPath, sql + "\r\nGO\r\n", sjis);
    }

    /// <summary>
    /// bat を実行し、標準出力の行を（ログ転送と同時に）呼び出し元へ返す。
    /// SQL Server 用の呼び出し（Step2/Step3/Step5）は戻り値を使わないため挙動は変わらないが、
    /// MariaDB 用 deploy.bat（Step5）は "RESULT:OK:xxx.sql" 形式の行をここから受け取り成否判定に使う。
    /// </summary>
    private async Task<List<string>> RunBatAsync(ChannelWriter<LogEntry> w, string batPath, string workingDir, CancellationToken ct)
    {
        var stdoutLines = new List<string>();

        if (!File.Exists(batPath))
        {
            await Log(w, "WARN", $"bat ファイルが見つかりません: {batPath}");
            return stdoutLines;
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            // chcp 932 を先行して設定し、bat およびその子プロセス（PowerShell 等）が SJIS で動作するようにする
            Arguments = $"/c \"chcp 932 > nul && \"{batPath}\"\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding("shift_jis"),
            StandardErrorEncoding = Encoding.GetEncoding("shift_jis"),
        };

        proc.Start();
        var stdoutTask = ReadOutputAsync(proc.StandardOutput, w, "DETAIL", ct, stdoutLines);
        var stderrTask = ReadOutputAsync(proc.StandardError, w, "WARN", ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new Exception($"bat 終了コード: {proc.ExitCode}");

        return stdoutLines;
    }

    private static async Task ReadOutputAsync(StreamReader reader, ChannelWriter<LogEntry> w, string level, CancellationToken ct, List<string>? capture = null)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (!string.IsNullOrWhiteSpace(line))
            {
                capture?.Add(line);
                await Log(w, level, line);
            }
        }
    }

    private static async Task Log(ChannelWriter<LogEntry> w, string level, string message, string? step = null, string? stepDone = null)
    {
        var entry = new LogEntry
        {
            Timestamp = $"[{DateTime.Now:HH:mm:ss}]",
            Level = level,
            Message = message,
            Step = stepDone ?? step,
        };
        await w.WriteAsync(entry);
    }
}
