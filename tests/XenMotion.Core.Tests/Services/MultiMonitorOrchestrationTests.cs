using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class MultiMonitorOrchestrationTests
{
    private readonly NullLogger<WallpaperEngineOrchestrator> _logger = NullLogger<WallpaperEngineOrchestrator>.Instance;

    [Fact]
    public async Task MultiMonitor_IndependentMonitors_ManageSeparateSessions()
    {
        var mockRenderer1 = new Mock<IWallpaperRenderer>();
        var mockRenderer2 = new Mock<IWallpaperRenderer>();

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, type =>
            type == WallpaperType.Video ? mockRenderer1.Object : mockRenderer2.Object);

        var wpVideo = new Wallpaper { Title = "Video WP", Type = WallpaperType.Video, FilePath = "C:\\v.mp4", ContentHash = "v1" };
        var wpImage = new Wallpaper { Title = "Image WP", Type = WallpaperType.Image, FilePath = "C:\\i.png", ContentHash = "i1" };

        const string mon1 = "MONITOR_1";
        const string mon2 = "MONITOR_2";

        await orchestrator.ApplyWallpaperAsync(mon1, wpVideo);
        await orchestrator.ApplyWallpaperAsync(mon2, wpImage);

        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(mon1));
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(mon2));

        // Pausing Monitor 1 should not affect Monitor 2
        await orchestrator.PauseAsync(mon1);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(mon1));
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(mon2));

        // Stopping Monitor 2 should not affect Monitor 1
        await orchestrator.StopAsync(mon2);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(mon1));
        Assert.Equal(WallpaperState.Stopped, orchestrator.GetMonitorState(mon2));
    }

    [Fact]
    public async Task MultiMonitor_RapidSwitching_DisposesSupersededRenderer()
    {
        var mockRendererA = new Mock<IWallpaperRenderer>();
        var mockRendererB = new Mock<IWallpaperRenderer>();

        int factoryCallCount = 0;
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ =>
        {
            factoryCallCount++;
            return factoryCallCount == 1 ? mockRendererA.Object : mockRendererB.Object;
        });

        var wpA = new Wallpaper { Title = "WP A", Type = WallpaperType.Video, FilePath = "C:\\a.mp4", ContentHash = "a" };
        var wpB = new Wallpaper { Title = "WP B", Type = WallpaperType.Video, FilePath = "C:\\b.mp4", ContentHash = "b" };

        const string mon = "MONITOR_1";

        // Apply A
        await orchestrator.ApplyWallpaperAsync(mon, wpA);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(mon));

        // Apply B (superseding A)
        await orchestrator.ApplyWallpaperAsync(mon, wpB);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(mon));

        // Superseded renderer A must be stopped and disposed
        mockRendererA.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockRendererA.Verify(r => r.DisposeAsync(), Times.Once);

        // Active renderer B must be loaded and playing
        mockRendererB.Verify(r => r.LoadAsync("C:\\b.mp4", It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()), Times.Once);
        mockRendererB.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultiMonitor_GlobalPauseAndResume_MaintainsMonitorTracking()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var monitors = new[] { "MON_LEFT", "MON_CENTER", "MON_RIGHT" };
        foreach (var m in monitors)
        {
            var wp = new Wallpaper { Title = $"WP_{m}", Type = WallpaperType.Video, FilePath = $"C:\\{m}.mp4", ContentHash = m };
            await orchestrator.ApplyWallpaperAsync(m, wp);
            Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(m));
        }

        // Global pause
        await orchestrator.PauseAllAsync();
        Assert.True(orchestrator.IsGloballyPaused);
        foreach (var m in monitors)
        {
            Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(m));
        }

        // Global resume
        await orchestrator.ResumeAllAsync();
        Assert.False(orchestrator.IsGloballyPaused);
        foreach (var m in monitors)
        {
            Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(m));
        }
    }
}
