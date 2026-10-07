using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Models;

public class DesktopVisibilityEvaluatorTests
{
    [Fact]
    public void SmallForegroundOverlayDoesNotResumeWallpaperBehindFullscreenGame()
    {
        var monitor = new MonitorBounds(0, 0, 1920, 1080);
        var windows = new[] { new VisibleDesktopWindow(new MonitorBounds(500, 300, 700, 500), false), new VisibleDesktopWindow(monitor, false) };
        Assert.Equal(PerformanceState.FullscreenApplication, DesktopVisibilityEvaluator.Evaluate(monitor, windows, new(), false));
    }

    [Fact]
    public void GameOnSecondMonitorDoesNotPauseExposedPrimaryMonitor()
    {
        var primary = new MonitorBounds(0, 0, 1920, 1080);
        var secondary = new MonitorBounds(-2560, 0, 2560, 1440);
        var windows = new[] { new VisibleDesktopWindow(secondary, false) };
        Assert.Equal(PerformanceState.ActiveVisible, DesktopVisibilityEvaluator.Evaluate(primary, windows, new(), false));
        Assert.Equal(PerformanceState.FullscreenApplication, DesktopVisibilityEvaluator.Evaluate(secondary, windows, new(), false));
    }

    [Fact]
    public void ReturningToExposedDesktopResumesWallpaper()
    {
        Assert.Equal(PerformanceState.ActiveVisible, DesktopVisibilityEvaluator.Evaluate(new(0, 0, 1920, 1080), [], new(), false));
    }
}
