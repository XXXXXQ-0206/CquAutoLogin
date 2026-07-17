using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CquVpnCore.Contracts;
using CquVpnCore.Portal;
using CquVpnCore.State;

namespace CquVpnCore.Ipc;

public sealed class NamedPipeVpnCoreServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly VpnCoreStateMachine _stateMachine;
    private readonly IBrowserPortalLauncher _portalLauncher;
    private Task? _runTask;

    public NamedPipeVpnCoreServer(
        string pipeName,
        VpnCoreStateMachine stateMachine,
        IBrowserPortalLauncher portalLauncher)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A pipe name is required.", nameof(pipeName));
        }

        PipeName = pipeName;
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _portalLauncher = portalLauncher ?? throw new ArgumentNullException(nameof(portalLauncher));
    }

    public string PipeName { get; }

    public void Start()
    {
        _runTask ??= Task.Run(() => RunAsync(_cancellationTokenSource.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        var status = ProcessRequest(requestLine);
        await writer.WriteLineAsync(JsonSerializer.Serialize(status, SerializerOptions));
    }

    private VpnCoreStatus ProcessRequest(string? requestLine)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return CreateError("InvalidRequest", "The IPC request was invalid.");
        }

        try
        {
            var request = JsonSerializer.Deserialize<VpnCoreRequest>(requestLine, SerializerOptions);
            if (request is null)
            {
                return CreateError("InvalidRequest", "The IPC request was invalid.");
            }

            if (request.ProtocolVersion != VpnCoreRequest.CurrentProtocolVersion)
            {
                return CreateError("UnsupportedProtocol", "The IPC protocol version is not supported.");
            }

            return request.Command switch
            {
                VpnCoreCommand.GetStatus => _stateMachine.GetStatus(),
                VpnCoreCommand.BeginBrowserLogin => BeginBrowserLogin(),
                VpnCoreCommand.ReportBrowserAuth => ReportBrowserAuth(request),
                VpnCoreCommand.Stop => _stateMachine.Stop(),
                _ => CreateError("UnsupportedCommand", "The IPC command is not supported.")
            };
        }
        catch (JsonException)
        {
            return CreateError("InvalidRequest", "The IPC request was invalid.");
        }
        catch (VpnCoreTransitionException)
        {
            return CreateError("InvalidTransition", "The requested state transition is not allowed.");
        }
    }

    private VpnCoreStatus BeginBrowserLogin()
    {
        var currentStatus = _stateMachine.GetStatus();
        if (currentStatus.State != VpnCoreState.Stopped)
        {
            return currentStatus;
        }

        try
        {
            _portalLauncher.Launch(ShellBrowserPortalLauncher.PortalUri);
            return _stateMachine.BeginBrowserLogin();
        }
        catch (VpnCoreTransitionException)
        {
            throw;
        }
        catch
        {
            return CreateError("BrowserLaunchFailed", "The browser portal could not be opened.");
        }
    }

    private VpnCoreStatus ReportBrowserAuth(VpnCoreRequest request)
    {
        if (request.BrowserAuth is null)
        {
            return CreateError("InvalidRequest", "The browser-authentication signal was invalid.");
        }

        return _stateMachine.ReportBrowserAuth(request.BrowserAuth.State);
    }

    private VpnCoreStatus CreateError(string errorCode, string detail)
    {
        return _stateMachine.GetStatus() with
        {
            Detail = detail,
            ErrorCode = errorCode
        };
    }
}
