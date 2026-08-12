using System.Diagnostics;
using System.Text;

namespace MaintenanceManagement.Api.Services;

/// <summary>実プロセス起動（robocopy / cmd.exe 経由 bat）。出力は Shift-JIS で読む。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 日本語環境の robocopy / bat は OEM（Shift-JIS）出力のため UTF-8 だと文字化けする。
            StandardOutputEncoding = Encoding.GetEncoding("shift_jis"),
            StandardErrorEncoding = Encoding.GetEncoding("shift_jis"),
        };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(proc);
            throw;
        }

        return proc.ExitCode;
    }

    private static void TryKillProcess(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // ベストエフォート。Kill 自体の失敗でキャンセル処理を止めない。
        }
    }
}
