using System.Text;
using System.Text.RegularExpressions;

namespace MaintenanceManagement.Api.Services;

/// <summary>
/// DEV ブランチから master へ取り込んだ SQL Server モジュール内の、DB 名修飾された参照
/// （<c>KaiosDB_dev.dbo.X</c> / <c>[KaiosDB_dev].dbo.X</c> / <c>KaiosDB_dev..X</c>）から
/// 末尾の <c>_dev</c> を外す。開発 DB を直接参照したまま STG / 本番へ流れるのを防ぐため、
/// git_merge.bat の checkout 直後・commit &amp; push 前に適用する。
/// </summary>
public static class DevDbNameReplacer
{
    /// <summary>
    /// 3 部構成以上の名前の先頭側（後ろに「.スキーマ.」または「..」が続く識別子）のうち、
    /// 末尾が _dev のものだけを対象にする。2 部構成（alias.col_dev 等）は DB 名ではないため対象外。
    /// </summary>
    private static readonly Regex DevDbRegex = new(
        @"(?<!\w)(?:\[(?<b>\w+?)_dev\]|(?<p>\w+?)_dev)(?=\s*\.\s*(?:\[[^\]\r\n]*\]|\w+)?\s*\.)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>SQL 文字列中の _dev 付き DB 名を置換する。置換した元の表記（重複除去）を返す。</summary>
    internal static string Replace(string sql, out IReadOnlyList<string> replaced)
    {
        var found = new List<string>();
        var result = DevDbRegex.Replace(sql, m =>
        {
            if (!found.Contains(m.Value, StringComparer.OrdinalIgnoreCase)) found.Add(m.Value);
            return m.Groups["b"].Success ? $"[{m.Groups["b"].Value}]" : m.Groups["p"].Value;
        });
        replaced = found;
        return result;
    }

    /// <summary>
    /// ファイルを元のエンコーディング（BOM 付き UTF-8 / UTF-16 / BOM 無しは SJIS）のまま置換する。
    /// 読み込んだ内容を同じエンコーディングで書き戻しても元のバイト列に戻らない場合
    /// （BOM 無し UTF-8 など判定不能なファイル）は、壊さないよう書き換えずに例外を投げる。
    /// 置換が無ければファイルには触れない。
    /// </summary>
    internal static async Task<IReadOnlyList<string>> ReplaceFileAsync(string path)
    {
        var original = await File.ReadAllBytesAsync(path);

        Encoding encoding;
        string content;
        using (var reader = new StreamReader(new MemoryStream(original), Encoding.GetEncoding("shift_jis"), detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync();
            encoding = reader.CurrentEncoding;
        }

        var updated = Replace(content, out var replaced);
        if (replaced.Count == 0) return replaced;

        if (!Encode(content, encoding).AsSpan().SequenceEqual(original))
            throw new InvalidDataException($"文字コードを判定できないため置換できません: {path}");

        await File.WriteAllBytesAsync(path, Encode(updated, encoding));
        return replaced;
    }

    private static byte[] Encode(string text, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        return [.. preamble, .. body];
    }
}
