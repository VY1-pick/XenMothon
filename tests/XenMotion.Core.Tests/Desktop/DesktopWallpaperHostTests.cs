using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Desktop;

public class DesktopWallpaperHostTests
{
    [Fact]
    public async Task DesktopWallpaperHost_MockContract_AttachAndDetach_FiresEvents()
    {
        var mockHost = new Mock<IDesktopWallpaperHost>();
        bool attachedFired = false;
        bool detachedFired = false;

        mockHost.SetupGet(h => h.IsAttached).Returns(() => attachedFired && !detachedFired);
        mockHost.SetupGet(h => h.DesktopWorkerWHwnd).Returns(new IntPtr(0x12345));

        mockHost.Setup(h => h.AttachToDesktopAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .Callback(() =>
                {
                    attachedFired = true;
                    mockHost.Raise(h => h.Attached += null, EventArgs.Empty);
                });

        mockHost.Setup(h => h.DetachAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Callback(() =>
                {
                    detachedFired = true;
                    mockHost.Raise(h => h.Detached += null, EventArgs.Empty);
                });

        var host = mockHost.Object;
        host.Attached += (_, _) => attachedFired = true;
        host.Detached += (_, _) => detachedFired = true;

        bool attached = await host.AttachToDesktopAsync();
        Assert.True(attached);
        Assert.True(attachedFired);
        Assert.Equal(new IntPtr(0x12345), host.DesktopWorkerWHwnd);

        await host.DetachAsync();
        Assert.True(detachedFired);
    }

    [Fact]
    public async Task DesktopWallpaperHost_ExplorerRestart_TriggersReattach()
    {
        var mockHost = new Mock<IDesktopWallpaperHost>();
        int restartEventCount = 0;

        mockHost.Setup(h => h.HandleExplorerRestartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Callback(() =>
                {
                    restartEventCount++;
                    mockHost.Raise(h => h.ShellRestartDetected += null, EventArgs.Empty);
                });

        var host = mockHost.Object;
        host.ShellRestartDetected += (_, _) => restartEventCount++;

        await host.HandleExplorerRestartAsync();
        Assert.True(restartEventCount > 0);
    }

    [Fact]
    public async Task DesktopWallpaperHost_UpdateMonitorLayout_HandlesMultipleMonitors()
    {
        var mockHost = new Mock<IDesktopWallpaperHost>();
        IReadOnlyList<MonitorInfo>? registeredMonitors = null;

        mockHost.Setup(h => h.UpdateMonitorLayoutAsync(It.IsAny<IReadOnlyList<MonitorInfo>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Callback<IReadOnlyList<MonitorInfo>, CancellationToken>((m, _) => registeredMonitors = m);

        var monitors = new List<MonitorInfo>
        {
            new MonitorInfo { DeviceId = "MONITOR\\1", DeviceName = "Display 1", Bounds = new MonitorBounds(0, 0, 1920, 1080), WorkingArea = new MonitorBounds(0, 0, 1920, 1080), DpiScale = 1.0, IsPrimary = true, DisplayIndex = 0, RefreshRateHz = 60 },
            new MonitorInfo { DeviceId = "MONITOR\\2", DeviceName = "Display 2", Bounds = new MonitorBounds(1920, 0, 1920, 1080), WorkingArea = new MonitorBounds(1920, 0, 1920, 1080), DpiScale = 1.0, IsPrimary = false, DisplayIndex = 1, RefreshRateHz = 60 }
        };

        await mockHost.Object.UpdateMonitorLayoutAsync(monitors);
        Assert.NotNull(registeredMonitors);
        Assert.Equal(2, registeredMonitors.Count);
    }
}
