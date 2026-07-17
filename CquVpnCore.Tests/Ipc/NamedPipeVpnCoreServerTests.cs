using CquVpnCore.Contracts;
using CquVpnCore.Ipc;
using CquVpnCore.Portal;
using CquVpnCore.State;
using Xunit;

namespace CquVpnCore.Tests.Ipc;

public sealed class NamedPipeVpnCoreServerTests
{
    [Fact]
    public async Task BeginBrowserLogin_returns_waiting_status_and_opens_only_the_portal()
    {
        var launcher = new RecordingPortalLauncher();
        await using var server = new NamedPipeVpnCoreServer(
            CreatePipeName(),
            new VpnCoreStateMachine(TimeProvider.System),
            launcher);
        server.Start();
        var client = new NamedPipeVpnCoreClient(server.PipeName);

        var status = await client.SendAsync(VpnCoreRequest.BeginBrowserLogin(), CancellationToken.None);

        Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
        Assert.Null(status.ErrorCode);
        Assert.Equal(new Uri("https://atrust.cqu.edu.cn/portal/"), Assert.Single(launcher.OpenedUris));
    }

    [Fact]
    public async Task Unsupported_protocol_returns_a_non_sensitive_error_class()
    {
        await using var server = new NamedPipeVpnCoreServer(
            CreatePipeName(),
            new VpnCoreStateMachine(TimeProvider.System),
            new RecordingPortalLauncher());
        server.Start();
        var client = new NamedPipeVpnCoreClient(server.PipeName);

        var status = await client.SendAsync(
            new VpnCoreRequest(VpnCoreRequest.CurrentProtocolVersion + 1, VpnCoreCommand.GetStatus),
            CancellationToken.None);

        Assert.Equal(VpnCoreState.Stopped, status.State);
        Assert.Equal("UnsupportedProtocol", status.ErrorCode);
        Assert.DoesNotContain("token", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BeginBrowserLogin_is_idempotent_after_automatic_authentication()
    {
        var launcher = new RecordingPortalLauncher();
        await using var server = new NamedPipeVpnCoreServer(
            CreatePipeName(),
            new VpnCoreStateMachine(TimeProvider.System),
            launcher);
        server.Start();
        var client = new NamedPipeVpnCoreClient(server.PipeName);

        await client.SendAsync(VpnCoreRequest.BeginBrowserLogin(), CancellationToken.None);
        var detected = await client.SendAsync(
            VpnCoreRequest.ReportBrowserAuth(BrowserAuthState.Authenticated),
            CancellationToken.None);
        var repeated = await client.SendAsync(VpnCoreRequest.BeginBrowserLogin(), CancellationToken.None);

        Assert.Equal(VpnCoreState.BrowserLoginComplete, detected.State);
        Assert.Equal(VpnCoreState.BrowserLoginComplete, repeated.State);
        Assert.Null(repeated.ErrorCode);
        Assert.Single(launcher.OpenedUris);
    }

    private static string CreatePipeName() => $"CquVpnCore.Tests.{Guid.NewGuid():N}";

    private sealed class RecordingPortalLauncher : IBrowserPortalLauncher
    {
        public List<Uri> OpenedUris { get; } = [];

        public void Launch(Uri portalUri)
        {
            OpenedUris.Add(portalUri);
        }
    }
}
