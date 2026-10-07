using XenMotion.UI.Tray.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace XenMotion.UI.Services;

/// <summary>
/// Controls the activation, visibility, and foreground focusing of the WinUI 3 main window.
/// </summary>
public sealed class WindowManagementService : IWindowManagementService
{
    private readonly ILogger<WindowManagementService> _logger;
    private readonly SingleWindowController<Window> _windows = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isVisible;
    public void SetMainWindowFactory(Action factory) => _windows.Factory = factory;

    public bool IsVisible => _isVisible;
    public IntPtr MainWindowHwnd => _hwnd;

    public WindowManagementService(ILogger<WindowManagementService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterMainWindow(Window window)
    {
        _windows.Register(window);
        _hwnd = WindowNative.GetWindowHandle(window);
        _logger.LogInformation("Registered MainWindow with HWND: 0x{Hwnd:X}.", _hwnd.ToInt64());
    }

    public void ShowAndActivate()
    {
        // WinUI construction and Activate can pump messages, including another tray selection.
        // Protect the whole operation, even before RegisterMainWindow has run.
        _windows.Show(window =>
        {
            _logger.LogInformation("Showing and activating main application window.");
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(_hwnd);
            }
            window.Activate();
            _isVisible = true;
        });
    }

    public void Hide()
    {
        _windows.Hide(_ =>
        {
            if (_hwnd == IntPtr.Zero) return;
            _logger.LogInformation("Hiding main window to system tray.");
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            _isVisible = false;
        });
    }
}
