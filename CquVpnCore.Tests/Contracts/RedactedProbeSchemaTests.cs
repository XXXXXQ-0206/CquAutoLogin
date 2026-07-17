using CquVpnCore.Contracts;
using Xunit;

namespace CquVpnCore.Tests.Contracts;

public sealed class RedactedProbeSchemaTests
{
    [Fact]
    public void Serialize_rejects_sensitive_field_names()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RedactedProbeSerializer.Serialize(new { token = "secret" }));

        Assert.Contains("token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_allows_the_explicit_redacted_probe_schema()
    {
        var snapshot = new RedactedProbeSnapshot(
            new RedactedTopologySnapshot([new RedactedProcessRelation("core", "launcher")]),
            new RedactedEndpointSnapshot("controller", 443, "tls", "h2", TimeSpan.FromMilliseconds(120)),
            new RedactedFramingSnapshot("outbound", 128, "opaque"),
            new RedactedNetworkSnapshot(0, 0),
            BrowserLoginConfirmed: false);

        var serialized = RedactedProbeSerializer.Serialize(snapshot);

        Assert.Contains("browserLoginConfirmed", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
