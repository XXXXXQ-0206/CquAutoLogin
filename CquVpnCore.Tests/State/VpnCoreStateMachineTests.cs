using CquVpnCore.Contracts;
using CquVpnCore.State;
using Xunit;

namespace CquVpnCore.Tests.State;

public sealed class VpnCoreStateMachineTests
{
    [Fact]
    public void Authenticated_browser_signal_starts_observation_when_core_is_stopped()
    {
        var machine = new VpnCoreStateMachine(TimeProvider.System);

        var status = machine.ReportBrowserAuth(BrowserAuthState.Authenticated);

        Assert.Equal(VpnCoreState.BrowserLoginComplete, status.State);
        Assert.NotEqual(Guid.Empty, status.OperationId);
    }

    [Fact]
    public void Browser_login_flow_completes_when_authenticated_signal_arrives()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
        var machine = new VpnCoreStateMachine(timeProvider);

        var waiting = machine.BeginBrowserLogin();
        var confirmed = machine.ReportBrowserAuth(BrowserAuthState.Authenticated);

        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, waiting.State);
        Assert.Equal(VpnCoreState.BrowserLoginComplete, confirmed.State);
        Assert.Equal(waiting.OperationId, confirmed.OperationId);
        Assert.Equal(timeProvider.GetUtcNow(), confirmed.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(BrowserAuthState.Unknown)]
    [InlineData(BrowserAuthState.AuthRequired)]
    public void Non_authenticated_browser_signals_keep_observation_waiting(BrowserAuthState browserAuthState)
    {
        var machine = new VpnCoreStateMachine(TimeProvider.System);

        machine.BeginBrowserLogin();
        var status = machine.ReportBrowserAuth(browserAuthState);

        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
        Assert.NotEqual(VpnCoreState.BrowserLoginComplete, status.State);
    }

    [Fact]
    public void Stop_is_legal_from_every_c0_c1_state()
    {
        var machine = new VpnCoreStateMachine(TimeProvider.System);

        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
        machine.BeginBrowserLogin();
        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
        machine.BeginBrowserLogin();
        machine.ReportBrowserAuth(BrowserAuthState.Authenticated);
        Assert.Equal(VpnCoreState.Stopped, machine.Stop().State);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
