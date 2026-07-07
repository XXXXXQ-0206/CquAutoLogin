using System.Text.RegularExpressions;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class WifiService
{
    private readonly ProcessRunner _processRunner;

    public WifiService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WifiInfo> GetCurrentStateAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync("netsh", new[] { "wlan", "show", "interfaces" }, cancellationToken: cancellationToken);
        var text = result.StandardOutput;

        if (string.IsNullOrWhiteSpace(text) ||
            text.Contains("There is no wireless interface", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("无线接口", StringComparison.OrdinalIgnoreCase))
        {
            return new WifiInfo
            {
                Available = false,
                State = "未发现无线网卡"
            };
        }

        var state = GetValue(text, "State", "状态");
        var interfaceName = GetValue(text, "Name", "名称");
        var ssid = GetValue(text, "SSID");

        var radioStatusBlock = Regex.Match(
            text,
            @"^\s*Radio status\s*:\s*(?<hardware>.+?)\r?\n\s+(?<software>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var hardwareOn = true;
        var softwareOn = true;
        if (radioStatusBlock.Success)
        {
            hardwareOn = radioStatusBlock.Groups["hardware"].Value.Contains("On", StringComparison.OrdinalIgnoreCase);
            softwareOn = radioStatusBlock.Groups["software"].Value.Contains("On", StringComparison.OrdinalIgnoreCase);
        }

        return new WifiInfo
        {
            Available = true,
            InterfaceName = interfaceName,
            State = string.IsNullOrWhiteSpace(state) ? "未知" : state,
            Ssid = ssid,
            HardwareOn = hardwareOn,
            SoftwareOn = softwareOn
        };
    }

    public async Task<(bool Accepted, string Message)> ConnectAsync(string ssid, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync("netsh", new[] { "wlan", "connect", $"name={ssid}" }, cancellationToken: cancellationToken);
        var message = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;

        var accepted =
            result.ExitCode == 0 &&
            (message.Contains("Connection request was completed successfully", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("已成功完成连接请求", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("正在连接", StringComparison.OrdinalIgnoreCase));

        return (accepted, message.Trim());
    }

    private static string GetValue(string text, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = Regex.Match(
                text,
                $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }
}
