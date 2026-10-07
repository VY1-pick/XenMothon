using System.Diagnostics;
using System.Runtime.InteropServices;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering.Direct3D;
using XenMotion.UI.Rendering.Media;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.UI.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace XenMotion.Core.Tests.Rendering;

public sealed class NativeGpuResolutionTests(ITestOutputHelper output)
{
    [Fact]
    public void MildSharpeningChangesEdgesWithoutIncreasingDimensionsAndGpuFenceCompletes()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 8, 8));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 8, 8);
        var data = Enumerable.Repeat(0x606060, 64).ToArray(); data[4 * 8 + 4] = 0x808080;
        var pixels = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            chain.SetVisualAdjustments(1, 1, 1, .15f);
            chain.Render(pixels.AddrOfPinnedObject(), 8, 8, new ViewportRectangle(0, 0, 8, 8));
            chain.CompleteCalibrationFrame(default);
            Assert.InRange(chain.ReadPixel(4, 4, 8, 8) & 255, 132u, 134u);
            Assert.Equal(1, chain.TextureMipLevels);
            chain.SetVisualAdjustments(1, 1, 1, 0);
            chain.Render(pixels.AddrOfPinnedObject(), 8, 8, new ViewportRectangle(0, 0, 8, 8));
            Assert.Equal(0x808080u, chain.ReadPixel(4, 4, 8, 8));
        }
        finally { pixels.Free(); }
    }

    [Fact]
    public async Task AdaptiveProfileIsAppliedBeforePlaybackWithoutChangingRequestedSettings()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)) return;
        var path=Path.Combine(Path.GetTempPath(),$"adaptive-profile-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path,NativeMediaFoundationTests.CreateAvi());
            using var surface=DesktopSurface.TryCreate(GetDesktopWindow(),new MonitorBounds(-16000,-16000,64,36));
            Assert.NotNull(surface);
            bool called=false;
            Task<WallpaperSettings> Profile(string media,WallpaperSettings settings,MonitorBounds bounds,CancellationToken ct)
            {
                called=true;Assert.Equal(path,media);Assert.Equal(64,bounds.Width);
                Assert.Equal(.7f,settings.Brightness);
                var result=settings.Clone();result.TargetFps=15;result.VideoHeightLimit=720;result.RenderScale=.5f;
                return Task.FromResult(result);
            }
            await using var renderer=new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance,adaptiveQuality:Profile);
            await renderer.InitializeAsync(surface.Handle,surface.LocalBounds);
            var requested=new WallpaperSettings {AutoTunePlayback=true,TargetFps=30,Brightness=.7f};
            await renderer.LoadAsync(path,requested);
            Assert.True(called);Assert.Equal(15,renderer.CurrentFpsLimit);Assert.Equal(30,requested.TargetFps);
            Assert.Null(renderer.CalibrationSummary);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AutomaticCalibrationMeasuresNativePlaybackWithinEconomyLimits()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-calibration-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, NativeMediaFoundationTests.CreateAvi());
            using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 64, 36));
            Assert.NotNull(surface);
            string? report = null;
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance, value => report = value);
            await renderer.InitializeAsync(surface.Handle, surface.LocalBounds);
            await renderer.LoadAsync(path, new WallpaperSettings { AutoTunePlayback = true, VideoHeightLimit = 720, RenderScale = .5f, TargetFps = 15, Sharpness = .15f });
            Assert.NotNull(report);
            Assert.Contains("p90 decode", report);
            Assert.True(renderer.CurrentFpsLimit <= 15);
            Assert.True(renderer.DecodedFrameCount >= 3);
            await renderer.PlayAsync();
            await Task.Delay(100);
            Assert.Equal(WallpaperState.Playing, renderer.CurrentState);
            output.WriteLine(report);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void MipStorageIsAllocatedOnlyForFilteredDownscalingAndPixelsStayCorrect()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 64, 64));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 64, 64);
        var pixels = GCHandle.Alloc(Enumerable.Repeat(0x204080, 64 * 64).ToArray(), GCHandleType.Pinned);
        try
        {
            chain.Render(pixels.AddrOfPinnedObject(), 64, 64, new ViewportRectangle(0, 0, 64, 64));
            Assert.Equal(1, chain.TextureMipLevels);
            Assert.Equal(0x804020u, chain.ReadPixel(16, 16, 64, 64));
            chain.Render(pixels.AddrOfPinnedObject(), 64, 64, new ViewportRectangle(0, 0, 32, 32));
            Assert.Equal(7, chain.TextureMipLevels);
            Assert.Equal(0x804020u, chain.ReadPixel(16, 16, 64, 64));
            chain.SetFiltering(false);
            chain.Render(pixels.AddrOfPinnedObject(), 64, 64, new ViewportRectangle(0, 0, 32, 32));
            Assert.Equal(1, chain.TextureMipLevels);
            Assert.Equal(0x804020u, chain.ReadPixel(16, 16, 64, 64));
        }
        finally { pixels.Free(); }
    }
    [Fact]
    public void EconomyFilteringDrawsPixelsAndRenderScaleTracksDisplayChanges()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 32, 32));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 32, 32);
        chain.SetFiltering(false);
        var pixels = GCHandle.Alloc(Enumerable.Repeat(0x204080, 64 * 64).ToArray(), GCHandleType.Pinned);
        try
        {
            chain.Render(pixels.AddrOfPinnedObject(), 64, 64, new ViewportRectangle(0, 0, 32, 32));
            Assert.Equal(0x804020u, chain.ReadPixel(16, 16, 32, 32));
        }
        finally { pixels.Free(); }
        using var presenter = new Direct3D11VideoPresenter(NullLogger.Instance);
        presenter.Initialize(surface.Handle, new MonitorBounds(0, 0, 32, 32));
        presenter.SetVisualAdjustments(new WallpaperSettings { RenderScale = .5f });
        Assert.Equal(16, presenter.RenderBounds.Width);
        Assert.Equal(32, presenter.CurrentBounds.Width);
        presenter.Resize(new MonitorBounds(0, 0, 2560, 1440));
        Assert.Equal(new MonitorBounds(0, 0, 1280, 720), presenter.RenderBounds);
        presenter.SetVisualAdjustments(new WallpaperSettings { RenderScale = 1 });
        Assert.Equal(2560, presenter.RenderBounds.Width);
    }
    [Fact]
    public void VisualSettingsChangeActualGpuPixelsAndNeutralRestoresOriginal()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 32, 32));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 32, 32);
        var pixel = GCHandle.Alloc(new[] { 0x204080 }, GCHandleType.Pinned);
        uint Render(float brightness, float contrast, float saturation)
        {
            chain.SetVisualAdjustments(brightness, contrast, saturation);
            chain.Render(pixel.AddrOfPinnedObject(), 1, 1, new ViewportRectangle(0, 0, 32, 32));
            uint result = chain.ReadPixel(16, 16, 32, 32);
            chain.PresentGpu();
            return result;
        }
        try
        {
            Assert.Equal(0x804020u, Render(1, 1, 1));
            Assert.Equal(0x402010u, Render(0.5f, 1, 1));
            Assert.Equal(0u, Render(0, 1, 1));
            Assert.Equal(0x808080u, Render(1, 0, 1));
            uint gray = Render(1, 1, 0);
            Assert.Equal(gray & 255, (gray >> 8) & 255);
            Assert.Equal(gray & 255, (gray >> 16) & 255);
            Assert.Equal(0x804020u, Render(1, 1, 1));
        }
        finally { pixel.Free(); }
    }
    [Fact]
    public void ImportedGpuPixelsMatchCpuReference()
    {
        string? path = Environment.GetEnvironmentVariable("XENMOTION_VIDEO_BENCHMARK");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || string.IsNullOrWhiteSpace(path)) return;
        using var cpu = new MediaFoundationPlaybackEngine(NullLogger.Instance);
        cpu.Open(path);
        int width = cpu.VideoWidth, height = cpu.VideoHeight;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, width, height));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, width, height);
        using var gpu = new MediaFoundationPlaybackEngine(NullLogger.Instance);
        gpu.Open(path, chain.Device);
        using var reference = cpu.ReadNextFrame(false);
        using var frame = gpu.ReadNextFrame(false);
        Assert.NotNull(reference);
        Assert.NotNull(frame);
        Assert.NotEqual(IntPtr.Zero, frame.TexturePointer);
        Assert.Equal(reference.Timestamp, frame.Timestamp);
        chain.RenderTexture(frame.TexturePointer, frame.TextureSubresource, width, height, new ViewportRectangle(0, 0, width, height));
        int nonBlack = 0;
        for (int row = 1; row < 4; row++)
            for (int column = 1; column < 4; column++)
            {
                int x = width * column / 4, y = height * row / 4;
                uint expected = (uint)Marshal.ReadInt32(reference.DataPointer, (y * width + x) * 4);
                uint actual = chain.ReadPixel(x, y, width, height);
                if ((expected & 0xFFFFFF) != 0) nonBlack++;
                for (int channel = 0; channel < 3; channel++)
                    Assert.InRange(Math.Abs((int)((expected >> (16 - channel * 8)) & 255) - (int)((actual >> (channel * 8)) & 255)), 0, 8);
            }
        Assert.True(nonBlack > 0);
        chain.PresentGpu();
        gpu.SeekToStart();
        using var rewound = gpu.ReadNextFrame(false);
        Assert.NotNull(rewound);
        Assert.Equal(frame.Timestamp, rewound.Timestamp);
        output.WriteLine("GPU output matches CPU reference at nine native-resolution points (orientation/color tolerance <= 8); GPU seek verified.");
    }

    [Fact]
    public async Task ImportedGpuPlaybackPauseResizeResume()
    {
        string? path = Environment.GetEnvironmentVariable("XENMOTION_VIDEO_BENCHMARK");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || string.IsNullOrWhiteSpace(path)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 3840, 2160));
        Assert.NotNull(surface);
        await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
        await renderer.InitializeAsync(surface.Handle, surface.LocalBounds);
        await renderer.LoadAsync(path, new WallpaperSettings { TargetFps = 30, IsMuted = true, Loop = true });
        await renderer.PlayAsync();
        await Task.Delay(700);
        await renderer.PauseAsync();
        await Task.Delay(100);
        long paused = renderer.DecodedFrameCount;
        Assert.True(paused >= 10);
        Assert.Equal(paused, renderer.GpuDecodedFrameCount);
        await Task.Delay(200);
        Assert.Equal(paused, renderer.DecodedFrameCount);
        await surface.ResizeAsync(new MonitorBounds(-16000, -16000, 2560, 1440));
        await renderer.ResizeAsync(surface.LocalBounds);
        await renderer.PlayAsync();
        await Task.Delay(350);
        Assert.Equal(WallpaperState.Playing, renderer.CurrentState);
        Assert.InRange(renderer.DecodedFrameCount - paused, 6, 15);
        await renderer.StopAsync();
        Assert.Equal(WallpaperState.Stopped, renderer.CurrentState);
        output.WriteLine($"Actual 4K GPU playback: {paused} decoded frames before pause; pause, resize, resume and stop verified.");
    }

    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(3840, 2160, 2560, 1440)]
    [InlineData(3840, 2160, 3840, 2160)]
    [InlineData(1920, 1080, 1080, 1920)]
    [InlineData(2560, 1440, 3440, 1440)]
    [InlineData(1280, 720, 1366, 768)]
    [InlineData(7680, 4320, 3840, 2160)]
    public async Task GpuOutputKeepsColorsOrientationAndFitBorders(int sourceWidth, int sourceHeight, int width, int height)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        // An off-screen window exercises real D3D resources at each size without changing display settings.
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, width, height));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, width, height);
        int[] pixels = new int[sourceWidth * sourceHeight];
        for (int y = 0; y < sourceHeight; y++)
            for (int x = 0; x < sourceWidth; x++)
                pixels[y * sourceWidth + x] = y < sourceHeight / 2 ? (x < sourceWidth / 2 ? 0xFF0000 : 0x00FF00) : (x < sourceWidth / 2 ? 0x0000FF : 0xFFFFFF);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var bounds = new MonitorBounds(0, 0, width, height);
            var fit = VideoViewportCalculator.Calculate(sourceWidth, sourceHeight, bounds, PlaybackScalingMode.Fit);
            chain.Render(handle.AddrOfPinnedObject(), sourceWidth, sourceHeight, fit);
            uint Pixel(int x, int y) => chain.ReadPixel(x, y, width, height);
            Assert.Equal(0x0000FFu, Pixel(fit.X + fit.Width / 4, fit.Y + fit.Height / 4));
            Assert.Equal(0x00FF00u, Pixel(fit.X + fit.Width * 3 / 4, fit.Y + fit.Height / 4));
            Assert.Equal(0xFF0000u, Pixel(fit.X + fit.Width / 4, fit.Y + fit.Height * 3 / 4));
            if (fit.Y > 0 || fit.X > 0) Assert.Equal(0u, Pixel(0, 0));
            chain.PresentGpu();
            // Recreate the back buffer, then change source size on the same device.
            await surface.ResizeAsync(new MonitorBounds(-16000, -16000, 720, 1280));
            Assert.Equal(new MonitorBounds(0, 0, 720, 1280), surface.LocalBounds);
            Assert.True(GetClientRect(surface.Handle, out var client));
            Assert.Equal(720, client.Right);
            Assert.Equal(1280, client.Bottom);
            chain.Resize(720, 1280);
            chain.Render(handle.AddrOfPinnedObject(), sourceWidth, sourceHeight, new ViewportRectangle(0, 0, 720, 1280));

            Assert.Equal(0xFFFFFFu, chain.ReadPixel(540, 960, 720, 1280));
            chain.PresentGpu();
            // Fill must clip a larger viewport correctly, including a negative origin.
            var fill = VideoViewportCalculator.Calculate(sourceWidth, sourceHeight, new MonitorBounds(0, 0, 720, 1280), PlaybackScalingMode.Fill);
            chain.Render(handle.AddrOfPinnedObject(), sourceWidth, sourceHeight, fill);
            Assert.Equal(0x0000FFu, chain.ReadPixel(180, 320, 720, 1280));
            Assert.Equal(0xFFFFFFu, chain.ReadPixel(540, 960, 720, 1280));
            chain.PresentGpu();
            int[] tiny = Enumerable.Repeat(0x123456, 64 * 36).ToArray();
            var small = GCHandle.Alloc(tiny, GCHandleType.Pinned);
            try { chain.Render(small.AddrOfPinnedObject(), 64, 36, new ViewportRectangle(0, 0, 720, 1280)); }
            finally { small.Free(); }

            Assert.Equal(0x563412u, chain.ReadPixel(360, 640, 720, 1280));
            chain.PresentGpu();
        }
        finally { handle.Free(); }
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(1080, 1920)]
    [InlineData(3440, 1440)]
    public void ImportedVideoDecodeAndGpuPresentationTiming(int width, int height)
    {
        string? path = Environment.GetEnvironmentVariable("XENMOTION_VIDEO_BENCHMARK");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || string.IsNullOrWhiteSpace(path)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, width, height));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, width, height);
        using var engine = new MediaFoundationPlaybackEngine(NullLogger.Instance);
        bool useGpu = Environment.GetEnvironmentVariable("XENMOTION_CPU_BASELINE") != "1";
        engine.Open(path, useGpu ? chain.Device : IntPtr.Zero);
        double decode = 0, present = 0;
        int frames = 0, gpuFrames = 0;
        var viewport = VideoViewportCalculator.Calculate(engine.VideoWidth, engine.VideoHeight, new MonitorBounds(0, 0, width, height), PlaybackScalingMode.Fill);
        for (int i = 0; i < 75; i++)
        {
            var watch = Stopwatch.StartNew();
            using var frame = engine.ReadNextFrame(true);
            if (frame is null) continue;
            double decodeMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            if (frame.TexturePointer != IntPtr.Zero)
            {
                chain.RenderTexture(frame.TexturePointer, frame.TextureSubresource, frame.Width, frame.Height, viewport);
                gpuFrames++;
            }
            else chain.Render(frame.DataPointer, frame.Width, frame.Height, viewport);
            chain.PresentGpu();
            if (i < 5) continue;
            decode += decodeMs; present += watch.Elapsed.TotalMilliseconds; frames++;
        }
        Assert.True(frames > 0);
        if (useGpu && Environment.GetEnvironmentVariable("XENMOTION_REQUIRE_GPU") == "1") Assert.Equal(75, gpuFrames);
        output.WriteLine($"GPU-resident frames: {gpuFrames}/75; GPU requested: {useGpu}.");
        output.WriteLine($"Imported video {engine.VideoWidth}x{engine.VideoHeight} @ {engine.NativeFps:F2} FPS to {width}x{height}, {frames} warmed frames: decode {decode / frames:F2} ms; GPU upload/filter/Present {present / frames:F2} ms; total {(decode + present) / frames:F2} ms/frame; source budget {1000 / engine.NativeFps:F2} ms/frame.");
    }

    [Fact]
    public void FourKPresentationTiming()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 1920, 1080));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 1920, 1080);
        var pixels = GCHandle.Alloc(new int[3840 * 2160], GCHandleType.Pinned);
        try
        {
            var bmi = new D3D11NativeMethods.BITMAPINFO { bmiHeader = new() { biSize = 40, biWidth = 3840, biHeight = -2160, biPlanes = 1, biBitCount = 32 } };
            void Gdi() { var dc = chain.GetDc(); SetStretchBltMode(dc, 4); SetBrushOrgEx(dc, 0, 0, IntPtr.Zero); D3D11NativeMethods.StretchDIBits(dc, 0, 0, 1920, 1080, 0, 0, 3840, 2160, pixels.AddrOfPinnedObject(), ref bmi, 0, 0xCC0020); GdiFlush(); chain.Present(); }
            void Gpu() { chain.Render(pixels.AddrOfPinnedObject(), 3840, 2160, new ViewportRectangle(0, 0, 1920, 1080)); chain.PresentGpu(); }
            double Measure(Action frame) { for (int i = 0; i < 3; i++) frame(); var watch = Stopwatch.StartNew(); for (int i = 0; i < 30; i++) frame(); return watch.Elapsed.TotalMilliseconds / 30; }
            double before = Measure(Gdi), after = Measure(Gpu);
            output.WriteLine($"4K to 1080p, 30 warmed frames: GDI {before:F2} ms/frame; D3D11 {after:F2} ms/frame. Includes upload and Present; excludes video decoding.");
            // Timing is diagnostic, not a machine-dependent pass/fail threshold.
        }
        finally { pixels.Free(); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr point);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
