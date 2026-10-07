namespace XenMotion.Core.Models;

public static class PlaybackCalibration
{
    public static double Percentile90(IEnumerable<double> samples)
    {
        var values = samples.Where(double.IsFinite).Where(value => value >= 0).OrderBy(value => value).ToArray();
        return values.Length == 0 ? double.PositiveInfinity : values[(int)Math.Ceiling(values.Length * .9) - 1];
    }

    // A background wallpaper should leave most frame time for other applications.
    // This is a measured headroom policy, not a claim of actual GPU/CPU utilization.
    public static bool FitsBudget(double decodeMs, double renderMs, double sourceFps, int targetFps, double budgetMsPerSecond = 250)
        => decodeMs * Math.Max(1, sourceFps) + renderMs * Math.Min(Math.Max(1, sourceFps), targetFps) <= budgetMsPerSecond;

    public static WallpaperSettings LowerBudget(WallpaperSettings current)
    {
        var lower = current.Clone();
        var next = new ApplicationSettings();
        QualityPresets.Apply(next, current.VideoHeightLimit == 0 || current.VideoHeightLimit > 1440 ? QualityPreset.High :
            current.VideoHeightLimit > 1080 ? QualityPreset.Balanced : QualityPreset.Economy);
        lower.VideoHeightLimit = current.VideoHeightLimit == 0 ? next.VideoHeightLimit : Math.Min(current.VideoHeightLimit, next.VideoHeightLimit);
        lower.TargetFps = Math.Min(current.TargetFps, next.GlobalTargetFps);
        lower.RenderScale = Math.Min(current.RenderScale, next.RenderScale);
        return lower;
    }
}
