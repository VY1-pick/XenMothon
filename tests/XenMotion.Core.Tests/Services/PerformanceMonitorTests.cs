using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class PerformanceMonitorTests
{
    private readonly NullLogger<WallpaperEngineOrchestrator> _logger = NullLogger<WallpaperEngineOrchestrator>.Instance;

    [Fact]
    public async Task PerformanceState_ActiveVisible_ResumesPausedWallpaper()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test Wallpaper", Type = WallpaperType.Video, FilePath = "C:\test.mp4", ContentHash = "h1" };
        const string monitorId = "MON_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        // Occluded by fullscreen app
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.FullscreenApplication);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));

        // Restored to active visible
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.ActiveVisible);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task PerformanceState_Covered_PausesPlayingWallpaper()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test Wallpaper", Type = WallpaperType.Video, FilePath = "C:\test.mp4", ContentHash = "h1" };
        const string monitorId = "MON_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.Covered);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task PerformanceState_DisplayOff_PausesPlayback()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test Wallpaper", Type = WallpaperType.Video, FilePath = "C:\test.mp4", ContentHash = "h1" };
        const string monitorId = "MON_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.DisplayOff);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));
    }
}
