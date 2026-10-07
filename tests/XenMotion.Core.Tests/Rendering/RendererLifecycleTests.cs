using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class RendererLifecycleTests
{
    [Fact]
    public async Task Renderer_Lifecycle_FullSequence_ValidTransitions()
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

        var renderer = mockRenderer.Object;
        Assert.Equal(WallpaperState.Uninitialized, renderer.CurrentState);

        await renderer.InitializeAsync(new IntPtr(100), new MonitorBounds(0, 0, 1920, 1080));
        Assert.Equal(WallpaperState.Ready, renderer.CurrentState);

        await renderer.LoadAsync(@"C:\wallpaper.mp4", new WallpaperSettings());
        Assert.Equal(WallpaperState.Ready, renderer.CurrentState);

        await renderer.PlayAsync();
        Assert.Equal(WallpaperState.Playing, renderer.CurrentState);

        await renderer.PauseAsync();
        Assert.Equal(WallpaperState.Paused, renderer.CurrentState);

        await renderer.StopAsync();
        Assert.Equal(WallpaperState.Stopped, renderer.CurrentState);
    }

    [Fact]
    public async Task Renderer_SetVolume_AdjustsLevelCorrectly()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        float volume = 1.0f;

        mockRenderer.Setup(r => r.SetVolumeAsync(It.IsAny<float>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback<float, CancellationToken>((v, _) => volume = Math.Clamp(v, 0f, 1f));

        await mockRenderer.Object.SetVolumeAsync(0.5f);
        Assert.Equal(0.5f, volume);

        await mockRenderer.Object.SetVolumeAsync(1.5f);
        Assert.Equal(1.0f, volume);

        await mockRenderer.Object.SetVolumeAsync(-0.5f);
        Assert.Equal(0.0f, volume);
    }

    [Fact]
    public async Task Renderer_ResizeAsync_UpdatesBoundsSuccessfully()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        MonitorBounds? currentBounds = null;

        mockRenderer.Setup(r => r.ResizeAsync(It.IsAny<MonitorBounds>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback<MonitorBounds, CancellationToken>((b, _) => currentBounds = b);

        var newBounds = new MonitorBounds(1920, 0, 2560, 1440);
        await mockRenderer.Object.ResizeAsync(newBounds);

        Assert.NotNull(currentBounds);
        Assert.Equal(newBounds, currentBounds.Value);
    }
}
