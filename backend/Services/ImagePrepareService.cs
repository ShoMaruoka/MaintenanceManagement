using MaintenanceManagement.Api.Models;
using Microsoft.AspNetCore.Http;

namespace MaintenanceManagement.Api.Services;

/// <summary>
/// Deploy_DEV2STG\Files 配下の列挙・パス検証・アップロード／フォルダ作成。
/// カテゴリは Images / news / pdf、サブフォルダは最大 2 階層。
/// </summary>
public class ImagePrepareService
{
    public const int MaxSubfolderDepth = 2;
    public const long MaxUploadBytes = 50L * 1024 * 1024; // 50MB

    public static readonly string[] AllowedCategories = ["Images", "news", "pdf"];

    private static readonly HashSet<string> AllowedCategorySet =
        new(AllowedCategories, StringComparer.Ordinal);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico",
        ".pdf",
        ".css", ".js", ".json", ".xml", ".txt", ".html", ".htm", ".csv",
        ".woff", ".woff2", ".ttf", ".eot",
        ".zip",
    };

    private readonly bool _dryRun;

    public ImagePrepareService(IConfiguration config)
    {
        _dryRun = config.GetValue<bool>("DryRun");
    }

    public bool IsDryRun => _dryRun;

    public ImagePrepareTreeResponse GetTree(DbConfig config)
    {
        var response = new ImagePrepareTreeResponse { DbName = config.Name };
        var filesPath = config.FilesPath;

        foreach (var category in AllowedCategories)
        {
            var node = new ImageCategoryNode { Name = category };
            var categoryDir = Path.Combine(filesPath, category);

            if (Directory.Exists(categoryDir))
            {
                node.Entries = ReadEntries(categoryDir, category);
            }

            response.Categories.Add(node);
        }

        return response;
    }

    /// <summary>
    /// Files 配下の全ファイルを相対パス（/ 区切り）で列挙する。ディレクトリが無ければ空。
    /// </summary>
    public IReadOnlyList<string> ListRelativeFilePaths(DbConfig config)
    {
        var filesPath = config.FilesPath;
        if (!Directory.Exists(filesPath))
            return [];

        var rootFull = Path.GetFullPath(filesPath);
        var results = new List<string>();

        foreach (var category in AllowedCategories)
        {
            var categoryDir = Path.Combine(filesPath, category);
            if (!Directory.Exists(categoryDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(categoryDir, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                if (!IsUnderRoot(rootFull, full))
                    continue;

                var relative = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
                results.Add(relative);
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    public ImageCreateFolderResponse CreateFolder(DbConfig config, string category, string? relativeSubPath)
    {
        if (string.IsNullOrWhiteSpace(relativeSubPath))
            throw new ArgumentException("サブフォルダパスを指定してください");

        if (!TryResolveDirectory(config, category, relativeSubPath, out var fullDir, out var error))
            throw new ArgumentException(error);

        var relative = BuildRelativeDir(category, relativeSubPath);
        var existed = Directory.Exists(fullDir);

        if (!_dryRun && !existed)
            Directory.CreateDirectory(fullDir);

        return new ImageCreateFolderResponse
        {
            DbName = config.Name,
            RelativePath = relative,
            DryRun = _dryRun,
            Created = !existed,
        };
    }

    public ImageUploadResponse Upload(
        DbConfig config,
        string category,
        string? relativeSubPath,
        IReadOnlyList<IFormFile> files,
        bool overwrite)
    {
        if (files.Count == 0)
            throw new ArgumentException("アップロードするファイルがありません");

        if (!TryResolveDirectory(config, category, relativeSubPath, out var destDir, out var error))
            throw new ArgumentException(error);

        var planned = new List<(IFormFile File, string FullPath, string RelativePath, bool Exists)>();

        foreach (var file in files)
        {
            if (file.Length <= 0)
                throw new ArgumentException($"空のファイルはアップロードできません: {file.FileName}");

            if (file.Length > MaxUploadBytes)
                throw new ArgumentException($"ファイルサイズが上限（50MB）を超えています: {file.FileName}");

            var safeName = SanitizeFileName(file.FileName);
            var ext = Path.GetExtension(safeName);
            if (!AllowedExtensions.Contains(ext))
                throw new ArgumentException($"許可されていない拡張子です: {safeName}");

            var fullPath = Path.GetFullPath(Path.Combine(destDir, safeName));
            var root = Path.GetFullPath(config.FilesPath);
            if (!IsUnderRoot(root, fullPath))
                throw new ArgumentException($"Files 配下以外のパスは指定できません: {safeName}");

            var relative = BuildRelativeFile(category, relativeSubPath, safeName);
            planned.Add((file, fullPath, relative, File.Exists(fullPath)));
        }

        var conflicts = planned.Where(p => p.Exists && !overwrite).Select(p => p.RelativePath).ToList();
        if (conflicts.Count > 0)
            throw new ImagePrepareConflictException(conflicts);

        if (!_dryRun)
            Directory.CreateDirectory(destDir);

        var saved = new List<ImageUploadSavedFile>();
        foreach (var item in planned)
        {
            if (!_dryRun)
            {
                using var stream = new FileStream(item.FullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                item.File.CopyTo(stream);
            }

            saved.Add(new ImageUploadSavedFile
            {
                RelativePath = item.RelativePath,
                Overwritten = item.Exists,
            });
        }

        return new ImageUploadResponse
        {
            DbName = config.Name,
            DryRun = _dryRun,
            Saved = saved,
        };
    }

    /// <summary>
    /// カテゴリ＋サブフォルダ（最大 2 階層）を Files 配下の絶対ディレクトリパスに解決する。
    /// </summary>
    public bool TryResolveDirectory(
        DbConfig config,
        string category,
        string? relativeSubPath,
        out string fullDirectoryPath,
        out string error)
    {
        fullDirectoryPath = "";
        error = "";

        if (!TryValidateCategory(category, out error))
            return false;

        if (!TryNormalizeSubPath(relativeSubPath, out var segments, out error))
            return false;

        var parts = new List<string> { config.FilesPath, category };
        parts.AddRange(segments);

        var candidate = Path.GetFullPath(Path.Combine(parts.ToArray()));
        var root = Path.GetFullPath(config.FilesPath);

        if (!IsUnderRoot(root, candidate))
        {
            error = "Files 配下以外のパスは指定できません";
            return false;
        }

        fullDirectoryPath = candidate;
        return true;
    }

    /// <summary>
    /// Files ルートからの相対パス（例: Images/flash/img/a.png）を絶対パスに解決する。
    /// </summary>
    public bool TryResolveRelativeFile(
        DbConfig config,
        string relativePath,
        out string fullFilePath,
        out string error)
    {
        fullFilePath = "";
        error = "";

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "相対パスが空です";
            return false;
        }

        var normalized = relativePath.Trim().Replace('\\', '/');
        if (normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || normalized.StartsWith('/')
            || normalized.Contains(':'))
        {
            error = "不正な相対パスです";
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            error = "相対パスは カテゴリ/ファイル名 以上である必要があります";
            return false;
        }

        var category = segments[0];
        if (!TryValidateCategory(category, out error))
            return false;

        var folderDepth = segments.Length - 2;
        if (folderDepth > MaxSubfolderDepth)
        {
            error = $"サブフォルダは最大 {MaxSubfolderDepth} 階層までです";
            return false;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg is "." or "..")
            {
                error = "相対参照 (.. や .) は使用できません";
                return false;
            }
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = $"パスに使用できない文字が含まれています: {seg}";
                return false;
            }
        }

        var candidate = Path.GetFullPath(Path.Combine(new[] { config.FilesPath }.Concat(segments).ToArray()));
        var root = Path.GetFullPath(config.FilesPath);

        if (!IsUnderRoot(root, candidate))
        {
            error = "Files 配下以外のパスは指定できません";
            return false;
        }

        fullFilePath = candidate;
        return true;
    }

    public static bool TryValidateCategory(string category, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(category) || !AllowedCategorySet.Contains(category))
        {
            error = "カテゴリは Images / news / pdf のいずれかである必要があります";
            return false;
        }
        return true;
    }

    public static bool TryNormalizeSubPath(string? relativeSubPath, out string[] segments, out string error)
    {
        segments = [];
        error = "";

        if (string.IsNullOrWhiteSpace(relativeSubPath))
            return true;

        var trimmed = relativeSubPath.Trim().Replace('\\', '/');

        if (trimmed.StartsWith('/')
            || trimmed.Contains(':')
            || Path.IsPathRooted(relativeSubPath))
        {
            error = "絶対パスは指定できません";
            return false;
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return true;

        if (parts.Length > MaxSubfolderDepth)
        {
            error = $"サブフォルダは最大 {MaxSubfolderDepth} 階層までです";
            return false;
        }

        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                error = "相対参照 (.. や .) は使用できません";
                return false;
            }
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = $"フォルダ名に使用できない文字が含まれています: {part}";
                return false;
            }
        }

        segments = parts;
        return true;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName?.Replace('\\', '/') ?? "");
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"不正なファイル名です: {fileName}");
        }
        return name;
    }

    private static string BuildRelativeDir(string category, string? relativeSubPath)
    {
        if (!TryNormalizeSubPath(relativeSubPath, out var segments, out _))
            return category;
        return segments.Length == 0
            ? category
            : $"{category}/{string.Join('/', segments)}";
    }

    private static string BuildRelativeFile(string category, string? relativeSubPath, string fileName)
    {
        var dir = BuildRelativeDir(category, relativeSubPath);
        return $"{dir}/{fileName}";
    }

    private static List<ImageTreeEntry> ReadEntries(string directory, string relativePrefix)
    {
        var entries = new List<ImageTreeEntry>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                var relative = $"{relativePrefix}/{name}";
                entries.Add(new ImageTreeEntry
                {
                    Name = name,
                    RelativePath = relative,
                    IsDirectory = true,
                    Children = ReadEntries(dir, relative),
                });
            }

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                entries.Add(new ImageTreeEntry
                {
                    Name = name,
                    RelativePath = $"{relativePrefix}/{name}",
                    IsDirectory = false,
                });
            }
        }
        catch (Exception)
        {
            // 列挙失敗時は空（権限不足等）。呼び出し側で例外にしない。
        }

        return entries;
    }

    private static bool IsUnderRoot(string rootFullPath, string candidateFullPath)
    {
        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var candidate = candidateFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   candidateFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>同名ファイルが存在し overwrite=false のときの競合。</summary>
public class ImagePrepareConflictException : Exception
{
    public IReadOnlyList<string> Conflicts { get; }

    public ImagePrepareConflictException(IReadOnlyList<string> conflicts)
        : base("同名のファイルが既に存在します")
    {
        Conflicts = conflicts;
    }
}
