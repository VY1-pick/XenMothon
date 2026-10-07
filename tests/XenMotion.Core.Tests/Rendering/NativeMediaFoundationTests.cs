using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.UI.Rendering.Media;
using XenMotion.UI.Rendering.Video;
using XenMotion.UI.Rendering;
using XenMotion.Core.Models;
using XenMotion.Core.Enums;
using Xunit;
using Xunit.Abstractions;
using XenMotion.UI.Desktop;
using System.Runtime.InteropServices;
using XenMotion.Application.Services;

namespace XenMotion.Core.Tests.Rendering;

public sealed class NativeMediaFoundationTests(ITestOutputHelper output)
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
    [Fact]
    public async Task RepeatedResumeSubmitsGpuFrameWithoutSecondLongDelay()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-resume-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 64, 36));
            Assert.NotNull(surface);
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(surface.Handle, surface.LocalBounds);
            await renderer.LoadAsync(path, new WallpaperSettings { IsMuted = true, Loop = true });
            await renderer.PlayAsync();
            await Task.Delay(150);
            for (int i = 0; i < 5; i++)
            {
                await renderer.PauseAsync();
                await Task.Delay(100);
                long count = renderer.PresentedFrameCount;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await renderer.PlayAsync();
                while (renderer.PresentedFrameCount == count && watch.ElapsedMilliseconds < 500)
                    await Task.Delay(5);
                Assert.True(renderer.PresentedFrameCount > count, "GPU presentation did not resume within 500ms.");
                output.WriteLine($"Native resume cycle {i + 1}: {watch.Elapsed.TotalMilliseconds:F1}ms to submitted GPU frame.");
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task SmallWindowCoverageStopsActualDecoderAndFullModeResumesIt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-coverage-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => renderer);
            await engine.ApplyWallpaperAsync("M1", new Wallpaper { Title = "Coverage", FilePath = path, ContentHash = "coverage", Type = WallpaperType.Video });
            await Task.Delay(150);
            var performance = new PerformanceSettings { CoveredThresholdPercentage = 1 };
            var windows = new[] { new VisibleDesktopWindow(new(0, 0, 10, 10), false) };
            var bounds = new MonitorBounds(0, 0, 100, 100);
            await engine.UpdatePerformanceStateAsync("M1", DesktopVisibilityEvaluator.Evaluate(bounds, windows, performance, false));
            Assert.Equal(WallpaperState.Paused, engine.GetMonitorState("M1"));
            await Task.Delay(100);
            long paused = renderer.DecodedFrameCount;
            await Task.Delay(250);
            Assert.True(paused > 0);
            Assert.Equal(paused, renderer.DecodedFrameCount);
            performance.PauseOnlyWhenFullyCovered = true;
            await engine.UpdatePerformanceStateAsync("M1", DesktopVisibilityEvaluator.Evaluate(bounds, windows, performance, false));
            await Task.Delay(150);
            Assert.True(renderer.DecodedFrameCount > paused);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ProcessingHeightActuallyResizesDecodedFramesWithoutUpscaling()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-scale-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            using var engine = new MediaFoundationPlaybackEngine(NullLogger.Instance);
            engine.Open(path, heightLimit: 18);
            using var frame = engine.ReadNextFrame(false);
            Assert.NotNull(frame);
            Assert.Equal(18, frame.Height);
            Assert.Equal(32, frame.Width);
            engine.Open(path, heightLimit: 720);
            Assert.Equal(36, engine.VideoHeight);
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(0.5f, 2400)]
    [InlineData(2f, 800)]
    public async Task PlaybackSpeedChangesActualVideoDuration(float speed, int waitMs)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-speed-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(IntPtr.Zero, new MonitorBounds(0, 0, 64, 36));
            await renderer.LoadAsync(path, new WallpaperSettings { PlaybackSpeed = speed, Loop = false });
            await renderer.PlayAsync();
            if (speed < 1)
            {
                await Task.Delay(1200);
                Assert.Equal(WallpaperState.Playing, renderer.CurrentState);
                waitMs -= 1200;
            }
            await Task.Delay(waitMs);
            Assert.Equal(WallpaperState.Stopped, renderer.CurrentState);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task PausedVideoStopsDecodingAndResumesWithoutCatchUpBurst()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-pause-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(IntPtr.Zero, new MonitorBounds(0, 0, 64, 36));
            await renderer.LoadAsync(path, new WallpaperSettings { Loop = true });
            await renderer.PlayAsync();
            await Task.Delay(180);
            await renderer.PauseAsync();
            await Task.Delay(100); // Let one in-flight decode settle.
            long pausedCount = renderer.DecodedFrameCount;
            Assert.True(pausedCount > 0);
            await Task.Delay(400);
            Assert.Equal(pausedCount, renderer.DecodedFrameCount);
            await renderer.PlayAsync();
            await Task.Delay(180);
            Assert.InRange(renderer.DecodedFrameCount - pausedCount, 1, 9);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task RealVideoRenderer_FpsCapDoesNotSlowPlayback_AndPauseFreezesIt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            await using var renderer = new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(IntPtr.Zero, new MonitorBounds(0, 0, 64, 36));
            await renderer.LoadAsync(path, new WallpaperSettings { TargetFps = 15, Loop = false });
            await renderer.PlayAsync();
            await renderer.PauseAsync();
            await Task.Delay(150);
            Assert.Equal(WallpaperState.Paused, renderer.CurrentState);
            await renderer.PlayAsync();
            // A 1-second/30fps clip must finish near 1 second, not 2 seconds at a 15fps presentation cap.
            await Task.Delay(1400);
            Assert.Equal(WallpaperState.Stopped, renderer.CurrentState);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void RealWindowsDecoder_OpensReadsAndRewindsAvi()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi());
            using var engine = new MediaFoundationPlaybackEngine(NullLogger.Instance);
            engine.Open(path);
            Assert.Equal(64, engine.VideoWidth);
            Assert.Equal(36, engine.VideoHeight);
            Assert.InRange(engine.NativeFps, 29.9, 30.1);
            int frames = 0;
            long lastTicks = -1;
            for (int i = 0; i < 40; i++)
            {
                using var frame = engine.ReadNextFrame(false);
                if (engine.EndOfStream) break;
                if (frame is null) continue;
                Assert.NotEqual(IntPtr.Zero, frame.DataPointer);
                Assert.True(frame.Timestamp.Ticks > lastTicks);
                lastTicks = frame.Timestamp.Ticks;
                frames++;
            }
            Assert.Equal(30, frames);
            engine.SeekToStart();
            using var first = engine.ReadNextFrame(false);
            Assert.NotNull(first);
            Assert.Equal(TimeSpan.Zero, first.Timestamp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TimestampClock_CanCancelLongWait()
    {
        var clock = new VideoPlaybackClock();
        using var cts = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clock.WaitUntilAsync(60000, cts.Token));
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(1080, 1920)]
    [InlineData(3440, 1440)]
    public void NativeDecoderPreservesSourceResolution(int width, int height)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-resolution-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, CreateAvi(width: width, height: height, frameCount: 2));
            using var engine = new MediaFoundationPlaybackEngine(NullLogger.Instance);
            engine.Open(path);
            Assert.Equal(width, engine.VideoWidth);
            Assert.Equal(height, engine.VideoHeight);
            using var first = engine.ReadNextFrame(false);
            Assert.NotNull(first);
            Assert.Equal(width, first.Width);
            Assert.Equal(height, first.Height);
            using var second = engine.ReadNextFrame(false);
            Assert.NotNull(second);
            Assert.Equal(7, System.Runtime.InteropServices.Marshal.ReadByte(second.DataPointer));
            Assert.Equal(7, System.Runtime.InteropServices.Marshal.ReadByte(second.DataPointer, width * height * 4 - 4));
        }
        finally { File.Delete(path); }
    }

    internal static byte[] CreateAvi(bool includeAudio = false, int width = 64, int height = 36, int frameCount = 30)
    {
        int frameSize = ((width * 3 + 3) / 4 * 4) * height;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.ASCII, true);
        void Four(string value) => w.Write(Encoding.ASCII.GetBytes(value));
        long Begin(string id) { Four(id); w.Write(0); return stream.Position; }
        void End(long start) { long end = stream.Position; stream.Position = start - 4; w.Write((int)(end - start)); stream.Position = end; }
        long riff = Begin("RIFF"); Four("AVI ");
        long header = Begin("LIST"); Four("hdrl");
        long main = Begin("avih");
        foreach (int value in new[] { 33333, frameSize * 30, 0, 0, frameCount, 0, includeAudio ? 2 : 1, frameSize, width, height, 0, 0, 0, 0 }) w.Write(value);
        End(main);
        long list = Begin("LIST"); Four("strl");
        long sh = Begin("strh"); Four("vids"); Four("DIB "); w.Write(0); w.Write((short)0); w.Write((short)0);
        foreach (int value in new[] { 0, 1, 30, 0, frameCount, frameSize, -1, 0 }) w.Write(value);
        w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height); End(sh);
        long sf = Begin("strf"); w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24);
        foreach (int value in new[] { 0, frameSize, 0, 0, 0, 0 }) w.Write(value);
        End(sf); End(list);
        if (includeAudio)
        {
            long audioList = Begin("LIST"); Four("strl");
            long audioHeader = Begin("strh"); Four("auds"); w.Write(0); w.Write(0); w.Write((short)0); w.Write((short)0);
            foreach (int value in new[] { 0, 2, 96000, 0, 48000, 3200, -1, 2 }) w.Write(value);
            for (int i = 0; i < 4; i++) w.Write((short)0);
            End(audioHeader);
            long audioFormat = Begin("strf"); w.Write((short)1); w.Write((short)1); w.Write(48000); w.Write(96000); w.Write((short)2); w.Write((short)16);
            End(audioFormat); End(audioList);
        }
        End(header);
        long movie = Begin("LIST"); Four("movi");
        byte[] frameData = new byte[frameSize];
        for (int frame = 0; frame < frameCount; frame++)
        {
            long chunk = Begin("00db");
            Array.Fill(frameData, (byte)(frame * 7));
            w.Write(frameData);
            End(chunk);
            if (includeAudio)
            {
                long audioChunk = Begin("01wb");
                for (int i = 0; i < 1600; i++) w.Write((short)(1000 * Math.Sin((frame * 1600 + i) * 2 * Math.PI * 440 / 48000)));
                End(audioChunk);
            }
        }
        End(movie); End(riff);
        return stream.ToArray();
    }
}
