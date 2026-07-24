using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Xml;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

/// <summary>STG → pilot サーバーへの Web ソース配布（Issue #25）。</summary>
public class WebSourceDeployService
{
    /// <summary>
    /// robocopy の既定除外ファイルパターン。"WebSourceDeploy:ExcludeFiles" が appsettings.json に
    /// 設定されていればそちらを優先し、未設定または空配列の場合はこの既定値を使う。
    /// </summary>
    private static readonly string[] DefaultExcludeFiles = ["*.tmp", "*.log", "*.user"];

    /// <summary>
    /// robocopy の既定除外ディレクトリ名。"WebSourceDeploy:ExcludeDirs" が appsettings.json に
    /// 設定されていればそちらを優先し、未設定または空配列の場合はこの既定値を使う。
    /// "bin\obj" は "bin" 配下にネストした "obj" フォルダ（例: bin\Debug\obj ではなく bin\obj 構成）を指す。
    /// robocopy の /XD はディレクトリ名（パス階層は問わない）でマッチするため、単独の "obj" 指定と合わせて
    /// 通常の "bin" 配下 "obj" フォルダはどちらの条件でも除外される。
    /// </summary>
    private static readonly string[] DefaultExcludeDirs = [".vs", "obj", "bin\\obj"];

    private readonly bool _dryRun;
    private readonly ILogger<WebSourceDeployService> _logger;
    private readonly string[] _excludeFiles;
    private readonly string[] _excludeDirs;

    public WebSourceDeployService(IConfiguration config, ILogger<WebSourceDeployService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _logger = logger;

        var configuredExcludeFiles = config.GetSection("WebSourceDeploy:ExcludeFiles").Get<string[]>();
        var configuredExcludeDirs = config.GetSection("WebSourceDeploy:ExcludeDirs").Get<string[]>();
        _excludeFiles = configuredExcludeFiles is { Length: > 0 } ? configuredExcludeFiles : DefaultExcludeFiles;
        _excludeDirs = configuredExcludeDirs is { Length: > 0 } ? configuredExcludeDirs : DefaultExcludeDirs;
    }

    /// <summary>
    /// robocopy の終了コードが成功範囲（0〜7）かどうかを判定する。
    /// 8 以上はエラー（コピー失敗・アクセス不可等）を意味する。
    /// </summary>
    public static bool IsRobocopySuccess(int exitCode) => exitCode is >= 0 and <= 7;

    /// <summary>
    /// robocopy 実行前にコピー元・コピー先パスの安全性を検証する。
    /// WebSourcePath / PilotTarget.DestWebSourcePath は appsettings.json（信頼できる設定）由来だが、
    /// 設定ミスによる事故（空文字・相対パス・ドライブルート指定・src=dest 一致）を防ぐガードとして
    /// PathSafety を用いる。特にドライブルート（例: "C:\"）を dest に指定すると誤操作でドライブ全体に
    /// 書き込みかねないため、明示的に拒否する。
    /// </summary>
    public static void ValidateDeployPaths(string src, string dest)
    {
        if (string.IsNullOrWhiteSpace(src))
            throw new InvalidOperationException("コピー元パス（WebSourcePath）が設定されていません");
        if (string.IsNullOrWhiteSpace(dest))
            throw new InvalidOperationException("コピー先パス（PilotTarget.DestWebSourcePath）が設定されていません");
        if (!Path.IsPathRooted(src))
            throw new InvalidOperationException($"コピー元パスは絶対パスである必要があります: {src}");
        if (!Path.IsPathRooted(dest))
            throw new InvalidOperationException($"コピー先パスは絶対パスである必要があります: {dest}");

        var srcFull = Path.GetFullPath(src);
        var destFull = Path.GetFullPath(dest);

        if (PathSafety.AreSamePath(srcFull, destFull))
            throw new InvalidOperationException($"コピー元とコピー先が同一パスです: {srcFull}");

        if (IsDriveOrShareRoot(destFull))
            throw new InvalidOperationException(
                $"コピー先にドライブ/共有のルートは指定できません（誤操作によるフォルダ全消去を防止）: {destFull}");
    }

    /// <summary>dest がドライブルート（例: "C:\"）や共有ルート（例: "\\server\share"）そのものかを判定する。</summary>
    private static bool IsDriveOrShareRoot(string fullPath) =>
        Directory.GetParent(fullPath) is null;

    /// <summary>
    /// robocopy を起動し、標準出力を1行ずつ <paramref name="onOutputLine"/> へ渡す。
    /// 誤操作によるコピー先ファイルの意図しない削除を避けるため、常に /E（削除同期なしの全量コピー。
    /// 既定の比較により新規・変更ファイルのみ実際にはコピーされる）で実行する（/MIR は使用しない）。
    /// </summary>
    public async Task<int> RunRobocopyAsync(
        string src,
        string dest,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        ValidateDeployPaths(src, dest);

        var args = BuildArguments(src, dest);

        if (_dryRun)
        {
            onOutputLine($"[DRY-RUN] robocopy {args}");
            return 1; // 1 = ファイルコピーあり（成功扱い）
        }

        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // robocopy は日本語環境では OEM コードページ（Shift-JIS）で出力するため、
            // 既定の UTF-8 読み取りのままだと文字化けする（DeployService と同様の対処）。
            StandardOutputEncoding = Encoding.GetEncoding("shift_jis"),
            StandardErrorEncoding = Encoding.GetEncoding("shift_jis"),
        };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時に robocopy プロセスを残留させない（ベストエフォート）。
            TryKillProcess(proc);
            throw;
        }

        return proc.ExitCode;
    }

    private static void TryKillProcess(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // ベストエフォート。Kill 自体の失敗でキャンセル処理を止めない。
        }
    }

    private string BuildArguments(string src, string dest)
    {
        var excludeFiles = string.Join(" ", _excludeFiles.Select(f => $"\"{f}\""));
        var excludeDirs = string.Join(" ", _excludeDirs.Select(d => $"\"{d}\""));
        // /XX: コピー先にのみ存在する「余分な」ファイル・フォルダを対象外とする。
        // /MIR を使わない（削除同期をしない）運用のため、*EXTRA の検出・ログ出力自体が不要なログノイズになる。
        return $"\"{src}\" \"{dest}\" /E /MT:8 /R:2 /W:5 /NP /XX " +
               $"/XF {excludeFiles} /XD {excludeDirs}";
    }

    /// <summary>
    /// pilot側 web.config の connectionStrings/add[@name] を PilotConnectionStrings の値で置換する。
    /// コメントアウトされた &lt;add&gt;（逆システム向けの残骸）は XmlReader が要素として読み飛ばすため、
    /// name 属性照合だけで有効な要素のみが自動的にヒットする（SPEC 7.1 参照）。
    ///
    /// 実装上の注意: XDocument/XmlDocument でロード→Saveする方式は、自己終了タグの空白挿入
    /// （"/>" → " />"）や XML 宣言への encoding 属性付与など、対象外の箇所まで書式を変えてしまう
    /// （検証で確認済み）。そのため XmlReader で対象行番号と旧値のみを特定し、元テキストの該当行を
    /// 文字列置換する方式にして、connectionStrings 以外の書式を一切変えないようにしている。
    ///
    /// dryRun=true の場合はファイルを一切書き換えない（存在チェックと対象特定のみ行う）。
    /// PilotConnectionStrings に定義された name が web.config 側で1件も見つからない場合、
    /// 「置換したつもりでSTGの接続先が残る」事故を避けるため例外を送出する（未ヒットが1件でもあれば失敗）。
    /// 一部ヒット・一部未ヒットの場合はファイルへの書き込みを行わず例外を送出する（部分適用を避ける）。
    /// </summary>
    /// <returns>置換した件数。</returns>
    public static int ReplaceConnectionStrings(string webConfigPath, List<PilotConnectionString> pilotConnectionStrings, bool dryRun)
    {
        if (!File.Exists(webConfigPath))
            throw new FileNotFoundException($"web.config が見つかりません: {webConfigPath}", webConfigPath);

        if (pilotConnectionStrings.Count == 0)
            return 0;

        // Encoding.UTF8 は BOM の有無に関わらず GetPreamble() が常に BOM を返すため、
        // 元ファイルに BOM が無い場合でも書き込み時に BOM が付いてしまう。
        // 実バイト列から BOM の有無を判定し、書き込み時も同じ状態を再現する。
        var fileBytes = File.ReadAllBytes(webConfigPath);
        var hasBom = fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);
        var rawText = hasBom
            ? Encoding.UTF8.GetString(fileBytes, 3, fileBytes.Length - 3)
            : Encoding.UTF8.GetString(fileBytes);

        var lines = rawText.Split('\n');
        var unmatchedNames = new List<string>();
        var replacedCount = 0;

        foreach (var pcs in pilotConnectionStrings)
        {
            var (lineIndex, oldValue) = FindActiveConnectionStringLine(webConfigPath, pcs.Name);
            if (lineIndex < 0)
            {
                unmatchedNames.Add(pcs.Name);
                continue;
            }

            var oldAttr = $"connectionString=\"{EscapeXmlAttribute(oldValue)}\"";
            var newAttr = $"connectionString=\"{EscapeXmlAttribute(pcs.ConnectionString)}\"";

            if (!lines[lineIndex].Contains(oldAttr, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"web.config の {lineIndex + 1} 行目で connectionString 属性の位置特定に失敗しました（name={pcs.Name}）: {webConfigPath}");

            lines[lineIndex] = lines[lineIndex].Replace(oldAttr, newAttr, StringComparison.Ordinal);
            replacedCount++;
        }

        if (unmatchedNames.Count > 0)
            throw new InvalidOperationException(
                $"web.config に該当する connectionStrings/add が見つかりません（name={string.Join(", ", unmatchedNames)}）: {webConfigPath}");

        if (dryRun)
            return replacedCount; // ファイルは書き換えない

        File.WriteAllText(webConfigPath, string.Join('\n', lines), encoding);
        return replacedCount;
    }

    /// <summary>
    /// connectionStrings セクション配下（コメントアウトされていない）の add[@name=name] を探し、
    /// 見つかった要素の行番号（0-based）と現在の connectionString 値を返す。見つからなければ (-1, "")。
    /// </summary>
    private static (int LineIndex, string OldValue) FindActiveConnectionStringLine(string webConfigPath, string name)
    {
        using var reader = XmlReader.Create(webConfigPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var lineInfo = (IXmlLineInfo)reader;
        var inConnectionStrings = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "connectionStrings")
            {
                inConnectionStrings = false;
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.Name == "connectionStrings")
            {
                inConnectionStrings = true;
                continue;
            }

            if (!inConnectionStrings || reader.Name != "add") continue;

            var elementLine = lineInfo.LineNumber; // 1-based
            var addName = reader.GetAttribute("name");
            var connectionString = reader.GetAttribute("connectionString");

            if (addName == name && connectionString is not null)
                return (elementLine - 1, connectionString);
        }

        return (-1, "");
    }

    private static string EscapeXmlAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// PilotSqlDeployPath\Source を空にしてから Deploy2PrdPath の SQL ファイル一式をコピーし、
    /// 続けて deploy.bat（事前配置・本システムは作成しない）を引数なし・作業ディレクトリ
    /// PilotSqlDeployPath で実行する。deploy.bat の標準出力/標準エラーは onOutputLine へ流す。
    /// PilotSqlDeployPath が未設定の場合は何もせず null を返す（本ステップ自体をスキップ）。
    /// </summary>
    public async Task<WebSourceSqlDeployResult?> RunSqlDeployAsync(
        DbConfig config,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.PilotSqlDeployPath))
            return null;

        var sourceDir = config.PilotSqlDeploySourcePath;

        // 前回実行分の古い SQL が残らないよう、コピー前に Source を空にする。
        // PilotSqlDeployPath 配下の "Source" 固定パスのみを対象とするため、誤って上位フォルダを
        // 削除する事故は起きない。
        if (!_dryRun)
        {
            if (Directory.Exists(sourceDir))
                Directory.Delete(sourceDir, recursive: true);
            Directory.CreateDirectory(sourceDir);
        }
        else
        {
            onOutputLine($"[DRY-RUN] Source フォルダを初期化: {sourceDir}");
        }

        var copyExitCode = await RunRobocopyAsync(config.Deploy2PrdPath, sourceDir, onOutputLine, ct);
        if (!IsRobocopySuccess(copyExitCode))
            return new WebSourceSqlDeployResult(false, copyExitCode, $"SQL コピーが robocopy エラー終了しました (exit code {copyExitCode})");

        if (_dryRun)
        {
            onOutputLine($"[DRY-RUN] deploy.bat 実行: {config.PilotSqlDeployBatPath}");
            return new WebSourceSqlDeployResult(true, 0, null);
        }

        if (!File.Exists(config.PilotSqlDeployBatPath))
            return new WebSourceSqlDeployResult(false, null, $"deploy.bat が見つかりません: {config.PilotSqlDeployBatPath}");

        var batExitCode = await RunDeployBatAsync(config.PilotSqlDeployPath, config.PilotSqlDeployBatPath, onOutputLine, ct);
        if (batExitCode != 0)
            return new WebSourceSqlDeployResult(false, batExitCode, $"deploy.bat がエラー終了しました (exit code {batExitCode})");

        return new WebSourceSqlDeployResult(true, batExitCode, null);
    }

    private static async Task<int> RunDeployBatAsync(
        string workingDirectory,
        string batPath,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = batPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding("shift_jis"),
            StandardErrorEncoding = Encoding.GetEncoding("shift_jis"),
        };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(proc);
            throw;
        }

        return proc.ExitCode;
    }

    /// <summary>
    /// DbConfig.PilotTargets を pilot1 → pilot2 の順に処理し、成功した場合は続けて SQL 適用
    /// （PilotSqlDeployPath への SQL コピー＋deploy.bat 実行）を行う。
    /// あるターゲットで robocopy がエラー終了、または web.config 置換が失敗した場合、
    /// 以降のターゲット・SQL 適用ステップはスキップする。
    /// 誤操作によるファイル消失を避けるため、コピーは常に /E（削除同期なし）で行う（/MIR は使用しない）。
    /// SQL 適用の成否は Web ソースコピーとは独立した結果として返す（互いのステータスに影響しない）。
    /// <paramref name="step"/> により実行内容を絞り込める（前回失敗した側だけを再実行するため）。
    /// "web" 指定時は SQL 適用ステップ自体を行わない。"sql" 指定時は Web ソースコピーを一切行わず、
    /// 成否に関わらず（未実行でも）無条件で SQL 適用のみを実行する。
    /// </summary>
    public async Task<(List<WebSourceDeployTargetResult> Targets, WebSourceSqlDeployResult? SqlDeploy)> ExecuteAsync(
        DbConfig config,
        ChannelWriter<LogEntry> writer,
        CancellationToken ct,
        WebSourceDeployStep step = WebSourceDeployStep.Both)
    {
        var results = new List<WebSourceDeployTargetResult>();

        void LogLine(string level, string msg)
        {
            writer.TryWrite(new LogEntry
            {
                Timestamp = $"[{DateTime.Now:HH:mm:ss}]",
                Level = level,
                Message = msg,
            });
        }

        LogLine("INFO", $"Pilot環境適用を開始します（{config.Name} / {DescribeStep(step)}）");

        if (step == WebSourceDeployStep.SqlOnly)
        {
            WebSourceSqlDeployResult? onlySqlResult;
            try
            {
                onlySqlResult = await RunSqlDeployAsync(config, line => LogLine("DETAIL", line), ct);
                if (onlySqlResult is not null)
                {
                    LogLine(onlySqlResult.Success ? "OK" : "ERROR",
                        onlySqlResult.Success
                            ? "SQL適用: 完了しました"
                            : $"SQL適用: 失敗しました ({onlySqlResult.ErrorMessage})");
                }
            }
            catch (Exception ex)
            {
                onlySqlResult = new WebSourceSqlDeployResult(false, null, ex.Message);
                LogLine("ERROR", $"SQL適用: {ex.Message}");
            }

            var onlySqlFailed = onlySqlResult is { Success: false };
            LogLine(onlySqlFailed ? "ERROR" : "OK",
                onlySqlFailed ? "❌ Pilot環境適用が中断されました" : "✅ Pilot環境適用が完了しました");

            return (results, onlySqlResult);
        }

        foreach (var target in config.PilotTargets)
        {
            LogLine("STEP", $"▶ {target.Name} 適用開始");

            try
            {
                var exitCode = await RunRobocopyAsync(
                    config.WebSourcePath,
                    target.DestWebSourcePath,
                    line => LogLine("DETAIL", line),
                    ct);

                if (!IsRobocopySuccess(exitCode))
                {
                    LogLine("ERROR", $"{target.Name}: robocopy がエラー終了しました (exit code {exitCode})");
                    results.Add(new WebSourceDeployTargetResult(target.Name, false, $"robocopy exit code {exitCode}"));
                    break;
                }

                LogLine("OK", $"{target.Name}: robocopy コピー完了 (exit code {exitCode})");

                // FilesDeploy2PrdPath（本番前準備で確定した画像・静的ファイル。Images/news/pdfカテゴリを直下に持つ）が
                // 設定されていれば、その中身（Images/news/pdf等）を pilot側 Web ソースルート直下へ追加でコピーする
                // （"Files" というフォルダ名は挟まない。本番側と同じ階層構成に合わせるため）。
                // WebSourcePath 単体には本番前準備で選定済みの Files 内容が含まれないため、
                // pilot でも本番同等の画像・静的ファイルを反映するために別ステップとして実行する。
                if (!string.IsNullOrWhiteSpace(config.FilesDeploy2PrdPath))
                {
                    var filesExitCode = await RunRobocopyAsync(
                        config.FilesDeploy2PrdPath,
                        target.DestWebSourcePath,
                        line => LogLine("DETAIL", line),
                        ct);

                    if (!IsRobocopySuccess(filesExitCode))
                    {
                        LogLine("ERROR", $"{target.Name}: Files コピーが robocopy エラー終了しました (exit code {filesExitCode})");
                        results.Add(new WebSourceDeployTargetResult(target.Name, false, $"Files robocopy exit code {filesExitCode}"));
                        break;
                    }

                    LogLine("OK", $"{target.Name}: Files コピー完了 (exit code {filesExitCode})");
                }

                var webConfigPath = Path.Combine(target.DestWebSourcePath, "web.config");
                var dryRunTag = _dryRun ? " [DRY-RUN]" : "";
                var replacedCount = ReplaceConnectionStrings(webConfigPath, config.PilotConnectionStrings, _dryRun);
                LogLine("OK", $"{target.Name}: web.config の接続文字列を{replacedCount}件置換しました{dryRunTag}");

                results.Add(new WebSourceDeployTargetResult(target.Name, true, null));
            }
            catch (Exception ex)
            {
                LogLine("ERROR", $"{target.Name}: {ex.Message}");
                results.Add(new WebSourceDeployTargetResult(target.Name, false, ex.Message));
                break;
            }
        }

        var failed = results.Any(r => !r.Success);

        // Web ソースコピーが失敗（中断）している場合、または "web" 指定（Webソースコピーのみ）の場合は
        // SQL 適用ステップを実行しない（連結実行のため、コピー失敗後に適用する意味がない）。
        WebSourceSqlDeployResult? sqlDeployResult = null;
        if (!failed && step == WebSourceDeployStep.Both)
        {
            try
            {
                sqlDeployResult = await RunSqlDeployAsync(config, line => LogLine("DETAIL", line), ct);
                if (sqlDeployResult is not null)
                {
                    LogLine(sqlDeployResult.Success ? "OK" : "ERROR",
                        sqlDeployResult.Success
                            ? "SQL適用: 完了しました"
                            : $"SQL適用: 失敗しました ({sqlDeployResult.ErrorMessage})");
                }
            }
            catch (Exception ex)
            {
                sqlDeployResult = new WebSourceSqlDeployResult(false, null, ex.Message);
                LogLine("ERROR", $"SQL適用: {ex.Message}");
            }
        }

        LogLine(failed ? "ERROR" : "OK",
            failed ? "❌ Pilot環境適用が中断されました" : "✅ Pilot環境適用が完了しました");

        return (results, sqlDeployResult);
    }

    private static string DescribeStep(WebSourceDeployStep step) => step switch
    {
        WebSourceDeployStep.WebOnly => "Webソースコピーのみ",
        WebSourceDeployStep.SqlOnly => "SQL適用のみ",
        _ => "Webソースコピー＋SQL適用",
    };
}

/// <summary>Webソース配布の1ターゲット（pilot1 / pilot2）分の実行結果。</summary>
public record WebSourceDeployTargetResult(string TargetName, bool Success, string? ErrorMessage);

/// <summary>SQL適用（PilotSqlDeployPath への SQL コピー＋deploy.bat 実行）の結果。</summary>
public record WebSourceSqlDeployResult(bool Success, int? ExitCode, string? ErrorMessage);

/// <summary>「Pilot環境適用」実行時にどのステップを実行するか（前回失敗した側だけの再実行に対応するため）。</summary>
public enum WebSourceDeployStep
{
    /// <summary>Webソースコピー（pilot1→pilot2）＋SQL適用（全成功時のみ連結実行）。</summary>
    Both,
    /// <summary>Webソースコピーのみ。SQL適用ステップは行わない。</summary>
    WebOnly,
    /// <summary>SQL適用のみ。Webソースコピーは行わず、成否に関わらず無条件で実行する。</summary>
    SqlOnly,
}
