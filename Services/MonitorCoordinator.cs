using System.Net.NetworkInformation;
using CquAutoLogin.Models;

namespace CquAutoLogin.Services;

public sealed class MonitorCoordinator : IDisposable
{
    private enum CampusEnsureOutcome
    {
        ConfirmedOnline,
        WaitingForConfirmation,
        Rejected
    }

    private static readonly TimeSpan[] FailureBackoff =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    };

    private readonly NetworkEnvironmentService _networkEnvironmentService;
    private readonly InternetProbeService _internetProbeService;
    private readonly CampusPortalService _campusPortalService;
    private readonly WifiService _wifiService;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private readonly object _scheduleLock = new();
    private CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _scheduledCheckCts;
    private DateTimeOffset? _scheduledAt;
    private PeriodicTimer? _periodicTimer;
    private int _failureCount;
    private AppSettings _settings;

    public MonitorCoordinator(
        AppSettings settings,
        NetworkEnvironmentService networkEnvironmentService,
        InternetProbeService internetProbeService,
        CampusPortalService campusPortalService,
        WifiService wifiService,
        FileLogger logger)
    {
        _settings = settings;
        _networkEnvironmentService = networkEnvironmentService;
        _internetProbeService = internetProbeService;
        _campusPortalService = campusPortalService;
        _wifiService = wifiService;
        _logger = logger;
    }

    public event EventHandler<MonitorState>? StateChanged;

    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        _periodicTimer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _settings.HealthyCheckMinutes)));
        _ = RunPeriodicLoopAsync(_lifetimeCts.Token);

        ScheduleCheck("程序启动", TimeSpan.FromSeconds(2), force: true);
    }

    public async Task RunManualCheckAsync()
    {
        await ExecuteCycleAsync("手动检测", CancellationToken.None);
    }

    public async Task RunOneCycleAsync(string reason)
    {
        await ExecuteCycleAsync(reason, CancellationToken.None);
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        ScheduleCheck("配置已更新", TimeSpan.FromSeconds(1), force: true);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _lifetimeCts.Cancel();
        _scheduledCheckCts?.Cancel();
        _periodicTimer?.Dispose();
        _cycleGate.Dispose();
        _lifetimeCts.Dispose();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        ScheduleCheck("网络可用性发生变化", e.IsAvailable ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(6));
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        ScheduleCheck("网络地址发生变化", TimeSpan.FromSeconds(4));
    }

    private async Task RunPeriodicLoopAsync(CancellationToken cancellationToken)
    {
        if (_periodicTimer is null)
        {
            return;
        }

        try
        {
            while (await _periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                ScheduleCheck("兜底轮询", TimeSpan.Zero);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleCheck(string reason, TimeSpan delay, bool force = false)
    {
        var nextAt = DateTimeOffset.Now.Add(delay);

        lock (_scheduleLock)
        {
            if (!force && _scheduledAt is not null && _scheduledAt <= nextAt)
            {
                return;
            }

            _scheduledCheckCts?.Cancel();
            _scheduledCheckCts?.Dispose();
            _scheduledCheckCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _scheduledAt = nextAt;

            var linkedToken = _scheduledCheckCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, linkedToken);
                    await ExecuteCycleAsync(reason, linkedToken);
                }
                catch (OperationCanceledException)
                {
                }
            }, linkedToken);
        }
    }

    private async Task ExecuteCycleAsync(string reason, CancellationToken cancellationToken)
    {
        await _cycleGate.WaitAsync(cancellationToken);
        try
        {
            lock (_scheduleLock)
            {
                _scheduledAt = null;
            }

            Publish(new MonitorState
            {
                Headline = "正在检查",
                Detail = $"{reason}，正在识别当前网络环境。",
                InternetState = "检测中",
                CampusState = "检测中",
                PreferredNetwork = "正在识别",
                WifiState = "正在读取",
                LastAction = $"{DateTime.Now:HH:mm:ss} · {reason}",
                NextCheck = "即将更新结果"
            });

            var snapshot = await _networkEnvironmentService.CaptureAsync(_settings, cancellationToken);
            var campusCandidates = GetCampusCandidates(snapshot);

            foreach (var candidate in campusCandidates)
            {
                var candidateReason = candidate.IsEthernet
                    ? "检测到可接管的以太网链路"
                    : $"检测到 {_settings.WifiName}";

                var outcome = await EnsureCampusOnlineAsync(candidateReason, candidate, snapshot.Wifi, cancellationToken);
                if (outcome is CampusEnsureOutcome.ConfirmedOnline or CampusEnsureOutcome.WaitingForConfirmation)
                {
                    return;
                }
            }

            if (snapshot.Wifi.IsConnected &&
                string.Equals(snapshot.Wifi.Ssid, _settings.WifiName, StringComparison.OrdinalIgnoreCase) &&
                snapshot.ActiveWifiCandidate is null)
            {
                Publish(new MonitorState
                {
                    Headline = "等待校园网分配地址",
                    Detail = $"已连接 {_settings.WifiName}，但系统还没有拿到可用 IPv4 地址，稍后继续检查。",
                    InternetState = "公网不可用",
                    CampusState = "等待获取地址",
                    PreferredNetwork = _settings.WifiName,
                    WifiState = DescribeWifi(snapshot.Wifi),
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 等待获取 IP",
                    NextCheck = "8 秒后再次确认"
                });
                ScheduleCheck("等待校园网分配地址", TimeSpan.FromSeconds(8), force: true);
                return;
            }

            var hasInternet = await _internetProbeService.HasInternetAsync(cancellationToken);
            if (hasInternet)
            {
                _failureCount = 0;
                _logger.Info("当前已有正常外网，且当前链路未能识别为校园网，保持现有网络，不切换校园网。");
                Publish(new MonitorState
                {
                    Headline = "网络已在线",
                    Detail = "当前网络已经能正常访问互联网，且当前链路未通过校园网 Portal 检测，应用保持待命。",
                    InternetState = "公网可用",
                    CampusState = "不接管",
                    PreferredNetwork = DescribePreferred(snapshot.PreferredCampusCandidate),
                    WifiState = DescribeWifi(snapshot.Wifi),
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 检测到正常外网",
                    NextCheck = $"网络事件触发，另有 {_settings.HealthyCheckMinutes} 分钟兜底检查",
                    CurrentIp = GetCurrentIp(snapshot.PreferredCampusCandidate, snapshot.ActiveWifiCandidate),
                    IsInternetAvailable = true
                });
                return;
            }

            if (!_settings.AutoConnectCampusWifi)
            {
                var delay = GetBackoffDelay();
                _logger.Warn("公网不可用，且已关闭自动连接 CQU_Wifi。");
                Publish(new MonitorState
                {
                    Headline = "等待网络恢复",
                    Detail = "目前没有可用外网，且你关闭了自动连接 CQU_Wifi，所以应用暂不主动切换网络。",
                    InternetState = "公网不可用",
                    CampusState = "未登录",
                    PreferredNetwork = "尚未找到可接管的校园网链路",
                    WifiState = DescribeWifi(snapshot.Wifi),
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 保持等待",
                    NextCheck = $"将在 {delay.TotalSeconds:0} 秒后重试"
                });
                ScheduleCheck("断网后继续等待", delay);
                return;
            }

            if (!snapshot.Wifi.Available)
            {
                var delay = GetBackoffDelay();
                _logger.Warn("未发现无线网卡，无法回落连接 CQU_Wifi。");
                Publish(new MonitorState
                {
                    Headline = "未发现无线网卡",
                    Detail = "以太网不可用，系统中也没有可用无线网卡，因此暂时无法回落到 CQU_Wifi。",
                    InternetState = "公网不可用",
                    CampusState = "未登录",
                    PreferredNetwork = "无可用接入方式",
                    WifiState = "无线网卡不可用",
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 等待硬件恢复",
                    NextCheck = $"将在 {delay.TotalSeconds:0} 秒后重试"
                });
                ScheduleCheck("等待无线网卡恢复", delay);
                return;
            }

            if (!snapshot.Wifi.HardwareOn || !snapshot.Wifi.SoftwareOn)
            {
                var delay = GetBackoffDelay();
                _logger.Warn("无线网卡处于关闭状态，暂时无法连接 CQU_Wifi。");
                Publish(new MonitorState
                {
                    Headline = "无线已关闭",
                    Detail = "检测到无线网卡硬件或软件开关关闭，当前无法自动连接 CQU_Wifi。",
                    InternetState = "公网不可用",
                    CampusState = "未登录",
                    PreferredNetwork = "等待无线重新开启",
                    WifiState = "无线开关关闭",
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 无线关闭",
                    NextCheck = $"将在 {delay.TotalSeconds:0} 秒后重试"
                });
                ScheduleCheck("等待无线开启", delay);
                return;
            }

            var connectResult = await _wifiService.ConnectAsync(_settings.WifiName, cancellationToken);
            if (connectResult.Accepted)
            {
                _failureCount = 0;
                _logger.Info($"已请求连接 {_settings.WifiName}。");
                Publish(new MonitorState
                {
                    Headline = "正在连接校园 Wi-Fi",
                    Detail = $"当前未检测到正常外网，也没有可直接接管的校园网链路，应用已请求连接 {_settings.WifiName}。",
                    InternetState = "公网不可用",
                    CampusState = "等待连接后登录",
                    PreferredNetwork = $"回落到 {_settings.WifiName}",
                    WifiState = connectResult.Message,
                    LastAction = $"{DateTime.Now:HH:mm:ss} · 请求连接 {_settings.WifiName}",
                    NextCheck = "18 秒后确认 Wi-Fi 状态"
                });
                ScheduleCheck("等待 CQU_Wifi 建链", TimeSpan.FromSeconds(18), force: true);
                return;
            }

            var backoff = GetBackoffDelay();
            _logger.Warn($"连接 {_settings.WifiName} 失败：{connectResult.Message}");
            Publish(new MonitorState
            {
                Headline = "连接校园 Wi-Fi 失败",
                Detail = $"系统未能成功发起 {_settings.WifiName} 连接。若这是首次使用，请先手动连接一次以保存 Wi-Fi 配置。",
                InternetState = "公网不可用",
                CampusState = "未登录",
                PreferredNetwork = $"尝试连接 {_settings.WifiName}",
                WifiState = string.IsNullOrWhiteSpace(connectResult.Message) ? "连接请求被系统拒绝" : connectResult.Message,
                LastAction = $"{DateTime.Now:HH:mm:ss} · Wi-Fi 连接失败",
                NextCheck = $"将在 {backoff.TotalSeconds:0} 秒后重试"
            });
            ScheduleCheck("重试连接 CQU_Wifi", backoff);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var backoff = GetBackoffDelay();
            _logger.Error(ex, "监控循环发生异常。");
            Publish(new MonitorState
            {
                Headline = "检测遇到异常",
                Detail = "本轮检测中出现异常，应用会自动退避后再试，不会高频重试。",
                InternetState = "异常",
                CampusState = "异常",
                PreferredNetwork = "异常",
                WifiState = "异常",
                LastAction = $"{DateTime.Now:HH:mm:ss} · {ex.Message}",
                NextCheck = $"将在 {backoff.TotalSeconds:0} 秒后重试"
            });
            ScheduleCheck("异常恢复重试", backoff);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    private async Task<CampusEnsureOutcome> EnsureCampusOnlineAsync(
        string reason,
        NetworkCandidate candidate,
        WifiInfo wifi,
        CancellationToken cancellationToken)
    {
        Publish(new MonitorState
        {
            Headline = "正在检查校园网登录",
            Detail = $"{reason}，已锁定 {candidate.Name}，正在确认重庆大学 Portal 登录状态。",
            InternetState = "公网不可用",
            CampusState = "查询中",
            PreferredNetwork = DescribePreferred(candidate),
            WifiState = DescribeWifi(wifi),
            LastAction = $"{DateTime.Now:HH:mm:ss} · 查询 Portal 状态",
            NextCheck = "本轮完成后更新"
        });

        PortalOnlineStatus onlineStatus;
        try
        {
            onlineStatus = await _campusPortalService.QueryOnlineStatusAsync(_settings, candidate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn($"查询 Portal 在线状态失败，将按未登录继续处理：{ex.Message}");
            onlineStatus = new PortalOnlineStatus
            {
                IsOnline = false,
                Message = "Portal 在线状态暂时不可用。"
            };
        }

        if (onlineStatus.IsOnline)
        {
            _failureCount = 0;
            _logger.Info($"校园网已在线，无需重复登录：{candidate.Name} {candidate.IPv4Address}");
            Publish(new MonitorState
            {
                Headline = "校园网已登录",
                Detail = "Portal 显示当前设备已经在线，因此应用不会重复发起登录。",
                InternetState = "校园网可用",
                CampusState = "已登录",
                PreferredNetwork = DescribePreferred(candidate),
                WifiState = DescribeWifi(wifi),
                LastAction = $"{DateTime.Now:HH:mm:ss} · 已确认在线",
                NextCheck = $"网络事件触发，另有 {_settings.HealthyCheckMinutes} 分钟兜底检查",
                CurrentIp = candidate.IPv4Address,
                IsCampusLoggedIn = true,
                IsInternetAvailable = true
            });
            return CampusEnsureOutcome.ConfirmedOnline;
        }

        PortalPageConfig? config;
        try
        {
            config = await _campusPortalService.LoadConfigAsync(_settings, candidate, wifi, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn($"读取 Portal 配置失败，将使用重庆大学 Portal 默认登录参数继续尝试：{ex.Message}");
            config = CreateDefaultPortalConfig();
        }

        if (config is null)
        {
            _logger.Warn($"Portal 未返回有效配置，将使用重庆大学 Portal 默认登录参数继续尝试：{candidate.Name} {candidate.IPv4Address}");
            config = CreateDefaultPortalConfig();
        }

        Publish(new MonitorState
        {
            Headline = "正在自动登录",
            Detail = $"{reason}，当前没有检测到有效 Portal 会话，正在使用保存的账号登录重庆大学校园网。",
            InternetState = "公网不可用",
            CampusState = "正在登录",
            PreferredNetwork = DescribePreferred(candidate),
            WifiState = DescribeWifi(wifi),
            LastAction = $"{DateTime.Now:HH:mm:ss} · 发起自动登录",
            NextCheck = "稍后确认登录结果"
        });

        var loginResult = await _campusPortalService.LoginAsync(_settings, config, candidate, cancellationToken);
        if (!loginResult.Success)
        {
            _logger.Warn($"Portal 登录失败：{loginResult.Message}");
            Publish(new MonitorState
            {
                Headline = "自动登录失败",
                Detail = loginResult.Message,
                InternetState = "公网不可用",
                CampusState = "登录失败",
                PreferredNetwork = DescribePreferred(candidate),
                WifiState = DescribeWifi(wifi),
                LastAction = $"{DateTime.Now:HH:mm:ss} · Portal 返回失败",
                NextCheck = "正在尝试其它候选链路"
            });
            return CampusEnsureOutcome.Rejected;
        }

        _logger.Info($"Portal 已接受登录请求：{loginResult.Message}");

        var ambiguousAlreadyOnline =
            loginResult.RetCode == 2 ||
            loginResult.Message.Contains("已经在线", StringComparison.OrdinalIgnoreCase);

        await Task.Delay(ambiguousAlreadyOnline ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(4), cancellationToken);

        var confirmedStatus = await _campusPortalService.QueryOnlineStatusAsync(_settings, candidate, cancellationToken);
        if (confirmedStatus.IsOnline)
        {
            _failureCount = 0;
            _logger.Info($"Portal 登录已确认生效：{candidate.Name} {candidate.IPv4Address}");
            Publish(new MonitorState
            {
                Headline = "校园网已登录",
                Detail = "Portal 已确认当前链路在线，校园网接管成功。",
                InternetState = "校园网可用",
                CampusState = "已登录",
                PreferredNetwork = DescribePreferred(candidate),
                WifiState = DescribeWifi(wifi),
                LastAction = $"{DateTime.Now:HH:mm:ss} · 登录确认成功",
                NextCheck = $"网络事件触发，另有 {_settings.HealthyCheckMinutes} 分钟兜底检查",
                CurrentIp = candidate.IPv4Address,
                IsCampusLoggedIn = true,
                IsInternetAvailable = true
            });
            return CampusEnsureOutcome.ConfirmedOnline;
        }

        if (ambiguousAlreadyOnline)
        {
            _logger.Warn($"Portal 返回“已在线”，但链路未确认上线：{candidate.Name} {candidate.IPv4Address}");
            Publish(new MonitorState
            {
                Headline = "当前链路未确认上线",
                Detail = $"Portal 返回“{loginResult.Message}”，但 {candidate.Name} ({candidate.IPv4Address}) 没有确认在线，应用会尝试其它候选链路。",
                InternetState = "公网不可用",
                CampusState = "未确认",
                PreferredNetwork = DescribePreferred(candidate),
                WifiState = DescribeWifi(wifi),
                LastAction = $"{DateTime.Now:HH:mm:ss} · 登录结果存在歧义",
                NextCheck = "正在尝试其它候选链路"
            });
            return CampusEnsureOutcome.Rejected;
        }

        Publish(new MonitorState
        {
            Headline = "登录请求已发送",
            Detail = "Portal 已接受登录请求，正在等待会话真正生效。",
            InternetState = "公网不可用",
            CampusState = "等待确认",
            PreferredNetwork = DescribePreferred(candidate),
            WifiState = DescribeWifi(wifi),
            LastAction = $"{DateTime.Now:HH:mm:ss} · 等待登录生效",
            NextCheck = "8 秒后再次确认"
        });
        ScheduleCheck("确认 Portal 登录结果", TimeSpan.FromSeconds(8), force: true);
        return CampusEnsureOutcome.WaitingForConfirmation;
    }

    private List<NetworkCandidate> GetCampusCandidates(NetworkSnapshot snapshot)
    {
        var ethernetCandidate = snapshot.ActivePhysicalInterfaces.FirstOrDefault(candidate => candidate.IsEthernet);
        var campusWifiCandidate =
            snapshot.Wifi.IsConnected &&
            string.Equals(snapshot.Wifi.Ssid, _settings.WifiName, StringComparison.OrdinalIgnoreCase)
                ? snapshot.ActiveWifiCandidate
                : null;

        var candidates = new List<NetworkCandidate>();

        void AddCandidate(NetworkCandidate? candidate)
        {
            if (candidate is null)
            {
                return;
            }

            if (candidates.Any(existing =>
                    existing.IPv4Address.Equals(candidate.IPv4Address, StringComparison.OrdinalIgnoreCase) &&
                    existing.MacAddress.Equals(candidate.MacAddress, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(candidate);
        }

        if (_settings.PreferEthernet)
        {
            AddCandidate(ethernetCandidate);
            AddCandidate(campusWifiCandidate);
        }
        else
        {
            AddCandidate(campusWifiCandidate);
            AddCandidate(ethernetCandidate);
        }

        return candidates;
    }

    private TimeSpan GetBackoffDelay()
    {
        var index = Math.Min(_failureCount, FailureBackoff.Length - 1);
        var delay = FailureBackoff[index];
        _failureCount++;
        return delay;
    }

    private static PortalPageConfig CreateDefaultPortalConfig()
    {
        return new PortalPageConfig
        {
            LoginMethod = "1",
            AccountPrefixFlag = 1,
            CustomPerceive = 0,
            AccountSuffix = string.Empty
        };
    }

    private static string GetCurrentIp(NetworkCandidate? primaryCandidate, NetworkCandidate? fallbackCandidate)
    {
        if (!string.IsNullOrWhiteSpace(primaryCandidate?.IPv4Address))
        {
            return primaryCandidate.IPv4Address;
        }

        if (!string.IsNullOrWhiteSpace(fallbackCandidate?.IPv4Address))
        {
            return fallbackCandidate.IPv4Address;
        }

        return string.Empty;
    }

    private void Publish(MonitorState state)
    {
        StateChanged?.Invoke(this, state);
    }

    private static string DescribePreferred(NetworkCandidate? candidate)
    {
        if (candidate is null)
        {
            return "暂未找到可接管链路";
        }

        var type = candidate.IsEthernet ? "以太网" : candidate.IsWifi ? "Wi-Fi" : "网络";
        return $"{type} · {candidate.Name} · {candidate.IPv4Address}";
    }

    private static string DescribeWifi(WifiInfo wifi)
    {
        if (!wifi.Available)
        {
            return "无线网卡不可用";
        }

        if (!wifi.IsConnected)
        {
            return "Wi-Fi 未连接";
        }

        return string.IsNullOrWhiteSpace(wifi.Ssid)
            ? $"Wi-Fi 已连接 · {wifi.State}"
            : $"Wi-Fi 已连接 · {wifi.Ssid}";
    }
}
