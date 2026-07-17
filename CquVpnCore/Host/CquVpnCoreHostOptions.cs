namespace CquVpnCore.Host;

public sealed record CquVpnCoreHostOptions(string PipeName, int ParentProcessId)
{
    public static bool TryParse(string[] arguments, out CquVpnCoreHostOptions options)
    {
        options = default!;
        if (arguments.Length != 4 ||
            !string.Equals(arguments[0], "--pipe", StringComparison.Ordinal) ||
            !string.Equals(arguments[2], "--parent-pid", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(arguments[1]) ||
            arguments[1].Contains('\\') ||
            !int.TryParse(arguments[3], out var parentProcessId) ||
            parentProcessId <= 0)
        {
            return false;
        }

        options = new CquVpnCoreHostOptions(arguments[1], parentProcessId);
        return true;
    }
}
