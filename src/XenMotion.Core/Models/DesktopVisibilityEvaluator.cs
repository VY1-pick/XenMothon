using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

public readonly record struct VisibleDesktopWindow(MonitorBounds Bounds, bool IsMaximized);

public static class DesktopVisibilityEvaluator
{
    public static PerformanceState Evaluate(MonitorBounds monitor, IEnumerable<VisibleDesktopWindow> windows,
        PerformanceSettings settings, bool onBattery)
    {
        if (onBattery && settings.PauseOnBattery) return PerformanceState.PausedByUser;
        double coverage = 0;
        bool fullscreen = false, maximized = false;
        foreach (var window in windows)
        {
            var b = window.Bounds;
            long width = Math.Max(0, Math.Min(b.Right, monitor.Right) - Math.Max(b.X, monitor.X));
            long height = Math.Max(0, Math.Min(b.Bottom, monitor.Bottom) - Math.Max(b.Y, monitor.Y));
            double candidate = width * height * 100.0 / Math.Max(1L, (long)monitor.Width * monitor.Height);
            coverage = Math.Max(coverage, candidate);
            fullscreen |= b.X <= monitor.X && b.Y <= monitor.Y && b.Right >= monitor.Right && b.Bottom >= monitor.Bottom;
            maximized |= window.IsMaximized && candidate >= 99;
        }
        if (fullscreen && settings.PauseOnFullscreen) return PerformanceState.FullscreenApplication;
        if (maximized && settings.PauseOnMaximized) return PerformanceState.FullscreenApplication;
        if (coverage >= Math.Clamp(settings.CoveredThresholdPercentage, 1, 100) && settings.PauseWhenCovered) return PerformanceState.Covered;
        return coverage > 0 ? PerformanceState.PartiallyVisible : PerformanceState.ActiveVisible;
    }
}
