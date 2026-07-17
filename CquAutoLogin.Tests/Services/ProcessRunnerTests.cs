using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_timeout_kills_local_command_child_tree()
    {
        var runner = new ProcessRunner();
        var stopwatch = Stopwatch.StartNew();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}.cmd");
        var childProcessIdPath = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\n" +
                $"powershell.exe -NoProfile -Command \"$PID | Out-File -FilePath '{childProcessIdPath}' -Encoding ascii -NoNewline; Start-Sleep -Seconds 10\"\r\n");

            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                new[] { "/c", scriptPath },
                timeoutMs: 1000,
                default));
            stopwatch.Stop();

            var childProcessId = int.Parse(await File.ReadAllTextAsync(childProcessIdPath));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(childProcessId));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }

            if (File.Exists(childProcessIdPath))
            {
                File.Delete(childProcessIdPath);
            }
        }
    }

    [Fact]
    public async Task RunAsync_cancellation_kills_local_command_child_tree()
    {
        var runner = new ProcessRunner();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}.cmd");
        var childProcessIdPath = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\n" +
                $"powershell.exe -NoProfile -Command \"$PID | Out-File -FilePath '{childProcessIdPath}' -Encoding ascii -NoNewline; Start-Sleep -Seconds 10\"\r\n");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                new[] { "/c", scriptPath },
                timeoutMs: 15000,
                cancellation.Token));

            var childProcessId = int.Parse(await File.ReadAllTextAsync(childProcessIdPath));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(childProcessId));
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }

            if (File.Exists(childProcessIdPath))
            {
                File.Delete(childProcessIdPath);
            }
        }
    }
}
