using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Xml;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

/// <summary>STG → pilot サーバーへの Web ソース配布（Issue #25）。</summary>
public class WebSourceDeployService
{
    /// <summary>robocopy の既定除外ファイルパターン（Plan Task 12 で設定可能化予定）。</summary>
    private static readonly string[] DefaultExcludeFiles = ["*.tmp", "*.log", "*.user"];

    /// <summary>robocopy の既定除外ディレクトリ名（Plan Task 12 で設定可能化予定）。</summary>
    private static readonly string[] DefaultExcludeDirs = [".vs", "obj", "bin\\obj"];

    private readonly bool _dryRun;
    private readonly ILogger<WebSourceDeployService> _logger;

    public WebSourceDeployService(IConfiguration config, ILogger<WebSourceDeployService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _logger = logger;
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
    /// PathSafety を用いる。特にドライブルート（例: "C:\"）を dest に指定すると /MIR がドライブ全体を
    /// 消しかねないため、明示的に拒否する。
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
                $"コピー先にドライブ/共有のルートは指定できません（/MIR によるフォルダ全消去を防止）: {destFull}");
    }

    /// <summary>dest がドライブルート（例: "C:\"）や共有ルート（例: "\\server\share"）そのものかを判定する。</summary>
    private static bool IsDriveOrShareRoot(string fullPath) =>
        Directory.GetParent(fullPath) is null;

    /// <summary>
    /// robocopy を起動し、標準出力を1行ずつ <paramref name="onOutputLine"/> へ渡す。
    /// mode="mirror" は /MIR（差分ミラー・削除同期あり）、mode="full" は /E（単純上書きコピー）。
    /// </summary>
    public async Task<int> RunRobocopyAsync(
        string src,
        string dest,
        string mode,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        ValidateDeployPaths(src, dest);

        var modeFlag = mode == "mirror" ? "/MIR" : "/E";
        var args = BuildArguments(src, dest, modeFlag);

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
        await proc.WaitForExitAsync(ct);

        return proc.ExitCode;
    }

    private static string BuildArguments(string src, string dest, string modeFlag)
    {
        var excludeFiles = string.Join(" ", DefaultExcludeFiles.Select(f => $"\"{f}\""));
        var excludeDirs = string.Join(" ", DefaultExcludeDirs.Select(d => $"\"{d}\""));
        return $"\"{src}\" \"{dest}\" {modeFlag} /MT:8 /R:2 /W:5 /NP " +
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
    /// </summary>
    public static void ReplaceConnectionStrings(string webConfigPath, List<PilotConnectionString> pilotConnectionStrings)
    {
        if (!File.Exists(webConfigPath))
            throw new FileNotFoundException($"web.config が見つかりません: {webConfigPath}", webConfigPath);

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

        foreach (var pcs in pilotConnectionStrings)
        {
            var (lineIndex, oldValue) = FindActiveConnectionStringLine(webConfigPath, pcs.Name);
            if (lineIndex < 0) continue; // 未定義の name は変更しない（STG の値を維持）

            var oldAttr = $"connectionString=\"{EscapeXmlAttribute(oldValue)}\"";
            var newAttr = $"connectionString=\"{EscapeXmlAttribute(pcs.ConnectionString)}\"";

            if (!lines[lineIndex].Contains(oldAttr, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"web.config の {lineIndex + 1} 行目で connectionString 属性の位置特定に失敗しました（name={pcs.Name}）: {webConfigPath}");

            lines[lineIndex] = lines[lineIndex].Replace(oldAttr, newAttr, StringComparison.Ordinal);
        }

        File.WriteAllText(webConfigPath, string.Join('\n', lines), encoding);
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
    /// DbConfig.PilotTargets を pilot1 → pilot2 の順に処理する。
    /// あるターゲットで robocopy がエラー終了、または web.config 置換が失敗した場合、
    /// 以降のターゲットはスキップする。
    /// </summary>
    public async Task<List<WebSourceDeployTargetResult>> ExecuteAsync(
        DbConfig config,
        string mode,
        ChannelWriter<LogEntry> writer,
        CancellationToken ct)
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

        LogLine("INFO", $"Webソース配布を開始します（{config.Name} / mode={mode}）");

        foreach (var target in config.PilotTargets)
        {
            LogLine("STEP", $"▶ {target.Name} 適用開始");

            try
            {
                var exitCode = await RunRobocopyAsync(
                    config.WebSourcePath,
                    target.DestWebSourcePath,
                    mode,
                    line => LogLine("DETAIL", line),
                    ct);

                if (!IsRobocopySuccess(exitCode))
                {
                    LogLine("ERROR", $"{target.Name}: robocopy がエラー終了しました (exit code {exitCode})");
                    results.Add(new WebSourceDeployTargetResult(target.Name, false, $"robocopy exit code {exitCode}"));
                    break;
                }

                LogLine("OK", $"{target.Name}: robocopy コピー完了 (exit code {exitCode})");

                var webConfigPath = Path.Combine(target.DestWebSourcePath, "web.config");
                ReplaceConnectionStrings(webConfigPath, config.PilotConnectionStrings);
                LogLine("OK", $"{target.Name}: web.config の接続文字列を置換しました");

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
        LogLine(failed ? "ERROR" : "OK",
            failed ? "❌ Webソース配布が中断されました" : "✅ Webソース配布が完了しました");

        return results;
    }
}

/// <summary>Webソース配布の1ターゲット（pilot1 / pilot2）分の実行結果。</summary>
public record WebSourceDeployTargetResult(string TargetName, bool Success, string? ErrorMessage);
