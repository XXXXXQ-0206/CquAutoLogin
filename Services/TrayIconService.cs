using System.Drawing;
using System.Runtime.InteropServices;

namespace CquAutoLogin.Services;

public enum TrayMenuCommand
{
    RunCheck,
    OpenPortal,
    OpenATrust,
    ExitATrust,
    OpenSettingsFolder,
    ReloadSettings,
    Exit
}

public enum TraySettingToggle
{
    LaunchAtStartup,
    PreferEthernet,
    AutoConnectCampusWifi,
    OpenATrustAtStartup
}

public sealed class TrayIconService : IDisposable
{
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int WmApp = 0x8000;
    private const int WmTrayIcon = WmApp + 1;
    private const int WmCommand = 0x0111;
    private const int WmDestroy = 0x0002;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int TpmLeftAlign = 0x0000;
    private const int TpmBottomAlign = 0x0020;
    private const int TpmRightButton = 0x0002;
    private const int MiimState = 0x00000001;
    private const int MiimId = 0x00000002;
    private const int MiimSubmenu = 0x00000004;
    private const int MiimType = 0x00000010;
    private const int MftString = 0x00000000;
    private const int MftSeparator = 0x00000800;
    private const int MfsEnabled = 0x00000000;
    private const int MfsDisabled = 0x00000003;
    private const int MfsChecked = 0x00000008;
    private const int MfsUnchecked = 0x00000000;
    private const int DwmwaUseImmersiveDarkMode = 20;

    private const int IdRunCheck = 1001;
    private const int IdOpenPortal = 1002;
    private const int IdOpenSettingsFolder = 1003;
    private const int IdReloadSettings = 1004;
    private const int IdOpenATrust = 1005;
    private const int IdExitATrust = 1006;
    private const int IdToggleLaunchAtStartup = 1101;
    private const int IdTogglePreferEthernet = 1102;
    private const int IdToggleAutoConnectWifi = 1103;
    private const int IdToggleOpenATrustAtStartup = 1104;
    private const int IdExit = 1999;

    private readonly string _windowClassName = $"CquAutoLogin.TrayWindow.{Guid.NewGuid():N}";
    private readonly WndProc _wndProc;
    private readonly Icon _onlineIcon;
    private readonly Icon _offlineIcon;
    private readonly nint _onlineHandle;
    private readonly nint _offlineHandle;
    private readonly nint _windowHandle;
    private readonly uint _windowClassAtom;
    private readonly uint _taskbarCreatedMessage;
    private readonly SynchronizationContext _syncContext;
    private readonly object _stateLock = new();
    private MonitorSnapshot _snapshot = MonitorSnapshot.Default;
    private ATrustStatus _aTrustStatus = ATrustStatus.NotDetected;

    public TrayIconService(string iconPath)
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _wndProc = WindowProc;
        _windowClassAtom = RegisterWindowClass(_windowClassName, _wndProc);
        if (_windowClassAtom == 0)
        {
            throw new InvalidOperationException("Failed to register tray window class.");
        }

        _windowHandle = CreateMessageWindow(_windowClassAtom, _windowClassName);
        if (_windowHandle == 0)
        {
            throw new InvalidOperationException("Failed to create tray message window.");
        }

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        _onlineIcon = CreateIcon(iconPath, false, out _onlineHandle);
        _offlineIcon = CreateIcon(iconPath, true, out _offlineHandle);
        AddTrayIcon(_offlineIcon, MonitorSnapshot.Default.Tooltip);
    }

    public event Action<TrayMenuCommand>? CommandRequested;

    public event Action<TraySettingToggle, bool>? ToggleRequested;

    public void Update(Models.MonitorState state, Models.AppSettings? settings)
    {
        var snapshot = MonitorSnapshot.From(state, settings, _aTrustStatus);
        lock (_stateLock)
        {
            _snapshot = snapshot;
        }

        var icon = snapshot.IsOnline ? _onlineIcon : _offlineIcon;
        ModifyTrayIcon(icon, snapshot.Tooltip);
    }

    public void UpdateATrustStatus(ATrustStatus status)
    {
        lock (_stateLock)
        {
            _aTrustStatus = status;
            _snapshot = _snapshot with
            {
                ATrustState = status.DisplayText,
                IsATrustInstalled = status.IsInstalled,
                IsATrustConnected = status.IsConnected
            };
        }
    }

    public void ShowContextMenuAtCursor()
    {
        PostToUi(ShowContextMenuCore);
    }

    public void Dispose()
    {
        RemoveTrayIcon();

        if (_windowHandle != 0)
        {
            DestroyWindow(_windowHandle);
        }

        if (_windowClassAtom != 0)
        {
            UnregisterClass(_windowClassName, GetModuleHandle(null));
        }

        _onlineIcon.Dispose();
        _offlineIcon.Dispose();

        if (_onlineHandle != 0)
        {
            DestroyIcon(_onlineHandle);
        }

        if (_offlineHandle != 0)
        {
            DestroyIcon(_offlineHandle);
        }
    }

    private void PostToUi(Action action)
    {
        _syncContext.Post(_ => action(), null);
    }

    private void ShowContextMenuCore()
    {
        var snapshot = GetSnapshot();
        var menu = BuildMenu(snapshot);
        if (menu == 0)
        {
            return;
        }

        try
        {
            TryEnableDarkMode(menu);

            if (!GetCursorPos(out var point))
            {
                return;
            }

            SetForegroundWindow(_windowHandle);
            TrackPopupMenuEx(
                menu,
                TpmLeftAlign | TpmBottomAlign | TpmRightButton,
                point.X,
                point.Y,
                _windowHandle,
                nint.Zero);
            PostMessage(_windowHandle, 0, 0, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private nint BuildMenu(MonitorSnapshot snapshot)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }

        try
        {
            AppendInfoItem(menu, 2001, $"状态：{snapshot.Headline}");

            var statusMenu = CreatePopupMenu();
            if (statusMenu == 0)
            {
                throw new InvalidOperationException("Failed to create status submenu.");
            }

            AppendInfoItem(statusMenu, 2101, $"说明：{snapshot.Detail}");
            AppendInfoItem(statusMenu, 2102, $"公网：{snapshot.InternetState}");
            AppendInfoItem(statusMenu, 2103, $"校园网：{snapshot.CampusState}");
            AppendInfoItem(statusMenu, 2104, $"首选网络：{snapshot.PreferredNetwork}");
            AppendInfoItem(statusMenu, 2105, $"Wi‑Fi：{snapshot.WifiState}");
            AppendInfoItem(statusMenu, 2106, $"ATrust VPN：{snapshot.ATrustState}");
            AppendInfoItem(statusMenu, 2107, $"上次动作：{snapshot.LastAction}");
            AppendInfoItem(statusMenu, 2108, $"下次检查：{snapshot.NextCheck}");
            AppendSubMenu(menu, statusMenu, "状态详情");
            AppendSeparator(menu);
            AppendActionItem(menu, IdRunCheck, "立即检测");
            AppendActionItem(menu, IdOpenPortal, "打开认证页");

            var aTrustMenu = CreatePopupMenu();
            if (aTrustMenu == 0)
            {
                throw new InvalidOperationException("Failed to create ATrust submenu.");
            }

            AppendInfoItem(aTrustMenu, 2201, $"状态：{snapshot.ATrustState}");
            if (snapshot.IsATrustInstalled)
            {
                AppendActionItem(aTrustMenu, IdOpenATrust, "打开/连接 ATrust");
                AppendActionItem(aTrustMenu, IdExitATrust, "关闭 ATrust（完全退出）");
            }
            AppendSubMenu(menu, aTrustMenu, "ATrust VPN");
            AppendSeparator(menu);

            var configMenu = CreatePopupMenu();
            if (configMenu == 0)
            {
                throw new InvalidOperationException("Failed to create config submenu.");
            }

            AppendActionItem(configMenu, IdOpenSettingsFolder, "打开配置目录");
            AppendActionItem(configMenu, IdReloadSettings, "重新加载配置");
            AppendSubMenu(menu, configMenu, "配置");

            var settingsMenu = CreatePopupMenu();
            if (settingsMenu == 0)
            {
                throw new InvalidOperationException("Failed to create settings submenu.");
            }

            AppendToggleItem(settingsMenu, IdToggleLaunchAtStartup, "开机自启", snapshot.LaunchAtStartup);
            AppendToggleItem(settingsMenu, IdTogglePreferEthernet, "优先使用以太网", snapshot.PreferEthernet);
            AppendToggleItem(settingsMenu, IdToggleAutoConnectWifi, "断网时自动连接 CQU_Wifi", snapshot.AutoConnectCampusWifi);
            AppendToggleItem(settingsMenu, IdToggleOpenATrustAtStartup, "启动时打开 ATrust", snapshot.OpenATrustAtStartup);
            AppendSubMenu(menu, settingsMenu, "偏好设置");
            AppendSeparator(menu);
            AppendActionItem(menu, IdExit, "退出程序");
            return menu;
        }
        catch
        {
            DestroyMenu(menu);
            throw;
        }
    }

    private void AppendInfoItem(nint menu, uint id, string text)
    {
        AppendMenuItem(menu, id, text, enabled: false, isChecked: false, subMenu: 0, isSeparator: false);
    }

    private void AppendActionItem(nint menu, uint id, string text)
    {
        AppendMenuItem(menu, id, text, enabled: true, isChecked: false, subMenu: 0, isSeparator: false);
    }

    private void AppendToggleItem(nint menu, uint id, string text, bool isChecked)
    {
        AppendMenuItem(menu, id, text, enabled: true, isChecked, subMenu: 0, isSeparator: false);
    }

    private void AppendSubMenu(nint menu, nint subMenu, string text)
    {
        AppendMenuItem(menu, 0, text, enabled: true, isChecked: false, subMenu, isSeparator: false);
    }

    private void AppendSeparator(nint menu)
    {
        AppendMenuItem(menu, 0, string.Empty, enabled: false, isChecked: false, subMenu: 0, isSeparator: true);
    }

    private static void AppendMenuItem(nint menu, uint id, string text, bool enabled, bool isChecked, nint subMenu, bool isSeparator)
    {
        var item = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MiimId | MiimState | MiimType
        };

        if (isSeparator)
        {
            item.fType = MftSeparator;
            item.fState = MfsDisabled;
        }
        else
        {
            item.fType = MftString;
            item.fState = (uint)((enabled ? MfsEnabled : MfsDisabled) | (isChecked ? MfsChecked : MfsUnchecked));
            item.wID = id;
            item.dwTypeData = text;
            item.cch = (uint)text.Length;
        }

        if (subMenu != 0)
        {
            item.fMask |= MiimSubmenu;
            item.hSubMenu = subMenu;
            item.fState = MfsEnabled;
            item.dwTypeData = text;
            item.cch = (uint)text.Length;
        }

        if (!InsertMenuItem(menu, uint.MaxValue, true, ref item))
        {
            throw new InvalidOperationException("Failed to insert tray menu item.");
        }
    }

    private void TryEnableDarkMode(nint menu)
    {
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(_windowHandle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));

            if (SetPreferredAppMode is not null)
            {
                SetPreferredAppMode(PreferredAppMode.AllowDark);
            }

            if (FlushMenuThemes is not null)
            {
                FlushMenuThemes();
            }

            if (AllowDarkModeForWindow is not null)
            {
                AllowDarkModeForWindow(_windowHandle, true);
            }
        }
        catch
        {
        }
    }

    private void AddTrayIcon(Icon icon, string tooltip)
    {
        var data = CreateNotifyIconData(icon, tooltip);
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException("Failed to add tray icon.");
        }
    }

    private void ReAddTrayIcon()
    {
        var snapshot = GetSnapshot();
        var icon = snapshot.IsOnline ? _onlineIcon : _offlineIcon;
        AddTrayIcon(icon, snapshot.Tooltip);
    }

    private void ModifyTrayIcon(Icon icon, string tooltip)
    {
        var data = CreateNotifyIconData(icon, tooltip);
        if (!Shell_NotifyIcon(NimModify, ref data))
        {
            if (Shell_NotifyIcon(NimAdd, ref data))
            {
                return;
            }
        }
    }

    private void RemoveTrayIcon()
    {
        var data = CreateNotifyIconData(_offlineIcon, string.Empty);
        Shell_NotifyIcon(NimDelete, ref data);
    }

    private NOTIFYICONDATA CreateNotifyIconData(Icon icon, string tooltip)
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = icon.Handle,
            szTip = TrimTooltip(tooltip)
        };
    }

    private MonitorSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return _snapshot;
        }
    }

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            ReAddTrayIcon();
            return 0;
        }

        switch (msg)
        {
            case WmTrayIcon:
                HandleTrayMouseMessage((int)lParam);
                return 0;

            case WmCommand:
                HandleCommand((int)(wParam.ToUInt64() & 0xFFFF));
                return 0;

            case WmDestroy:
                RemoveTrayIcon();
                return 0;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void HandleTrayMouseMessage(int message)
    {
        switch (message)
        {
            case WmRButtonUp:
                ShowContextMenuCore();
                break;

            case WmLButtonUp:
            case WmLButtonDblClk:
                ShowContextMenuCore();
                break;
        }
    }

    private void HandleCommand(int id)
    {
        switch (id)
        {
            case IdRunCheck:
                CommandRequested?.Invoke(TrayMenuCommand.RunCheck);
                break;
            case IdOpenPortal:
                CommandRequested?.Invoke(TrayMenuCommand.OpenPortal);
                break;
            case IdOpenATrust:
                CommandRequested?.Invoke(TrayMenuCommand.OpenATrust);
                break;
            case IdExitATrust:
                CommandRequested?.Invoke(TrayMenuCommand.ExitATrust);
                break;
            case IdOpenSettingsFolder:
                CommandRequested?.Invoke(TrayMenuCommand.OpenSettingsFolder);
                break;
            case IdReloadSettings:
                CommandRequested?.Invoke(TrayMenuCommand.ReloadSettings);
                break;
            case IdToggleLaunchAtStartup:
                ToggleRequested?.Invoke(TraySettingToggle.LaunchAtStartup, !GetSnapshot().LaunchAtStartup);
                break;
            case IdTogglePreferEthernet:
                ToggleRequested?.Invoke(TraySettingToggle.PreferEthernet, !GetSnapshot().PreferEthernet);
                break;
            case IdToggleAutoConnectWifi:
                ToggleRequested?.Invoke(TraySettingToggle.AutoConnectCampusWifi, !GetSnapshot().AutoConnectCampusWifi);
                break;
            case IdToggleOpenATrustAtStartup:
                ToggleRequested?.Invoke(TraySettingToggle.OpenATrustAtStartup, !GetSnapshot().OpenATrustAtStartup);
                break;
            case IdExit:
                CommandRequested?.Invoke(TrayMenuCommand.Exit);
                break;
        }
    }

    private static uint RegisterWindowClass(string className, WndProc wndProc)
    {
        var module = GetModuleHandle(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = wndProc,
            hInstance = module,
            lpszClassName = className
        };

        return RegisterClassEx(ref windowClass);
    }

    private static nint CreateMessageWindow(uint atom, string className)
    {
        return CreateWindowEx(
            0,
            className,
            className,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            GetModuleHandle(null),
            0);
    }

    private static string TrimTooltip(string text)
    {
        const int max = 127;
        return text.Length <= max ? text : text[..max];
    }

    private static Icon CreateIcon(string iconPath, bool grayscale, out nint handle)
    {
        using var baseIcon = IconUtilities.LoadBaseIcon(iconPath);
        using var baseBitmap = baseIcon.ToBitmap();
        using var converted = new Bitmap(baseBitmap.Width, baseBitmap.Height);
        using var graphics = Graphics.FromImage(converted);

        if (grayscale)
        {
            var colorMatrix = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            });

            using var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
            imageAttributes.SetColorMatrix(colorMatrix);
            graphics.DrawImage(
                baseBitmap,
                new Rectangle(0, 0, converted.Width, converted.Height),
                0,
                0,
                baseBitmap.Width,
                baseBitmap.Height,
                GraphicsUnit.Pixel,
                imageAttributes);
        }
        else
        {
            graphics.DrawImage(baseBitmap, 0, 0, converted.Width, converted.Height);
        }

        handle = converted.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static readonly SetPreferredAppModeDelegate? SetPreferredAppMode = LoadSetPreferredAppMode();
    private static readonly FlushMenuThemesDelegate? FlushMenuThemes = LoadFlushMenuThemes();
    private static readonly AllowDarkModeForWindowDelegate? AllowDarkModeForWindow = LoadAllowDarkModeForWindow();

    private static SetPreferredAppModeDelegate? LoadSetPreferredAppMode()
    {
        return LoadUxThemeDelegate<SetPreferredAppModeDelegate>(135);
    }

    private static FlushMenuThemesDelegate? LoadFlushMenuThemes()
    {
        return LoadUxThemeDelegate<FlushMenuThemesDelegate>(136);
    }

    private static AllowDarkModeForWindowDelegate? LoadAllowDarkModeForWindow()
    {
        return LoadUxThemeDelegate<AllowDarkModeForWindowDelegate>(133);
    }

    private static TDelegate? LoadUxThemeDelegate<TDelegate>(int ordinal)
        where TDelegate : Delegate
    {
        var module = LoadLibrary("uxtheme.dll");
        if (module == 0)
        {
            return null;
        }

        var address = GetProcAddress(module, (nint)ordinal);
        return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    private readonly record struct MonitorSnapshot(
        string Headline,
        string Detail,
        string InternetState,
        string CampusState,
        string PreferredNetwork,
        string WifiState,
        string ATrustState,
        string LastAction,
        string NextCheck,
        string CurrentIp,
        bool IsOnline,
        bool IsATrustInstalled,
        bool IsATrustConnected,
        bool LaunchAtStartup,
        bool PreferEthernet,
        bool AutoConnectCampusWifi,
        bool OpenATrustAtStartup)
    {
        public string Tooltip => string.IsNullOrWhiteSpace(CurrentIp)
            ? Headline
            : $"{Headline} | {CurrentIp}";

        public static MonitorSnapshot Default => new(
            "待命中",
            "等待第一次检测。",
            "未知",
            "未知",
            "未选择",
            "未知",
            "未检测",
            "尚无动作",
            "网络事件触发 + 5 分钟兜底",
            string.Empty,
            false,
            false,
            false,
            true,
            true,
            true,
            false);

        public static MonitorSnapshot From(
            Models.MonitorState state,
            Models.AppSettings? settings,
            ATrustStatus aTrustStatus)
        {
            return new MonitorSnapshot(
                state.Headline,
                state.Detail,
                state.InternetState,
                state.CampusState,
                state.PreferredNetwork,
                state.WifiState,
                aTrustStatus.DisplayText,
                state.LastAction,
                state.NextCheck,
                state.CurrentIp,
                state.IsInternetAvailable || state.IsCampusLoggedIn,
                aTrustStatus.IsInstalled,
                aTrustStatus.IsConnected,
                settings?.LaunchAtStartup ?? Default.LaunchAtStartup,
                settings?.PreferEthernet ?? Default.PreferEthernet,
                settings?.AutoConnectCampusWifi ?? Default.AutoConnectCampusWifi,
                settings?.OpenATrustAtStartup ?? Default.OpenATrustAtStartup);
        }
    }

    private enum PreferredAppMode
    {
        Default,
        AllowDark
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate PreferredAppMode SetPreferredAppModeDelegate(PreferredAppMode appMode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void FlushMenuThemesDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool AllowDarkModeForWindowDelegate(nint hwnd, bool allow);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nuint dwItemData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool InsertMenuItem(nint hMenu, uint item, bool fByPosition, ref MENUITEMINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenuEx(nint hmenu, int uFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, nint lpProcName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
