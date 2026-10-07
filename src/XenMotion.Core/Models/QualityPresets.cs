namespace XenMotion.Core.Models;

public enum QualityPreset { Custom, Economy, Balanced, High, Ultra }

public sealed record HardwareSnapshot(int MemoryMb, int LogicalProcessors, int DedicatedGpuMb, long DesktopPixels, string GraphicsName);

public static class QualityPresets
{
    public static QualityPreset Recommend(HardwareSnapshot hardware)
    {
        // ponytail: conservative capability heuristic; use measured playback throughput if automatic adaptation is added.
        if (hardware.MemoryMb < 6144 || hardware.LogicalProcessors <= 2) return QualityPreset.Economy;
        if (hardware.DesktopPixels > 3840L * 2160 && hardware.DedicatedGpuMb < 2048) return QualityPreset.Economy;
        if (hardware.MemoryMb >= 12288 && hardware.LogicalProcessors >= 8 && hardware.DedicatedGpuMb >= 4096)
            return QualityPreset.High;
        return QualityPreset.Balanced;
    }

    public static void Apply(ApplicationSettings settings, QualityPreset preset)
    {
        if (preset == QualityPreset.Custom) return;
        settings.QualityPreset = preset;
        settings.GlobalTargetFps = preset switch { QualityPreset.Economy => 15, QualityPreset.Balanced => 30, QualityPreset.High => 60, _ => 120 };
        settings.VideoHeightLimit = preset switch { QualityPreset.Economy => 720, QualityPreset.Balanced => 1440, _ => 0 };
        settings.RenderScale = preset switch { QualityPreset.Economy => 0.5f, QualityPreset.Balanced => 0.75f, _ => 1f };
        settings.HighQualityFiltering = preset != QualityPreset.Economy;
        settings.UseHardwareDecoding = true;
        settings.EnableGlassEffects = preset != QualityPreset.Economy;
        settings.Performance.BatteryFps = preset == QualityPreset.Economy ? 15 : 30;
        settings.Performance.PauseOnBattery = preset == QualityPreset.Economy;
        settings.Performance.PauseOnFullscreen = settings.Performance.PauseOnMaximized = settings.Performance.PauseWhenCovered = true;
        settings.Performance.HeartbeatIntervalMs = preset == QualityPreset.Economy ? 3000 : 1500;
    }
}
