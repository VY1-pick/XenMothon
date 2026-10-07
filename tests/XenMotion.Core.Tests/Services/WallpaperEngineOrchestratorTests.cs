using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class WallpaperEngineOrchestratorTests
{
    private readonly NullLogger<WallpaperEngineOrchestrator> _logger = NullLogger<WallpaperEngineOrchestrator>.Instance;

    [Fact]
    public async Task InitiallyMutedWallpaper_FirstToggleRestoresConfiguredVolume()
    {
        var renderer = new Mock<IWallpaperRenderer>();
        await using var engine = new WallpaperEngineOrchestrator(_logger, _ => renderer.Object);
        await engine.ApplyWallpaperAsync("M1", new Wallpaper { Title = "Audio", FilePath = "audio.mp4", Type = WallpaperType.Video, ContentHash = "audio" },
            new WallpaperSettings { IsMuted = true, Volume = 0.35f });
        await engine.ToggleMuteAllAsync();
        renderer.Verify(r => r.SetVolumeAsync(0.35f, It.IsAny<CancellationToken>()), Times.Once);
        await engine.ToggleMuteAllAsync();
        renderer.Verify(r => r.SetVolumeAsync(0f, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task ApplyAfterLoadFailure_RecoversAndPlays()
    {
        var renderer = new Mock<IWallpaperRenderer>();
        renderer.SetupSequence(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Invalid media"))
            .Returns(Task.CompletedTask);
        await using var engine = new WallpaperEngineOrchestrator(_logger, _ => renderer.Object);
        var wallpaper = new Wallpaper { Title = "Retry", Type = WallpaperType.Video, FilePath = "retry.mp4", ContentHash = "retry" };
        await Assert.ThrowsAsync<IOException>(() => engine.ApplyWallpaperAsync("M1", wallpaper));
        Assert.Equal(WallpaperState.Error, engine.GetMonitorState("M1"));
        await engine.ApplyWallpaperAsync("M1", wallpaper);
        Assert.Equal(WallpaperState.Playing, engine.GetMonitorState("M1"));
    }

    [Fact]
    public async Task PartiallyVisibleAfterOcclusion_ResumesRenderer()
    {
        var renderer = new Mock<IWallpaperRenderer>();
        await using var engine = new WallpaperEngineOrchestrator(_logger, _ => renderer.Object);
        var wallpaper = new Wallpaper { Title = "Visible", Type = WallpaperType.Video, FilePath = "visible.mp4", ContentHash = "visible" };
        await engine.ApplyWallpaperAsync("M1", wallpaper);
        await engine.UpdatePerformanceStateAsync("M1", PerformanceState.Covered);
        await engine.UpdatePerformanceStateAsync("M1", PerformanceState.PartiallyVisible);
        renderer.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(WallpaperState.PartiallyVisible, engine.GetMonitorState("M1"));
    }

    [Fact]
    public async Task ApplyWallpaperAsync_ValidWallpaper_TransitionsToPlaying()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        mockRenderer.Setup(r => r.PlayAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Aurora",
            Type = WallpaperType.Video,
            FilePath = "C:\\Wallpapers\\aurora.mp4",
            ContentHash = "hash1"
        };

        const string monitorId = "MONITOR_1";
        await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);

        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.LoadAsync("C:\\Wallpapers\\aurora.mp4", It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()), Times.Once);
        mockRenderer.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PauseAndResume_ShouldUpdateMonitorStateAccurately()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Neon City",
            Type = WallpaperType.Video,
            FilePath = "C:\\Wallpapers\\neon.mp4",
            ContentHash = "hash2"
        };

        const string monitorId = "MONITOR_1";
        await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        // Pause
        await orchestrator.PauseAsync(monitorId);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Resume
        await orchestrator.ResumeAsync(monitorId);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.PlayAsync(It.IsAny<CancellationToken>()), Times.Exactly(2)); // Once on apply, once on resume
    }

    [Fact]
    public async Task GlobalPauseAndResume_ShouldControlAllMonitorsAndFlags()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp1 = new Wallpaper { Title = "W1", Type = WallpaperType.Video, FilePath = "C:\\w1.mp4", ContentHash = "h1" };
        var wp2 = new Wallpaper { Title = "W2", Type = WallpaperType.Video, FilePath = "C:\\w2.mp4", ContentHash = "h2" };

        await orchestrator.ApplyWallpaperAsync("M1", wp1);
        await orchestrator.ApplyWallpaperAsync("M2", wp2);

        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState("M1"));
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState("M2"));
        Assert.False(orchestrator.IsGloballyPaused);

        // Global Pause
        await orchestrator.PauseAllAsync();
        Assert.True(orchestrator.IsGloballyPaused);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState("M1"));
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState("M2"));

        // Global Resume
        await orchestrator.ResumeAllAsync();
        Assert.False(orchestrator.IsGloballyPaused);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState("M1"));
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState("M2"));

        // Toggle Pause
        await orchestrator.TogglePauseAllAsync();
        Assert.True(orchestrator.IsGloballyPaused);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState("M1"));
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState("M2"));
    }

    [Fact]
    public async Task ApplyWallpaper_WhenGloballyPaused_LoadsInPausedState()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        await orchestrator.PauseAllAsync();
        Assert.True(orchestrator.IsGloballyPaused);

        var wp = new Wallpaper { Title = "W1", Type = WallpaperType.Video, FilePath = "C:\\w1.mp4", ContentHash = "h1" };
        await orchestrator.ApplyWallpaperAsync("M1", wp);

        // Must settle in Paused state, not Playing
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState("M1"));
    }

    [Fact]
    public async Task StopAsync_ShouldTransitionToStopped()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Deep Sea",
            Type = WallpaperType.Image,
            FilePath = "C:\\Wallpapers\\sea.jpg",
            ContentHash = "hash3"
        };

        const string monitorId = "MONITOR_1";
        await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);
        await orchestrator.StopAsync(monitorId);

        Assert.Equal(WallpaperState.Stopped, orchestrator.GetMonitorState(monitorId));
        mockRenderer.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePerformanceState_CoveredOrFullscreen_PausesPlayingWallpaper()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Mountain Peak",
            Type = WallpaperType.Video,
            FilePath = "C:\\Wallpapers\\mountain.mp4",
            ContentHash = "hash4"
        };

        const string monitorId = "MONITOR_1";
        await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));

        // When window is fullscreen or monitor is covered
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.FullscreenApplication);
        Assert.Equal(WallpaperState.Paused, orchestrator.GetMonitorState(monitorId));

        // When desktop becomes visible again
        await orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.ActiveVisible);
        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task RapidWallpaperSwitch_LatestRequestWins_SettlesOnFinalWallpaper()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string _, WallpaperSettings _, CancellationToken ct) =>
                    {
                        await Task.Delay(25, ct); // simulate asynchronous loading delay
                    });

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wp1 = new Wallpaper { Title = "W1", Type = WallpaperType.Video, FilePath = "C:\\w1.mp4", ContentHash = "h1" };
        var wp2 = new Wallpaper { Title = "W2", Type = WallpaperType.Video, FilePath = "C:\\w2.mp4", ContentHash = "h2" };
        var wp3 = new Wallpaper { Title = "W3", Type = WallpaperType.Video, FilePath = "C:\\w3.mp4", ContentHash = "h3" };

        const string monitorId = "MONITOR_1";

        // Dispatch rapid switches without awaiting completion of prior ones
        var t1 = orchestrator.ApplyWallpaperAsync(monitorId, wp1);
        var t2 = orchestrator.ApplyWallpaperAsync(monitorId, wp2);
        var t3 = orchestrator.ApplyWallpaperAsync(monitorId, wp3);

        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(WallpaperState.Playing, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task ApplyWallpaperAsync_WhenCancelledDirectly_TransitionsToStoppedAndDoesNotHangInLoading()
    {
        using var cts = new CancellationTokenSource();
        var mockRenderer = new Mock<IWallpaperRenderer>();
        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string _, WallpaperSettings _, CancellationToken ct) =>
                    {
                        await Task.Delay(100, ct);
                    });

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);
        var wp = new Wallpaper { Title = "W1", Type = WallpaperType.Video, FilePath = "C:\\w1.mp4", ContentHash = "h1" };

        const string monitorId = "MONITOR_1";
        var applyTask = orchestrator.ApplyWallpaperAsync(monitorId, wp, ct: cts.Token);
        cts.Cancel();

        await applyTask; // Handled gracefully, does not rethrow OperationCanceledException

        Assert.Equal(WallpaperState.Stopped, orchestrator.GetMonitorState(monitorId));
    }
}
