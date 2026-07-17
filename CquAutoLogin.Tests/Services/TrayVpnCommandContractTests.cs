using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class TrayVpnCommandContractTests
{
    [Fact]
    public void Tray_command_contract_uses_vendor_neutral_vpn_identifiers()
    {
        Assert.True(Enum.IsDefined(TrayMenuCommand.ConnectVpn));
        Assert.True(Enum.IsDefined(TrayMenuCommand.OpenBrowserBridgeFolder));
        Assert.True(Enum.IsDefined(TraySettingToggle.OpenVpnPortalAtStartup));
        Assert.False(Enum.TryParse<TrayMenuCommand>("ConfirmVpnBrowserLogin", out _));
        Assert.False(Enum.TryParse<TrayMenuCommand>("OpenATrust", out _));
        Assert.False(Enum.TryParse<TrayMenuCommand>("ExitATrust", out _));
        Assert.False(Enum.TryParse<TraySettingToggle>("OpenATrustAtStartup", out _));
    }

    [Fact]
    public void Browser_bridge_asset_reports_readiness_without_page_access()
    {
        var assetPath = FindBrowserBridgeAssetPath();
        var script = File.ReadAllText(assetPath);

        Assert.Contains("browser-bridge-ready", script, StringComparison.Ordinal);
        Assert.Contains("chrome.runtime.onStartup.addListener", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cookies", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_bridge_tray_action_uses_the_setup_launcher()
    {
        Assert.Contains("new BrowserBridgeSetupLauncher().Open(directory)", FindSourceFile("App.xaml.cs"));
        Assert.Contains("设置浏览器桥接", FindSourceFile(Path.Combine("Services", "TrayIconService.cs")));
    }

    private static string FindBrowserBridgeAssetPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var assetPath = Path.Combine(directory.FullName, "Assets", "BrowserBridge", "background.js");
            if (File.Exists(assetPath))
            {
                return assetPath;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the browser bridge background script.");
    }

    private static string FindSourceFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidatePath = Path.Combine(new[] { directory.FullName }.Concat(relativePath).ToArray());
            if (File.Exists(candidatePath))
            {
                return File.ReadAllText(candidatePath);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate source file '{Path.Combine(relativePath)}'.");
    }
}
