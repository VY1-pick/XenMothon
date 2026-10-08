namespace XenMotion.Core.Models;

/// <summary>
/// Configures thresholds and policies for the visibility/activity manager.
/// </summary>
public sealed class PerformanceSettings
{
    public bool PauseOnScreenSaver { get; set; } = true;
    public bool PauseOnPowerSaver { get; set; } = true;
    public bool PauseOnRemoteSession { get; set; } = true;
    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseOnMaximized { get; set; } = true;
    public bool PauseOnBattery { get; set; } = false;
    public bool PauseOnApplicationFocus { get; set; }
    /// <summary>Foreground process names, separated by commas; optional .exe suffix.</summary>
    public string PauseApplications { get; set; } = string.Empty;
    public bool PauseWhenCovered { get; set; } = true;
    public bool PauseOnlyWhenFullyCovered { get; set; }
    public int CoveredThresholdPercentage { get; set; } = 85;
    public bool MuteAudioWhenCovered { get; set; } = true;
    public int HeartbeatIntervalMs { get; set; } = 1500;
    public int BatteryFps { get; set; } = 15;
}
