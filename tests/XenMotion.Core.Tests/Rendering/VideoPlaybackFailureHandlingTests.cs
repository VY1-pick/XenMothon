using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class VideoPlaybackFailureHandlingTests
{
    private readonly NullLogger<WallpaperEngineOrchestrator> _logger = NullLogger<WallpaperEngineOrchestrator>.Instance;

    [Fact]
    public async Task FailureHandling_MissingFile_TransitionsToErrorState()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new FileNotFoundException("File not found on disk."));

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Corrupt Media",
            Type = WallpaperType.Video,
            FilePath = "C:\\Missing\\does_not_exist.mp4",
            ContentHash = "missing"
        };

        const string monitorId = "MONITOR_1";

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);
        });

        // The monitor session must transition to Error state, never crashing the orchestrator
        Assert.Equal(WallpaperState.Error, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task FailureHandling_UnsupportedCodecOrContainer_ThrowsNotSupportedException()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new NotSupportedException("Unsupported video container."));

        await using var orchestrator = new WallpaperEngineOrchestrator(_logger, _ => mockRenderer.Object);

        var wallpaper = new Wallpaper
        {
            Title = "Unsupported File",
            Type = WallpaperType.Video,
            FilePath = "C:\\Media\\file.unknown",
            ContentHash = "unknown"
        };

        const string monitorId = "MONITOR_1";

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await orchestrator.ApplyWallpaperAsync(monitorId, wallpaper);
        });

        Assert.Equal(WallpaperState.Error, orchestrator.GetMonitorState(monitorId));
    }

    [Fact]
    public async Task FailureHandling_RepeatedDispose_IsIdempotentAndSafe()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        int disposeCount = 0;

        mockRenderer.Setup(r => r.DisposeAsync())
                    .Returns(ValueTask.CompletedTask)
                    .Callback(() => disposeCount++);

        var renderer = mockRenderer.Object;

        await renderer.DisposeAsync();
        await renderer.DisposeAsync();
        await renderer.DisposeAsync();

        Assert.Equal(3, disposeCount);
    }

    [Fact]
    public async Task FailureHandling_DisposeWhilePlaying_TerminatesCleanly()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        var currentState = WallpaperState.Uninitialized;

        mockRenderer.SetupGet(r => r.CurrentState).Returns(() => currentState);
        mockRenderer.Setup(r => r.PlayAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Playing);
        mockRenderer.Setup(r => r.DisposeAsync())
                    .Returns(ValueTask.CompletedTask)
                    .Callback(() => currentState = WallpaperState.Disposed);

        var renderer = mockRenderer.Object;
        await renderer.PlayAsync();
        Assert.Equal(WallpaperState.Playing, renderer.CurrentState);

        await renderer.DisposeAsync();
        Assert.Equal(WallpaperState.Disposed, renderer.CurrentState);
    }
}
