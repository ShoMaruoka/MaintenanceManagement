namespace MaintenanceManagement.Api.Services;

/// <summary>
/// 外部プロセス起動の抽象（robocopy / deploy.bat 等）。
/// DryRun 判定は呼び出し側で行い、本 IF は実起動のみを担当する（PR #37 #4）。
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// プロセスを起動し、標準出力/標準エラーを1行ずつ <paramref name="onOutputLine"/> へ渡す。
    /// 終了コードを返す。キャンセル時は可能ならプロセスツリーを終了する。
    /// </summary>
    Task<int> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string> onOutputLine,
        CancellationToken ct);
}
