using CquVpnCore.Contracts;
using CquVpnCore.State;
using Xunit;

namespace CquVpnCore.Tests.State;

public sealed class VpnCoreStateMachineTests
{
    [Fact]
    public void ConfirmBrowserLogin_requires_waiting_state()
    {
        var machine = new VpnCoreStateMachine(TimeProvider.System);

        Assert.Throws<VpnCoreTransitionException>(() => machine.ConfirmBrowserLogin());
    }

    [Fact]
    public void Browser_login_flow_requires_an_explicit_confirmation()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
        var machine = new VpnCoreStateMachine(timeProvider);

        var waiting = machine.BeginBrowserLogin();
        var confirmed = machine.ConfirmBrowserLogin();

        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, waiting.State);
        Assert.Equal(VpnCoreState.BrowserLoginComplete, confirmed.State);
        Assert.Equal(waiting.OperationId, confirmed.OperationId);
        Assert.Equal(timeProvider.GetUtcNow(), confirmed.UpdatedAtUtc);
    }

    [Fact]
    public void Stop_is_legal_from_every_c0_c1_state()
    {
        var machine = new VpnCoreStateMachine(TimeProvider.System);

        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
        machine.BeginBrowserLogin();
        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
        machine.BeginBrowserLogin();
        machine.ConfirmBrowserLogin();
        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
