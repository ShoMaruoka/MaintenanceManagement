namespace MaintenanceManagement.Api.Models;

/// <summary>AppSetting テーブルの1行（汎用 key-value 設定）。UpdatedAt は UTC の ISO 8601。</summary>
public record AppSettingEntry(string Key, string Value, string UpdatedBy, string UpdatedAt);

/// <summary>
/// Pilot環境適用で Web.config をコピーしないかどうかの設定（システムごと。pilot-webconfig-exclude）。
/// 未設定時は ExcludeWebConfig=true（安全側）で、UpdatedBy / UpdatedAt は null。
/// </summary>
public record PilotExcludeWebConfigSetting(bool ExcludeWebConfig, string? UpdatedBy, string? UpdatedAt);
