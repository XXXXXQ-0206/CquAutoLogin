using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CquAutoLogin.Services;

public sealed class BrowserBridgeSetupLauncher
{
    private readonly Action<ProcessStartInfo> _start;

    public BrowserBridgeSetupLauncher(Action<ProcessStartInfo>? start = null)
    {
        _start = start ?? (static info =>
        {
            Process.Start(info);
        });
    }

    public void Open(string bridgeDirectory)
    {
        var directory = Path.GetFullPath(bridgeDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"The browser bridge directory was not found: {directory}");
        }

        Start(directory);
        try
        {
            Start("chrome://extensions");
        }
        catch (Win32Exception)
        {
            Start("edge://extensions");
        }
    }

    private void Start(string target)
    {
        _start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }
}
