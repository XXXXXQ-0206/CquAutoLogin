using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CquAutoLogin.Models;
using CquAutoLogin.Services;
using CquVpnCore.Contracts;

namespace CquAutoLogin;

public partial class App : System.Windows.Application
{
    private const string DataFolderName = "CquAutoLogin";
    private const string SingleInstanceMutexName = @"Local\CquAutoLogin.Singleton";
    private const string ActivateTrayMenuSignalName = @"Local\CquAutoLogin.ActivateTrayMenu";
    private const string PortalEntryUrl = "http://login.cqu.edu.cn:801/eportal/";

    private readonly string _dataDirectory;
    private readonly string _bootstrapLogPath;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSignal;
    private CancellationTokenSource? _showSignalCts;
    private Thread? _showSignalThread;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIconService;
    private MonitorCoordinator? _monitorCoordinator;
    private FileLogger? _logger;
    private SettingsService? _settingsService;
    private AutoStartService? _autoStartService;
    private CquVpnCoreClient? _cquVpnCoreClient;
    private BrowserAuthSignalPoller? _browserAuthSignalPoller;
    private AppSettings? _settings;
    private MonitorState _currentTrayState = CreateInitialState();
    private volatile bool _browserAuthObservationActive;
    private bool _forceShutdown;

    public App()
    {
        _dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataFolderName);
        _bootstrapLogPath = Path.Combine(_dataDirectory, "Logs", "startup.log");
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        EnsureBootstrapLogDirectory();
        RegisterGlobalExceptionHooks();
        WriteBootstrapLine("INFO", "OnStartup begin.");

        try
        {
            if (!TryBecomeSingleInstance())
            {
                WriteBootstrapLine("INFO", "Detected existing instance. Sent tray-menu activation signal.");
                Shutdown();
                return;
            }

            await InitializeAsync(e.Args);
        }
        catch (Exception ex)
        {
            LogCriticalStartupFailure("Application startup failed.", ex);
            ShowFatalStartupMessage(ex);
            ExitApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_forceShutdown)
        {
            DisposeInfrastructure();
        }

        base.OnExit(e);
    }

    public void ExitApplication()
    {
        if (_forceShutdown)
        {
            return;
        }

        _forceShutdown = true;
        DisposeInfrastructure();
        Shutdown();
    }

    private async Task InitializeAsync(string[] args)
    {
        var silent = args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
        var runOnce = args.Any(arg => string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase));

        WriteBootstrapLine("INFO", $"InitializeAsync begin. silent={silent}, runOnce={runOnce}");

        _settingsService = new SettingsService(_dataDirectory);
        _logger = new FileLogger(Path.Combine(_dataDirectory, "Logs"));
        _autoStartService = new AutoStartService();
        LogInfo("Core services created.");

        _settings = await _settingsService.LoadAsync();
        LogInfo("Settings loaded.");

        var processRunner = new ProcessRunner();
        var wifiService = new WifiService(processRunner);
        var vpnPipeName = $"CquVpnCore.{Environment.ProcessId}";
        var vpnCorePath = Path.Combine(AppContext.BaseDirectory, "CquVpnCore.exe");
        _cquVpnCoreClient = new CquVpnCoreClient(
            new ProcessCquVpnCoreHost(vpnCorePath, vpnPipeName, Environment.ProcessId),
            new NamedPipeCquVpnCoreCommandClient(vpnPipeName));
        _browserAuthSignalPoller = new BrowserAuthSignalPoller(
            new BrowserAuthSignalStore(),
            HandleBrowserAuthSignalAsync);
        _browserAuthSignalPoller.Start();
        try
        {
            var manifestPath = new BrowserBridgeRegistrationService(_dataDirectory).Register(vpnCorePath);
            LogInfo($"Browser bridge native host registered: {manifestPath}");
        }
        catch (Exception exception)
        {
            LogInfo($"Browser bridge registration skipped: {exception.Message}");
        }
        var networkEnvironmentService = new NetworkEnvironmentService(wifiService);
        var internetProbeService = new InternetProbeService();
        var campusPortalService = new CampusPortalService();
        _monitorCoordinator = new MonitorCoordinator(
            _settings,
            networkEnvironmentService,
            internetProbeService,
            campusPortalService,
            wifiService,
            _logger);
        LogInfo("Background services created.");

        if (runOnce)
        {
            LogInfo("Entering run-once mode.");
            await _monitorCoordinator.RunOneCycleAsync("单次检测模式");
            await Task.Delay(300);
            Shutdown();
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shiroko.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CquAutoLogin.exe");
        }

        _trayIconService = new TrayIconService(iconPath);
        _trayIconService.CommandRequested += OnTrayCommandRequested;
        _trayIconService.ToggleRequested += OnTrayToggleRequested;
        _trayIconService.Update(_currentTrayState, _settings);
        _trayIconService.UpdateVpnStatus(CquVpnCoreClient.GetStoppedDisplayStatus());
        LogInfo("Native tray icon created.");

        _monitorCoordinator.StateChanged += (_, state) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _currentTrayState = state;
                _trayIconService?.Update(state, _settings);
            });
        };
        LogInfo("Tray state bindings wired.");

        _autoStartService.Apply(_settings);
        _monitorCoordinator.Start();
        LogInfo("Monitoring started.");

        if (_settings.OpenVpnPortalAtStartup)
        {
            await ConnectVpnAsync(showError: false);
        }

        if (!silent)
        {
            _ = Dispatcher.BeginInvoke(ShowTrayMenu);
            LogInfo("Application started and opened tray menu.");
        }
        else
        {
            LogInfo("Application started silently in tray mode.");
        }
    }

    private void OnTrayCommandRequested(TrayMenuCommand command)
    {
        _ = Dispatcher.BeginInvoke(async () => await HandleTrayCommandAsync(command));
    }

    private void OnTrayToggleRequested(TraySettingToggle setting, bool enabled)
    {
        _ = Dispatcher.BeginInvoke(async () => await ApplyTrayToggleAsync(setting, enabled));
    }

    private void ShowTrayMenu()
    {
        _trayIconService?.ShowContextMenuAtCursor();
    }

    private async Task HandleTrayCommandAsync(TrayMenuCommand command)
    {
        try
        {
            switch (command)
            {
                case TrayMenuCommand.RunCheck:
                    if (_monitorCoordinator is not null)
                    {
                        await _monitorCoordinator.RunManualCheckAsync();
                    }
                    break;

                case TrayMenuCommand.OpenPortal:
                    OpenPortal();
                    break;

                case TrayMenuCommand.ConnectVpn:
                    await ConnectVpnAsync(showError: true);
                    break;

                case TrayMenuCommand.OpenBrowserBridgeFolder:
                    OpenBrowserBridgeFolder();
                    break;

                case TrayMenuCommand.OpenSettingsFolder:
                    OpenSettingsFolder();
                    break;

                case TrayMenuCommand.ReloadSettings:
                    await ReloadSettingsAsync();
                    break;

                case TrayMenuCommand.Exit:
                    ExitApplication();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to handle tray command.");
            ShowNonFatalMessage($"操作失败：{ex.Message}");
        }
    }

    private async Task ApplyTrayToggleAsync(TraySettingToggle setting, bool enabled)
    {
        if (_settings is null || _settingsService is null || _autoStartService is null || _monitorCoordinator is null)
        {
            return;
        }

        switch (setting)
        {
            case TraySettingToggle.LaunchAtStartup:
                _settings.LaunchAtStartup = enabled;
                break;

            case TraySettingToggle.PreferEthernet:
                _settings.PreferEthernet = enabled;
                break;

            case TraySettingToggle.AutoConnectCampusWifi:
                _settings.AutoConnectCampusWifi = enabled;
                break;

            case TraySettingToggle.OpenVpnPortalAtStartup:
                _settings.OpenVpnPortalAtStartup = enabled;
                break;

            default:
                return;
        }

        await _settingsService.SaveAsync(_settings);
        _autoStartService.Apply(_settings);
        _monitorCoordinator.UpdateSettings(_settings);
        _trayIconService?.Update(_currentTrayState, _settings);
        _logger?.Info($"托盘菜单已更新设置：{setting}={enabled}");
    }

    private async Task ReloadSettingsAsync()
    {
        if (_settingsService is null || _autoStartService is null || _monitorCoordinator is null)
        {
            return;
        }

        _settings = await _settingsService.LoadAsync();
        _autoStartService.Apply(_settings);
        _monitorCoordinator.UpdateSettings(_settings);
        _trayIconService?.Update(_currentTrayState, _settings);
        _logger?.Info("配置文件已重新加载。");
    }

    private static void OpenPortal()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = PortalEntryUrl,
            UseShellExecute = true
        });
    }

    private void OpenSettingsFolder()
    {
        if (_settingsService is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_settingsService.SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private static void OpenBrowserBridgeFolder()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "BrowserBridge");
        if (!Directory.Exists(directory))
        {
            ShowNonFatalMessage("未找到浏览器桥接文件。请重新安装 CquAutoLogin。");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private async Task ConnectVpnAsync(bool showError)
    {
        if (_cquVpnCoreClient is null)
        {
            return;
        }

        _browserAuthObservationActive = true;
        try
        {
            var status = await _cquVpnCoreClient.BeginBrowserLoginAsync(CancellationToken.None);
            ApplyVpnStatus(status);
            _logger?.Info($"CquVpnCore: {status.Detail}");
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "CquVpnCore could not start browser login.");
            _trayIconService?.UpdateVpnStatus(new CquVpnDisplayStatus(
                "CquVpnCore 启动失败",
                IsCoreRunning: false,
                IsConnected: false));
            if (showError)
            {
                ShowNonFatalMessage("CquVpnCore 未能启动或响应。请检查应用目录中是否存在 CquVpnCore.exe。");
            }
        }
    }

    private async Task HandleBrowserAuthSignalAsync(BrowserAuthSignal signal)
    {
        if (_cquVpnCoreClient is null)
        {
            return;
        }

        if (!_browserAuthObservationActive && signal.State != BrowserAuthState.Authenticated)
        {
            return;
        }

        _browserAuthObservationActive = true;
        try
        {
            var status = await _cquVpnCoreClient.ReportBrowserAuthAsync(
                signal.State,
                CancellationToken.None);
            await Dispatcher.InvokeAsync(() => ApplyVpnStatus(status));
            _logger?.Info($"Browser bridge: {signal.State}; CquVpnCore: {status.Detail}");
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "CquVpnCore could not process browser-authentication state.");
        }
    }

    private void ApplyVpnStatus(CquVpnCore.Contracts.VpnCoreStatus status)
    {
        _trayIconService?.UpdateVpnStatus(CquVpnCoreClient.ToDisplayStatus(status));
    }

    private bool TryBecomeSingleInstance()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateTrayMenuSignalName);
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);

        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        if (!_ownsSingleInstanceMutex)
        {
            _showSignal.Set();
            return false;
        }

        _showSignalCts = new CancellationTokenSource();
        _showSignalThread = new Thread(() => ListenForActivationSignals(_showSignalCts.Token))
        {
            IsBackground = true,
            Name = "CquAutoLogin.SingleInstanceListener"
        };
        _showSignalThread.Start();
        WriteBootstrapLine("INFO", "Single-instance ownership acquired.");
        return true;
    }

    private void ListenForActivationSignals(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = _showSignal?.WaitOne();
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (received == true)
            {
                LogInfo("Single-instance listener received tray-menu activation signal.");
                Dispatcher.BeginInvoke(ShowTrayMenu);
            }
        }
    }

    private void DisposeInfrastructure()
    {
        try
        {
            _showSignalCts?.Cancel();
            _showSignal?.Set();
        }
        catch
        {
        }

        try
        {
            if (_showSignalThread is not null && _showSignalThread.IsAlive)
            {
                _showSignalThread.Join(TimeSpan.FromSeconds(1));
            }
        }
        catch
        {
        }

        if (_trayIconService is not null)
        {
            _trayIconService.CommandRequested -= OnTrayCommandRequested;
            _trayIconService.ToggleRequested -= OnTrayToggleRequested;
        }

        _showSignal?.Dispose();
        _showSignalCts?.Dispose();
        _showSignalThread = null;
        try
        {
            _browserAuthSignalPoller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _monitorCoordinator?.Dispose();
        _trayIconService?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            _singleInstanceMutex.Dispose();
        }
    }

    private void RegisterGlobalExceptionHooks()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            LogCriticalStartupFailure("Unhandled exception on UI thread.", eventArgs.Exception);
            eventArgs.Handled = true;

            if (_trayIconService is null)
            {
                ShowFatalStartupMessage(eventArgs.Exception);
                ExitApplication();
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                LogCriticalStartupFailure("Process-level unhandled exception.", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            LogCriticalStartupFailure("Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private void LogInfo(string message)
    {
        WriteBootstrapLine("INFO", message);
        _logger?.Info(message);
    }

    private void LogCriticalStartupFailure(string message, Exception exception)
    {
        var fullMessage = $"{message} {exception.GetType().Name}: {exception.Message}";
        WriteBootstrapLine("ERROR", fullMessage);
        _logger?.Error(exception, message);
    }

    private void EnsureBootstrapLogDirectory()
    {
        var directory = Path.GetDirectoryName(_bootstrapLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void WriteBootstrapLine(string level, string message)
    {
        try
        {
            EnsureBootstrapLogDirectory();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            File.AppendAllText(_bootstrapLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void ShowFatalStartupMessage(Exception exception)
    {
        try
        {
            System.Windows.MessageBox.Show(
                $"CquAutoLogin 启动失败。\n\n{exception.Message}\n\n请查看日志：\n{_bootstrapLogPath}",
                "CquAutoLogin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static void ShowNonFatalMessage(string message)
    {
        try
        {
            System.Windows.MessageBox.Show(
                message,
                "CquAutoLogin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
        }
    }

    private static MonitorState CreateInitialState()
    {
        return new MonitorState
        {
            Headline = "待命中",
            Detail = "等待第一次检测。",
            InternetState = "未知",
            CampusState = "未知",
            PreferredNetwork = "未选择",
            WifiState = "未知",
            VpnState = "正在检测",
            LastAction = "尚无动作",
            NextCheck = "网络事件触发 + 5 分钟兜底"
        };
    }
}
