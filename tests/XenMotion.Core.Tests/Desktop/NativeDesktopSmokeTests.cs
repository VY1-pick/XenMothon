using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.Core.Tests.Rendering;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering;
using XenMotion.UI.Rendering.Direct3D;
using Xunit;

namespace XenMotion.Core.Tests.Desktop;

public class NativeDesktopSmokeTests
{
    [Fact]
    public async Task InteractiveDesktop_ShowsStillFrameAndVideoBehindIcons()
    {
        // Explicit opt-in: requires an exposed Explorer desktop at pixel (920,520).
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ||
            Environment.GetEnvironmentVariable("XENMOTION_DESKTOP_SMOKE") != "1") return;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var name = new StringBuilder(256); GetClassName(WindowFromPoint(new POINT { X = 920, Y = 520 }), name, 256);
            if (name.ToString() is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman") break;
            await Task.Delay(250);
        }
        await using var host = new DesktopWallpaperHost(NullLogger<DesktopWallpaperHost>.Instance);
        Assert.True(await host.AttachToDesktopAsync());
        using var surface = DesktopSurface.TryCreate(host.DesktopWorkerWHwnd, new MonitorBounds(900, 500, 64, 36));
        Assert.NotNull(surface);
        Assert.True(IsWindowVisible(surface.Handle));
        var shellParent = GetParent(host.DesktopWorkerWHwnd);
        if (shellParent != IntPtr.Zero && (GetWindowLongPtr(shellParent, -20).ToInt64() & 0x00200000) != 0)
        {
            IntPtr view = FindWindowEx(shellParent, IntPtr.Zero, "SHELLDLL_DefView", null);
            Assert.Equal(shellParent, GetParent(surface.Handle));
            Assert.NotEqual(0, GetWindowLongPtr(surface.Handle, -20).ToInt64() & 0x00080000);
            Assert.Equal(surface.Handle, GetWindow(view, 2)); // Immediately below the icon view.
        }

        using (var presenter = new Direct3D11VideoPresenter(NullLogger.Instance))
        {
            var opaque = GCHandle.Alloc(new uint[] { 0xFF123456, 0xFF123456, 0xFF123456, 0xFF123456 }, GCHandleType.Pinned);
            try
            {
                presenter.Initialize(surface.Handle, surface.LocalBounds);
                presenter.PresentFrame(opaque.AddrOfPinnedObject(), 2, 2, PlaybackScalingMode.Stretch);
                await Task.Delay(100);
                var hit = WindowFromPoint(new POINT { X = 920, Y = 520 }); var name = new StringBuilder(256); GetClassName(hit, name, 256); Console.WriteLine($"Screen hit {name}; pixel {ReadScreenPixel():X8}");
                Assert.Equal(0x00563412u, ReadScreenPixel()); // COLORREF is BGR, input is BGRA.
                Console.WriteLine("Still frame verified on the actual desktop screen.");
            }
            finally { opaque.Free(); }
        }

        string mediaPath = Path.Combine(Path.GetTempPath(), $"xenmotion-desktop-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(mediaPath, NativeMediaFoundationTests.CreateAvi());
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(surface.Handle, surface.LocalBounds);
            await renderer.LoadAsync(mediaPath, new WallpaperSettings { Loop = true });
            await renderer.PlayAsync();
            await Task.Delay(400);
            await renderer.PauseAsync();
            await Task.Delay(100); // Allow an in-flight frame to finish.
            Assert.Equal(WallpaperState.Paused, renderer.CurrentState);
            uint pixel = ReadScreenPixel();
            Assert.NotEqual(0xFFFFFFFFu, pixel);
            Assert.NotEqual(0x00563412u, pixel);
            Assert.Equal(pixel & 255, (pixel >> 8) & 255); // Generated AVI frames are grayscale.
            Assert.Equal(pixel & 255, (pixel >> 16) & 255);
            Console.WriteLine($"Video frame verified on the actual desktop screen: 0x{pixel:X8}.");
            await renderer.StopAsync();
        }
        finally { File.Delete(mediaPath); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    private static uint ReadScreenPixel()
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        try { return GetPixel(screen, 920, 520); }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
