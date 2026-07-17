using System.ComponentModel;
using System.Diagnostics;
using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class BrowserBridgeSetupLauncherTests
{
    [Fact]
    public void Open_launches_the_bridge_directory_then_the_Chrome_extensions_page()
    {
        var launched = new List<ProcessStartInfo>();
        var launcher = new BrowserBridgeSetupLauncher(launched.Add);
        var directory = CreateTemporaryDirectory();

        try
        {
            launcher.Open(directory);

            Assert.Collection(
                launched,
                item => Assert.Equal(Path.GetFullPath(directory), item.FileName),
                item => Assert.Equal("chrome://extensions", item.FileName));
            Assert.All(launched, item => Assert.True(item.UseShellExecute));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void Open_uses_the_Edge_extensions_page_when_Chrome_cannot_be_opened()
    {
        var launched = new List<ProcessStartInfo>();
        var launcher = new BrowserBridgeSetupLauncher(info =>
        {
            launched.Add(info);
            if (info.FileName == "chrome://extensions")
            {
                throw new Win32Exception("Chrome is unavailable.");
            }
        });
        var directory = CreateTemporaryDirectory();

        try
        {
            launcher.Open(directory);

            Assert.Equal(
                new[] { Path.GetFullPath(directory), "chrome://extensions", "edge://extensions" },
                launched.Select(item => item.FileName));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
