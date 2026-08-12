using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
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
    private readonly IProcessRunner _processRunner;
    private readonly string[] _excludeFiles;
    private readonly string[] _excludeDirs;

    public WebSourceDeployService(
        IConfiguration config,
        ILogger<WebSourceDeployService> logger,
        IProcessRunner processRunner)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _logger = logger;
        _processRunner = processRunner;

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

        return await RunRobocopyProcessAsync(args, onOutputLine, ct);
    }

    /// <summary>
    /// Pilot SQL 専用コピー（*.sql のみ）。共通 BuildArguments（Web／画像／Files）には触れない（B1）。
    /// DryRun 時はプロセスを起動せず引数のみログする（IProcessRunner 注入でも DryRun 不変条件を保つ — PR #37 #4）。
    /// </summary>
    internal async Task<int> CopyPilotSqlFilesAsync(
        string src,
        string dest,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        ValidateDeployPaths(src, dest);

        // ファイルクラス *.sql のみ。/XF /XD は付けない（専用経路）。
        var args = BuildPilotSqlRobocopyArgs(src, dest);

        if (_dryRun)
        {
            onOutputLine($"[DRY-RUN] robocopy {args}");
            return 1;
        }

        return await RunRobocopyProcessAsync(args, onOutputLine, ct);
    }

    /// <summary>
    /// Pilot SQL 専用 robocopy 引数（*.sql のみ）。共通 BuildArguments とは分離（PR #37 #3）。
    /// </summary>
    internal static string BuildPilotSqlRobocopyArgs(string src, string dest) =>
        $"\"{src}\" \"{dest}\" *.sql /E /MT:32 /R:2 /W:5 /NP /XX";

    private Task<int> RunRobocopyProcessAsync(
        string args,
        Action<string> onOutputLine,
        CancellationToken ct) =>
        _processRunner.RunAsync("robocopy.exe", args, workingDirectory: null, onOutputLine, ct);

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
    /// コピー先のパイロット用 Web.config を web.config として上書きする。
    /// ファイル名は Web.config.DC.{dbConfigName}.pilot（例: kaios → Web.config.DC.kaios.pilot）。
    /// dryRun=true の場合はコピー元（webSourcePath）側の存在チェックのみ行い、上書きしない。
    /// dryRun=false の場合はコピー先（destWebSourcePath）側のファイルを web.config へ上書きする。
    /// ソースファイル（.pilot）は削除しない。
    /// </summary>
    /// <returns>存在確認に使ったパイロット用ファイルのフルパス（ログ用）。</returns>
    public static string ApplyPilotWebConfig(
        string webSourcePath,
        string destWebSourcePath,
        string dbConfigName,
        bool dryRun)
    {
        var sourceName = $"Web.config.DC.{dbConfigName}.pilot";
        // DryRun 時は robocopy が走らないため、コピー先ではなくコピー元（STG）の存在を検査する。
        var appliedSourcePath = Path.Combine(dryRun ? webSourcePath : destWebSourcePath, sourceName);

        if (!File.Exists(appliedSourcePath))
            throw new FileNotFoundException(
                $"パイロット用 web.config が見つかりません: {appliedSourcePath}", appliedSourcePath);

        if (!dryRun)
        {
            var destPath = Path.Combine(destWebSourcePath, "web.config");
            File.Copy(appliedSourcePath, destPath, overwrite: true);
        }

        return appliedSourcePath;
    }

    /// <summary>
    /// DeployedPath / MariaDbDeployedPath の *.sql をそれぞれ Pilot SQL Server / Pilot MariaDB の Source へコピーし、
    /// 対応する deploy.bat（事前配置・本システムは作成しない）を実行する。
    /// PilotSqlDeployPath と PilotMariaDbSqlDeployPath の両方が未設定の場合は null（本ステップ自体をスキップ）。
    /// 両ソースに *.sql が無い場合は bat より前に return し Skipped=true（Issue #35）。
    /// MariaDB は SQL Server Source 配下ではなく別ツリーへコピーし、専用 bat で適用する（B1）。
    /// </summary>
    public async Task<WebSourceSqlDeployResult?> RunSqlDeployAsync(
        DbConfig config,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        var hasPilotSqlPath = !string.IsNullOrWhiteSpace(config.PilotSqlDeployPath);
        var hasPilotMariaPath = !string.IsNullOrWhiteSpace(config.PilotMariaDbSqlDeployPath);
        if (!hasPilotSqlPath && !hasPilotMariaPath)
            return null;

        // 未設定（相対パス等）は空スキップより先にエラー（I1）
        EnsureAbsoluteSqlSourcePath(config.DeployedPath, "DeployedPath", "DeployDev2StgPath");
        EnsureAbsoluteSqlSourcePath(config.MariaDbDeployedPath, "MariaDbDeployedPath", "DeployDev2StgPath");

        var sqlServerSrc = config.DeployedPath;
        var mariaDbSrc = config.MariaDbDeployedPath;
        var hasSqlServer = HasSqlFiles(sqlServerSrc);
        var hasMariaDb = HasSqlFiles(mariaDbSrc);

        // 両空 → bat 存在チェックより前に return（A1）
        if (!hasSqlServer && !hasMariaDb)
        {
            onOutputLine("WARN: 適用対象 SQL なし（DeployedPath / MariaDbDeployedPath に *.sql がありません）");
            return new WebSourceSqlDeployResult(true, null, null, Skipped: true);
        }

        if (hasSqlServer && !hasPilotSqlPath)
            throw new InvalidOperationException(
                "DeployedPath に *.sql がありますが PilotSqlDeployPath が未設定です");
        if (hasMariaDb && !hasPilotMariaPath)
            throw new InvalidOperationException(
                "MariaDbDeployedPath に *.sql がありますが PilotMariaDbSqlDeployPath が未設定です（MariaDB は専用 bat で自動適用します）");

        // Source 初期化（再帰削除）より前に絶対パスを検証（PR #37 N2）
        if (hasSqlServer)
            EnsureAbsoluteSqlSourcePath(config.PilotSqlDeploySourcePath, "PilotSqlDeploySourcePath", "PilotSqlDeployPath");
        if (hasMariaDb)
            EnsureAbsoluteSqlSourcePath(config.PilotMariaDbSqlDeploySourcePath, "PilotMariaDbSqlDeploySourcePath", "PilotMariaDbSqlDeployPath");

        if (hasSqlServer)
        {
            var sourceDir = config.PilotSqlDeploySourcePath;
            if (!_dryRun)
            {
                if (Directory.Exists(sourceDir))
                    Directory.Delete(sourceDir, recursive: true);
                Directory.CreateDirectory(sourceDir);
            }
            else
            {
                onOutputLine($"[DRY-RUN] SQL Server Source フォルダを初期化: {sourceDir}");
            }

            var copyExitCode = await CopyPilotSqlFilesAsync(sqlServerSrc, sourceDir, onOutputLine, ct);
            if (!IsRobocopySuccess(copyExitCode))
                return new WebSourceSqlDeployResult(false, copyExitCode, $"SQL コピーが robocopy エラー終了しました (exit code {copyExitCode})");
        }
        else
        {
            onOutputLine("INFO: SQL Server deployed に *.sql なし — スキップ");
        }

        if (hasMariaDb)
        {
            var mariaSourceDir = config.PilotMariaDbSqlDeploySourcePath;
            if (!_dryRun)
            {
                if (Directory.Exists(mariaSourceDir))
                    Directory.Delete(mariaSourceDir, recursive: true);
                Directory.CreateDirectory(mariaSourceDir);
            }
            else
            {
                onOutputLine($"[DRY-RUN] MariaDB Source フォルダを初期化: {mariaSourceDir}");
            }

            var copyExitCode = await CopyPilotSqlFilesAsync(mariaDbSrc, mariaSourceDir, onOutputLine, ct);
            if (!IsRobocopySuccess(copyExitCode))
                return new WebSourceSqlDeployResult(false, copyExitCode, $"MariaDB SQL コピーが robocopy エラー終了しました (exit code {copyExitCode})");
        }
        else
        {
            onOutputLine("INFO: MariaDB deployed に *.sql なし — スキップ");
        }

        // View ソース内の DB 名置換（Issue #27）。DryRun はソース、実実行はコピー先 Source を走査。
        // 存在する側だけ走査し、無い側の WARN を出さない（PR #37 Nit）。
        // SQL Server / MariaDB の両 Source に同じルールを適用する（N5）。
        // 現行ルール（例: KaiosDB→KaiosDB_pilot）は SQL Server 前提のため、MariaDB 側は通常 no-op。
        if (config.PilotSqlDbNameReplacements.Count > 0)
        {
            var replaceDirs = new List<string>();
            if (_dryRun)
            {
                if (hasSqlServer) replaceDirs.Add(sqlServerSrc);
                if (hasMariaDb) replaceDirs.Add(mariaDbSrc);
            }
            else
            {
                if (hasSqlServer) replaceDirs.Add(config.PilotSqlDeploySourcePath);
                if (hasMariaDb) replaceDirs.Add(config.PilotMariaDbSqlDeploySourcePath);
            }

            foreach (var replaceDir in replaceDirs)
            {
                var targetLabel = _dryRun ? $"{replaceDir}（DryRunプレビュー）" : replaceDir;
                onOutputLine($"View DB名置換: 走査対象={targetLabel}");
                var (fileCount, occurrenceCount, skippedCount) = ReplaceViewDbNames(
                    replaceDir, config.PilotSqlDbNameReplacements, _dryRun, onOutputLine);
                var countSuffix = _dryRun ? " [DRY-RUN]" : "";
                onOutputLine($"View DB名置換: {fileCount} ファイル / {occurrenceCount} 箇所 / スキップ {skippedCount} 件{countSuffix}");
                if (skippedCount > 0)
                    onOutputLine($"WARN: View DB名置換で {skippedCount} 件スキップしました（エンコーディング判定不可）。該当 View は KaiosDB 参照のまま残る可能性があります");
            }
        }
        else
        {
            onOutputLine("View DB名置換: スキップ（PilotSqlDbNameReplacements 未設定）");
        }

        if (_dryRun)
        {
            if (hasSqlServer)
                onOutputLine($"[DRY-RUN] SQL Server deploy.bat 実行: {config.PilotSqlDeployBatPath}");
            if (hasMariaDb)
                onOutputLine($"[DRY-RUN] MariaDB deploy.bat 実行: {config.PilotMariaDbSqlDeployBatPath}");
            return new WebSourceSqlDeployResult(true, null, null);
        }

        if (hasSqlServer)
        {
            if (!File.Exists(config.PilotSqlDeployBatPath))
                return new WebSourceSqlDeployResult(false, null, $"SQL Server deploy.bat が見つかりません: {config.PilotSqlDeployBatPath}");

            onOutputLine($"SQL Server deploy.bat 実行: {config.PilotSqlDeployBatPath}");
            var batExitCode = await RunDeployBatAsync(
                config.PilotSqlDeployPath, config.PilotSqlDeployBatPath, onOutputLine, ct);
            if (batExitCode != 0)
                return new WebSourceSqlDeployResult(false, batExitCode, $"SQL Server deploy.bat がエラー終了しました (exit code {batExitCode})");
        }

        if (hasMariaDb)
        {
            if (!File.Exists(config.PilotMariaDbSqlDeployBatPath))
                return new WebSourceSqlDeployResult(false, null, $"MariaDB deploy.bat が見つかりません: {config.PilotMariaDbSqlDeployBatPath}");

            onOutputLine($"MariaDB deploy.bat 実行: {config.PilotMariaDbSqlDeployBatPath}");
            var batExitCode = await RunDeployBatAsync(
                config.PilotMariaDbSqlDeployPath, config.PilotMariaDbSqlDeployBatPath, onOutputLine, ct);
            if (batExitCode != 0)
                return new WebSourceSqlDeployResult(false, batExitCode, $"MariaDB deploy.bat がエラー終了しました (exit code {batExitCode})");
        }

        // 成功時 ExitCode は意味を持たない（複数 bat の代表値にならない）ため null（PR #37 N4）
        return new WebSourceSqlDeployResult(true, null, null);
    }

    /// <summary>
    /// 未設定・相対パスは設定ミスとしてエラーにする（再帰削除より前に呼ぶ）。
    /// <paramref name="configKeyHint"/> は運用者が直すべき設定キー（導出プロパティの場合は親キー）。
    /// </summary>
    private static void EnsureAbsoluteSqlSourcePath(string path, string label, string configKeyHint)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new InvalidOperationException(
                $"{label} が絶対パスとして解決できません（{configKeyHint} の設定を確認してください）: {path}");
    }

    /// <summary>再帰で *.sql が1件以上あるか（ディレクトリ不存在は空扱い）。</summary>
    internal static bool HasSqlFiles(string dir) =>
        Directory.Exists(dir)
        && Directory.EnumerateFiles(dir, "*.sql", SearchOption.AllDirectories).Any();

    /// <summary>再帰で通常ファイルが1件以上あるか（空カテゴリフォルダのみは空扱い）。</summary>
    internal static bool HasAnyFiles(string dir) =>
        Directory.Exists(dir)
        && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();


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

    private Task<int> RunDeployBatAsync(
        string workingDirectory,
        string batPath,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        // .bat は UseShellExecute=false のまま FileName に直接指定しても起動できないため、
        // cmd.exe /c 経由で起動する（DeployService.RunBatAsync と同じパターン）。
        // chcp 932 を先行実行し、bat およびその子プロセスが Shift-JIS で動作するようにする。
        var arguments = $"/c \"chcp 932 > nul && \"{batPath}\"\"";
        return _processRunner.RunAsync("cmd.exe", arguments, workingDirectory, onOutputLine, ct);
    }

    /// <summary>
    /// DbConfig.PilotTargets を pilot1 → pilot2 の順に処理し、成功した場合は続けて SQL 適用
    /// （PilotSqlDeployPath への SQL コピー＋deploy.bat 実行）を行う。
    /// あるターゲットで robocopy がエラー終了、またはパイロット用 web.config 適用が失敗した場合、
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
                        FormatSqlDeployCompletionMessage(onlySqlResult));
                }
            }
            catch (Exception ex)
            {
                onlySqlResult = new WebSourceSqlDeployResult(false, null, ex.Message);
                LogLine("ERROR", $"SQL適用: {ex.Message}");
            }

            var onlySqlFailed = onlySqlResult is { Success: false };
            var onlySqlSkipped = onlySqlResult is { Success: true, Skipped: true };
            LogLine(onlySqlFailed ? "ERROR" : "OK",
                FormatOverallCompletionMessage(failed: onlySqlFailed, skippedOnly: onlySqlSkipped));

            return (results, onlySqlResult);
        }

        // Files の有無はターゲットループ外で1回判定し再利用する（F2）
        var filesPathReady = Path.IsPathRooted(config.FilesPath) && HasAnyFiles(config.FilesPath);

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

                // FilesPath（STG 適用後の静的ファイル。Images/news/pdf カテゴリを直下に持つ）を
                // pilot 側 Web ソースルート直下へコピーする（Issue #35: FilesDeploy2PrdPath は使わない）。
                // ファイル0件（またはパス未解決）はスキップして成功継続。
                if (filesPathReady)
                {
                    var filesExitCode = await RunRobocopyAsync(
                        config.FilesPath,
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
                else
                {
                    LogLine("INFO", $"{target.Name}: Files コピーをスキップ（FilesPath にファイルなし）");
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

                var dryRunTag = _dryRun ? " [DRY-RUN]" : "";
                var appliedSourcePath = ApplyPilotWebConfig(
                    webSourcePath: config.WebSourcePath,
                    destWebSourcePath: target.DestWebSourcePath,
                    dbConfigName: config.Name,
                    dryRun: _dryRun);
                var lastWrite = File.GetLastWriteTime(appliedSourcePath);
                LogLine("OK",
                    $"{target.Name}: {Path.GetFileName(appliedSourcePath)} を web.config として適用しました" +
                    $"（更新日時: {lastWrite:yyyy-MM-dd HH:mm:ss}）{dryRunTag}");

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
                        FormatSqlDeployCompletionMessage(sqlDeployResult));
                }
            }
            catch (Exception ex)
            {
                sqlDeployResult = new WebSourceSqlDeployResult(false, null, ex.Message);
                LogLine("ERROR", $"SQL適用: {ex.Message}");
            }
        }

        // Both で Web 成功＋SQL スキップのみのときは Web 適用済みのため「完了」。SqlOnly スキップは別経路。
        LogLine(failed ? "ERROR" : "OK",
            FormatOverallCompletionMessage(failed: failed, skippedOnly: false));

        return (results, sqlDeployResult);
    }

    private static string FormatSqlDeployCompletionMessage(WebSourceSqlDeployResult sqlDeploy) =>
        sqlDeploy.Skipped
            ? "SQL適用: スキップ（適用対象 SQL なし）"
            : sqlDeploy.Success
                ? "SQL適用: 完了しました"
                : $"SQL適用: 失敗しました ({sqlDeploy.ErrorMessage})";

    /// <summary>全体サマリ。SqlOnly＋スキップ時は「完了」と誤読されない文言にする（PR #37 Consider）。</summary>
    internal static string FormatOverallCompletionMessage(bool failed, bool skippedOnly) =>
        failed
            ? "❌ Pilot環境適用が中断されました"
            : skippedOnly
                ? "⏭ Pilot環境適用をスキップしました（適用対象なし）"
                : "✅ Pilot環境適用が完了しました";


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
/// <param name="Skipped">適用対象 *.sql が無く処理をスキップした場合 true（Result は success 相当）。</param>
public record WebSourceSqlDeployResult(bool Success, int? ExitCode, string? ErrorMessage, bool Skipped = false);

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
