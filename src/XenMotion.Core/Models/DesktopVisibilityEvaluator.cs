using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

public readonly record struct VisibleDesktopWindow(MonitorBounds Bounds, bool IsMaximized);

public static class DesktopVisibilityEvaluator
{
    public static PerformanceState Evaluate(MonitorBounds monitor, IEnumerable<VisibleDesktopWindow> windows,
        PerformanceSettings settings, bool onBattery)
    {
        if (onBattery && settings.PauseOnBattery) return PerformanceState.PausedByUser;
        var covered = new List<MonitorBounds>();
        bool fullscreen = false, maximized = false;
        foreach (var window in windows)
        {
            var b = window.Bounds;
            long width = Math.Max(0, Math.Min(b.Right, monitor.Right) - Math.Max(b.X, monitor.X));
            long height = Math.Max(0, Math.Min(b.Bottom, monitor.Bottom) - Math.Max(b.Y, monitor.Y));
            double candidate = width * height * 100.0 / Math.Max(1L, (long)monitor.Width * monitor.Height);
            var intersection = MonitorBounds.Intersect(monitor, b);
            if (!intersection.IsEmpty) covered.Add(intersection);
            fullscreen |= b.X <= monitor.X && b.Y <= monitor.Y && b.Right >= monitor.Right && b.Bottom >= monitor.Bottom;
            maximized |= window.IsMaximized && candidate >= 99;
        }
        if (fullscreen && settings.PauseOnFullscreen) return PerformanceState.FullscreenApplication;
        if (maximized && settings.PauseOnMaximized) return PerformanceState.FullscreenApplication;
        double coverage = CoveredArea(covered) * 100.0 / Math.Max(1L, (long)monitor.Width * monitor.Height);
        int threshold = settings.PauseOnlyWhenFullyCovered ? 100 : Math.Clamp(settings.CoveredThresholdPercentage, 1, 100);
        if (coverage >= threshold && settings.PauseWhenCovered) return PerformanceState.Covered;
        return coverage > 0 ? PerformanceState.PartiallyVisible : PerformanceState.ActiveVisible;
    }

    private static long CoveredArea(List<MonitorBounds> rectangles)
    {
        // ponytail: quadratic sweep is sufficient for desktop window counts; use a spatial index only if profiling warrants it.
        var edges = rectangles.SelectMany(r => new[] { r.X, r.Right }).Distinct().OrderBy(x => x).ToArray();
        long area = 0;
        for (int i = 1; i < edges.Length; i++)
        {
            long height = 0;
            int end = int.MinValue;
            foreach (var r in rectangles.Where(r => r.X < edges[i] && r.Right > edges[i - 1]).OrderBy(r => r.Y))
            {
                height += Math.Max(0L, (long)r.Bottom - Math.Max(r.Y, end));
                end = Math.Max(end, r.Bottom);
            }
            area += ((long)edges[i] - edges[i - 1]) * height;
        }
        return area;
    }
}
