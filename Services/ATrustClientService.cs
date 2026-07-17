using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CquAutoLogin.Services;

public sealed record ATrustStatus(
    bool IsInstalled,
    bool IsServiceRunning,
    bool IsVirtualAdapterUp,
    bool IsConnected,
    string AgentStatus)
{
    public static ATrustStatus NotDetected { get; } = new(false, false, false, false, string.Empty);

    public string DisplayText => !IsInstalled
        ? "未检测到官方客户端"
        : !IsServiceRunning
            ? "客户端服务未运行"
            : IsConnected
                ? "已连接"
                : IsVirtualAdapterUp
                    ? "隧道已建立，正在确认"
                    : DescribeAgentStatus();

    private string DescribeAgentStatus()
    {
        return AgentStatus.ToLowerInvariant() switch
        {
            "logout" => "未登录",
            "offline" => "已断开",
            "starting" => "正在启动",
            "connecting" => "正在连接",
            _ => "客户端已启动"
        };
    }
}

public sealed record ATrustActionResult(bool Success, string Message);

public sealed class ATrustClientService
{
    private const string ServiceName = "aTrustService";
    private const string VirtualAdapterKeyword = "Sangfor aTrust VNIC";
    private const string AgentPortFileName = "httpserver";
    private readonly ProcessRunner _processRunner;

    public ATrustClientService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<ATrustStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var installation = TryResolveInstallation();
        if (installation is null)
        {
            return ATrustStatus.NotDetected;
        }

        var serviceRunning = await IsServiceRunningAsync(cancellationToken);
        var virtualAdapterUp = IsVirtualAdapterUp();
        if (!serviceRunning)
        {
            return new ATrustStatus(true, false, virtualAdapterUp, false, string.Empty);
        }

        var agentStatus = await QueryAgentStatusAsync(installation, cancellationToken);
        var connected = string.Equals(agentStatus, "online", StringComparison.OrdinalIgnoreCase);
        return new ATrustStatus(true, true, virtualAdapterUp, connected, agentStatus ?? string.Empty);
    }

    public ATrustActionResult OpenInteractive()
    {
        var installation = TryResolveInstallation();
        if (installation is null)
        {
            return new ATrustActionResult(false, "未检测到 ATrust 官方客户端。");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installation.TrayPath,
                Arguments = "-- -s browserstart",
                UseShellExecute = true
            });
            return new ATrustActionResult(true, "已打开 ATrust 官方认证窗口。");
        }
        catch (Exception ex)
        {
            return new ATrustActionResult(false, $"无法打开 ATrust：{ex.Message}");
        }
    }

    public ATrustActionResult ExitClient()
    {
        var installation = TryResolveInstallation();
        if (installation is null)
        {
            return new ATrustActionResult(false, "未检测到 ATrust 官方客户端。");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installation.RepairToolPath,
                Arguments = "--exit-client",
                UseShellExecute = true,
                Verb = "runas"
            });
            return new ATrustActionResult(true, "已请求 ATrust 完整退出，系统可能会要求管理员确认。");
        }
        catch (Exception ex)
        {
            return new ATrustActionResult(false, $"无法退出 ATrust：{ex.Message}");
        }
    }

    private async Task<bool> IsServiceRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var serviceControlPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
            var result = await _processRunner.RunAsync(
                serviceControlPath,
                new[] { "query", ServiceName },
                timeoutMs: 5000,
                cancellationToken);
            return result.ExitCode == 0 &&
                   Regex.IsMatch(result.StandardOutput, @"STATE\s*:\s*4\b", RegexOptions.IgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVirtualAdapterUp()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Any(networkInterface =>
            networkInterface.OperationalStatus == OperationalStatus.Up &&
            networkInterface.Description.Contains(VirtualAdapterKeyword, StringComparison.OrdinalIgnoreCase));
    }

    private static ATrustInstallation? TryResolveInstallation()
    {
        var agentPath = GetServiceAgentPath();
        if (string.IsNullOrWhiteSpace(agentPath))
        {
            var fallbackRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Sangfor",
                "aTrust");
            agentPath = Path.Combine(fallbackRoot, "aTrustAgent", "aTrustAgent.exe");
        }

        if (!File.Exists(agentPath))
        {
            return null;
        }

        var agentDirectory = Path.GetDirectoryName(agentPath);
        if (string.IsNullOrWhiteSpace(agentDirectory))
        {
            return null;
        }

        var installationRoot = Directory.GetParent(agentDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(installationRoot))
        {
            return null;
        }

        var trayPath = Path.Combine(installationRoot, "aTrustTray", "aTrustTray.exe");
        var repairToolPath = Path.Combine(installationRoot, "aTrustAgent", "aTrustServRepair.exe");
        if (!File.Exists(trayPath) || !File.Exists(repairToolPath))
        {
            return null;
        }

        return new ATrustInstallation(agentDirectory, trayPath, repairToolPath);
    }

    private static string? GetServiceAgentPath()
    {
        try
        {
            using var serviceKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}",
                writable: false);
            var imagePath = serviceKey?.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var expanded = Environment.ExpandEnvironmentVariables(imagePath);
            var quotedPath = Regex.Match(expanded, "^\\s*\\\"(?<path>[^\\\"]+)\\\"");
            return quotedPath.Success ? quotedPath.Groups["path"].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> QueryAgentStatusAsync(
        ATrustInstallation installation,
        CancellationToken cancellationToken)
    {
        if (!TryReadAgentPort(installation.AgentDirectory, out var port))
        {
            return null;
        }

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is { IsLoopback: true } uri &&
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                uri.Port == port
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{port}/v1/service/status")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "cs",
                    lang = "zh-CN",
                    guid = string.Empty,
                    addr = string.Empty,
                    token = string.Empty,
                    sdpTraceId = Guid.NewGuid().ToString("N"),
                    data = new Dictionary<string, string>()
                }),
                Encoding.UTF8,
                "application/json")
        };

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("status", out var status))
            {
                return null;
            }

            return status.GetString();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadAgentPort(string agentDirectory, out int port)
    {
        port = 0;
        try
        {
            var portFilePath = Path.Combine(agentDirectory, "var", "run", AgentPortFileName);
            return int.TryParse(File.ReadAllText(portFilePath).Trim(), out port) &&
                   port is > 0 and <= 65535;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ATrustInstallation(string AgentDirectory, string TrayPath, string RepairToolPath);
}
