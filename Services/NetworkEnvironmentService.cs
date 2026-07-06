using System.Net.NetworkInformation;
using System.Net.Sockets;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class NetworkEnvironmentService
{
    private readonly WifiService _wifiService;

    public NetworkEnvironmentService(WifiService wifiService)
    {
        _wifiService = wifiService;
    }

    public async Task<NetworkSnapshot> CaptureAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var wifi = await _wifiService.GetCurrentStateAsync(cancellationToken);

        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsPhysicalLike)
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Select(ToCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.IPv4Address))
            .ToList();

        var ethernet = candidates.FirstOrDefault(candidate => candidate.IsEthernet);
        var activeWifi = candidates.FirstOrDefault(candidate => candidate.IsWifi && NamesMatch(candidate.Name, wifi.InterfaceName))
            ?? candidates.FirstOrDefault(candidate => candidate.IsWifi);

        NetworkCandidate? preferred = null;
        if (settings.PreferEthernet && ethernet is not null)
        {
            preferred = ethernet;
        }
        else if (wifi.IsConnected && string.Equals(wifi.Ssid, settings.WifiName, StringComparison.OrdinalIgnoreCase) && activeWifi is not null)
        {
            preferred = activeWifi;
        }
        else if (ethernet is not null)
        {
            preferred = ethernet;
        }
        else if (activeWifi is not null && string.Equals(wifi.Ssid, settings.WifiName, StringComparison.OrdinalIgnoreCase))
        {
            preferred = activeWifi;
        }

        return new NetworkSnapshot
        {
            EthernetConnected = ethernet is not null,
            WifiConnected = wifi.IsConnected,
            PreferredCampusCandidate = preferred,
            ActiveWifiCandidate = activeWifi,
            Wifi = wifi,
            ActivePhysicalInterfaces = candidates
        };
    }

    private static bool NamesMatch(string left, string right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPhysicalLike(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        var description = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
        var virtualKeywords = new[]
        {
            "virtual", "vmware", "hyper-v", "tap", "vpn", "loopback", "pseudo", "mihomo", "vethernet", "oraybox"
        };

        if (virtualKeywords.Any(description.Contains))
        {
            return false;
        }

        return networkInterface.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.FastEthernetFx or
            NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.Wireless80211;
    }

    private static NetworkCandidate ToCandidate(NetworkInterface networkInterface)
    {
        var properties = networkInterface.GetIPProperties();
        var ipv4 = properties.UnicastAddresses.FirstOrDefault(address =>
            address.Address.AddressFamily == AddressFamily.InterNetwork &&
            !address.Address.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase));

        var ipv6 = properties.UnicastAddresses.FirstOrDefault(address =>
            address.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
            !address.Address.IsIPv6LinkLocal);

        var gateway = properties.GatewayAddresses
            .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;

        return new NetworkCandidate
        {
            Name = networkInterface.Name,
            Description = networkInterface.Description,
            IsEthernet = networkInterface.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
                         NetworkInterfaceType.GigabitEthernet or
                         NetworkInterfaceType.FastEthernetFx or
                         NetworkInterfaceType.FastEthernetT,
            IsWifi = networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            IPv4Address = ipv4?.Address.ToString() ?? string.Empty,
            IPv6Address = ipv6?.Address.ToString() ?? string.Empty,
            MacAddress = networkInterface.GetPhysicalAddress().ToString().ToUpperInvariant(),
            GatewayAddress = gateway
        };
    }
}
