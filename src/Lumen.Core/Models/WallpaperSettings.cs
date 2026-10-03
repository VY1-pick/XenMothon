using Lumen.Core.Enums;

namespace Lumen.Core.Models;

/// <summary>
/// Playback and visual adjustments applied to a specific wallpaper or monitor output.
/// </summary>
public sealed class WallpaperSettings
{
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = true;
    public int TargetFps { get; set; } = 60;
    public PlaybackScalingMode ScalingMode { get; set; } = PlaybackScalingMode.Fill;
    public float PlaybackSpeed { get; set; } = 1.0f;
    public bool Loop { get; set; } = true;
    public float Brightness { get; set; } = 1.0f;
    public float Contrast { get; set; } = 1.0f;
    public float Saturation { get; set; } = 1.0f;

    public WallpaperSettings Clone() => new()
    {
        Volume = Volume,
        IsMuted = IsMuted,
        TargetFps = TargetFps,
        ScalingMode = ScalingMode,
        PlaybackSpeed = PlaybackSpeed,
        Loop = Loop,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation
    };
}
