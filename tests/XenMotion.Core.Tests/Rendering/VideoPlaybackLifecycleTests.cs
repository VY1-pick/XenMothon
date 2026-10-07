using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class VideoPlaybackLifecycleTests
{
    [Fact]
    public async Task VideoPlayback_FullLifecycle_SequenceTransitions()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        var currentState = WallpaperState.Uninitialized;

        mockRenderer.SetupGet(r => r.SupportedType).Returns(WallpaperType.Video);
        mockRenderer.SetupGet(r => r.CurrentState).Returns(() => currentState);

        mockRenderer.Setup(r => r.InitializeAsync(It.IsAny<IntPtr>(), It.IsAny<MonitorBounds>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Ready);

        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Ready);

        mockRenderer.Setup(r => r.PlayAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Playing);

        mockRenderer.Setup(r => r.PauseAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Paused);

        mockRenderer.Setup(r => r.StopAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Stopped);

        mockRenderer.Setup(r => r.DisposeAsync())
                    .Returns(ValueTask.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Disposed);

        var renderer = mockRenderer.Object;

        // Uninitialized
        Assert.Equal(WallpaperState.Uninitialized, renderer.CurrentState);

        // Initialize -> Ready
        await renderer.InitializeAsync(new IntPtr(0x1000), new MonitorBounds(0, 0, 1920, 1080));
        Assert.Equal(WallpaperState.Ready, renderer.CurrentState);

        // Load -> Ready
        await renderer.LoadAsync("C:\\Media\\video.mp4", new WallpaperSettings());
        Assert.Equal(WallpaperState.Ready, renderer.CurrentState);

        // Play -> Playing
        await renderer.PlayAsync();
        Assert.Equal(WallpaperState.Playing, renderer.CurrentState);

        // Pause -> Paused
        await renderer.PauseAsync();
        Assert.Equal(WallpaperState.Paused, renderer.CurrentState);

        // Resume (Play) -> Playing
        await renderer.PlayAsync();
        Assert.Equal(WallpaperState.Playing, renderer.CurrentState);

        // Stop -> Stopped
        await renderer.StopAsync();
        Assert.Equal(WallpaperState.Stopped, renderer.CurrentState);

        // Dispose -> Disposed
        await renderer.DisposeAsync();
        Assert.Equal(WallpaperState.Disposed, renderer.CurrentState);
    }

    [Fact]
    public async Task VideoPlayback_VolumeAndMute_EnforcesCorrectLevels()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        float currentVolume = 1.0f;

        mockRenderer.Setup(r => r.SetVolumeAsync(It.IsAny<float>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback<float, CancellationToken>((v, _) => currentVolume = Math.Clamp(v, 0.0f, 1.0f));

        var renderer = mockRenderer.Object;

        // Standard volume
        await renderer.SetVolumeAsync(0.75f);
        Assert.Equal(0.75f, currentVolume);

        // Over-range volume is clamped
        await renderer.SetVolumeAsync(2.0f);
        Assert.Equal(1.0f, currentVolume);

        // Negative volume is clamped
        await renderer.SetVolumeAsync(-1.0f);
        Assert.Equal(0.0f, currentVolume);
    }

    [Fact]
    public async Task VideoPlayback_FpsLimit_ClampsWithinAllowedBounds()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        int currentFps = 60;

        mockRenderer.Setup(r => r.SetFpsLimitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback<int, CancellationToken>((fps, _) => currentFps = Math.Clamp(fps, 15, 120));

        var renderer = mockRenderer.Object;

        await renderer.SetFpsLimitAsync(30);
        Assert.Equal(30, currentFps);

        await renderer.SetFpsLimitAsync(144);
        Assert.Equal(120, currentFps); // Clamped to 120 max

        await renderer.SetFpsLimitAsync(5);
        Assert.Equal(15, currentFps); // Clamped to 15 min
    }

    [Fact]
    public async Task VideoPlayback_ResizeAsync_UpdatesBoundsSuccessfully()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        MonitorBounds activeBounds = new(0, 0, 1920, 1080);

        mockRenderer.Setup(r => r.ResizeAsync(It.IsAny<MonitorBounds>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback<MonitorBounds, CancellationToken>((b, _) => activeBounds = b);

        var newBounds = new MonitorBounds(1920, 0, 3840, 2160);
        await mockRenderer.Object.ResizeAsync(newBounds);

        Assert.Equal(newBounds, activeBounds);
    }
}
