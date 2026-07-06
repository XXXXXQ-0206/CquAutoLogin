using System.IO;
using System.Text.Json;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class SettingsService
{
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

        AppSettings? settings;
        {
            await using var stream = File.OpenRead(SettingsPath);
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
        }

        settings ??= new AppSettings();

        if (NormalizeSettings(settings))
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
}
