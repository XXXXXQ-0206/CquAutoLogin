using CquVpnCore.Host;
using Xunit;

namespace CquVpnCore.Tests.Host;

public sealed class CquVpnCoreHostOptionsTests
{
    [Fact]
    public void TryParse_accepts_a_pipe_name_and_parent_process_id()
    {
        var parsed = CquVpnCoreHostOptions.TryParse(
            ["--pipe", "CquVpnCore.42", "--parent-pid", "42"],
            out var options);

        Assert.True(parsed);
        Assert.Equal("CquVpnCore.42", options.PipeName);
        Assert.Equal(42, options.ParentProcessId);
    }

    [Fact]
    public void TryParse_rejects_incomplete_or_invalid_arguments()
    {
        var invalidArgumentSets = new[]
        {
            new[] { "--pipe", "CquVpnCore.42" },
            new[] { "--pipe", "", "--parent-pid", "42" },
            new[] { "--pipe", "CquVpnCore.42", "--parent-pid", "0" }
        };

        Assert.All(invalidArgumentSets, arguments =>
            Assert.False(CquVpnCoreHostOptions.TryParse(arguments, out _)));
    }
}
