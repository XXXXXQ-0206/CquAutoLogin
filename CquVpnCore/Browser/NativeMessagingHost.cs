using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CquVpnCore.Contracts;

namespace CquVpnCore.Browser;

public static class NativeMessagingHost
{
    private const int MaxMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await RunAsync(input, output, new BrowserAuthSignalStore(), cancellationToken);
    }

    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        BrowserAuthSignalStore signalStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(signalStore);
        var lengthBuffer = new byte[sizeof(int)];

        while (!cancellationToken.IsCancellationRequested)
        {
            var firstRead = await input.ReadAsync(lengthBuffer, cancellationToken);
            if (firstRead == 0)
            {
                return 0;
            }

            if (!await ReadExactlyAsync(input, lengthBuffer.AsMemory(firstRead), cancellationToken))
            {
                return 2;
            }

            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (messageLength <= 0 || messageLength > MaxMessageBytes)
            {
                return 2;
            }

            var payload = ArrayPool<byte>.Shared.Rent(messageLength);
            try
            {
                if (!await ReadExactlyAsync(input, payload.AsMemory(0, messageLength), cancellationToken) ||
                    !TryParseBrowserBridgeReport(
                        payload.AsSpan(0, messageLength),
                        out var reportKind,
                        out var state))
                {
                    return 2;
                }

                var result = await ForwardSignalAsync(signalStore, reportKind, state, cancellationToken);
                await WriteResponseAsync(output, result, cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }

        return 0;
    }

    private static async Task<bool> ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        while (!buffer.IsEmpty)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return false;
            }

            buffer = buffer[read..];
        }

        return true;
    }

    private static bool TryParseBrowserBridgeReport(
        ReadOnlySpan<byte> payload,
        out BrowserBridgeReportKind reportKind,
        out BrowserAuthState state)
    {
        reportKind = BrowserBridgeReportKind.PortalState;
        state = BrowserAuthState.Unknown;
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type))
            {
                return false;
            }

            if (string.Equals(type.GetString(), "browser-bridge-ready", StringComparison.Ordinal))
            {
                reportKind = BrowserBridgeReportKind.BridgeReady;
                return true;
            }

            return string.Equals(type.GetString(), "browser-auth-state", StringComparison.Ordinal) &&
                   root.TryGetProperty("state", out var stateValue) &&
                   Enum.TryParse(stateValue.GetString(), ignoreCase: true, out state) &&
                   Enum.IsDefined(state);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<ForwardResult> ForwardSignalAsync(
        BrowserAuthSignalStore signalStore,
        BrowserBridgeReportKind reportKind,
        BrowserAuthState state,
        CancellationToken cancellationToken)
    {
        try
        {
            if (reportKind == BrowserBridgeReportKind.BridgeReady)
            {
                await signalStore.WriteBridgeReadyAsync(cancellationToken);
            }
            else
            {
                await signalStore.WriteAsync(state, cancellationToken);
            }

            return ForwardResult.Accepted;
        }
        catch (TimeoutException)
        {
            return ForwardResult.StateStoreUnavailable;
        }
        catch (IOException)
        {
            return ForwardResult.StateStoreUnavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return ForwardResult.StateStoreUnavailable;
        }
    }

    private static async Task WriteResponseAsync(
        Stream output,
        ForwardResult result,
        CancellationToken cancellationToken)
    {
        var response = Encoding.UTF8.GetBytes(
            result == ForwardResult.Accepted
                ? "{\"accepted\":true}"
                : $"{{\"accepted\":false,\"errorCode\":\"{result}\"}}");
        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, response.Length);
        await output.WriteAsync(lengthBuffer, cancellationToken);
        await output.WriteAsync(response, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private enum ForwardResult
    {
        Accepted,
        StateStoreUnavailable
    }
}
