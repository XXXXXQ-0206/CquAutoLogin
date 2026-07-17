namespace CquVpnCore.State;

public sealed class VpnCoreTransitionException(string message) : InvalidOperationException(message);
