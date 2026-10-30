using System.Text.RegularExpressions;

namespace MaintenanceManagement.Api.Services;

/// <summary>
/// STG 適用時に SQL Server モジュールの定義文を <c>CREATE OR ALTER</c> に揃える。
///
/// Pilot 環境への適用は約 1 か月周期のため、その間に「新規 → 更新」と STG 適用されたモジュールは
/// deployed/ のファイルが更新側で上書きされる。更新側が <c>ALTER</c> のままだと、まだオブジェクトが
/// 存在しない Pilot ではエラーになるため、適用先の状態に依存しない <c>CREATE OR ALTER</c> に変換する。
/// </summary>
public static class SqlDefinitionConverter
{
    /// <summary>
    /// 行頭の PROCEDURE / PROC / FUNCTION / VIEW 定義文。CREATE / CREATE OR ALTER も拾うのは、
    /// 「ファイル中で最初の定義文」を特定し、それが既に CREATE 系なら後続の行頭 ALTER（動的 SQL 等）に
    /// 手を出さないようにするため。
    /// </summary>
    private static readonly Regex DefinitionRegex = new(
        @"^[ \t]*(?:(?<create>CREATE\s+(?:OR\s+ALTER\s+)?)|(?<alter>ALTER\s+))(?:PROC(?:EDURE)?|FUNCTION|VIEW)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// 最初の定義文が <c>ALTER ...</c> の場合のみ、その直前に <c>CREATE OR </c> を挿入する。
    /// 元の表記（大文字小文字・空白）は保持し、2 つ目以降の定義文や CREATE 系の定義文は変更しない。
    /// </summary>
    internal static string ToCreateOrAlter(string sql)
    {
        var match = DefinitionRegex.Match(sql);
        if (!match.Success || !match.Groups["alter"].Success)
            return sql;

        return sql.Insert(match.Groups["alter"].Index, "CREATE OR ");
    }
}
