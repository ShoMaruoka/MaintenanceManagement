using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
    /// 設定ミスによる事故（空文字・相対パス・ローカルドライブルート指定・src=dest 一致）を防ぐガードとして
    /// PathSafety を用いる。特にドライブルート（例: "C:\"）を dest に指定すると誤操作でドライブ全体に
    /// 書き込みかねないため、明示的に拒否する。
    /// 一方、UNC 共有ルート（例: "\\server\WWW_KAIOS_pilot"）は共有そのものが IIS Web ルートである
    /// 運用があり得るため許可する。
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

        if (IsLocalDriveRoot(destFull))
            throw new InvalidOperationException(
                $"コピー先にローカルドライブのルートは指定できません（誤操作によるドライブ全体への書き込みを防止）: {destFull}");
    }

    /// <summary>
    /// dest がローカルドライブルート（例: "C:\"）かどうかを判定する。
    /// UNC 共有ルート（例: "\\server\share"）は Web ルートとして正当なコピー先になり得るため false。
    /// </summary>
    private static bool IsLocalDriveRoot(string fullPath)
    {
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            || fullPath.StartsWith("//", StringComparison.Ordinal))
            return false;

        return Directory.GetParent(fullPath) is null;
    }

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
        // /MT:32: 共通画像など小サイズ・大量ファイルの UNC コピー向け（robocopy の上限は 128。Issue #27）。
        return $"\"{src}\" \"{dest}\" /E /MT:32 /R:2 /W:5 /NP /XX " +
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

        // View ソース内の DB 名置換（Issue #27）。実書き込みはコピー先 Source のみ。
        // DryRun 時は robocopy が実コピーしないため、プレビューは Deploy2PrdPath を走査する
        // （dryRun=true なので Deploy2PrdPath 自体は書き換えない）。
        if (config.PilotSqlDbNameReplacements.Count > 0)
        {
            var replaceDir = _dryRun ? config.Deploy2PrdPath : sourceDir;
            onOutputLine($"View DB名置換: 走査対象={replaceDir}{(_dryRun ? "（DryRunプレビュー）" : "")}");
            var (fileCount, occurrenceCount, skippedCount) = ReplaceViewDbNames(
                replaceDir, config.PilotSqlDbNameReplacements, _dryRun, onOutputLine);
            var dryRunTag = _dryRun ? " [DRY-RUN]" : "";
            onOutputLine($"View DB名置換: {fileCount} ファイル / {occurrenceCount} 箇所 / スキップ {skippedCount} 件{dryRunTag}");
            if (skippedCount > 0)
                onOutputLine($"WARN: View DB名置換で {skippedCount} 件スキップしました（エンコーディング判定不可）。該当 View は KaiosDB 参照のまま残る可能性があります");
        }
        else
        {
            onOutputLine("View DB名置換: スキップ（PilotSqlDbNameReplacements 未設定）");
        }

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

    /// <summary>
    /// CREATE/ALTER VIEW を含む .sql のみを対象に、DB 名参照を置換する（Issue #27）。
    /// 単語境界付き・大文字小文字無視で From を To に置換し、KaiosDB_pilot / KaiosDB2 等は対象外。
    /// エンコーディングは BOM（UTF-8 / UTF-16）または BOM なし時のラウンドトリップ検証
    /// （Shift-JIS → UTF-8）で判定する。判定できないファイルは置換せず警告ログを出す。
    /// </summary>
    /// <returns>置換したファイル数・箇所数・エンコーディング判定不可でスキップした件数。</returns>
    public static (int FileCount, int OccurrenceCount, int SkippedCount) ReplaceViewDbNames(
        string sourceDir,
        List<PilotDbNameReplacement> rules,
        bool dryRun,
        Action<string>? onOutputLine = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || rules.Count == 0)
            return (0, 0, 0);

        if (!Directory.Exists(sourceDir))
        {
            onOutputLine?.Invoke($"WARN: View DB名置換の走査先ディレクトリが存在しません: {sourceDir}");
            return (0, 0, 0);
        }

        var activeRules = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
            .ToList();
        if (activeRules.Count == 0)
            return (0, 0, 0);

        // ルールは順次適用する。あるルールの To が後続ルールの From にマッチすると二重置換し得る。
        // 現状の運用設定は1ルールのみ。複数ルールを追加する場合は選択的な単一パス置換への変更を検討すること。
        var fileCount = 0;
        var occurrenceCount = 0;
        var skippedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*.sql", SearchOption.AllDirectories))
        {
            if (!TryReadSqlFilePreservingEncoding(filePath, out var encoding, out var text, out var skipReason))
            {
                skippedCount++;
                onOutputLine?.Invoke($"WARN: View DB名置換をスキップ（エンコーディング判定不可）: {Path.GetFileName(filePath)} ({skipReason})");
                continue;
            }

            if (!IsViewDefinition(text))
                continue;

            var replaced = text;
            var fileOccurrences = 0;
            foreach (var rule in activeRules)
            {
                var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(rule.From)}(?![A-Za-z0-9_])";
                var matches = Regex.Matches(replaced, pattern, RegexOptions.IgnoreCase);
                if (matches.Count == 0)
                    continue;
                fileOccurrences += matches.Count;
                // MatchEvaluator を使い、rule.To 内の `$` が置換パターン（$1/$&/$$）として解釈されないようにする。
                replaced = Regex.Replace(replaced, pattern, _ => rule.To, RegexOptions.IgnoreCase);
            }

            if (fileOccurrences == 0)
                continue;

            fileCount++;
            occurrenceCount += fileOccurrences;
            onOutputLine?.Invoke(
                dryRun
                    ? $"[DRY-RUN] View DB名置換予定: {Path.GetFileName(filePath)} ({fileOccurrences} 箇所)"
                    : $"View DB名置換: {Path.GetFileName(filePath)} ({fileOccurrences} 箇所)");

            if (!dryRun)
                File.WriteAllText(filePath, replaced, encoding);
        }

        return (fileCount, occurrenceCount, skippedCount);
    }

    /// <summary>CREATE VIEW / ALTER VIEW / CREATE OR ALTER VIEW（空白・大文字小文字の揺れを許容）。</summary>
    internal static bool IsViewDefinition(string sql) =>
        ViewDefinitionRegex.IsMatch(sql);

    private static readonly Regex ViewDefinitionRegex = new(
        @"\bCREATE\s+(OR\s+ALTER\s+)?VIEW\b|\bALTER\s+VIEW\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// SQL ファイルを読み、書き戻し用の Encoding を返す。
    /// UTF-8 BOM / UTF-16 LE・BE BOM を優先検出する。
    /// BOM なしの場合は Shift-JIS → UTF-8（BOMなし）の順でラウンドトリップ検証し、
    /// どちらでも元バイト列に戻らない場合は失敗（呼び出し側でスキップ＋警告）とする。
    /// </summary>
    private static bool TryReadSqlFilePreservingEncoding(
        string path,
        out Encoding encoding,
        out string text,
        out string? error)
    {
        encoding = Encoding.UTF8;
        text = "";
        error = null;

        var bytes = File.ReadAllBytes(path);

        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            text = encoding.GetString(bytes, 3, bytes.Length - 3);
            return true;
        }

        // UTF-16 LE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return true;
        }

        // UTF-16 BE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return true;
        }

        // BOM なし: Shift-JIS（既存 DeployService と同じ既定）を優先し、ラウンドトリップで検証する。
        // BOM なし UTF-8 を SJIS で誤デコードすると日本語が壊れ、書き戻しでファイル全体が破損するため。
        var sjis = Encoding.GetEncoding("shift_jis");
        var sjisText = sjis.GetString(bytes);
        if (sjis.GetBytes(sjisText).AsSpan().SequenceEqual(bytes))
        {
            encoding = sjis;
            text = sjisText;
            return true;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            var utf8Text = utf8.GetString(bytes);
            if (utf8.GetBytes(utf8Text).AsSpan().SequenceEqual(bytes))
            {
                encoding = utf8;
                text = utf8Text;
                return true;
            }
        }
        catch (DecoderFallbackException)
        {
            // 無効な UTF-8 シーケンス
        }

        error = "Shift-JIS / UTF-8（BOMなし）のいずれでもバイト列を再現できません";
        return false;
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
            FileName = "cmd.exe",
            // .bat は UseShellExecute=false のまま FileName に直接指定しても起動できないため、
            // cmd.exe /c 経由で起動する（DeployService.RunBatAsync と同じパターン）。
            // chcp 932 を先行実行し、bat およびその子プロセスが Shift-JIS で動作するようにする。
            Arguments = $"/c \"chcp 932 > nul && \"{batPath}\"\"",
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
                onlySqlResult = await RunSqlDeployAsync(config, line => LogSqlDeployLine(LogLine, line), ct);
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

                // 共通画像フォルダ → pilot の Images\products（Issue #27）。
                // Files コピーの後に実行し、重複時は共通画像側を後勝ちとする。
                if (string.IsNullOrWhiteSpace(config.CommonImagePath) || string.IsNullOrWhiteSpace(target.DestImagePath))
                {
                    LogLine("INFO", $"{target.Name}: 画像コピーをスキップ（CommonImagePath または DestImagePath が未設定）");
                }
                else
                {
                    LogLine("STEP", $"▶ {target.Name} 画像コピー開始");
                    var imageExitCode = await RunRobocopyAsync(
                        config.CommonImagePath,
                        target.DestImagePath,
                        line => LogLine("DETAIL", line),
                        ct);

                    if (!IsRobocopySuccess(imageExitCode))
                    {
                        LogLine("ERROR", $"{target.Name}: 画像コピーが robocopy エラー終了しました (exit code {imageExitCode})");
                        results.Add(new WebSourceDeployTargetResult(target.Name, false, $"画像コピー robocopy exit code {imageExitCode}"));
                        break;
                    }

                    LogLine("OK", $"{target.Name}: 画像コピー完了 (exit code {imageExitCode})");
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
                sqlDeployResult = await RunSqlDeployAsync(config, line => LogSqlDeployLine(LogLine, line), ct);
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

    /// <summary>
    /// SQL 適用ログのうち "WARN:" で始まる行は WARN レベルで出す（robocopy の DETAIL に埋もれないようにする）。
    /// レベル昇格時はメッセージ先頭の "WARN:" を落とし、UI 上で "[WARN] WARN: ..." と二重表示しない。
    /// </summary>
    private static void LogSqlDeployLine(Action<string, string> logLine, string message)
    {
        if (message.StartsWith("WARN:", StringComparison.Ordinal))
            logLine("WARN", message["WARN:".Length..].TrimStart());
        else
            logLine("DETAIL", message);
    }
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
