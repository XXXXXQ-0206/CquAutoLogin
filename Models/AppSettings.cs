namespace CquAutoLogin.Models;

public sealed class AppSettings
{
    public string PortalBaseUrl { get; set; } = "http://login.cqu.edu.cn:801";

    public string PortalHost { get; set; } = "login.cqu.edu.cn";

    public string WifiName { get; set; } = "CQU_Wifi";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool LaunchAtStartup { get; set; } = true;

    public bool PreferEthernet { get; set; } = true;

    public bool AutoConnectCampusWifi { get; set; } = true;

    public bool OpenVpnPortalAtStartup { get; set; }

    public int HealthyCheckMinutes { get; set; } = 5;
}
