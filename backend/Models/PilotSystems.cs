namespace MaintenanceManagement.Api.Models;

/// <summary>
/// pilot 環境が存在するシステム（Pilot環境適用の対象。pilot-webconfig-exclude で PAF / Duskin を追加）。
/// 並び順はダッシュボードの Pilot 最終適用カードの表示順にも使う。
/// </summary>
public static class PilotSystems
{
    public static readonly IReadOnlyList<string> DbNames = ["kaios", "gos", "paf", "duskin"];

    public static bool IsSupported(string? dbName) =>
        !string.IsNullOrEmpty(dbName) && DbNames.Contains(dbName, StringComparer.OrdinalIgnoreCase);
}
