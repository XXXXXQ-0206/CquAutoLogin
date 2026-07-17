using CquAutoLogin.Models;
using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void Monitor_snapshot_uses_the_vpn_state_published_by_the_monitor()
    {
        var state = new MonitorState
        {
            VpnState = "已连接",
            IsVpnInstalled = true,
            IsVpnConnected = true
        };

        var snapshot = TrayIconService.MonitorSnapshot.From(state, settings: null);

        Assert.Equal("已连接", snapshot.VpnState);
        Assert.True(snapshot.IsVpnInstalled);
        Assert.True(snapshot.IsVpnConnected);
    }
}
