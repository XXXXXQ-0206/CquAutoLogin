using System;
using System.IO;
using CquAutoLogin.Services;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Constructor_places_settings_file_in_the_configured_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
        try
        {
            var service = new SettingsService(directory);

            Assert.Equal(Path.Combine(directory, "settings.json"), service.SettingsPath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_migrates_legacy_vpn_startup_setting_and_removes_legacy_key()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(directory);
            await File.WriteAllTextAsync(
                service.SettingsPath,
                """{"OpenATrustAtStartup":true}""");

            var settings = await service.LoadAsync();
            var persisted = await File.ReadAllTextAsync(service.SettingsPath);

            Assert.True(settings.OpenVpnPortalAtStartup);
            Assert.Contains("\"OpenVpnPortalAtStartup\"", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("\"OpenATrustAtStartup\"", persisted, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Theory]
    [InlineData("""{"OpenATrustAtStartup":true,"OpenVpnPortalAtStartup":false}""")]
    [InlineData("""{"OpenVpnPortalAtStartup":false,"OpenATrustAtStartup":true}""")]
    public async Task LoadAsync_prefers_new_vpn_startup_setting_regardless_of_json_key_order(string json)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(directory);
            await File.WriteAllTextAsync(service.SettingsPath, json);

            var settings = await service.LoadAsync();

            Assert.False(settings.OpenVpnPortalAtStartup);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
