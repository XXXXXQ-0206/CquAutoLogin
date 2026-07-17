using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class TrayVpnCommandContractTests
{
    [Fact]
    public void Tray_command_contract_uses_vendor_neutral_vpn_identifiers()
    {
        Assert.True(Enum.IsDefined(TrayMenuCommand.ConnectVpn));
        Assert.True(Enum.IsDefined(TraySettingToggle.OpenVpnPortalAtStartup));
        Assert.False(Enum.TryParse<TrayMenuCommand>("ConfirmVpnBrowserLogin", out _));
        Assert.False(Enum.TryParse<TrayMenuCommand>("OpenATrust", out _));
        Assert.False(Enum.TryParse<TrayMenuCommand>("ExitATrust", out _));
        Assert.False(Enum.TryParse<TraySettingToggle>("OpenATrustAtStartup", out _));
    }
}
