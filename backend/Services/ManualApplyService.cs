using System.Text.Json;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Services;

/// <summary>
/// Table / UserDefinedTableType の手動適用待ちリストを管理する。
///
/// この 2 種別は「テーブルのデプロイは危険なので構造のみ管理」という運用ポリシーにより
/// STG 適用時は Git マージのみを行い、STG / 本番への反映は人が SSMS で実施する。
/// 本サービスはその対象を deployed_manual フォルダとマニフェストに残し、
/// 本番前準備画面で確認・消化できるようにする。
/// </summary>
public class ManualApplyService
{
    public static readonly string[] ManualApplyTypes = ["Table", "UserDefinedTableType"];

    private static readonly HashSet<string> ManualApplyTypeSet =
        new(ManualApplyTypes, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly bool _dryRun;
    private readonly ILogger<ManualApplyService> _logger;

    public ManualApplyService(IConfiguration config, ILogger<ManualApplyService> logger)
    {
        _dryRun = config.GetValue<bool>("DryRun");
        _logger = logger;
    }

    public static bool IsManualApplyType(string type) => ManualApplyTypeSet.Contains(type);

    /// <summary>
    /// STG 適用（Git マージ）した Table / UDTT を手動適用待ちとして登録する。
    /// Git 上の定義 SQL を deployed_manual へコピーし、マニフェストへ追記する。
    /// 同一モジュールが既に待機中の場合は最新の操作内容で上書きする。
    /// </summary>
    /// <returns>登録した項目（SQL ファイルが見つからなかったものは FileName が空）。</returns>
    public List<ManualApplyItem> Register(
        DbConfig config,
        IEnumerable<DeployModule> modules,
        string executedBy,
        Action<string, string>? log = null)
    {
        var registered = new List<ManualApplyItem>();
        var appliedAt = DateTime.Now.ToString("s");

        foreach (var module in modules)
        {
            var item = new ManualApplyItem
            {
                ModuleType   = module.Type,
                ModuleName   = module.Name,
                OpType       = module.OpType,
                StgAppliedAt = appliedAt,
                StgAppliedBy = executedBy,
            };

            var srcPath = Path.Combine(config.GitRepoPath, module.Type, $"dbo.{module.Name}.sql");
            if (File.Exists(srcPath))
            {
                var fileName = $"dbo.{module.Name}.sql";
                var destPath = ResolveManualPath(config, fileName);
                item.FileName = fileName;

                if (!_dryRun)
                {
                    Directory.CreateDirectory(config.DeployedManualPath);
                    File.Copy(srcPath, destPath, overwrite: true);
                }
            }
            else
            {
                log?.Invoke("WARN", $"Git に定義 SQL が見つかりません: {module.Type}/dbo.{module.Name}.sql");
            }

            registered.Add(item);
        }

        if (registered.Count > 0 && !_dryRun)
            Upsert(config, registered);

        return registered;
    }

    /// <summary>
    /// 手動適用待ちの一覧を返す。マニフェストを正とし、記載の無い SQL ファイルは
    /// 由来不明として最小情報で拾う（手動配置・旧バージョンからの移行を想定）。
    /// </summary>
    public List<ManualApplyItem> List(DbConfig config)
    {
        var items = ReadManifest(config);
        var known = new HashSet<string>(
            items.Where(i => !string.IsNullOrEmpty(i.FileName)).Select(i => i.FileName),
            StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(config.DeployedManualPath))
        {
            foreach (var file in Directory.GetFiles(config.DeployedManualPath, "*.sql"))
            {
                var fileName = Path.GetFileName(file);
                if (known.Contains(fileName)) continue;

                var moduleName = Path.GetFileNameWithoutExtension(fileName);
                if (moduleName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase))
                    moduleName = moduleName["dbo.".Length..];

                items.Add(new ManualApplyItem
                {
                    ModuleType = "Table",
                    ModuleName = moduleName,
                    OpType     = "不明",
                    FileName   = fileName,
                });
            }
        }

        return items
            .OrderBy(i => i.ModuleType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 確認済みとして選択された項目を本番受け渡しフォルダへコピーし、待機リストから消化する。
    /// 未選択の項目はそのまま残り、次回の本番前準備でも表示される。
    /// </summary>
    /// <returns>消化した件数。</returns>
    public int Consume(
        DbConfig config,
        IEnumerable<PrepareManualSelection> selections,
        Action<string, string> log)
    {
        var targetKeys = new HashSet<string>(
            selections.Where(s => s.Apply).Select(s => $"{s.ModuleType}:{s.ModuleName}"),
            StringComparer.OrdinalIgnoreCase);

        if (targetKeys.Count == 0) return 0;

        var pending = List(config);
        var consumed = 0;

        foreach (var item in pending.Where(i => targetKeys.Contains(i.Key)))
        {
            if (!string.IsNullOrEmpty(item.FileName))
            {
                var src = ResolveManualPath(config, item.FileName);

                if (string.IsNullOrWhiteSpace(config.Deploy2PrdPath))
                    throw new InvalidOperationException(
                        $"Deploy2PrdPath is not configured for DB '{config.Name}'");

                var dest = PathSafety.CombineUnderRoot(
                    config.ManualApplyDeploy2PrdPath,
                    [item.FileName],
                    $"ManualApply フォルダ外への書き込みは拒否しました: {item.FileName}");

                log("DETAIL", $"  → {item.ModuleType}/{item.ModuleName}  [手動適用 確認済み]");

                if (!_dryRun)
                {
                    Directory.CreateDirectory(config.ManualApplyDeploy2PrdPath);
                    File.Copy(src, dest, overwrite: true);
                    if (File.Exists(src)) File.Delete(src);
                }
            }
            else
            {
                log("DETAIL", $"  → {item.ModuleType}/{item.ModuleName}  [手動適用 確認済み・SQL なし]");
            }

            consumed++;
        }

        if (consumed > 0 && !_dryRun)
        {
            var remaining = ReadManifest(config).Where(i => !targetKeys.Contains(i.Key)).ToList();
            WriteManifest(config, remaining);
        }

        return consumed;
    }

    private void Upsert(DbConfig config, List<ManualApplyItem> items)
    {
        var merged = ReadManifest(config);
        foreach (var item in items)
        {
            merged.RemoveAll(i => string.Equals(i.Key, item.Key, StringComparison.OrdinalIgnoreCase));
            merged.Add(item);
        }
        WriteManifest(config, merged);
    }

    private List<ManualApplyItem> ReadManifest(DbConfig config)
    {
        var path = config.DeployedManualManifestPath;
        if (!File.Exists(path)) return [];

        var items = new List<ManualApplyItem>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<ManualApplyItem>(line, _jsonOptions);
                if (item is not null && !string.IsNullOrEmpty(item.ModuleName))
                    items.Add(item);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid manual apply manifest line in {Path}", path);
            }
        }
        return items;
    }

    private static void WriteManifest(DbConfig config, List<ManualApplyItem> items)
    {
        Directory.CreateDirectory(config.DeployedManualPath);
        var lines = items.Select(i => JsonSerializer.Serialize(i, _jsonOptions));
        File.WriteAllLines(config.DeployedManualManifestPath, lines);
    }

    private static string ResolveManualPath(DbConfig config, string fileName) =>
        PathSafety.CombineUnderRoot(
            config.DeployedManualPath,
            [fileName],
            $"deployed_manual フォルダ外へのアクセスは拒否しました: {fileName}");
}
