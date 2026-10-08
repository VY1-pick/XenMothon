using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

/// <summary>Non-destructive visual edits, independent of global playback quality.</summary>
public sealed class WallpaperEdits
{
    public bool FlipHorizontal { get; set; }
    public float Opacity { get; set; } = 1;
    public float Brightness { get; set; } = 1;
    public float Contrast { get; set; } = 1;
    public float Saturation { get; set; } = 1;
    public float Red { get; set; } = 1;
    public float Green { get; set; } = 1;
    public float Blue { get; set; } = 1;
    public float? PlaybackSpeed { get; set; }
    public float? Volume { get; set; }
    public float? Sharpness { get; set; }
    public bool? Loop { get; set; }
    public bool? IsMuted { get; set; }
    public PlaybackScalingMode? ScalingMode { get; set; }

    public WallpaperEdits Clone() => (WallpaperEdits)MemberwiseClone();

    public void ApplyTo(WallpaperSettings settings)
    {
        static float Valid(float value, float maximum) => float.IsFinite(value) ? Math.Clamp(value, 0, maximum) : 1;
        settings.FlipHorizontal = FlipHorizontal;
        settings.Opacity = Valid(Opacity, 1);
        settings.Brightness = Valid(Brightness, 2);
        settings.Contrast = Valid(Contrast, 2);
        settings.Saturation = Valid(Saturation, 2);
        settings.Red = Valid(Red, 2);
        settings.Green = Valid(Green, 2);
        settings.Blue = Valid(Blue, 2);
        if (PlaybackSpeed is { } speed) settings.PlaybackSpeed = Math.Clamp(Valid(speed, 2), .25f, 2);
        if (Volume is { } volume) settings.Volume = Valid(volume, 1);
        if (Sharpness is { } sharpness) settings.Sharpness = float.IsFinite(sharpness) ? Math.Clamp(sharpness, 0, .3f) : 0;
        if (Loop is { } loop) settings.Loop = loop;
        if (IsMuted is { } muted) settings.IsMuted = muted;
        if (ScalingMode is { } mode && Enum.IsDefined(mode)) settings.ScalingMode = mode;
    }
}
