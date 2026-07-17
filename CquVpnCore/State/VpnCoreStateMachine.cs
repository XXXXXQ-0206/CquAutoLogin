using CquVpnCore.Contracts;

namespace CquVpnCore.State;

public sealed class VpnCoreStateMachine
{
    private readonly TimeProvider _timeProvider;
    private VpnCoreStatus _status;

    public VpnCoreStateMachine(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _status = CreateStatus(VpnCoreState.Stopped, Guid.Empty, "Stopped");
    }

    public VpnCoreStatus GetStatus() => _status;

    public VpnCoreStatus BeginBrowserLogin()
    {
        EnsureState(VpnCoreState.Stopped, "Browser login can only begin while the core is stopped.");
        _status = CreateStatus(
            VpnCoreState.AwaitingBrowserLogin,
            Guid.NewGuid(),
            "Waiting for automatic browser-authentication detection.");
        return _status;
    }

    public VpnCoreStatus ReportBrowserAuth(BrowserAuthState browserAuthState)
    {
        if (_status.State == VpnCoreState.Stopped)
        {
            if (browserAuthState != BrowserAuthState.Authenticated)
            {
                return _status;
            }

            _status = CreateStatus(
                VpnCoreState.BrowserLoginComplete,
                Guid.NewGuid(),
                "Browser authentication detected automatically. No VPN tunnel has been established.");
            return _status;
        }

        var nextState = browserAuthState == BrowserAuthState.Authenticated
            ? VpnCoreState.BrowserLoginComplete
            : VpnCoreState.AwaitingBrowserLogin;
        var detail = browserAuthState switch
        {
            BrowserAuthState.Authenticated =>
                "Browser authentication detected automatically. No VPN tunnel has been established.",
            BrowserAuthState.AuthRequired => "The browser portal is waiting for authentication.",
            _ => "Waiting for the browser bridge to report the portal state."
        };

        _status = CreateStatus(nextState, _status.OperationId, detail);
        return _status;
    }

    public VpnCoreStatus Stop()
    {
        _status = CreateStatus(VpnCoreState.Stopped, Guid.Empty, "Stopped");
        return _status;
    }

    private VpnCoreStatus CreateStatus(VpnCoreState state, Guid operationId, string detail)
    {
        return new VpnCoreStatus(state, operationId, _timeProvider.GetUtcNow(), detail);
    }

    private void EnsureState(VpnCoreState expectedState, string message)
    {
        if (_status.State != expectedState)
        {
            throw new VpnCoreTransitionException(message);
        }
    }
}
