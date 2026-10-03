using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lumen.Core.Tests.Rendering;

public class VideoPlaybackPerformanceStateTests
{
    private readonly NullLogger<WallpaperEngineOrchestrator> _logger = NullLogger<WallpaperEngineOrchestrator>.Instance;

    [Fact]
    public async Task PerformanceState_Fullscreen_PausesPlayback()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test", Type = WallpaperType.Video, FilePath = "C:\\test.mp4", ContentHash = "h1" };
        const string monitorId = "MONITOR_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.FullscreenApplication);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PerformanceState_ActiveVisible_ResumesPlayback()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test", Type = WallpaperType.Video, FilePath = "C:\\test.mp4", ContentHash = "h1" };
        const string monitorId = "MONITOR_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.FullscreenApplication);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));

        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.ActiveVisible);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Exactly(2)); // Initial play + resume
    }

    [Fact]
    public async Task PerformanceState_ResumeWhenAlreadyPlaying_DoesNotSpawnDuplicateLoops()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test", Type = WallpaperType.Video, FilePath = "C:\\test.mp4", ContentHash = "h1" };
        const string monitorId = "MONITOR_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        // When already active and visible, another ActiveVisible should not trigger extra PlayAsync
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.ActiveVisible);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PerformanceState.DisplayOff)]
    [InlineData(PerformanceState.Sleep)]
    [InlineData(PerformanceState.Locked)]
    [InlineData(PerformanceState.Covered)]
    [InlineData(PerformanceState.PausedByUser)]
    public async Task PerformanceState_InactiveStates_PausePlayback(PerformanceState inactiveState)
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp = new Wallpaper { Title = "Test", Type = WallpaperType.Video, FilePath = "C:\\test.mp4", ContentHash = "h1" };
        const string monitorId = "MONITOR_1";

        await orchestrator.ApplyWallpaperAsync(monitorId, wp);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        await orchestrator.UpdatePerformanceStateAsync(monitorId, inactiveState);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));
    }
}
