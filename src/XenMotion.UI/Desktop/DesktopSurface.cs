using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.Versioning;
using XenMotion.Core.Models;
using System.Collections.Concurrent;

namespace XenMotion.UI.Desktop;

/// <summary>
/// A native child window behind the icons, parented to WorkerW or raised-desktop Progman.
/// Video renderers draw their frames into this window. It owns a dedicated thread so the window
/// survives independently of whichever worker thread requested it.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DesktopSurface : IDisposable
{
    private const uint WsChild = 0x40000000, WsClipSiblings = 0x04000000;
    private const uint PmRemove = 1;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _created = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private IntPtr _hwnd;
    private Exception? _creationError;
    private readonly WndProc _windowProc;
    private string? _className;
    private readonly ConcurrentQueue<(MonitorBounds Bounds, TaskCompletionSource Completion)> _resizes = new();
    public event EventHandler? PaintRequested;

    public IntPtr Handle => _hwnd;

    /// <summary>Size of the surface in pixels (viewport origin is always 0,0 inside the surface).</summary>
    public MonitorBounds LocalBounds { get; private set; }

    public Task ResizeAsync(MonitorBounds monitor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (monitor.IsEmpty) throw new ArgumentOutOfRangeException(nameof(monitor));
        if (_stop.IsSet || _hwnd == IntPtr.Zero) throw new ObjectDisposedException(nameof(DesktopSurface));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _resizes.Enqueue((monitor, completion));
        if (!PostMessage(_hwnd, 0x8001, IntPtr.Zero, IntPtr.Zero))
            completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error()));
        return completion.Task.WaitAsync(ct);
    }

    private DesktopSurface(IntPtr parent, MonitorBounds monitor)
    {
        _windowProc = WindowProcedure;
        LocalBounds = new MonitorBounds(0, 0, monitor.Width, monitor.Height);
        int x = monitor.X;
        int y = monitor.Y;

        _thread = new Thread(() => Run(parent, x, y, monitor.Width, monitor.Height))
        {
            IsBackground = true,
            Name = "XenMotion.DesktopSurface"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_created.Wait(TimeSpan.FromSeconds(5)))
            _creationError = new TimeoutException("Desktop surface creation timed out.");
    }

    public static DesktopSurface? TryCreate(IntPtr parent, MonitorBounds monitor)
    {
        if (parent == IntPtr.Zero || monitor.IsEmpty) return null;
        var surface = new DesktopSurface(parent, monitor);
        if (surface._hwnd != IntPtr.Zero) return surface;
        surface.Dispose();
        if (surface._creationError != null) throw surface._creationError;
        return null;
    }

    private void Run(IntPtr parent, int x, int y, int w, int h)
    {
        try
        {
            // Raised desktops (Windows 11 HDR desktop) compose the stock wallpaper in a
            // WorkerW child. Put the DXGI-composed surface below DefView and above that
            // background. Parenting inside DefView instead would hide it under the icon list.
            IntPtr shellParent = GetParent(parent);
            bool raisedDesktop = shellParent != IntPtr.Zero &&
                (GetWindowLongPtr(shellParent, -20).ToInt64() & 0x00200000) != 0;
            IntPtr insertAfter = new(1);
            if (raisedDesktop)
            {
                insertAfter = Win32.DesktopNativeMethods.FindWindowEx(shellParent, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (insertAfter == IntPtr.Zero)
                    throw new InvalidOperationException("The raised desktop icon layer was not found.");
                parent = shellParent;
            }
            // Create in our process first. Creating directly with Explorer as the parent can
            // be rejected with ERROR_ACCESS_DENIED; cross-process parenting uses SetParent.
            var parentDpi = GetWindowDpiAwarenessContext(parent);
            if (parentDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(parentDpi);
            _className = "XenMotionSurface_" + Guid.NewGuid().ToString("N");
            IntPtr module = GetModuleHandle(null);
            var windowClass = new WNDCLASS { lpfnWndProc = _windowProc, hInstance = module, lpszClassName = _className };
            if (RegisterClass(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _hwnd = CreateWindowEx(0x08000080u | (raisedDesktop ? 0x00080000u : 0u), _className, "XenMotionWallpaperSurface",
                0x80000000 | WsClipSiblings, x, y, w, h, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) _creationError = new Win32Exception(Marshal.GetLastWin32Error(), $"Desktop surface creation failed (Win32 {Marshal.GetLastWin32Error()}, parent 0x{parent:X})");
            else
            {
                SetWindowLongPtr(_hwnd, -16, new IntPtr(WsChild | WsClipSiblings));

                SetParent(_hwnd, parent);
                int attachError = Marshal.GetLastWin32Error();
                if (GetParent(_hwnd) != parent)
                {
                    _creationError = new Win32Exception(attachError, $"Could not attach wallpaper surface to Explorer (Win32 {attachError}, parent 0x{parent:X}).");
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }
                else
                {
                    if (raisedDesktop && !SetLayeredWindowAttributes(_hwnd, 0, 255, 2))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make the wallpaper surface opaque.");
                    var point = new POINT { x = x, y = y };
                    MapWindowPoints(IntPtr.Zero, parent, ref point, 1);
                    if (!SetWindowPos(_hwnd, insertAfter, point.x, point.y, w, h, 0x0070) || !IsWindowVisible(_hwnd))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not show the wallpaper surface.");

                }
            }
        }
        catch (Exception ex)
        {
            _creationError = ex;
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        finally
        {
            _created.Set();
        }

        if (_hwnd == IntPtr.Zero) return;

        while (!_stop.IsSet)
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            // Sleep until either shutdown or a native window message; no 20 Hz idle polling.
            MsgWaitForMultipleObjects(1, new[] { _stop.WaitHandle.SafeWaitHandle.DangerousGetHandle() }, false, uint.MaxValue, 0x04FF);
        }

        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        if (_className != null) UnregisterClass(_className, GetModuleHandle(null));
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x8001)
        {
            while (_resizes.TryDequeue(out var request))
            {
                try
                {
                    var point = new POINT { x = request.Bounds.X, y = request.Bounds.Y };
                    MapWindowPoints(IntPtr.Zero, GetParent(hwnd), ref point, 1);
                    if (!SetWindowPos(hwnd, IntPtr.Zero, point.x, point.y, request.Bounds.Width, request.Bounds.Height, 0x0014))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    LocalBounds = new MonitorBounds(0, 0, request.Bounds.Width, request.Bounds.Height);
                    request.Completion.TrySetResult();
                }
                catch (Exception ex) { request.Completion.TrySetException(ex); }
            }
            return IntPtr.Zero;
        }
        if (message == 0x0014) return new IntPtr(1); // Renderers own background painting.
        var result = DefWindowProc(hwnd, message, wParam, lParam);
        if (message == 0x000F)
        {
            try { PaintRequested?.Invoke(this, EventArgs.Empty); }
            catch { /* Never unwind managed exceptions through a native callback. */ }
        }
        return result;
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(2000);
        while (_resizes.TryDequeue(out var request)) request.Completion.TrySetException(new ObjectDisposedException(nameof(DesktopSurface)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT point, uint count);
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName; public string lpszClassName;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WNDCLASS windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);
}
