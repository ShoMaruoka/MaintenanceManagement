namespace MaintenanceManagement.Api.Services;

/// <summary>ファイルパスのルート配下チェック用ヘルパー。</summary>
internal static class PathSafety
{
    /// <summary>
    /// candidate が root 自身、または root 配下のパスであるかを判定する。
    /// 両方ともフルパス前提（呼び出し側で Path.GetFullPath 済みを推奨）。
    /// </summary>
    public static bool IsUnderRoot(string rootFullPath, string candidateFullPath)
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

    /// <summary>
    /// root と相対セグメントを結合し、結果が root 配下であることを保証したフルパスを返す。
    /// </summary>
    public static string CombineUnderRoot(string rootPath, IEnumerable<string> relativeSegments, string rejectMessage)
    {
        var segments = relativeSegments.ToArray();
        var candidate = Path.GetFullPath(Path.Combine(new[] { rootPath }.Concat(segments).ToArray()));
        var root = Path.GetFullPath(rootPath);
        if (!IsUnderRoot(root, candidate))
            throw new InvalidOperationException(rejectMessage);
        return candidate;
    }
}
