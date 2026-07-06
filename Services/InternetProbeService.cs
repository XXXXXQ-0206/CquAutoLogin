using System.Net.Http;
using System.Net.Http.Headers;

namespace CquAutoLogin.Services;

public sealed class InternetProbeService
{
    private readonly HttpClient _httpClient;

    public InternetProbeService()
    {
        _httpClient = new HttpClient(new HttpClientHandler())
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CquAutoLogin", "1.0"));
    }

    public async Task<bool> HasInternetAsync(CancellationToken cancellationToken)
    {
        if (await ProbeIconAsync("https://www.baidu.com/favicon.ico", "www.baidu.com", cancellationToken))
        {
            return true;
        }

        if (await ProbeTextAsync("https://www.qq.com/robots.txt", "www.qq.com", "User-agent", cancellationToken))
        {
            return true;
        }

        return await ProbeTextAsync("https://www.cloudflare.com/cdn-cgi/trace", "www.cloudflare.com", "fl=", cancellationToken);
    }

    private async Task<bool> ProbeIconAsync(string url, string expectedHost, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var finalHost = response.RequestMessage?.RequestUri?.Host ?? string.Empty;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        return finalHost.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) &&
               contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ProbeTextAsync(string url, string expectedHost, string marker, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var finalHost = response.RequestMessage?.RequestUri?.Host ?? string.Empty;
        if (!finalHost.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
