using System.Diagnostics;

namespace CquVpnCore.Portal;

public sealed class ShellBrowserPortalLauncher : IBrowserPortalLauncher
{
    public static readonly Uri PortalUri = new("https://atrust.cqu.edu.cn/portal/");

    public void Launch(Uri portalUri)
    {
        ArgumentNullException.ThrowIfNull(portalUri);
        if (portalUri != PortalUri)
        {
            throw new ArgumentException("Only the configured portal URI may be launched.", nameof(portalUri));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = PortalUri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
