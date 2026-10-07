using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

/// <summary>
/// Playback and visual adjustments applied to a specific wallpaper or monitor output.
/// </summary>
public sealed class WallpaperSettings
{
    public bool AutoTunePlayback { get; set; }
    public float Sharpness { get; set; }
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = true;
    public int TargetFps { get; set; } = 30;
    public PlaybackScalingMode ScalingMode { get; set; } = PlaybackScalingMode.Fill;
    public float PlaybackSpeed { get; set; } = 1.0f;
    public bool Loop { get; set; } = true;
    public float Brightness { get; set; } = 1.0f;
    public float Contrast { get; set; } = 1.0f;
    public float Saturation { get; set; } = 1.0f;
    public int VideoHeightLimit { get; set; }
    public float RenderScale { get; set; } = 1;
    public bool HighQualityFiltering { get; set; } = true;
    public bool UseHardwareDecoding { get; set; } = true;

    public WallpaperSettings Clone() => new()
    {
        AutoTunePlayback = AutoTunePlayback, Sharpness = Sharpness,
        Volume = Volume,
        IsMuted = IsMuted,
        TargetFps = TargetFps,
        ScalingMode = ScalingMode,
        PlaybackSpeed = PlaybackSpeed,
        Loop = Loop,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation, VideoHeightLimit = VideoHeightLimit, RenderScale = RenderScale,
        HighQualityFiltering = HighQualityFiltering, UseHardwareDecoding = UseHardwareDecoding
    };
}
