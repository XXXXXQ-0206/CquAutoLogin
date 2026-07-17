using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace CquVpnCore.Contracts;

public sealed record RedactedProcessRelation(string ProcessName, string ParentProcessName);

public sealed record RedactedTopologySnapshot(IReadOnlyList<RedactedProcessRelation> ProcessRelations);

public sealed record RedactedEndpointSnapshot(
    string EndpointClass,
    int Port,
    string Protocol,
    string? Alpn,
    TimeSpan ConnectionDuration);

public sealed record RedactedFramingSnapshot(string Direction, int Length, string FramingClass);

public sealed record RedactedNetworkSnapshot(int RouteCount, int DnsServerCount);

public sealed record RedactedProbeSnapshot(
    RedactedTopologySnapshot Topology,
    RedactedEndpointSnapshot Endpoint,
    RedactedFramingSnapshot Framing,
    RedactedNetworkSnapshot Network,
    bool BrowserLoginDetected);

public static class RedactedProbeSerializer
{
    private static readonly string[] ForbiddenMemberNameFragments =
    [
        "password",
        "token",
        "cookie",
        "credential",
        "secret",
        "ticket",
        "authorization",
        "header",
        "payload",
        "commandline"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Validate(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    private static void Validate(object value, HashSet<object> visited)
    {
        var valueType = value.GetType();
        if (IsScalar(valueType))
        {
            return;
        }

        if (!valueType.IsValueType && !visited.Add(value))
        {
            return;
        }

        if (value is IDictionary)
        {
            throw new InvalidOperationException("Probe data cannot contain dictionary members.");
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    Validate(item, visited);
                }
            }

            return;
        }

        foreach (var property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            EnsureMemberNameIsAllowed(property.Name);
            var propertyValue = property.GetValue(value);
            if (propertyValue is not null)
            {
                Validate(propertyValue, visited);
            }
        }
    }

    private static bool IsScalar(Type valueType)
    {
        return valueType.IsPrimitive ||
            valueType.IsEnum ||
            valueType == typeof(string) ||
            valueType == typeof(decimal) ||
            valueType == typeof(DateTime) ||
            valueType == typeof(DateTimeOffset) ||
            valueType == typeof(TimeSpan) ||
            valueType == typeof(Guid);
    }

    private static void EnsureMemberNameIsAllowed(string memberName)
    {
        if (ForbiddenMemberNameFragments.Any(fragment =>
            memberName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Probe data member '{memberName}' is not permitted.");
        }
    }
}
