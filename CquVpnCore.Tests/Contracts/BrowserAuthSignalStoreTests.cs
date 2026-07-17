using CquVpnCore.Contracts;
using Xunit;

namespace CquVpnCore.Tests.Contracts;

public sealed class BrowserAuthSignalStoreTests
{
    [Fact]
    public async Task Persisted_signal_is_read_while_it_is_fresh()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
            var store = new BrowserAuthSignalStore(directory, timeProvider);

            await store.WriteAsync(BrowserAuthState.Authenticated, CancellationToken.None);
            var signal = store.ReadRecent(TimeSpan.FromSeconds(10));

            Assert.NotNull(signal);
            Assert.Equal(BrowserAuthState.Authenticated, signal.State);
            Assert.Equal(timeProvider.GetUtcNow(), signal.ReportedAtUtc);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Expired_signal_is_not_returned()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
            var store = new BrowserAuthSignalStore(directory, timeProvider);

            await store.WriteAsync(BrowserAuthState.AuthRequired, CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromSeconds(11));

            Assert.Null(store.ReadRecent(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CquVpnCore.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
