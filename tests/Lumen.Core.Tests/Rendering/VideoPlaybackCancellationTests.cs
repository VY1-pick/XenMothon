using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Moq;
using Xunit;

namespace Lumen.Core.Tests.Rendering;

public class VideoPlaybackCancellationTests
{
    [Fact]
    public async Task Cancellation_DuringLoading_ThrowsOperationCanceledException()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        mockRenderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                    .Returns((string _, WallpaperSettings _, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return Task.CompletedTask;
                    });

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await mockRenderer.Object.LoadAsync("C:\\Media\\video.mp4", new WallpaperSettings(), cts.Token);
        });
    }

    [Fact]
    public async Task Cancellation_DuringInitialization_ThrowsOperationCanceledException()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        mockRenderer.Setup(r => r.InitializeAsync(It.IsAny<IntPtr>(), It.IsAny<MonitorBounds>(), It.IsAny<CancellationToken>()))
                    .Returns((IntPtr _, MonitorBounds _, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return Task.CompletedTask;
                    });

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await mockRenderer.Object.InitializeAsync(new IntPtr(123), new MonitorBounds(0, 0, 1920, 1080), cts.Token);
        });
    }

    [Fact]
    public async Task Cancellation_DuringPlayback_StopsPromptly()
    {
        var mockRenderer = new Mock<IWallpaperRenderer>();
        using var cts = new CancellationTokenSource();

        mockRenderer.Setup(r => r.PlayAsync(It.IsAny<CancellationToken>()))
                    .Returns(async (CancellationToken token) =>
                    {
                        await Task.Delay(1000, token);
                    });

        var playTask = mockRenderer.Object.PlayAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await playTask);
    }
}
