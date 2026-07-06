using System.Net.Http;
using System.Net;
using System.Globalization;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class CampusPortalService
{
    private const string JsVersion = "4.2.1";
    private static readonly Regex JsonpRegex = new(@"^[^(]+\((.*)\)\s*;?$", RegexOptions.Singleline | RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public CampusPortalService()
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CquAutoLogin/1.0");
    }

    public async Task<bool> IsPortalReachableAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var portalUri = BuildEndpointUri(settings, "eportal/");

            using var request = new HttpRequestMessage(HttpMethod.Get, portalUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null ||
                !finalUri.Host.Equals(settings.PortalHost, StringComparison.OrdinalIgnoreCase) ||
                finalUri.Port != portalUri.Port)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Contains("Dr.COM", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("注销页", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("统一认证", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("/eportal/", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<PortalPageConfig?> LoadConfigAsync(
        AppSettings settings,
        NetworkCandidate candidate,
        WifiInfo wifi,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["callback"] = "dr1003",
            ["program_index"] = string.Empty,
            ["wlan_vlan_id"] = "1",
            ["wlan_user_ip"] = EncodeBase64(candidate.IPv4Address),
            ["wlan_user_ipv6"] = EncodeBase64(candidate.IPv6Address),
            ["wlan_user_ssid"] = wifi.Ssid,
            ["wlan_user_areaid"] = string.Empty,
            ["wlan_ac_ip"] = EncodeBase64(candidate.GatewayAddress),
            ["wlan_ap_mac"] = string.Empty,
            ["gw_id"] = string.Empty,
            ["_"] = UnixMilliseconds()
        };

        using var document = await GetJsonAsync(BuildEndpointUri(settings, "eportal/portal/page/loadConfig"), query, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }

        return new PortalPageConfig
        {
            ProgramIndex = GetString(data, "program_index"),
            PageIndex = GetString(data, "page_index"),
            LoginMethod = GetString(data, "login_method", "1"),
            AccountSuffix = GetString(data, "account_suffix"),
            AccountPrefixFlag = GetInt(data, "account_prefix"),
            CustomPerceive = GetInt(data, "custom_perceive")
        };
    }

    public async Task<PortalOnlineStatus> QueryOnlineStatusAsync(
        AppSettings settings,
        NetworkCandidate candidate,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["callback"] = "dr1002",
            ["user_account"] = string.Empty,
            ["user_password"] = string.Empty,
            ["wlan_user_mac"] = string.Empty,
            ["wlan_user_ip"] = EncodeBase64(candidate.IPv4Address),
            ["wlan_user_ipv6"] = EncodeBase64(candidate.IPv6Address),
            ["jsVersion"] = JsVersion,
            ["_"] = UnixMilliseconds()
        };

        using var document = await GetJsonAsync(BuildEndpointUri(settings, "eportal/portal/online_list"), query, cancellationToken);
        var root = document.RootElement;
        var result = GetInt(root, "result");
        var message = GetString(root, "msg");

        if (result != 1 || !root.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            return new PortalOnlineStatus
            {
                IsOnline = false,
                Message = string.IsNullOrWhiteSpace(message) ? "当前设备未登录校园网。" : message
            };
        }

        var onlineItem = list.EnumerateArray().FirstOrDefault(item =>
            GetString(item, "online_ip").Equals(candidate.IPv4Address, StringComparison.OrdinalIgnoreCase) ||
            GetString(item, "online_mac").Equals(candidate.MacAddress, StringComparison.OrdinalIgnoreCase));

        if (onlineItem.ValueKind == JsonValueKind.Undefined)
        {
            onlineItem = list[0];
        }

        return new PortalOnlineStatus
        {
            IsOnline = true,
            Message = string.IsNullOrWhiteSpace(message) ? "校园网认证已在线。" : message,
            OnlineIp = GetString(onlineItem, "online_ip"),
            OnlineMac = GetString(onlineItem, "online_mac")
        };
    }

    public async Task<PortalLoginResult> LoginAsync(
        AppSettings settings,
        PortalPageConfig config,
        NetworkCandidate candidate,
        CancellationToken cancellationToken)
    {
        var prefix = string.Empty;
        if (config.AccountPrefixFlag == 1)
        {
            prefix = config.CustomPerceive == 1 ? ",b," : ",0,";
        }

        var account = $"{prefix}{settings.Username}{config.AccountSuffix}";

        var query = new Dictionary<string, string?>
        {
            ["callback"] = "dr1004",
            ["login_method"] = config.LoginMethod,
            ["user_account"] = account,
            ["user_password"] = settings.Password,
            ["wlan_user_ip"] = candidate.IPv4Address,
            ["wlan_user_ipv6"] = candidate.IPv6Address,
            ["wlan_user_mac"] = NormalizeMac(candidate.MacAddress),
            ["wlan_ac_ip"] = candidate.GatewayAddress,
            ["wlan_ac_name"] = string.Empty,
            ["term_ua"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CquAutoLogin/1.0",
            ["term_type"] = "1",
            ["jsVersion"] = JsVersion,
            ["terminal_type"] = "1",
            ["lang"] = "zh-cn",
            ["_"] = UnixMilliseconds()
        };

        using var document = await GetJsonAsync(BuildEndpointUri(settings, "eportal/portal/login"), query, cancellationToken);
        var root = document.RootElement;
        var result = GetInt(root, "result");
        var retCode = GetInt(root, "ret_code");
        var message = GetString(root, "msg");

        return new PortalLoginResult
        {
            Success = result == 1 ||
                      message.Contains("已经在线", StringComparison.OrdinalIgnoreCase) ||
                      retCode == 2,
            Message = string.IsNullOrWhiteSpace(message) ? "Portal 返回空消息。" : message,
            RetCode = retCode
        };
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri url,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var builder = new UriBuilder(url)
        {
            Query = string.Join("&", query
                .Where(pair => pair.Value is not null)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"))
        };

        try
        {
            using var response = await _httpClient.GetAsync(builder.Uri, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var endpoint = builder.Uri.GetLeftPart(UriPartial.Path);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{endpoint} 返回 HTTP {(int)response.StatusCode}：{ToSnippet(raw)}");
            }

            try
            {
                return JsonDocument.Parse(StripJsonp(raw));
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{endpoint} 返回内容不是有效 JSON/JSONP：{ToSnippet(raw)}", ex);
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{url.GetLeftPart(UriPartial.Path)} 请求超时。", ex);
        }
    }

    private static string StripJsonp(string raw)
    {
        var match = JsonpRegex.Match(raw.Trim());
        return match.Success ? match.Groups[1].Value : raw;
    }

    private static Uri BuildEndpointUri(AppSettings settings, string endpointPath)
    {
        var portalBase = settings.PortalBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? settings.PortalBaseUrl
            : $"{settings.PortalBaseUrl}/";
        var baseUri = new Uri(portalBase, UriKind.Absolute);
        var normalizedEndpoint = endpointPath.TrimStart('/');
        var basePath = baseUri.AbsolutePath.Trim('/');

        if (!string.IsNullOrWhiteSpace(basePath) &&
            (normalizedEndpoint.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
             normalizedEndpoint.StartsWith($"{basePath}/", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedEndpoint = normalizedEndpoint[basePath.Length..].TrimStart('/');
        }

        return new Uri(baseUri, normalizedEndpoint);
    }

    private static string EncodeBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string UnixMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeMac(string value)
    {
        return Regex.Replace(value, "[:-]", string.Empty).ToLowerInvariant();
    }

    private static string ToSnippet(string raw)
    {
        var normalized = Regex.Replace(raw, @"\s+", " ").Trim();
        return normalized.Length <= 160 ? normalized : $"{normalized[..160]}...";
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? fallback
                : property.ToString()
            : fallback;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetInt32(),
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }
}
