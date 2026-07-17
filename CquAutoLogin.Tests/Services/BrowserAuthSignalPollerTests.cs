using CquAutoLogin.Services;
using CquVpnCore.Contracts;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class BrowserAuthSignalPollerTests
{
    [Fact]
    public async Task Fresh_signal_is_forwarded_once()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new BrowserAuthSignalStore(directory);
            await store.WriteAsync(BrowserAuthState.Authenticated, CancellationToken.None);
            var received = new TaskCompletionSource<BrowserAuthSignal>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var poller = new BrowserAuthSignalPoller(
                store,
                signal =>
                {
                    received.TrySetResult(signal);
                    return Task.CompletedTask;
                },
                pollInterval: TimeSpan.FromMilliseconds(10));
            poller.Start();

            var signal = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(BrowserAuthState.Authenticated, signal.State);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
