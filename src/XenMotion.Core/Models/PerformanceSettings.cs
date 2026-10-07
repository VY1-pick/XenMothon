namespace XenMotion.Core.Models;

/// <summary>
/// Configures thresholds and policies for the visibility/activity manager.
/// </summary>
public sealed class PerformanceSettings
{
    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseOnMaximized { get; set; } = true;
    public bool PauseOnBattery { get; set; } = false;
    public bool PauseWhenCovered { get; set; } = true;
    public int CoveredThresholdPercentage { get; set; } = 85;
    public bool MuteAudioWhenCovered { get; set; } = true;
    public int HeartbeatIntervalMs { get; set; } = 1500;
    public int BatteryFps { get; set; } = 15;
}
