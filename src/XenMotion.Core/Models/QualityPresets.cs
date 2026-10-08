namespace XenMotion.Core.Models;

public enum QualityPreset { Custom, Economy, Balanced, High, Ultra }

public sealed record HardwareSnapshot(int MemoryMb, int LogicalProcessors, int DedicatedGpuMb, long DesktopPixels, string GraphicsName, string DriverVersion = "Unknown");

public static class QualityPresets
{
    public static QualityPreset Recommend(HardwareSnapshot hardware)
    {
        var native = ForDisplayPixels(hardware.DesktopPixels);
        return hardware.MemoryMb < 4096 || hardware.LogicalProcessors <= 2
            ? (QualityPreset)Math.Max((int)QualityPreset.Economy, (int)native - 1) : native;
    }

    public static QualityPreset ForDisplayPixels(long pixels)
        => new[] { QualityPreset.Economy, QualityPreset.Balanced, QualityPreset.High, QualityPreset.Ultra }
            .MinBy(p => Math.Abs((double)pixels - (p switch {
                QualityPreset.Economy => 1280L * 720, QualityPreset.Balanced => 1920L * 1080,
                QualityPreset.High => 2560L * 1440, _ => 3840L * 2160 })));

    public static void Apply(ApplicationSettings settings, QualityPreset preset)
    {
        if (preset == QualityPreset.Custom) return;
        settings.QualityPreset = preset;
        settings.GlobalTargetFps = preset switch { QualityPreset.Economy => 15, QualityPreset.Balanced => 24, QualityPreset.High => 30, _ => 60 };
        settings.VideoHeightLimit = preset switch { QualityPreset.Economy => 720, QualityPreset.Balanced => 1080, QualityPreset.High => 1440, _ => 2160 };
        settings.RenderScale = 1f;
        settings.HighQualityFiltering = preset != QualityPreset.Economy;
        settings.DefaultSharpness = preset == QualityPreset.Economy ? .15f : 0;
        settings.UseHardwareDecoding = true;
        settings.EnableGlassEffects = preset != QualityPreset.Economy;
        settings.Performance.BatteryFps = preset switch { QualityPreset.Economy => 15, QualityPreset.Balanced => 20, QualityPreset.High => 24, _ => 30 };
        settings.Performance.PauseOnBattery = preset == QualityPreset.Economy;
        settings.Performance.PauseOnFullscreen = settings.Performance.PauseOnMaximized = settings.Performance.PauseWhenCovered = true;
        settings.Performance.PauseOnlyWhenFullyCovered = false;
        settings.Performance.CoveredThresholdPercentage = 85;
        settings.Performance.HeartbeatIntervalMs = preset == QualityPreset.Economy ? 3000 : 1500;
    }
}
