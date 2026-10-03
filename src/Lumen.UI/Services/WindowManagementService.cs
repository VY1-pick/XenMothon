using Lumen.UI.Tray.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Lumen.UI.Services;

/// <summary>
/// Controls the activation, visibility, and foreground focusing of the WinUI 3 main window.
/// </summary>
public sealed class WindowManagementService : IWindowManagementService
{
    private readonly ILogger<WindowManagementService> _logger;
    private Window? _mainWindow;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isVisible;

    public bool IsVisible => _isVisible;
    public IntPtr MainWindowHwnd => _hwnd;

    public WindowManagementService(ILogger<WindowManagementService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterMainWindow(Window window)
    {
        _mainWindow = window ?? throw new ArgumentNullException(nameof(window));
        _hwnd = WindowNative.GetWindowHandle(window);
        _logger.LogInformation("Registered MainWindow with HWND: 0x{Hwnd:X}.", _hwnd.ToInt64());
    }

    public void ShowAndActivate()
    {
        if (_mainWindow == null)
        {
            _logger.LogWarning("Cannot show window: MainWindow is not registered.");
            return;
        }

        _logger.LogInformation("Showing and activating main application window.");

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        _mainWindow.Activate();
        _isVisible = true;
    }

    public void Hide()
    {
        if (_mainWindow == null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _logger.LogInformation("Hiding main window to system tray.");
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        _isVisible = false;
    }
}
