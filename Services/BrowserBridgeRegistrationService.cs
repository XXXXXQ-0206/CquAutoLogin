using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CquAutoLogin.Services;

public sealed class BrowserBridgeRegistrationService
{
    public const string NativeHostName = "com.cquautologin.browser_bridge";
    public const string ExtensionId = "jabeingaddefnjelglhdiofkcloickjb";

    private readonly string _dataDirectory;

    public BrowserBridgeRegistrationService(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        }

        _dataDirectory = dataDirectory;
    }

    public string Register(string hostExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(hostExecutablePath))
        {
            throw new ArgumentException("A native host executable path is required.", nameof(hostExecutablePath));
        }

        var executablePath = Path.GetFullPath(hostExecutablePath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The browser bridge executable was not found.", executablePath);
        }

        var bridgeDirectory = Path.Combine(_dataDirectory, "BrowserBridge");
        Directory.CreateDirectory(bridgeDirectory);
        var manifestPath = Path.Combine(bridgeDirectory, $"{NativeHostName}.json");
        var manifest = new
        {
            name = NativeHostName,
            description = "CquAutoLogin visible browser authentication bridge",
            path = executablePath,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" }
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        RegisterHostManifest(
            Registry.CurrentUser,
            @"Software\Google\Chrome\NativeMessagingHosts\" + NativeHostName,
            manifestPath);
        RegisterHostManifest(
            Registry.CurrentUser,
            @"Software\Microsoft\Edge\NativeMessagingHosts\" + NativeHostName,
            manifestPath);

        return manifestPath;
    }

    private static void RegisterHostManifest(RegistryKey root, string subKeyPath, string manifestPath)
    {
        using var key = root.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not register browser bridge key '{subKeyPath}'.");
        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
    }
}
