using System.Text;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Controllers;

/// <summary>Pilot 適用 SSE 行を実行履歴用の全文ログへ落とす（DeployController と同じ書式）。</summary>
public static class PilotLogFormatter
{
    public static string FormatLogEntry(LogEntry entry) =>
        $"{entry.Timestamp} [{entry.Level}] {entry.Message}";

    public static void Append(StringBuilder sb, LogEntry entry) =>
        sb.AppendLine(FormatLogEntry(entry));
}
