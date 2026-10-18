using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Infrastructure.Persistence.Updates;

namespace XenMotion.UI.ViewModels;

/// <summary>One choosable wallpaper for the schedule and per-application pickers.</summary>
public sealed class WallpaperChoice
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string Badge { get; init; } = string.Empty;
    public override string ToString() => Title;
}

/// <summary>A schedule slot as edited in the settings draft.</summary>
public sealed class ScheduleEntryDraft
{
    public required Guid WallpaperId { get; init; }
    public required string Title { get; init; }
    public bool UseTimeWindow { get; init; }
    public int StartMinutes { get; init; }
    public int EndMinutes { get; init; }
    public string MonitorId { get; init; } = WallpaperSchedule.PrimaryMonitorId;

    public string WindowText => UseTimeWindow
        ? $"{WallpaperSchedulePlanner.FormatTime(StartMinutes)} – {WallpaperSchedulePlanner.FormatTime(EndMinutes)}"
        : "Rotation";

    public string DetailText => UseTimeWindow ? $"Time window · {WindowText}" : "Rotation pool";
}

/// <summary>A per-application rule as edited in the settings draft.</summary>
public sealed class ApplicationRuleDraft
{
    public required string ProcessName { get; init; }
    public required Guid WallpaperId { get; init; }
    public required string Title { get; init; }
    public string MonitorId { get; init; } = WallpaperSchedule.PrimaryMonitorId;
    public string DetailText => $"While {ProcessName} is focused";
}

/// <summary>
/// Automation half of the settings sheet: online catalog selection, the wallpaper schedule,
/// per-application rules and the manual update check. Kept in a partial file so the large
/// performance/personalization half stays readable.
/// </summary>
public sealed partial class SettingsViewModel
{
    private CatalogSource _catalogSource = CatalogSource.Local;
    private string _catalogManifestUrl = string.Empty;
    private bool _checkForUpdates = true;
    private string _updateManifestUrl = ApplicationSettings.DefaultUpdateManifestUrl;
    private string _updateStatus = string.Empty;
    private bool _isCheckingForUpdates;
    private bool _scheduleEnabled;
    private int _rotationIntervalMinutes;
    private string _automationStatus = string.Empty;

    public ObservableCollection<ScheduleEntryDraft> ScheduleEntries { get; } = [];
    public ObservableCollection<ApplicationRuleDraft> ApplicationRules { get; } = [];
    public ObservableCollection<WallpaperChoice> WallpaperChoices { get; } = [];

    public CatalogSource CatalogSource
    {
        get => _catalogSource;
        set
        {
            if (SetField(ref _catalogSource, value))
            {
                OnPropertyChanged(nameof(IsServerCatalogSelected));
                OnPropertyChanged(nameof(IsStaticCatalogSelected));
                OnPropertyChanged(nameof(CatalogSourceIndex));
            }
        }
    }

    /// <summary>Index for the catalog source selector (0 local, 1 server, 2 static file).</summary>
    public int CatalogSourceIndex
    {
        get => (int)_catalogSource;
        set
        {
            if (value is < 0 or > 2) return;
            CatalogSource = (CatalogSource)value;
        }
    }

    public bool IsServerCatalogSelected => _catalogSource == CatalogSource.ServerApi;
    public bool IsStaticCatalogSelected => _catalogSource == CatalogSource.StaticManifest;

    /// <summary>Kept for the legacy single-toggle binding and the existing automation tests.</summary>
    public bool UseRemoteCatalog
    {
        get => _catalogSource == CatalogSource.ServerApi;
        set => CatalogSource = value ? CatalogSource.ServerApi : CatalogSource.Local;
    }

    public string CatalogManifestUrl { get => _catalogManifestUrl; set => SetField(ref _catalogManifestUrl, value); }
    public bool CheckForUpdates { get => _checkForUpdates; set => SetField(ref _checkForUpdates, value); }
    public string UpdateManifestUrl { get => _updateManifestUrl; set => SetField(ref _updateManifestUrl, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetField(ref _updateStatus, value); }
    public bool IsCheckingForUpdates { get => _isCheckingForUpdates; private set => SetField(ref _isCheckingForUpdates, value); }
    public string AutomationStatus { get => _automationStatus; private set => SetField(ref _automationStatus, value); }
    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set
        {
            if (SetField(ref _scheduleEnabled, value)) OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public int RotationIntervalMinutes
    {
        get => _rotationIntervalMinutes;
        set
        {
            if (SetField(ref _rotationIntervalMinutes, Math.Clamp(value, 0, 1440))) OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public string ScheduleSummary
    {
        get
        {
            if (!_scheduleEnabled) return "Schedule is off; wallpapers only change when you choose one.";
            int windows = ScheduleEntries.Count(entry => entry.UseTimeWindow);
            int rotation = ScheduleEntries.Count(entry => !entry.UseTimeWindow);
            string rotationText = _rotationIntervalMinutes > 0
                ? $"rotating {rotation} wallpaper(s) every {_rotationIntervalMinutes} min"
                : "rotation paused";
            return $"Schedule is on: {windows} time window(s), {rotationText}.";
        }
    }

    /// <summary>Loads the schedule/rules draft from persisted settings.</summary>
    public void LoadAutomationDraft(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _scheduleEnabled = settings.Schedule.Enabled;
        _rotationIntervalMinutes = settings.Schedule.RotationIntervalMinutes;

        ScheduleEntries.Clear();
        foreach (var entry in settings.Schedule.Entries)
        {
            ScheduleEntries.Add(new ScheduleEntryDraft
            {
                WallpaperId = entry.WallpaperId,
                Title = TitleFor(entry.WallpaperId),
                UseTimeWindow = entry.UseTimeWindow,
                StartMinutes = entry.StartMinutes,
                EndMinutes = entry.EndMinutes,
                MonitorId = entry.MonitorId
            });
        }

        ApplicationRules.Clear();
        foreach (var rule in settings.ApplicationWallpaperRules)
        {
            ApplicationRules.Add(new ApplicationRuleDraft
            {
                ProcessName = ApplicationWallpaperRules.NormalizeProcessName(rule.ProcessName),
                WallpaperId = rule.WallpaperId,
                Title = TitleFor(rule.WallpaperId),
                MonitorId = rule.MonitorId
            });
        }

        AutomationStatus = string.Empty;
        OnPropertyChanged(nameof(ScheduleEnabled));
        OnPropertyChanged(nameof(RotationIntervalMinutes));
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    /// <summary>Replaces the pending schedule and rules on the settings snapshot being saved.</summary>
    public void ApplyAutomationDraft(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Schedule ??= new WallpaperSchedule();
        settings.Schedule.Enabled = ScheduleEnabled;
        settings.Schedule.RotationIntervalMinutes = RotationIntervalMinutes;
        settings.Schedule.Entries = ScheduleEntries.Select(entry => new WallpaperScheduleEntry
        {
            WallpaperId = entry.WallpaperId,
            MonitorId = entry.MonitorId,
            UseTimeWindow = entry.UseTimeWindow,
            StartMinutes = entry.StartMinutes,
            EndMinutes = entry.EndMinutes
        }).ToList();

        settings.ApplicationWallpaperRules = ApplicationRules.Select(rule => new ApplicationWallpaperRule
        {
            ProcessName = rule.ProcessName,
            WallpaperId = rule.WallpaperId,
            MonitorId = rule.MonitorId
        }).ToList();
    }

    /// <summary>Refreshes the picker contents from the library.</summary>
    public async Task RefreshWallpaperChoicesAsync(ILibraryService library, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        var wallpapers = await library.GetWallpapersAsync(ct).ConfigureAwait(true);

        WallpaperChoices.Clear();
        foreach (var wallpaper in wallpapers)
        {
            WallpaperChoices.Add(new WallpaperChoice
            {
                Id = wallpaper.Id,
                Title = wallpaper.Title,
                Badge = wallpaper.IsVideo ? "Video" : "Image"
            });
        }

        // Re-resolve titles for drafts loaded before the library finished loading.
        if (ScheduleEntries.Count > 0 || ApplicationRules.Count > 0)
        {
            var entries = ScheduleEntries.Select(entry => new ScheduleEntryDraft
            {
                WallpaperId = entry.WallpaperId,
                Title = TitleFor(entry.WallpaperId),
                UseTimeWindow = entry.UseTimeWindow,
                StartMinutes = entry.StartMinutes,
                EndMinutes = entry.EndMinutes,
                MonitorId = entry.MonitorId
            }).ToList();
            ScheduleEntries.Clear();
            foreach (var entry in entries) ScheduleEntries.Add(entry);

            var rules = ApplicationRules.Select(rule => new ApplicationRuleDraft
            {
                ProcessName = rule.ProcessName,
                WallpaperId = rule.WallpaperId,
                Title = TitleFor(rule.WallpaperId),
                MonitorId = rule.MonitorId
            }).ToList();
            ApplicationRules.Clear();
            foreach (var rule in rules) ApplicationRules.Add(rule);
        }

        OnPropertyChanged(nameof(ScheduleSummary));
    }

    /// <summary>Adds a schedule slot. An empty window text adds the wallpaper to the rotation pool.</summary>
    public bool TryAddScheduleEntry(Guid wallpaperId, string? windowText, out string error)
    {
        error = string.Empty;
        if (wallpaperId == Guid.Empty)
        {
            error = "Choose a wallpaper first.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(windowText))
        {
            ScheduleEntries.Add(new ScheduleEntryDraft { WallpaperId = wallpaperId, Title = TitleFor(wallpaperId) });
            _hasDraftChanges = true;
            AutomationStatus = "Added to the rotation pool.";
            OnPropertyChanged(nameof(ScheduleSummary));
            return true;
        }

        var parts = windowText.Split('-', StringSplitOptions.TrimEntries);
        int? start = parts.Length == 2 ? WallpaperSchedulePlanner.TryParseTime(parts[0]) : null;
        int? end = parts.Length == 2 ? WallpaperSchedulePlanner.TryParseTime(parts[1]) : null;
        if (start is null || end is null)
        {
            error = "Use a window like 07:00-19:00, or leave the box empty to rotate this wallpaper.";
            return false;
        }

        ScheduleEntries.Add(new ScheduleEntryDraft
        {
            WallpaperId = wallpaperId,
            Title = TitleFor(wallpaperId),
            UseTimeWindow = true,
            StartMinutes = start.Value,
            EndMinutes = end.Value
        });
        AutomationStatus = $"Added a {parts[0]}–{parts[1]} window.";
        _hasDraftChanges = true;
        OnPropertyChanged(nameof(ScheduleSummary));
        return true;
    }

    public void RemoveScheduleEntry(ScheduleEntryDraft? entry)
    {
        if (entry is null) return;
        ScheduleEntries.Remove(entry);
        _hasDraftChanges = true;
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    public bool TryAddApplicationRule(string? processName, Guid wallpaperId, out string error)
    {
        error = string.Empty;
        string normalized = ApplicationWallpaperRules.NormalizeProcessName(processName);
        if (normalized.Length == 0)
        {
            error = "Enter the application name, for example code or chrome.";
            return false;
        }

        if (wallpaperId == Guid.Empty)
        {
            error = "Choose a wallpaper first.";
            return false;
        }

        if (ApplicationRules.Any(rule => string.Equals(rule.ProcessName, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"{normalized} already has a rule.";
            return false;
        }

        ApplicationRules.Add(new ApplicationRuleDraft { ProcessName = normalized, WallpaperId = wallpaperId, Title = TitleFor(wallpaperId) });
        _hasDraftChanges = true;
        AutomationStatus = $"Added a rule for {normalized}.";
        return true;
    }

    public void RemoveApplicationRule(ApplicationRuleDraft? rule)
    {
        if (rule is not null && ApplicationRules.Remove(rule)) _hasDraftChanges = true;
    }

    /// <summary>Manual update check used by the Updates section; the app never installs by itself.</summary>
    public async Task CheckForUpdatesNowAsync(CancellationToken ct = default)
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        UpdateStatus = "Checking for a newer release…";
        var watch = Stopwatch.StartNew();

        try
        {
            var settings = _settingsService.CurrentSettings;
            bool enabled = settings.CheckForUpdates;
            string url = settings.UpdateManifestUrl;
            if (!enabled || string.IsNullOrWhiteSpace(url))
            {
                UpdateStatus = "Turn on update checks and set an HTTPS manifest first.";
                return;
            }

            var service = new UpdateCheckService(_settingsService);
            var update = await service.CheckAsync(ct).ConfigureAwait(true);
            UpdateStatus = update is null
                ? $"You are on the newest release (version {UpdateCheckService.CurrentVersion})."
                : $"Version {update.Version} is available. Notes: {update.ReleaseNotesUrl ?? update.DownloadUrl}";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"The update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
            _ = watch.Elapsed;
        }
    }

    private string TitleFor(Guid wallpaperId)
    {
        var choice = WallpaperChoices.FirstOrDefault(candidate => candidate.Id == wallpaperId);
        return choice?.Title ?? wallpaperId.ToString("D")[..8];
    }
}
