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
    /// <summary>Enables the production HTTP catalog provider. Disabled by default to preserve offline-first behavior.</summary>
    public bool UseRemoteCatalog { get; set; } = false;
    /// <summary>Base URL for the catalog API, for example https://catalog.example.com/.</summary>
    public string CatalogBackendUrl { get; set; } = string.Empty;
    public int CatalogRequestTimeoutSeconds { get; set; } = 15;
    public bool UseLocalCatalogFallback { get; set; } = true;
    /// <summary>Optional HTTPS/HTTP release manifest endpoint. Empty disables update checks.</summary>
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
    public bool EnableGlassEffects { get; set; } = true;
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
    public WallpaperSettings CreatePlaybackSettings() => new()
    {
        TargetFps = Math.Clamp(GlobalTargetFps, 15, 240), ScalingMode = DefaultScalingMode,
        Volume = DefaultVolume, IsMuted = DefaultMuted, PlaybackSpeed = DefaultPlaybackSpeed,
        Loop = DefaultLoop, Brightness = DefaultBrightness, Contrast = DefaultContrast,
        Saturation = DefaultSaturation, VideoHeightLimit = VideoHeightLimit, RenderScale = RenderScale,
        HighQualityFiltering = HighQualityFiltering, UseHardwareDecoding = UseHardwareDecoding
    };
}
