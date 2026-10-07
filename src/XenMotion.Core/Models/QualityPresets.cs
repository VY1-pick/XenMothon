namespace XenMotion.Core.Models;

public enum QualityPreset { Custom, Economy, Balanced, High, Ultra }

public sealed record HardwareSnapshot(int MemoryMb, int LogicalProcessors, int DedicatedGpuMb, long DesktopPixels, string GraphicsName, string DriverVersion = "Unknown");

public static class QualityPresets
{
    public static QualityPreset Recommend(HardwareSnapshot hardware)
    {
        // ponytail: capability heuristic sets the starting ceiling; per-video calibration measures actual playback cost.
        if (hardware.MemoryMb < 6144 || hardware.LogicalProcessors <= 2) return QualityPreset.Economy;
        if (hardware.DesktopPixels > 3840L * 2160 && hardware.DedicatedGpuMb < 2048) return QualityPreset.Economy;
        if (hardware.MemoryMb >= 16384 && hardware.LogicalProcessors >= 8 && hardware.DedicatedGpuMb >= 6144)
            return QualityPreset.Ultra;
        if (hardware.MemoryMb >= 12288 && hardware.LogicalProcessors >= 8 && hardware.DedicatedGpuMb >= 4096)
            return QualityPreset.High;
        return QualityPreset.Balanced;
    }

    public static void Apply(ApplicationSettings settings, QualityPreset preset)
    {
        if (preset == QualityPreset.Custom) return;
        settings.QualityPreset = preset;
        settings.GlobalTargetFps = preset switch { QualityPreset.Economy => 15, QualityPreset.Balanced => 24, QualityPreset.High => 30, _ => 60 };
        settings.VideoHeightLimit = preset switch { QualityPreset.Economy => 720, QualityPreset.Balanced => 1080, QualityPreset.High => 1440, _ => 0 };
        settings.RenderScale = preset switch { QualityPreset.Economy => .5f, QualityPreset.Balanced => .75f, _ => 1f };
        settings.HighQualityFiltering = preset != QualityPreset.Economy;
        settings.DefaultSharpness = preset == QualityPreset.Economy ? .15f : 0;
        settings.UseHardwareDecoding = true;
        settings.EnableGlassEffects = preset != QualityPreset.Economy;
        settings.Performance.BatteryFps = preset switch { QualityPreset.Economy => 15, QualityPreset.Balanced => 20, QualityPreset.High => 24, _ => 30 };
        settings.Performance.PauseOnBattery = preset == QualityPreset.Economy;
        settings.Performance.PauseOnFullscreen = settings.Performance.PauseOnMaximized = settings.Performance.PauseWhenCovered = true;
        settings.Performance.PauseOnlyWhenFullyCovered = false;
        settings.Performance.CoveredThresholdPercentage = preset == QualityPreset.Economy ? 1 : 85;
        settings.Performance.HeartbeatIntervalMs = preset == QualityPreset.Economy ? 3000 : 1500;
    }
}
