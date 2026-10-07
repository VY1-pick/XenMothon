using System.ComponentModel;
using System.Runtime.CompilerServices;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;
using XenMotion.UI.Services;

namespace XenMotion.UI.ViewModels;

/// <summary>
/// Presentation model for application personalization and performance settings.
/// Changes are kept in memory until the user explicitly saves them.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly ILogger<SettingsViewModel> _logger;

    private string _displayName = "Guest";
    private string _theme = "Dark";
    private bool _startWithWindows = true;
    private bool _startMinimizedToTray = true;
    private int _globalTargetFps = 60;
    private PlaybackScalingMode _defaultScalingMode = PlaybackScalingMode.Fill;
    private float _defaultVolume = 1.0f;
    private bool _defaultMuted = true;
    private float _defaultPlaybackSpeed = 1.0f;
    private bool _defaultLoop = true;
    private float _defaultBrightness = 1.0f;
    private float _defaultContrast = 1.0f;
    private float _defaultSaturation = 1.0f;
    private bool _pauseOnFullscreen = true;
    private bool _pauseOnMaximized = true;
    private bool _pauseOnBattery;
    private bool _pauseWhenCovered = true;
    private bool _pauseOnlyWhenFullyCovered;
    private int _coveredThresholdPercentage = 85;
    private bool _muteAudioWhenCovered = true;
    private int _heartbeatIntervalMs = 1500;
    private bool _isSaving;
    private string? _statusMessage;
    private bool _autoTunePlayback;
    private float _defaultSharpness;
    public bool AutoTunePlayback { get => _autoTunePlayback; set => SetField(ref _autoTunePlayback, value); }
    public float DefaultSharpness { get => _defaultSharpness; set => SetField(ref _defaultSharpness, Math.Clamp(value, 0, .3f)); }
    public string PlaybackMeasurement => _tuning.PlaybackReport;
    private readonly HardwareTuningService _tuning;
    private QualityPreset _qualityPreset;
    private int _videoHeightLimit, _batteryFps;
    private float _renderScale = 1;
    private bool _highQualityFiltering = true, _useHardwareDecoding = true, _enableGlassEffects = true;
    public QualityPreset QualityPreset { get => _qualityPreset; private set => SetField(ref _qualityPreset, value); }
    public string HardwareSummary => _tuning.Summary;
    public QualityPreset RecommendedPreset => _tuning.RecommendedPreset;
    public int VideoHeightLimit { get => _videoHeightLimit; set => SetField(ref _videoHeightLimit, value); }
    public int BatteryFps { get => _batteryFps; set => SetField(ref _batteryFps, Math.Clamp(value, 15, 240)); }
    public float RenderScale { get => _renderScale; set => SetField(ref _renderScale, Math.Clamp(value, .25f, 1f)); }
    public bool HighQualityFiltering { get => _highQualityFiltering; set => SetField(ref _highQualityFiltering, value); }
    public bool UseHardwareDecoding { get => _useHardwareDecoding; set => SetField(ref _useHardwareDecoding, value); }
    public bool EnableGlassEffects { get => _enableGlassEffects; set => SetField(ref _enableGlassEffects, value); }

    public void ApplyQualityPreset(QualityPreset preset)
    {
        var draft = new ApplicationSettings();
        QualityPresets.Apply(draft, preset);
        GlobalTargetFps = draft.GlobalTargetFps;
        VideoHeightLimit = draft.VideoHeightLimit;
        RenderScale = draft.RenderScale;
        HighQualityFiltering = draft.HighQualityFiltering;
        DefaultSharpness = draft.DefaultSharpness;
        UseHardwareDecoding = draft.UseHardwareDecoding;
        EnableGlassEffects = draft.EnableGlassEffects;
        BatteryFps = draft.Performance.BatteryFps;
        PauseOnBattery = draft.Performance.PauseOnBattery;
        PauseOnFullscreen = PauseOnMaximized = PauseWhenCovered = true;
        PauseOnlyWhenFullyCovered = draft.Performance.PauseOnlyWhenFullyCovered;
        CoveredThresholdPercentage = draft.Performance.CoveredThresholdPercentage;
        HeartbeatIntervalMs = draft.Performance.HeartbeatIntervalMs;
        QualityPreset = preset;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsSaved;

    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string Theme { get => _theme; set => SetField(ref _theme, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetField(ref _startWithWindows, value); }
    public bool StartMinimizedToTray { get => _startMinimizedToTray; set => SetField(ref _startMinimizedToTray, value); }
    public int GlobalTargetFps { get => _globalTargetFps; set => SetField(ref _globalTargetFps, value); }
    public PlaybackScalingMode DefaultScalingMode { get => _defaultScalingMode; set => SetField(ref _defaultScalingMode, value); }
    public float DefaultVolume { get => _defaultVolume; set => SetField(ref _defaultVolume, Math.Clamp(value, 0f, 1f)); }
    public bool DefaultMuted { get => _defaultMuted; set => SetField(ref _defaultMuted, value); }
    public float DefaultPlaybackSpeed { get => _defaultPlaybackSpeed; set => SetField(ref _defaultPlaybackSpeed, Math.Clamp(value, 0.25f, 2f)); }
    public bool DefaultLoop { get => _defaultLoop; set => SetField(ref _defaultLoop, value); }
    public float DefaultBrightness { get => _defaultBrightness; set => SetField(ref _defaultBrightness, Math.Clamp(value, 0f, 2f)); }
    public float DefaultContrast { get => _defaultContrast; set => SetField(ref _defaultContrast, Math.Clamp(value, 0f, 2f)); }
    public float DefaultSaturation { get => _defaultSaturation; set => SetField(ref _defaultSaturation, Math.Clamp(value, 0f, 2f)); }
    public bool PauseOnFullscreen { get => _pauseOnFullscreen; set => SetField(ref _pauseOnFullscreen, value); }
    public bool PauseOnMaximized { get => _pauseOnMaximized; set => SetField(ref _pauseOnMaximized, value); }
    public bool PauseOnBattery { get => _pauseOnBattery; set => SetField(ref _pauseOnBattery, value); }
    public bool PauseWhenCovered { get => _pauseWhenCovered; set => SetField(ref _pauseWhenCovered, value); }
    public int CoveredThresholdPercentage { get => _coveredThresholdPercentage; set => SetField(ref _coveredThresholdPercentage, Math.Clamp(value, 1, 100)); }
    public bool PauseOnlyWhenFullyCovered { get => _pauseOnlyWhenFullyCovered; set => SetField(ref _pauseOnlyWhenFullyCovered, value); }
    public bool MuteAudioWhenCovered { get => _muteAudioWhenCovered; set => SetField(ref _muteAudioWhenCovered, value); }
    public int HeartbeatIntervalMs { get => _heartbeatIntervalMs; set => SetField(ref _heartbeatIntervalMs, Math.Clamp(value, 250, 5000)); }
    public bool IsSaving { get => _isSaving; private set => SetField(ref _isSaving, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }

    public SettingsViewModel(
        ISettingsService settingsService,
        IWallpaperEngineOrchestrator orchestrator,
        ILogger<SettingsViewModel> logger,
        HardwareTuningService? tuning = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tuning = tuning ?? new HardwareTuningService();
        _tuning.PlaybackReportChanged += (_, _) => OnPropertyChanged(nameof(PlaybackMeasurement));

        LoadFromSettings(_settingsService.CurrentSettings);
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public void Reload()
    {
        LoadFromSettings(_settingsService.CurrentSettings);
        StatusMessage = null;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (IsSaving) return;

        IsSaving = true;
        StatusMessage = null;

        try
        {
            Validate();

            await _settingsService.UpdateSettingsAsync(settings =>
            {
                settings.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "Guest" : DisplayName.Trim();
                settings.Theme = Theme;
                settings.StartWithWindows = StartWithWindows;
                settings.StartMinimizedToTray = StartMinimizedToTray;
                settings.GlobalTargetFps = GlobalTargetFps;
                settings.QualityPreset = QualityPreset;
                settings.AutoTunePlayback = AutoTunePlayback;
                settings.DefaultSharpness = DefaultSharpness;
                settings.VideoHeightLimit = VideoHeightLimit;
                settings.RenderScale = RenderScale;
                settings.HighQualityFiltering = HighQualityFiltering;
                settings.UseHardwareDecoding = UseHardwareDecoding;
                settings.EnableGlassEffects = EnableGlassEffects;
                settings.Performance.BatteryFps = BatteryFps;
                settings.DefaultScalingMode = DefaultScalingMode;
                settings.DefaultVolume = DefaultVolume;
                settings.DefaultMuted = DefaultMuted;
                settings.DefaultPlaybackSpeed = DefaultPlaybackSpeed;
                settings.DefaultLoop = DefaultLoop;
                settings.DefaultBrightness = DefaultBrightness;
                settings.DefaultContrast = DefaultContrast;
                settings.DefaultSaturation = DefaultSaturation;
                settings.Performance.PauseOnFullscreen = PauseOnFullscreen;
                settings.Performance.PauseOnMaximized = PauseOnMaximized;
                settings.Performance.PauseOnBattery = PauseOnBattery;
                settings.Performance.PauseWhenCovered = PauseWhenCovered;
                settings.Performance.PauseOnlyWhenFullyCovered = PauseOnlyWhenFullyCovered;
                settings.Performance.CoveredThresholdPercentage = CoveredThresholdPercentage;
                settings.Performance.MuteAudioWhenCovered = MuteAudioWhenCovered;
                settings.Performance.HeartbeatIntervalMs = HeartbeatIntervalMs;
            }, ct).ConfigureAwait(false);

            await _orchestrator.ApplyPlaybackSettingsAllAsync(_settingsService.CurrentSettings.CreatePlaybackSettings(), ct).ConfigureAwait(false);

            StatusMessage = "Settings saved.";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save application settings.");
            StatusMessage = $"Could not save settings: {ex.Message}";
            throw;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = "Guest";
        }

        if (GlobalTargetFps is < 15 or > 240)
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTargetFps), "FPS must be between 15 and 240.");
        }

        if (CoveredThresholdPercentage is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(CoveredThresholdPercentage), "Coverage threshold must be between 1% and 100%.");
        }

        if (HeartbeatIntervalMs is < 250 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(HeartbeatIntervalMs), "Heartbeat interval must be between 250 and 5000 ms.");
        }
    }

    private void LoadFromSettings(ApplicationSettings settings)
    {
        _displayName = settings.DisplayName;
        _qualityPreset = settings.QualityPreset;
        _autoTunePlayback = settings.AutoTunePlayback;
        _defaultSharpness = Math.Clamp(settings.DefaultSharpness, 0, .3f);
        _videoHeightLimit = settings.VideoHeightLimit;
        _renderScale = Math.Clamp(settings.RenderScale, .25f, 1f);
        _highQualityFiltering = settings.HighQualityFiltering;
        _useHardwareDecoding = settings.UseHardwareDecoding;
        _enableGlassEffects = settings.EnableGlassEffects;
        _batteryFps = Math.Clamp(settings.Performance.BatteryFps, 15, 240);
        _theme = "Dark"; // XenMotion is dark-only.
        _startWithWindows = settings.StartWithWindows;
        _startMinimizedToTray = settings.StartMinimizedToTray;
        _globalTargetFps = Math.Clamp(settings.GlobalTargetFps, 15, 240);
        _defaultScalingMode = settings.DefaultScalingMode;
        _defaultVolume = Math.Clamp(settings.DefaultVolume, 0f, 1f);
        _defaultMuted = settings.DefaultMuted;
        _defaultPlaybackSpeed = Math.Clamp(settings.DefaultPlaybackSpeed, 0.25f, 2f);
        _defaultLoop = settings.DefaultLoop;
        _defaultBrightness = Math.Clamp(settings.DefaultBrightness, 0f, 2f);
        _defaultContrast = Math.Clamp(settings.DefaultContrast, 0f, 2f);
        _defaultSaturation = Math.Clamp(settings.DefaultSaturation, 0f, 2f);
        _pauseOnFullscreen = settings.Performance.PauseOnFullscreen;
        _pauseOnMaximized = settings.Performance.PauseOnMaximized;
        _pauseOnBattery = settings.Performance.PauseOnBattery;
        _pauseWhenCovered = settings.Performance.PauseWhenCovered;
        _pauseOnlyWhenFullyCovered = settings.Performance.PauseOnlyWhenFullyCovered;
        _coveredThresholdPercentage = Math.Clamp(settings.Performance.CoveredThresholdPercentage, 1, 100);
        _muteAudioWhenCovered = settings.Performance.MuteAudioWhenCovered;
        _heartbeatIntervalMs = Math.Clamp(settings.Performance.HeartbeatIntervalMs, 250, 5000);

        OnPropertyChanged(string.Empty);
    }

    private void OnSettingsChanged(object? sender, ApplicationSettings settings)
    {
        LoadFromSettings(settings);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (propertyName is nameof(GlobalTargetFps) or nameof(VideoHeightLimit) or nameof(RenderScale) or nameof(HighQualityFiltering) or nameof(UseHardwareDecoding) or nameof(EnableGlassEffects) or nameof(BatteryFps) or nameof(PauseOnBattery) or nameof(PauseOnFullscreen) or nameof(PauseOnMaximized) or nameof(PauseWhenCovered) or nameof(PauseOnlyWhenFullyCovered) or nameof(CoveredThresholdPercentage) or nameof(HeartbeatIntervalMs) or nameof(DefaultSharpness))
        {
            _qualityPreset = QualityPreset.Custom;
            OnPropertyChanged(nameof(QualityPreset));
        }
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
