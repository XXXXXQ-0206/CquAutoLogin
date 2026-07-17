using System.Diagnostics;
using System.Text;

namespace CquAutoLogin.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface ICommandRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : ICommandRunner
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

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateProcessTreeAsync(process, outputTask, errorTask);
            throw;
        }
        catch (TimeoutException)
        {
            await TerminateProcessTreeAsync(process, outputTask, errorTask);
            throw;
        }
    }

    public Task<ProcessResult> RunAsync(string fileName, int timeoutMs = 15000, CancellationToken cancellationToken = default, params string[] arguments)
    {
        return RunAsync(fileName, arguments, timeoutMs, cancellationToken);
    }

    private static async Task TerminateProcessTreeAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask);
        }
        catch
        {
        }
    }
}
