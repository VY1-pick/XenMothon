using System.ComponentModel;
using System.Runtime.InteropServices;
using XenMotion.Application.Services;
using XenMotion.UI.Tray.Win32;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Tray;

/// <summary>
/// Native Windows System Tray implementation using Shell_NotifyIconW and a lightweight Win32 message window.
/// </summary>
public sealed class SystemTrayManager : ISystemTrayManager
{
    private const uint TrayIconId = 1001;
    private const uint CmdOpen = 2001;
    private const uint CmdPauseResume = 2002;
    private const uint CmdStop = 2003;
    private const uint CmdPlay = 2004;
    private const uint CmdNext = 2005;
    private const uint CmdMute = 2006;
    private const uint CmdExit = 2007;
    private const uint CmdSettings = 2008, CmdScreenshot = 2009;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _singleClickTimer;
    private long _ignoreSelectUntil;
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    private const uint NiifUser = 0x00000004;
    private const uint NiifLargeIcon = 0x00000020;

    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly ILogger<SystemTrayManager> _logger;
    private readonly NativeMethods.WndProc _wndProcDelegate;

    private string? _className;
    private IntPtr _messageHwnd = IntPtr.Zero;
    private NativeMethods.NOTIFYICONDATA _notifyIconData;
    private IntPtr _trayIconHandle = IntPtr.Zero;
    private bool _ownsTrayIconHandle;
    private bool _trayIconAdded;
    private bool _usesVersion4Callbacks;
    private bool _isInitialized;
    private bool _isDisposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ScreenshotRequested;
    public event EventHandler? PauseResumeRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? PlayRequested;
    public event EventHandler? NextWallpaperRequested;
    public event EventHandler? MuteRequested;
    public event EventHandler? ExitRequested;

    /// <summary>True only when the shell accepted the tray icon and the manager can be used.</summary>
    public bool IsInitialized => _isInitialized && _trayIconAdded && !_isDisposed;

    public SystemTrayManager(
        IWallpaperEngineOrchestrator orchestrator,
        ILogger<SystemTrayManager> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _wndProcDelegate = TrayWndProc;
    }

    public void Initialize()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            CreateMessageWindow();
            CreateTrayIcon();
            _isInitialized = true;
            _logger.LogInformation("System Tray icon initialized successfully.");
        }
        catch (Exception ex)
        {
            // A failed tray setup must not leave the app in an invisible, unreachable state.
            CleanupNativeResources();
            _isInitialized = false;
            _logger.LogError(ex, "Failed to initialize System Tray icon.");
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (!IsInitialized)
        {
            return;
        }

        // Windows suppresses a balloon whose text is identical to the previous one unless the
        // previous balloon was cleared first. Clear it, then show the new one.
        _notifyIconData.uFlags = NativeMethods.NIF_INFO;
        _notifyIconData.szInfoTitle = string.Empty;
        _notifyIconData.szInfo = string.Empty;
        _notifyIconData.dwInfoFlags = 0;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyIconData);

        // Vista and later use hBalloonIcon for NIIF_USER. hIcon alone only identifies
        // the notification-area icon and may leave the balloon's app-logo slot blank.
        _notifyIconData.uFlags = NativeMethods.NIF_INFO | NativeMethods.NIF_ICON;
        _notifyIconData.hIcon = _trayIconHandle;
        _notifyIconData.hBalloonIcon = _trayIconHandle;
        _notifyIconData.szInfoTitle = Truncate(title, 63);
        _notifyIconData.szInfo = Truncate(message, 255);
        _notifyIconData.dwInfoFlags = NiifUser | NiifLargeIcon;

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyIconData))
        {
            _logger.LogWarning(
                "Shell_NotifyIcon returned false while showing a notification. Win32Error={Error}.",
                Marshal.GetLastWin32Error());
        }
    }

    public void UpdateToolTip(string tooltip)
    {
        if (!IsInitialized)
        {
            return;
        }

        _notifyIconData.uFlags = NativeMethods.NIF_TIP;
        _notifyIconData.szTip = Truncate(tooltip, 127);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyIconData))
        {
            _logger.LogWarning(
                "Shell_NotifyIcon returned false while updating the tray tooltip. Win32Error={Error}.",
                Marshal.GetLastWin32Error());
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private void CreateMessageWindow()
    {
        _className = $"XenMotion_TrayMsgWin_{Guid.NewGuid():N}";

        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0x0008, // Receive double-click messages.
            lpfnWndProc = _wndProcDelegate,
            lpszClassName = _className,
            hInstance = IntPtr.Zero
        };

        if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the tray message window class.");
        }

        // HWND_MESSAGE makes this an actual message-only window rather than an invisible top-level window.
        _messageHwnd = NativeMethods.CreateWindowEx(
            0,
            _className,
            "XenMotionTrayWindow",
            0,
            0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_messageHwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the Win32 message-only window for the system tray.");
        }
    }

    private void CreateTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "xenmotion-logo.ico");
        IntPtr hIcon = IntPtr.Zero;
        if (File.Exists(iconPath))
        {
            hIcon = NativeMethods.LoadImage(
                IntPtr.Zero,
                iconPath,
                NativeMethods.IMAGE_ICON,
                32,
                32,
                NativeMethods.LR_LOADFROMFILE);
            _ownsTrayIconHandle = hIcon != IntPtr.Zero;
        }

        if (hIcon == IntPtr.Zero)
        {
            hIcon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IDI_APPLICATION);
            _ownsTrayIconHandle = false;
        }

        if (hIcon == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load either the XenMotion or fallback tray icon.");
        }

        _trayIconHandle = hIcon;
        _notifyIconData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _messageHwnd,
            uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = hIcon,
            szTip = "XenMotion — Live wallpapers",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            hBalloonIcon = IntPtr.Zero
        };

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _notifyIconData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Windows shell rejected the XenMotion tray icon.");
        }

        _trayIconAdded = true;
        _notifyIconData.uFlags = 0;
        _notifyIconData.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        _usesVersion4Callbacks = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref _notifyIconData);

        if (!_usesVersion4Callbacks)
        {
            _logger.LogWarning(
                "The shell did not accept NOTIFYICON_VERSION_4; using legacy tray callback message decoding. Win32Error={Error}.",
                Marshal.GetLastWin32Error());
        }
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_TRAYICON)
        {
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        try
        {
            long packedLParam = lParam.ToInt64();
            uint mouseMessage;
            uint iconId;

            if (_usesVersion4Callbacks)
            {
                // NOTIFYICON_VERSION_4 packs the event in LOWORD(lParam) and icon ID in HIWORD(lParam).
                mouseMessage = (uint)(packedLParam & 0xFFFF);
                iconId = (uint)((packedLParam >> 16) & 0xFFFF);
            }
            else
            {
                // Legacy callbacks use wParam for the icon ID and the complete lParam for the event.
                mouseMessage = unchecked((uint)packedLParam);
                iconId = unchecked((uint)wParam.ToInt64());
            }

            if (iconId != TrayIconId)
            {
                return IntPtr.Zero;
            }

            switch (mouseMessage)
            {
                case NativeMethods.WM_LBUTTONDBLCLK:
                    _singleClickTimer?.Stop();
                    _ignoreSelectUntil = Environment.TickCount64 + GetDoubleClickTime();
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
                case NativeMethods.NIN_KEYSELECT:
                    ShowContextMenu();
                    return IntPtr.Zero;
                case NativeMethods.NIN_SELECT:
                    if (_usesVersion4Callbacks) ScheduleSingleClick();
                    return IntPtr.Zero;
                case NativeMethods.WM_LBUTTONUP:
                    if (!_usesVersion4Callbacks) ScheduleSingleClick();
                    return IntPtr.Zero;

                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_CONTEXTMENU:
                    ShowContextMenu();
                    return IntPtr.Zero;

                default:
                    return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            // Never allow a managed exception to unwind through the native window procedure.
            _logger.LogError(ex, "Failed while processing a System Tray callback.");
            return IntPtr.Zero;
        }
    }

    private void ScheduleSingleClick()
    {
        if (Environment.TickCount64 < _ignoreSelectUntil) return;
        if (_singleClickTimer is null)
        {
            _singleClickTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _singleClickTimer.IsRepeating = false;
            _singleClickTimer.Tick += (_, _) => { try { ShowContextMenu(); } catch (Exception ex) { _logger.LogError(ex, "Could not open tray menu."); } };
        }
        _singleClickTimer.Stop();
        _singleClickTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime() + 50);
        _singleClickTimer.Start();
    }

    private void ShowContextMenu()
    {
        _singleClickTimer?.Stop();
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            _logger.LogWarning("GetCursorPos failed while opening the tray context menu. Win32Error={Error}.", Marshal.GetLastWin32Error());
            return;
        }

        IntPtr hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            _logger.LogWarning("CreatePopupMenu failed. Win32Error={Error}.", Marshal.GetLastWin32Error());
            return;
        }

        try
        {
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdOpen, "Open XenMotion");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdSettings, "Settings");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdScreenshot, "Take screenshot…");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, 0, null);

            string pauseResumeText = _orchestrator.IsGloballyPaused ? "Resume Wallpapers" : "Pause Wallpapers";
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdPauseResume, pauseResumeText);
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdPlay, "Play Wallpapers");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdStop, "Stop Wallpapers");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdNext, "Next Wallpaper");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdMute, "Mute / Unmute Audio");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CmdExit, "Exit XenMotion");
            NativeMethods.SetMenuDefaultItem(hMenu, CmdOpen, 0);

            // Win32 requires a foreground window for reliable dismissal on outside click.
            NativeMethods.SetForegroundWindow(_messageHwnd);

            int cmd = NativeMethods.TrackPopupMenuEx(
                hMenu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY | NativeMethods.TPM_BOTTOMALIGN,
                pt.X,
                pt.Y,
                _messageHwnd,
                IntPtr.Zero);

            NativeMethods.PostMessage(_messageHwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch ((uint)cmd)
            {
                case CmdOpen:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdPauseResume:
                    PauseResumeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdStop:
                    StopRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdPlay:
                    PlayRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdNext:
                    NextWallpaperRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdMute:
                    MuteRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdSettings:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdScreenshot:
                    ScreenshotRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(hMenu);
        }
    }

    private void CleanupNativeResources()
    {
        if (_trayIconAdded && _messageHwnd != IntPtr.Zero)
        {
            _notifyIconData.uFlags = 0;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _notifyIconData);
            _trayIconAdded = false;
        }

        if (_messageHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_messageHwnd);
            _messageHwnd = IntPtr.Zero;
        }

        if (_ownsTrayIconHandle && _trayIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayIconHandle);
        }
        _trayIconHandle = IntPtr.Zero;
        _ownsTrayIconHandle = false;
        _usesVersion4Callbacks = false;

        if (!string.IsNullOrEmpty(_className))
        {
            NativeMethods.UnregisterClass(_className, IntPtr.Zero);
            _className = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _singleClickTimer?.Stop();
        _isDisposed = true;
        CleanupNativeResources();
        _isInitialized = false;
        _logger.LogInformation("System Tray manager disposed.");
    }
}
