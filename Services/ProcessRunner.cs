using System.Diagnostics;
using System.Text;

namespace CquAutoLogin.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, int timeoutMs = 15000, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    public Task<ProcessResult> RunAsync(string fileName, int timeoutMs = 15000, CancellationToken cancellationToken = default, params string[] arguments)
    {
        return RunAsync(fileName, arguments, timeoutMs, cancellationToken);
    }
}
