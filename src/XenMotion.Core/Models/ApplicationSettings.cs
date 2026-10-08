using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

/// <summary>
/// Root user configuration stored locally for the offline-first application.
/// </summary>
public sealed class ApplicationSettings
{
    public string DisplayName { get; set; } = "Guest";
    public string StoragePath { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public bool SyncWindowsAccent { get; set; }
    public bool SyncDesktopBackground { get; set; }
    public LockScreenWallpaperMode LockScreenMode { get; set; }
    public string LockScreenImagePath { get; set; } = string.Empty;
    /// <summary>Enables the production HTTP catalog provider. Disabled by default to preserve offline-first behavior.</summary>
    public bool UseRemoteCatalog { get; set; } = false;
    /// <summary>Base URL for the catalog API, for example https://catalog.example.com/.</summary>
    public string CatalogBackendUrl { get; set; } = string.Empty;
    public int CatalogRequestTimeoutSeconds { get; set; } = 15;
    public bool UseLocalCatalogFallback { get; set; } = true;
    /// <summary>Optional HTTPS release manifest endpoint. Empty disables update checks.</summary>
    public string UpdateManifestUrl { get; set; } = string.Empty;
    /// <summary>Update checks are opt-in until a production release manifest is configured.</summary>
    public bool CheckForUpdates { get; set; } = false;
    /// <summary>Optional creator API base URL. Empty keeps creator upload unavailable until a backend is configured.</summary>
    public string CreatorBackendUrl { get; set; } = string.Empty;
    public int GlobalTargetFps { get; set; } = 30;
    public QualityPreset QualityPreset { get; set; } = QualityPreset.Custom;
    public int VideoHeightLimit { get; set; }
    public float RenderScale { get; set; } = 1;
    public bool HighQualityFiltering { get; set; } = true;
    public bool UseHardwareDecoding { get; set; } = true;
    public bool AutoPlayMotionPreviews { get; set; }
    public bool EnableGlassEffects { get; set; } = true;
    public bool AutoTunePlayback { get; set; }
    public float DefaultSharpness { get; set; }
    public PlaybackScalingMode DefaultScalingMode { get; set; } = PlaybackScalingMode.Fill;
    public float DefaultVolume { get; set; } = 1.0f;
    public bool DefaultMuted { get; set; } = true;
    public float DefaultPlaybackSpeed { get; set; } = 1.0f;
    public bool DefaultLoop { get; set; } = true;
    public float DefaultBrightness { get; set; } = 1.0f;
    public float DefaultContrast { get; set; } = 1.0f;
    public float DefaultSaturation { get; set; } = 1.0f;
    public PerformanceSettings Performance { get; set; } = new();
    public Dictionary<string, Guid> MonitorWallpaperAssignments { get; set; } = [];
    public Dictionary<string, WallpaperEdits> WallpaperEditPresets { get; set; } = [];
    public Dictionary<Guid, WallpaperEdits> WallpaperEdits { get; set; } = [];
    public bool GlobalEditsEnabled { get; set; }
    public WallpaperEdits GlobalEdits { get; set; } = new();
    public HashSet<Guid> CustomEditWallpapers { get; set; } = [];

    public void ApplyEdits(Guid id, WallpaperSettings playback)
    {
        if (GlobalEditsEnabled && !CustomEditWallpapers.Contains(id)) GlobalEdits.ApplyTo(playback);
        else if (WallpaperEdits.TryGetValue(id, out var edits)) edits.ApplyTo(playback);
    }
    public void Normalize()
    {
        static float Finite(float value, float min, float max, float fallback) => float.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
        DisplayName ??= "Guest"; Theme ??= "Dark"; StoragePath ??= string.Empty;
        CatalogBackendUrl ??= string.Empty; UpdateManifestUrl ??= string.Empty; CreatorBackendUrl ??= string.Empty;
        LockScreenImagePath ??= string.Empty;
        if (!Enum.IsDefined(LockScreenMode)) LockScreenMode = LockScreenWallpaperMode.Disabled;
        GlobalTargetFps = Math.Clamp(GlobalTargetFps,15,240);
        VideoHeightLimit = Math.Clamp(VideoHeightLimit,0,16384);
        RenderScale = Finite(RenderScale,.25f,1,1);
        DefaultSharpness = Finite(DefaultSharpness,0,.3f,0);
        DefaultVolume = Finite(DefaultVolume,0,1,1);
        DefaultPlaybackSpeed = Finite(DefaultPlaybackSpeed,.25f,2,1);
        DefaultBrightness = Finite(DefaultBrightness,0,2,1);
        DefaultContrast = Finite(DefaultContrast,0,2,1);
        DefaultSaturation = Finite(DefaultSaturation,0,2,1);
        Performance ??= new(); MonitorWallpaperAssignments ??= [];
        WallpaperEdits ??= [];
        WallpaperEditPresets ??= [];
        GlobalEdits ??= new(); CustomEditWallpapers ??= [];
        foreach (var key in WallpaperEdits.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) WallpaperEdits.Remove(key);
        foreach (var key in WallpaperEditPresets.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) WallpaperEditPresets.Remove(key);
        Performance.PauseApplications ??= string.Empty;
        Performance.HeartbeatIntervalMs = Math.Clamp(Performance.HeartbeatIntervalMs,250,5000);
        Performance.CoveredThresholdPercentage = Math.Clamp(Performance.CoveredThresholdPercentage,1,100);
        // Migrate the old Economy preset, whose 1% threshold paused before its first frame.
        if (QualityPreset == QualityPreset.Economy && Performance.CoveredThresholdPercentage == 1)
            Performance.CoveredThresholdPercentage = 85;
        Performance.BatteryFps = Math.Clamp(Performance.BatteryFps,15,240);
        CatalogRequestTimeoutSeconds = Math.Clamp(CatalogRequestTimeoutSeconds,1,60);
        if (!Enum.IsDefined(QualityPreset)) QualityPreset = QualityPreset.Custom;
        if (!Enum.IsDefined(DefaultScalingMode)) DefaultScalingMode = PlaybackScalingMode.Fill;
    }

    public WallpaperSettings CreatePlaybackSettings() => new()
    {
        TargetFps = Math.Clamp(GlobalTargetFps, 15, 240), ScalingMode = DefaultScalingMode,
        Volume = DefaultVolume, IsMuted = DefaultMuted, PlaybackSpeed = DefaultPlaybackSpeed,
        Loop = DefaultLoop, Brightness = DefaultBrightness, Contrast = DefaultContrast,
        Saturation = DefaultSaturation, VideoHeightLimit = VideoHeightLimit, RenderScale = RenderScale,
        HighQualityFiltering = HighQualityFiltering, UseHardwareDecoding = UseHardwareDecoding,
        AutoTunePlayback = AutoTunePlayback, Sharpness = DefaultSharpness
    };
}

public enum LockScreenWallpaperMode { Disabled, DesktopWallpaper, SeparateImage }
