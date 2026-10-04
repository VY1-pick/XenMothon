using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Core.Models;

namespace Lumen.UI.Desktop;

/// <summary>
/// A native child window parented to the desktop WorkerW (behind the icons) that covers one monitor.
/// Video renderers draw their frames into this window. It owns a dedicated thread so the window
/// survives independently of whichever worker thread requested it.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DesktopSurface : IDisposable
{
    private const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;
    private const uint PmRemove = 1;
    private const int SmXVirtual = 76, SmYVirtual = 77;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _created = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private IntPtr _hwnd;

    public IntPtr Handle => _hwnd;

    /// <summary>Size of the surface in pixels (viewport origin is always 0,0 inside the surface).</summary>
    public MonitorBounds LocalBounds { get; }

    private DesktopSurface(IntPtr parent, MonitorBounds monitor)
    {
        LocalBounds = new MonitorBounds(0, 0, monitor.Width, monitor.Height);
        int x = monitor.X - GetSystemMetrics(SmXVirtual);
        int y = monitor.Y - GetSystemMetrics(SmYVirtual);

        _thread = new Thread(() => Run(parent, x, y, monitor.Width, monitor.Height))
        {
            IsBackground = true,
            Name = "Lumen.DesktopSurface"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _created.Wait(TimeSpan.FromSeconds(5));
    }

    public static DesktopSurface? TryCreate(IntPtr parent, MonitorBounds monitor)
    {
        if (parent == IntPtr.Zero || monitor.IsEmpty) return null;
        var surface = new DesktopSurface(parent, monitor);
        if (surface._hwnd != IntPtr.Zero) return surface;
        surface.Dispose();
        return null;
    }

    private void Run(IntPtr parent, int x, int y, int w, int h)
    {
        try
        {
            _hwnd = CreateWindowEx(0, "Static", "LumenWallpaperSurface",
                WsChild | WsVisible | WsClipSiblings, x, y, w, h, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
            _stop.Wait(50);
        }

        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(2000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
