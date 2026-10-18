using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>Foreground rules override the schedule without changing the saved startup assignment.</summary>
public sealed class ApplicationWallpaperService(
    IForegroundApplicationMonitor monitor,
    ISettingsService settingsService,
    WallpaperScheduleService schedule,
    ILogger<ApplicationWallpaperService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _stopping;

    public event EventHandler<string>? WallpaperSwitched;
    public string? ActiveApplication { get; private set; }
    public bool IsRunning => monitor.IsRunning;

    public void Start()
    {
        if (monitor.IsRunning) return;
        _stopping = false;
        monitor.ForegroundApplicationChanged += OnForegroundApplicationChanged;
        monitor.Start();
        _ = HandleAsync(monitor.CurrentProcessName);
    }

    public async Task StopAsync()
    {
        _stopping = true;
        monitor.ForegroundApplicationChanged -= OnForegroundApplicationChanged;
        monitor.Stop();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private void OnForegroundApplicationChanged(object? sender, string? processName) => _ = HandleAsync(processName);

    public async Task HandleAsync(string? processName)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping) return;
            var rules = settingsService.CurrentSettings.ApplicationWallpaperRules;
            var overrides = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (string monitorId in rules.Select(rule => rule.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase))
                if (ApplicationWallpaperRules.Select(rules, processName, monitorId) is { } id) overrides[monitorId] = id;

            await schedule.SetApplicationOverridesAsync(overrides).ConfigureAwait(false);
            ActiveApplication = overrides.Count > 0 ? ApplicationWallpaperRules.NormalizeProcessName(processName) : null;
            WallpaperSwitched?.Invoke(this, ActiveApplication is null ? "Application override ended." : $"Wallpaper rules applied for {ActiveApplication}.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Per-application wallpaper switching failed."); }
        finally { _gate.Release(); }
    }
}
