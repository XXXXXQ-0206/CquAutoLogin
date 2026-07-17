using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CquVpnCore.Contracts;

namespace CquVpnCore.Ipc;

public sealed class NamedPipeVpnCoreClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public NamedPipeVpnCoreClient(string pipeName, TimeSpan? connectTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A pipe name is required.", nameof(pipeName));
        }

        _pipeName = pipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<VpnCoreStatus> SendAsync(VpnCoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, cancellationToken);

        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions));
        var response = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new IOException("The VPN core returned an empty IPC response.");
        }

        return JsonSerializer.Deserialize<VpnCoreStatus>(response, SerializerOptions)
            ?? throw new IOException("The VPN core returned an invalid IPC response.");
    }
}
