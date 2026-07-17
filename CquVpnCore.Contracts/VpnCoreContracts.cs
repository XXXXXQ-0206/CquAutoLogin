using System.Text.Json;

namespace CquVpnCore.Contracts;

public enum BrowserAuthState
{
    Unknown,
    AuthRequired,
    Authenticated
}

public enum BrowserBridgeReportKind
{
    PortalState,
    BridgeReady
}

public sealed record BrowserAuthSignal(
    BrowserAuthState State,
    DateTimeOffset ReportedAtUtc = default,
    BrowserBridgeReportKind Kind = BrowserBridgeReportKind.PortalState);

public sealed class BrowserAuthSignalStore
{
    private const string StateFileName = "browser-auth-state.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider;

    public BrowserAuthSignalStore(string? directoryPath = null, TimeProvider? timeProvider = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CquAutoLogin",
            "BrowserBridge");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string DirectoryPath { get; }

    public string StatePath => Path.Combine(DirectoryPath, StateFileName);

    public Task WriteAsync(BrowserAuthState state, CancellationToken cancellationToken)
    {
        return WriteAsync(state, BrowserBridgeReportKind.PortalState, cancellationToken);
    }

    public Task WriteBridgeReadyAsync(CancellationToken cancellationToken)
    {
        return WriteAsync(BrowserAuthState.Unknown, BrowserBridgeReportKind.BridgeReady, cancellationToken);
    }

    private async Task WriteAsync(
        BrowserAuthState state,
        BrowserBridgeReportKind kind,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(state) || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException();
        }

        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = Path.Combine(DirectoryPath, $".{StateFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var signal = new BrowserAuthSignal(state, _timeProvider.GetUtcNow(), kind);
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(signal, SerializerOptions),
                cancellationToken);
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public BrowserAuthSignal? ReadRecent(TimeSpan maximumAge)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        try
        {
            var signal = JsonSerializer.Deserialize<BrowserAuthSignal>(
                File.ReadAllText(StatePath),
                SerializerOptions);
            if (signal is null || !Enum.IsDefined(signal.State) || !Enum.IsDefined(signal.Kind))
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            if (signal.ReportedAtUtc > now + TimeSpan.FromMinutes(1) ||
                now - signal.ReportedAtUtc > maximumAge)
            {
                return null;
            }

            return signal;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public enum VpnCoreState
{
    Stopped,
    AwaitingBrowserLogin,
    BrowserLoginComplete
}

public enum VpnCoreCommand
{
    GetStatus,
    BeginBrowserLogin,
    ReportBrowserAuth,
    Stop
}

public sealed record VpnCoreRequest(
    int ProtocolVersion,
    VpnCoreCommand Command,
    BrowserAuthSignal? BrowserAuth = null)
{
    public const int CurrentProtocolVersion = 1;

    public static VpnCoreRequest GetStatus() => new(CurrentProtocolVersion, VpnCoreCommand.GetStatus);

    public static VpnCoreRequest BeginBrowserLogin() => new(CurrentProtocolVersion, VpnCoreCommand.BeginBrowserLogin);

    public static VpnCoreRequest ReportBrowserAuth(BrowserAuthState state) =>
        new(CurrentProtocolVersion, VpnCoreCommand.ReportBrowserAuth, new BrowserAuthSignal(state));

    public static VpnCoreRequest Stop() => new(CurrentProtocolVersion, VpnCoreCommand.Stop);
}

public sealed record VpnCoreStatus(
    VpnCoreState State,
    Guid OperationId,
    DateTimeOffset UpdatedAtUtc,
    string Detail,
    string? ErrorCode = null);
