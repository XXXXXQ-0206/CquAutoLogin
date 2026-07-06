using System.IO;
using Microsoft.Win32;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "CquAutoLogin";
    private const string ShortcutName = "CquAutoLogin.lnk";

    public void Apply(AppSettings settings)
    {
        var shortcutPath = GetStartupShortcutPath();

        if (!settings.LaunchAtStartup)
        {
            RemoveRegistryRunEntry();
            RemoveStartupShortcut(shortcutPath);
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return;
        }

        CreateOrUpdateStartupShortcut(shortcutPath, processPath, "--silent");
        RemoveRegistryRunEntry();
    }

    private static string GetStartupShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, ShortcutName);
    }

    private static void CreateOrUpdateStartupShortcut(string shortcutPath, string targetPath, string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup));

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
        shortcut.Description = "CquAutoLogin";
        shortcut.Save();
    }

    private static void RemoveStartupShortcut(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch
        {
        }
    }

    private static void RemoveRegistryRunEntry()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
        }

        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: true);
            approvedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}
