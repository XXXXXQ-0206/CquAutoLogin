using CquAutoLogin.Services;
using CquVpnCore.Contracts;
using Xunit;

namespace CquAutoLogin.Tests.Services;

public sealed class CquVpnCoreClientTests
{
    [Fact]
    public async Task BeginBrowserLogin_starts_host_and_returns_waiting_state()
    {
        var host = new RecordingCoreHost();
        var commandClient = new RecordingCommandClient(CreateStatus(VpnCoreState.AwaitingBrowserLogin));
        var client = new CquVpnCoreClient(host, commandClient);

        var status = await client.BeginBrowserLoginAsync(CancellationToken.None);

        Assert.True(host.Started);
        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
        Assert.Equal(VpnCoreCommand.BeginBrowserLogin, Assert.Single(commandClient.Commands).Command);
    }

    [Fact]
    public async Task ReportBrowserAuth_sends_an_automatic_signal_command()
    {
        var host = new RecordingCoreHost();
        var commandClient = new RecordingCommandClient(CreateStatus(VpnCoreState.BrowserLoginComplete));
        var client = new CquVpnCoreClient(host, commandClient);

        var status = await client.ReportBrowserAuthAsync(
            BrowserAuthState.Authenticated,
            CancellationToken.None);

        Assert.True(host.Started);
        Assert.Equal(VpnCoreState.BrowserLoginComplete, status.State);
        var request = Assert.Single(commandClient.Commands);
        Assert.Equal(VpnCoreCommand.ReportBrowserAuth, request.Command);
        Assert.Equal(BrowserAuthState.Authenticated, request.BrowserAuth?.State);
    }

    [Fact]
    public async Task BeginBrowserLogin_retries_until_the_new_core_listens()
    {
        var host = new RecordingCoreHost();
        var commandClient = new TransientFailureCommandClient(CreateStatus(VpnCoreState.AwaitingBrowserLogin));
        var client = new CquVpnCoreClient(host, commandClient, maxAttempts: 2, retryDelay: TimeSpan.Zero);

        var status = await client.BeginBrowserLoginAsync(CancellationToken.None);

        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
        Assert.Equal(2, commandClient.Attempts);
    }

    [Fact]
    public void Display_status_for_detected_browser_auth_does_not_claim_a_tunnel()
    {
        var display = CquVpnCoreClient.ToDisplayStatus(CreateStatus(VpnCoreState.BrowserLoginComplete));

        Assert.Equal("浏览器认证已检测到（等待后续连接能力）", display.Text);
        Assert.True(display.IsCoreRunning);
        Assert.False(display.IsConnected);
        Assert.DoesNotContain("已连接", display.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_status_for_recent_bridge_report_does_not_claim_a_tunnel()
    {
        var display = CquVpnCoreClient.GetStoppedDisplayStatus(browserBridgeReady: true);

        Assert.Equal("浏览器桥接已回执（等待认证页）", display.Text);
        Assert.False(display.IsCoreRunning);
        Assert.False(display.IsConnected);
        Assert.DoesNotContain("VPN 已连接", display.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_status_for_expired_bridge_report_does_not_claim_a_tunnel()
    {
        var display = CquVpnCoreClient.GetStoppedDisplayStatus(browserBridgeReportExpired: true);

        Assert.Equal("浏览器桥接上次报告已过期（打开认证页或重新加载扩展）", display.Text);
        Assert.False(display.IsCoreRunning);
        Assert.False(display.IsConnected);
        Assert.DoesNotContain("已连接", display.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Stopped_status_propagates_an_expired_bridge_report_without_claiming_a_tunnel()
    {
        var display = CquVpnCoreClient.ToDisplayStatus(
            CreateStatus(VpnCoreState.Stopped),
            browserBridgeReportExpired: true);

        Assert.Equal("浏览器桥接上次报告已过期（打开认证页或重新加载扩展）", display.Text);
        Assert.False(display.IsCoreRunning);
        Assert.False(display.IsConnected);
    }

    [Fact]
    public void Missing_bridge_report_is_not_attributed_to_a_stopped_core()
    {
        var display = CquVpnCoreClient.GetStoppedDisplayStatus();

        Assert.Equal("尚未收到浏览器桥接报告", display.Text);
        Assert.False(display.IsCoreRunning);
        Assert.False(display.IsConnected);
    }

    [Fact]
    public void Build_project_config_publishes_the_core_to_the_same_directory()
    {
        var projectPath = FindProjectPath();
        var project = File.ReadAllText(projectPath);

        Assert.Contains("Target Name=\"PublishCquVpnCoreHost\" AfterTargets=\"Publish\"", project, StringComparison.Ordinal);
        Assert.Contains("CquVpnCore\\CquVpnCore.csproj", project, StringComparison.Ordinal);
        Assert.Contains("PublishDir=$(PublishDir)", project, StringComparison.Ordinal);
    }

    private static string FindProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectPath = Path.Combine(directory.FullName, "CquAutoLogin.csproj");
            if (File.Exists(projectPath))
            {
                return projectPath;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CquAutoLogin.csproj.");
    }

    private static VpnCoreStatus CreateStatus(VpnCoreState state) =>
        new(state, Guid.NewGuid(), DateTimeOffset.UtcNow, "test");

    private sealed class RecordingCoreHost : ICquVpnCoreHost
    {
        public bool Started { get; private set; }

        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCommandClient(VpnCoreStatus status) : ICquVpnCoreCommandClient
    {
        public List<VpnCoreRequest> Commands { get; } = [];

        public Task<VpnCoreStatus> SendAsync(VpnCoreRequest request, CancellationToken cancellationToken)
        {
            Commands.Add(request);
            return Task.FromResult(status);
        }
    }

    private sealed class TransientFailureCommandClient(VpnCoreStatus status) : ICquVpnCoreCommandClient
    {
        public int Attempts { get; private set; }

        public Task<VpnCoreStatus> SendAsync(VpnCoreRequest request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Attempts == 1
                ? Task.FromException<VpnCoreStatus>(new IOException("Pipe is not ready."))
                : Task.FromResult(status);
        }
    }
}
