using System.Diagnostics;
using CquVpnCore.Browser;
using CquVpnCore.Host;
using CquVpnCore.Ipc;
using CquVpnCore.Portal;
using CquVpnCore.State;

namespace CquVpnCore;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length == 1 &&
            string.Equals(arguments[0], "--native-messaging", StringComparison.Ordinal))
        {
            return await NativeMessagingHost.RunAsync(CancellationToken.None);
        }

        if (!CquVpnCoreHostOptions.TryParse(arguments, out var options))
        {
            return 2;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        await using var server = new NamedPipeVpnCoreServer(
            options.PipeName,
            new VpnCoreStateMachine(TimeProvider.System),
            new ShellBrowserPortalLauncher());
        server.Start();

        await WaitForParentExitAsync(options.ParentProcessId, cancellationTokenSource.Token);
        return 0;
    }

    private static async Task WaitForParentExitAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsProcessRunning(parentProcessId))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
