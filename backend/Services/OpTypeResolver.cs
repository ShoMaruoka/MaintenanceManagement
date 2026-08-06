namespace MaintenanceManagement.Api.Services;

/// <summary>
/// 本番前準備画面で deployed/ の SQL ファイルに操作区分（新規／更新／削除）を対応付けるための
/// 名前正規化・DB 種別判定を提供する。
///
/// 区分の供給元は STG 適用時に記録される DeploySessionDetail であり、本クラスは
/// 「フォルダ上のファイル名」と「記録されたモジュール名」を同じ土俵に載せる役割を持つ。
/// 判定ルールを 1 か所に閉じ込めるため、SQL 側では分類せず C# 側だけで解決する。
/// </summary>
public static class OpTypeResolver
{
    /// <summary>区分を引き当てられなかったファイルの表示値。</summary>
    public const string Unknown = "不明";

    private const string DboPrefix = "dbo.";
    private const string SqlExtension = ".sql";

    private static readonly HashSet<string> KnownOpTypes =
        new(["新規", "更新", "削除"], StringComparer.Ordinal);

    /// <summary>
    /// MariaDB 側の ModuleType。"Stored" は MariaDB のストアドで、SQL Server のストアド
    /// （"StoredProcedure"）とは別値であるため衝突しない（Issue #22）。
    /// "MariaDB" は Issue #22 以前の旧値で、既存の実行履歴に残っている可能性がある。
    /// </summary>
    private static readonly HashSet<string> MariaDbModuleTypes =
        new(["Stored", "MariaDbFunction", "MariaDbTable", "MariaDB"], StringComparer.OrdinalIgnoreCase);

    /// <summary>ModuleType から DB 種別を判定する。未知の値は SQL Server 側へフォールバックする。</summary>
    public static string ToDbType(string? moduleType) =>
        MariaDbModuleTypes.Contains(moduleType ?? "") ? "mariadb" : "sqlserver";

    /// <summary>
    /// deployed/ 配下のファイル名を照合用の名前へ正規化する。
    /// 末尾の ".sql" のみを取り除く（Path.GetFileNameWithoutExtension では
    /// "a.b.c.sql" が "a.b.c" ではなく "a.b" に切り詰められてしまうため）。
    /// </summary>
    public static string NormalizeFileName(string? fileName)
    {
        var value = fileName ?? "";
        if (value.EndsWith(SqlExtension, StringComparison.OrdinalIgnoreCase))
            value = value[..^SqlExtension.Length];
        return StripDboPrefix(value);
    }

    /// <summary>
    /// DeploySessionDetail.ModuleName を照合用の名前へ正規化する。
    /// 実データには "dbo.TestSP" のようにプレフィックス付きで記録された行が存在するため、
    /// ファイル名側と同じくプレフィックスを取り除く。モジュール名は拡張子を持たないので
    /// 末尾が ".sql" に見えても切らない。
    /// </summary>
    public static string NormalizeModuleName(string? moduleName) => StripDboPrefix(moduleName ?? "");

    /// <summary>deployed/ 配下のファイルから逆引きキーを作る。</summary>
    public static string FileKey(string dbType, string? fileName) =>
        $"{dbType}:{NormalizeFileName(fileName)}";

    /// <summary>実行履歴の明細から逆引きキーを作る。</summary>
    public static string ModuleKey(string? moduleType, string? moduleName) =>
        $"{ToDbType(moduleType)}:{NormalizeModuleName(moduleName)}";

    /// <summary>
    /// 想定外の区分値（実データに存在する "更新2" など）や空値を "不明" に寄せる。
    /// 画面に未知のラベルを出さないための防御。
    /// </summary>
    public static string NormalizeOpType(string? opType) =>
        opType is not null && KnownOpTypes.Contains(opType) ? opType : Unknown;

    /// <summary>キー比較用の比較子。Windows のファイル名は大文字小文字を区別しない。</summary>
    public static StringComparer KeyComparer => StringComparer.OrdinalIgnoreCase;

    private static string StripDboPrefix(string value) =>
        value.StartsWith(DboPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[DboPrefix.Length..]
            : value;
}
