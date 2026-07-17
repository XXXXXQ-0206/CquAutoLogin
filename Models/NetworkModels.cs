namespace CquAutoLogin.Models;

public sealed class WifiInfo
{
    public bool Available { get; init; }

    public string InterfaceName { get; init; } = string.Empty;

    public string State { get; init; } = "Unknown";

    public string Ssid { get; init; } = string.Empty;

    public bool HardwareOn { get; init; } = true;

    public bool SoftwareOn { get; init; } = true;

    public bool IsConnected =>
        !string.IsNullOrWhiteSpace(Ssid) ||
        State.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
        State.Contains("已连接", StringComparison.OrdinalIgnoreCase);
}

public sealed class NetworkCandidate
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsEthernet { get; init; }

    public bool IsWifi { get; init; }

    public string IPv4Address { get; init; } = string.Empty;

    public string IPv6Address { get; init; } = string.Empty;

    public string MacAddress { get; init; } = string.Empty;

    public string GatewayAddress { get; init; } = string.Empty;
}

public sealed class NetworkSnapshot
{
    public bool EthernetConnected { get; init; }

    public bool WifiConnected { get; init; }

    public NetworkCandidate? PreferredCampusCandidate { get; init; }

    public NetworkCandidate? ActiveWifiCandidate { get; init; }

    public WifiInfo Wifi { get; init; } = new();

    public IReadOnlyList<NetworkCandidate> ActivePhysicalInterfaces { get; init; } = Array.Empty<NetworkCandidate>();
}

public sealed class PortalPageConfig
{
    public string ProgramIndex { get; init; } = string.Empty;

    public string PageIndex { get; init; } = string.Empty;

    public string LoginMethod { get; init; } = "1";

    public string AccountSuffix { get; init; } = string.Empty;

    public int AccountPrefixFlag { get; init; }

    public int CustomPerceive { get; init; }
}

public sealed class PortalOnlineStatus
{
    public bool IsOnline { get; init; }

    public string Message { get; init; } = string.Empty;

    public string OnlineIp { get; init; } = string.Empty;

    public string OnlineMac { get; init; } = string.Empty;
}

public sealed class PortalLoginResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int RetCode { get; init; }
}

public sealed record MonitorState
{
    public string Headline { get; init; } = "待命中";

    public string Detail { get; init; } = "应用已启动，等待第一次检查。";

    public string InternetState { get; init; } = "未知";

    public string CampusState { get; init; } = "未知";

    public string PreferredNetwork { get; init; } = "未选择";

    public string WifiState { get; init; } = "未知";

    public string VpnState { get; init; } = "未检测";

    public string LastAction { get; init; } = "尚无动作";

    public string NextCheck { get; init; } = "网络事件触发 + 5 分钟兜底";

    public string CurrentIp { get; init; } = string.Empty;

    public bool IsInternetAvailable { get; init; }

    public bool IsCampusLoggedIn { get; init; }

    public bool IsVpnInstalled { get; init; }

    public bool IsVpnConnected { get; init; }
}
