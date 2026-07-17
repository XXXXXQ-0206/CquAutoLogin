using System.Diagnostics;
using System.IO;
using CquVpnCore.Contracts;
using CquVpnCore.Ipc;

namespace CquAutoLogin.Services;

public interface ICquVpnCoreHost
{
    Task EnsureStartedAsync(CancellationToken cancellationToken);
}

public interface ICquVpnCoreCommandClient
{
    Task<VpnCoreStatus> SendAsync(VpnCoreRequest request, CancellationToken cancellationToken);
}

public sealed record CquVpnDisplayStatus(string Text, bool IsCoreRunning, bool IsConnected);

public sealed class CquVpnCoreClient
{
    private readonly ICquVpnCoreHost _host;
    private readonly ICquVpnCoreCommandClient _commandClient;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public CquVpnCoreClient(
        ICquVpnCoreHost host,
        ICquVpnCoreCommandClient commandClient,
        int maxAttempts = 20,
        TimeSpan? retryDelay = null)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        _host = host ?? throw new ArgumentNullException(nameof(host));
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
    }

    public Task<VpnCoreStatus> BeginBrowserLoginAsync(CancellationToken cancellationToken)
    {
        return SendAfterStartupAsync(VpnCoreRequest.BeginBrowserLogin(), cancellationToken);
    }

    public Task<VpnCoreStatus> ConfirmBrowserLoginAsync(CancellationToken cancellationToken)
    {
        return SendAfterStartupAsync(VpnCoreRequest.ConfirmBrowserLogin(), cancellationToken);
    }

    public Task<VpnCoreStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return SendAfterStartupAsync(VpnCoreRequest.GetStatus(), cancellationToken);
    }

    public Task<VpnCoreStatus> StopAsync(CancellationToken cancellationToken)
    {
        return SendAfterStartupAsync(VpnCoreRequest.Stop(), cancellationToken);
    }

    public static CquVpnDisplayStatus GetStoppedDisplayStatus()
    {
        return new CquVpnDisplayStatus("CquVpnCore 未启动", IsCoreRunning: false, IsConnected: false);
    }

    public static CquVpnDisplayStatus ToDisplayStatus(VpnCoreStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (!string.IsNullOrWhiteSpace(status.ErrorCode))
        {
            return new CquVpnDisplayStatus(
                $"CquVpnCore 错误：{status.ErrorCode}",
                status.State != VpnCoreState.Stopped,
                IsConnected: false);
        }

        return status.State switch
        {
            VpnCoreState.Stopped => GetStoppedDisplayStatus(),
            VpnCoreState.AwaitingBrowserLogin => new CquVpnDisplayStatus(
                "等待浏览器认证（实验阶段，尚未建立 VPN 隧道）",
                IsCoreRunning: true,
                IsConnected: false),
            VpnCoreState.BrowserLoginComplete => new CquVpnDisplayStatus(
                "浏览器认证已确认（等待后续连接能力）",
                IsCoreRunning: true,
                IsConnected: false),
            _ => new CquVpnDisplayStatus("CquVpnCore 状态未知", IsCoreRunning: false, IsConnected: false)
        };
    }

    private async Task<VpnCoreStatus> SendAfterStartupAsync(
        VpnCoreRequest request,
        CancellationToken cancellationToken)
    {
        await _host.EnsureStartedAsync(cancellationToken);

        IOException? lastPipeException = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return await _commandClient.SendAsync(request, cancellationToken);
            }
            catch (IOException exception) when (attempt < _maxAttempts)
            {
                lastPipeException = exception;
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }

        throw lastPipeException ?? new IOException("The VPN core pipe was unavailable.");
    }
}

public sealed class ProcessCquVpnCoreHost : ICquVpnCoreHost
{
    private readonly string _executablePath;
    private readonly string _pipeName;
    private readonly int _parentProcessId;
    private Process? _process;

    public ProcessCquVpnCoreHost(string executablePath, string pipeName, int parentProcessId)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("A core executable path is required.", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A pipe name is required.", nameof(pipeName));
        }

        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }

        _executablePath = executablePath;
        _pipeName = pipeName;
        _parentProcessId = parentProcessId;
    }

    public Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_process is { HasExited: false })
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException("CquVpnCore.exe was not found next to CquAutoLogin.", _executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(_pipeName);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(_parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("CquVpnCore.exe could not be started.");
        return Task.CompletedTask;
    }
}

public sealed class NamedPipeCquVpnCoreCommandClient(string pipeName) : ICquVpnCoreCommandClient
{
    private readonly NamedPipeVpnCoreClient _client = new(pipeName);

    public Task<VpnCoreStatus> SendAsync(VpnCoreRequest request, CancellationToken cancellationToken)
    {
        return _client.SendAsync(request, cancellationToken);
    }
}
