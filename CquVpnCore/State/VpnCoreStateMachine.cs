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
            "Awaiting explicit browser-login confirmation.");
        return _status;
    }

    public VpnCoreStatus ConfirmBrowserLogin()
    {
        EnsureState(
            VpnCoreState.AwaitingBrowserLogin,
            "Browser-login confirmation is only valid while waiting for it.");
        _status = CreateStatus(
            VpnCoreState.BrowserLoginComplete,
            _status.OperationId,
            "Browser login confirmed by the user. No VPN tunnel has been established.");
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
