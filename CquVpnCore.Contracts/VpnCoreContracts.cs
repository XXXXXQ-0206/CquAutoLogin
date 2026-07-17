namespace CquVpnCore.Contracts;

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
    ConfirmBrowserLogin,
    Stop
}

public sealed record VpnCoreRequest(int ProtocolVersion, VpnCoreCommand Command)
{
    public const int CurrentProtocolVersion = 1;

    public static VpnCoreRequest GetStatus() => new(CurrentProtocolVersion, VpnCoreCommand.GetStatus);

    public static VpnCoreRequest BeginBrowserLogin() => new(CurrentProtocolVersion, VpnCoreCommand.BeginBrowserLogin);

    public static VpnCoreRequest ConfirmBrowserLogin() => new(CurrentProtocolVersion, VpnCoreCommand.ConfirmBrowserLogin);

    public static VpnCoreRequest Stop() => new(CurrentProtocolVersion, VpnCoreCommand.Stop);
}

public sealed record VpnCoreStatus(
    VpnCoreState State,
    Guid OperationId,
    DateTimeOffset UpdatedAtUtc,
    string Detail,
    string? ErrorCode = null);
