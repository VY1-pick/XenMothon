using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.ViewModels;

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
    private string _theme = "Light";
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
    private int _coveredThresholdPercentage = 85;
    private bool _muteAudioWhenCovered = true;
    private int _heartbeatIntervalMs = 1500;
    private bool _isSaving;
    private string? _statusMessage;

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
    public int CoveredThresholdPercentage { get => _coveredThresholdPercentage; set => SetField(ref _coveredThresholdPercentage, Math.Clamp(value, 50, 100)); }
    public bool MuteAudioWhenCovered { get => _muteAudioWhenCovered; set => SetField(ref _muteAudioWhenCovered, value); }
    public int HeartbeatIntervalMs { get => _heartbeatIntervalMs; set => SetField(ref _heartbeatIntervalMs, Math.Clamp(value, 250, 5000)); }
    public bool IsSaving { get => _isSaving; private set => SetField(ref _isSaving, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }

    public SettingsViewModel(
        ISettingsService settingsService,
        IWallpaperEngineOrchestrator orchestrator,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
                settings.Performance.CoveredThresholdPercentage = CoveredThresholdPercentage;
                settings.Performance.MuteAudioWhenCovered = MuteAudioWhenCovered;
                settings.Performance.HeartbeatIntervalMs = HeartbeatIntervalMs;
            }, ct).ConfigureAwait(false);

            // Apply the global FPS limit immediately to currently active renderers.
            await _orchestrator.SetGlobalFpsLimitAsync(GlobalTargetFps, ct).ConfigureAwait(false);

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

        if (CoveredThresholdPercentage is < 50 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(CoveredThresholdPercentage), "Coverage threshold must be between 50% and 100%.");
        }

        if (HeartbeatIntervalMs is < 250 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(HeartbeatIntervalMs), "Heartbeat interval must be between 250 and 5000 ms.");
        }
    }

    private void LoadFromSettings(ApplicationSettings settings)
    {
        _displayName = settings.DisplayName;
        _theme = settings.Theme is "Dark" or "System" ? settings.Theme : "Light";
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
        _coveredThresholdPercentage = Math.Clamp(settings.Performance.CoveredThresholdPercentage, 50, 100);
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
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
