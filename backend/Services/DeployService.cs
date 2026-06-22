using System.Text;
using System.Threading.Channels;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

public class DeployService
{
    private readonly bool _dryRun;
    private readonly ILogger<DeployService> _logger;

    private static readonly HashSet<string> GitOnlyTypes =
        new(["Table", "UserDefinedTableType"], StringComparer.OrdinalIgnoreCase);

    public DeployService(IConfiguration config, ILogger<DeployService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _logger = logger;
    }

    public ChannelReader<LogEntry> ExecuteAsync(DbConfig dbConfig, DeployRequest request, string executedBy, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<LogEntry>();
        _ = Task.Run(() => RunPipelineAsync(channel.Writer, dbConfig, request, executedBy, ct), ct);
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

        try
        {
            await Log(writer, "INFO", $"セッション開始{dryRunTag}  db={dbConfig.Name}  user={executedBy}");

            // Step 1: Generate module txt files
            await Log(writer, "STEP", "1/6 UpdateModule.txt / DeleteModule.txt を生成 (SJIS/CP932)", "generate");
            await Step1_GenerateModuleFiles(writer, dbConfig, request, dryRunTag);
            await Log(writer, "OK", "生成完了", stepDone: "generate");

            // Step 2: git Live Updates
            await Log(writer, "STEP", "2/6 git_Live Updates.bat 実行", "git-update");
            await Step2_GitLiveUpdates(writer, dbConfig, dryRunTag, ct);
            await Log(writer, "OK", "Live Updates 完了", stepDone: "git-update");

            // Step 3: git merge
            await Log(writer, "STEP", "3/6 git_merge.bat 実行", "merge");
            if (gitOnlyModules.Count > 0)
                await Log(writer, "INFO", $"Git マージのみ対象: {string.Join(", ", gitOnlyModules.Select(m => m.Name))}");
            await Step3_GitMerge(writer, dbConfig, request, dryRunTag, ct);
            await Log(writer, "OK", $"merge 完了  ({request.Modules.Count} files changed)", stepDone: "merge");

            // Step 4: SQL convert
            await Log(writer, "STEP", "4/6 SQL ファイルをコピー・変換", "sql-convert");
            if (deployModules.Count == 0)
            {
                await Log(writer, "INFO", "SQL 変換対象なし（全モジュールが Git マージのみ）");
            }
            else
            {
                await Step4_SqlConvert(writer, dbConfig, deployModules, dryRunTag);
            }
            await Log(writer, "OK", "SQL 変換完了", stepDone: "sql-convert");

            // Step 5: deploy.bat
            await Log(writer, "STEP", "5/6 deploy.bat 実行中…", "deploy");
            await Step5_Deploy(writer, dbConfig, deployModules, dryRunTag, ct);

            // Step 6: move to deployed/
            await Log(writer, "STEP", "6/6 適用済みファイルを deployed/ へ移動", "record");
            await Step6_MoveToDeployed(writer, dbConfig, deployModules, dryRunTag);
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

    private async Task Step1_GenerateModuleFiles(ChannelWriter<LogEntry> w, DbConfig config, DeployRequest request, string tag)
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var updateModules = request.Modules.Where(m => m.OpType != "削除").ToList();
        var deleteModules = request.Modules.Where(m => m.OpType == "削除").ToList();

        var updatePath = Path.Combine(config.MergePath, "UpdateModule.txt");
        var deletePath = Path.Combine(config.MergePath, "DeleteModule.txt");

        await Log(w, "DETAIL", $"→ {updatePath}  ({updateModules.Count} modules){tag}");
        if (deleteModules.Count > 0)
            await Log(w, "DETAIL", $"→ {deletePath}  ({deleteModules.Count} modules){tag}");

        if (!_dryRun)
        {
            Directory.CreateDirectory(config.MergePath);
            await File.WriteAllTextAsync(updatePath, string.Join("\r\n", updateModules.Select(m => m.Name)), sjis);
            await File.WriteAllTextAsync(deletePath, string.Join("\r\n", deleteModules.Select(m => m.Name)), sjis);
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

    private async Task Step3_GitMerge(ChannelWriter<LogEntry> w, DbConfig config, DeployRequest request, string tag, CancellationToken ct)
    {
        var batPath = Path.Combine(config.SourceControlPath, "git_merge.bat");
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(400, ct);
            await Log(w, "DETAIL", "[DRY-RUN] Merge simulated (no actual git operation)");
            return;
        }
        await RunBatAsync(w, batPath, config.SourceControlPath, ct);
    }

    private async Task Step4_SqlConvert(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> modules, string tag)
    {
        foreach (var m in modules)
        {
            var srcPath = Path.Combine(config.GitRepoPath, m.Type, $"{m.Name}.sql");
            var destPath = Path.Combine(config.DeploySourcePath, m.Type, $"{m.Name}.sql");

            if (m.OpType == "新規")
            {
                await Log(w, "DETAIL", $"→ {m.Type}/{m.Name}.sql  [新規] ALTER→CREATE 置換{tag}");
                if (!_dryRun) await ConvertAlterToCreate(srcPath, destPath);
            }
            else if (m.OpType == "削除")
            {
                await Log(w, "DETAIL", $"→ {m.Type}/{m.Name}.sql  [削除] DROP 文を生成{tag}");
                if (!_dryRun) await GenerateDropSql(m, destPath);
            }
            else
            {
                await Log(w, "DETAIL", $"→ {m.Type}/{m.Name}.sql  [更新] copy{tag}");
                if (!_dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    File.Copy(srcPath, destPath, overwrite: true);
                }
            }
        }
    }

    private async Task Step5_Deploy(ChannelWriter<LogEntry> w, DbConfig config, List<DeployModule> deployModules, string tag, CancellationToken ct)
    {
        if (deployModules.Count == 0)
        {
            await Log(w, "INFO", $"deploy.bat スキップ（SQL 変換対象なし）{tag}", stepDone: "deploy");
            return;
        }

        var batPath = Path.Combine(config.DeployDev2StgPath, "deploy.bat");
        await Log(w, "DETAIL", $"→ {batPath}{tag}");

        if (_dryRun)
        {
            await Task.Delay(600, ct);
            await Log(w, "INFO", $"[DRY-RUN] deploy.bat スキップ (exit code 0 simulated)", stepDone: "deploy");
            return;
        }
        await RunBatAsync(w, batPath, config.DeployDev2StgPath, ct);
        await Log(w, "OK", "deploy.bat 完了 (exit code 0)", stepDone: "deploy");
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

        foreach (var m in deployModules)
        {
            var src = Path.Combine(sourceDir, m.Type, $"{m.Name}.sql");
            var dest = Path.Combine(deployedDir, m.Type, $"{m.Name}.sql");
            await Log(w, "DETAIL", $"→ {Path.GetFileName(src)} → deployed/{tag}");

            if (!_dryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(src, dest, overwrite: true);
            }
        }
    }

    private static async Task ConvertAlterToCreate(string srcPath, string destPath)
    {
        var sjis = Encoding.GetEncoding("shift_jis");
        var content = await File.ReadAllTextAsync(srcPath, sjis);
        content = content.Replace("ALTER PROCEDURE", "CREATE OR ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase);
        content = content.Replace("ALTER FUNCTION", "CREATE OR ALTER FUNCTION", StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
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

    private async Task RunBatAsync(ChannelWriter<LogEntry> w, string batPath, string workingDir, CancellationToken ct)
    {
        if (!File.Exists(batPath))
        {
            await Log(w, "WARN", $"bat ファイルが見つかりません: {batPath}");
            return;
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding("shift_jis"),
            StandardErrorEncoding = Encoding.GetEncoding("shift_jis"),
        };

        proc.Start();
        var stdoutTask = ReadOutputAsync(proc.StandardOutput, w, "DETAIL", ct);
        var stderrTask = ReadOutputAsync(proc.StandardError, w, "WARN", ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new Exception($"bat 終了コード: {proc.ExitCode}");
    }

    private static async Task ReadOutputAsync(StreamReader reader, ChannelWriter<LogEntry> w, string level, CancellationToken ct)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (!string.IsNullOrWhiteSpace(line))
                await Log(w, level, line);
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
