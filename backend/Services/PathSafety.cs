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
        var root = NormalizeDirectory(rootFullPath) + Path.DirectorySeparatorChar;
        var candidate = NormalizeDirectory(candidateFullPath) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || AreSamePath(rootFullPath, candidateFullPath);
    }

    /// <summary>末尾セパレータを除いたパス同士が同一かを判定する。</summary>
    public static bool AreSamePath(string pathA, string pathB)
    {
        return string.Equals(
            NormalizeDirectory(pathA),
            NormalizeDirectory(pathB),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// root と相対セグメントを結合し、結果が root 配下であれば true。
    /// </summary>
    public static bool TryCombineUnderRoot(
        string rootPath,
        IEnumerable<string> relativeSegments,
        out string fullPath,
        out string error,
        string? rejectMessage = null)
    {
        fullPath = "";
        error = "";

        var candidate = Path.GetFullPath(Path.Combine(new[] { rootPath }.Concat(relativeSegments).ToArray()));
        var root = Path.GetFullPath(rootPath);
        if (!IsUnderRoot(root, candidate))
        {
            error = rejectMessage ?? "指定ルート配下以外のパスは指定できません";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// root と相対セグメントを結合し、結果が root 配下であることを保証したフルパスを返す。
    /// </summary>
    public static string CombineUnderRoot(string rootPath, IEnumerable<string> relativeSegments, string rejectMessage)
    {
        if (!TryCombineUnderRoot(rootPath, relativeSegments, out var fullPath, out var error, rejectMessage))
            throw new InvalidOperationException(error);
        return fullPath;
    }

    private static string NormalizeDirectory(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
