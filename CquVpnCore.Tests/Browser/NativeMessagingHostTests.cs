using System.Buffers.Binary;
using System.Text;
using CquVpnCore.Browser;
using CquVpnCore.Contracts;
using Xunit;

namespace CquVpnCore.Tests.Browser;

public sealed class NativeMessagingHostTests
{
    [Fact]
    public async Task Bridge_ready_message_is_persisted_without_an_authentication_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CquVpnCore.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new BrowserAuthSignalStore(directory);
            await using var input = new MemoryStream(CreateMessage("{\"type\":\"browser-bridge-ready\"}"));
            await using var output = new MemoryStream();

            var exitCode = await NativeMessagingHost.RunAsync(input, output, store, CancellationToken.None);

            Assert.Equal(0, exitCode);
            var signal = store.ReadRecent(TimeSpan.FromMinutes(1));
            Assert.NotNull(signal);
            Assert.Equal(BrowserBridgeReportKind.BridgeReady, signal.Kind);
            Assert.Equal(BrowserAuthState.Unknown, signal.State);
            Assert.Equal("{\"accepted\":true}", ReadResponse(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateMessage(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var message = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message, payload.Length);
        payload.CopyTo(message, sizeof(int));
        return message;
    }

    private static string ReadResponse(Stream output)
    {
        output.Position = 0;
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        Assert.Equal(sizeof(int), output.Read(lengthBuffer));
        var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer)];
        Assert.Equal(payload.Length, output.Read(payload));
        return Encoding.UTF8.GetString(payload);
    }
}
