using System.Diagnostics;
using System.Text;

namespace YtDlpGui.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken cancellationToken, Action<string, bool>? onLine = null, bool captureOutput = false, string? workingDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("取得ツールを起動できませんでした。");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var output = new StringBuilder();
        var errors = new Queue<string>();
        async Task Drain(StreamReader reader, bool error)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (error) { errors.Enqueue(line); if (errors.Count > 80) errors.Dequeue(); }
                else if (captureOutput)
                {
                    if (output.Length + line.Length > 32 * 1024 * 1024)
                    {
                        try { process.Kill(true); } catch (InvalidOperationException) { }
                        throw new IOException("取得した情報が大きすぎます。個別の動画URLを指定してください。");
                    }
                    output.AppendLine(line);
                }
                onLine?.Invoke(line, error);
            }
        }
        var stdout = Drain(process.StandardOutput, false);
        var stderr = Drain(process.StandardError, true);
        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(stdout, stderr);
        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, output.ToString(), string.Join(Environment.NewLine, errors));
    }
}
