using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Models;

public class DesktopVisibilityEvaluatorTests
{
    [Fact]
    public void CustomPercentagePausesForSmallWindowsAndFullModeKeepsThreshold()
    {
        var monitor = new MonitorBounds(0, 0, 100, 100);
        var windows = new[] { new VisibleDesktopWindow(new(0, 0, 10, 10), false) };
        var settings = new PerformanceSettings { CoveredThresholdPercentage = 1 };
        Assert.Equal(PerformanceState.Covered, DesktopVisibilityEvaluator.Evaluate(monitor, windows, settings, false));
        settings.PauseOnlyWhenFullyCovered = true;
        Assert.Equal(PerformanceState.PartiallyVisible, DesktopVisibilityEvaluator.Evaluate(monitor, windows, settings, false));
        settings.PauseOnlyWhenFullyCovered = false;
        settings.CoveredThresholdPercentage = 2;
        Assert.Equal(PerformanceState.PartiallyVisible, DesktopVisibilityEvaluator.Evaluate(monitor, windows, settings, false));
        settings.PauseWhenCovered = false;
        settings.CoveredThresholdPercentage = 1;
        Assert.Equal(PerformanceState.PartiallyVisible, DesktopVisibilityEvaluator.Evaluate(monitor, windows, settings, false));
    }

    [Fact]
    public void CombinedCoverageCountsOverlapsOnceAndClipsToEachMonitor()
    {
        var monitor = new MonitorBounds(-100, 0, 100, 100);
        var windows = new[] { new VisibleDesktopWindow(new(-150, 0, 120, 100), false), new VisibleDesktopWindow(new(-60, 0, 60, 100), false) };
        var settings = new PerformanceSettings { PauseOnlyWhenFullyCovered = true };
        Assert.Equal(PerformanceState.Covered, DesktopVisibilityEvaluator.Evaluate(monitor, windows, settings, false));
        var duplicate = new[] { new VisibleDesktopWindow(new(-100, 0, 60, 100), false), new VisibleDesktopWindow(new(-100, 0, 60, 100), false) };
        settings.PauseOnlyWhenFullyCovered = false;
        settings.CoveredThresholdPercentage = 61;
        Assert.Equal(PerformanceState.PartiallyVisible, DesktopVisibilityEvaluator.Evaluate(monitor, duplicate, settings, false));
        settings.CoveredThresholdPercentage = 60;
        Assert.Equal(PerformanceState.Covered, DesktopVisibilityEvaluator.Evaluate(monitor, duplicate, settings, false));
    }
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
