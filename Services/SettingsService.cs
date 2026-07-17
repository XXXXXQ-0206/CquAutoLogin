using System.IO;
using System.Text.Json;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class SettingsService
{
    private const string LegacyVpnStartupPropertyName = "Open" + "ATrustAtStartup";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(string dataDirectoryPath)
    {
        Directory.CreateDirectory(dataDirectoryPath);
        SettingsPath = Path.Combine(dataDirectoryPath, "settings.json");
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults);
            return defaults;
        }

        var json = await File.ReadAllTextAsync(SettingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

        settings ??= new AppSettings();

        var migratedLegacyVpnSetting = MigrateLegacyVpnStartupSetting(json, settings);
        if (NormalizeSettings(settings) || migratedLegacyVpnSetting)
        {
            await SaveAsync(settings);
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }

    private static bool NormalizeSettings(AppSettings settings)
    {
        if (string.Equals(settings.PortalBaseUrl, "https://login.cqu.edu.cn:802", StringComparison.OrdinalIgnoreCase))
        {
            settings.PortalBaseUrl = "http://login.cqu.edu.cn:801";
            return true;
        }

        if (string.IsNullOrWhiteSpace(settings.PortalBaseUrl))
        {
            settings.PortalBaseUrl = new AppSettings().PortalBaseUrl;
            return true;
        }

        return false;
    }

    private static bool MigrateLegacyVpnStartupSetting(string json, AppSettings settings)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasNewKey = false;
        var hasLegacyKey = false;
        bool? legacyValue = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals(nameof(AppSettings.OpenVpnPortalAtStartup)) ||
                property.Name.Equals(nameof(AppSettings.OpenVpnPortalAtStartup), StringComparison.OrdinalIgnoreCase))
            {
                hasNewKey = true;
            }

            if (property.Name.Equals(LegacyVpnStartupPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                hasLegacyKey = true;
                legacyValue = property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => legacyValue
                };
            }
        }

        if (!hasNewKey && legacyValue.HasValue)
        {
            settings.OpenVpnPortalAtStartup = legacyValue.Value;
        }

        return hasLegacyKey;
    }
}
