using CquVpnCore.Contracts;

namespace CquAutoLogin.Services;

public sealed class BrowserAuthSignalPoller : IAsyncDisposable
{
    private readonly BrowserAuthSignalStore _store;
    private readonly Func<BrowserAuthSignal, Task> _signalHandler;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maximumSignalAge;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _runTask;
    private DateTimeOffset _lastReportedAtUtc;

    public BrowserAuthSignalPoller(
        BrowserAuthSignalStore store,
        Func<BrowserAuthSignal, Task> signalHandler,
        TimeSpan? pollInterval = null,
        TimeSpan? maximumSignalAge = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _signalHandler = signalHandler ?? throw new ArgumentNullException(nameof(signalHandler));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _maximumSignalAge = maximumSignalAge ?? TimeSpan.FromSeconds(15);
        if (_pollInterval <= TimeSpan.Zero || _maximumSignalAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    public void Start()
    {
        _runTask ??= Task.Run(() => RunAsync(_cancellationTokenSource.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var signal = _store.ReadRecent(_maximumSignalAge);
            if (signal is not null && signal.ReportedAtUtc != _lastReportedAtUtc)
            {
                _lastReportedAtUtc = signal.ReportedAtUtc;
                await _signalHandler(signal);
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }
}
