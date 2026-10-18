using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>
/// Applies the saved <see cref="WallpaperSchedule"/>: time-of-day windows pin a wallpaper, and a
/// configured interval rotates through the rotation pool. Selection rules live in
/// <see cref="WallpaperSchedulePlanner"/>; this service owns only the clock, the cursor per monitor
/// and the engine calls, which keeps the timing logic testable with a fake time provider.
/// </summary>
public sealed class WallpaperScheduleService
{
    /// <summary>Windows are minute-granular, so a wake-up once a minute is precise enough.</summary>
    public static readonly TimeSpan MaxIdleDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinIdleDelay = TimeSpan.FromSeconds(5);

    private readonly ISettingsService _settingsService;
    private readonly ILibraryService _libraryService;
    private readonly IStorageManager _storageManager;
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly ILogger<WallpaperScheduleService> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<string, Guid> _applied = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _applicationOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _rotationCursor = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset _lastRotationUtc;

    public WallpaperScheduleService(
        ISettingsService settingsService,
        ILibraryService libraryService,
        IStorageManager storageManager,
        IWallpaperEngineOrchestrator orchestrator,
        ILogger<WallpaperScheduleService> logger,
        TimeProvider? timeProvider = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastRotationUtc = _timeProvider.GetUtcNow();
    }

    public bool IsRunning => _loop is not null;

    /// <summary>Raised with a human-readable description of what the schedule last changed.</summary>
    public event EventHandler<string>? WallpaperScheduled;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _lastRotationUtc = _timeProvider.GetUtcNow();
        _loop = Task.Run(() => RunAsync(token), token);
        _logger.LogInformation("Wallpaper schedule service started.");
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is null) return;

        try { await cts.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        cts.Dispose();
        _logger.LogInformation("Wallpaper schedule service stopped.");
    }

    /// <summary>Advances the rotation immediately, for the "Next wallpaper" affordance.</summary>
    public Task RotateNowAsync(CancellationToken ct = default)
        => ApplyAsync(ct, forceRotation: true, waitForGate: true);

    public async Task SetApplicationOverridesAsync(IReadOnlyDictionary<string, Guid> overrides)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _applicationOverrides.Clear();
            foreach (var (monitor, wallpaper) in overrides) _applicationOverrides[monitor] = wallpaper;
            await ApplyCoreAsync(CancellationToken.None, false).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Evaluates the schedule once. Safe to call after a settings save; a concurrent evaluation is
    /// skipped unless the caller explicitly waits for the gate.
    /// </summary>
    public async Task ApplyAsync(CancellationToken ct = default, bool forceRotation = false, bool waitForGate = false)
    {
        bool acquired = waitForGate
            ? await WaitAsync(ct).ConfigureAwait(false)
            : await _gate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!acquired) return;
        try
        {
            await ApplyCoreAsync(ct, forceRotation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Applying the wallpaper schedule failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyCoreAsync(CancellationToken ct, bool forceRotation)
    {
        var settings = _settingsService.CurrentSettings;
        var schedule = settings.Schedule;
        schedule ??= new WallpaperSchedule();

        var localNow = _timeProvider.GetLocalNow();
        var now = _timeProvider.GetUtcNow();
        int minuteOfDay = (int)localNow.TimeOfDay.TotalMinutes;
        bool rotationDue = (schedule.Enabled && schedule.RotationIntervalMinutes > 0 &&
            (forceRotation || now - _lastRotationUtc >= TimeSpan.FromMinutes(schedule.RotationIntervalMinutes)))
            || forceRotation;

        if (rotationDue)
        {
            // The cursor advances once per rotation, so pool[0] is the first rotation result.
            foreach (string monitor in Monitors(schedule))
            {
                _rotationCursor[monitor] = _rotationCursor.TryGetValue(monitor, out int cursor) ? cursor + 1 : 1;
            }

            _lastRotationUtc = now;
        }

        foreach (string monitorId in Monitors(schedule).Concat(_applied.Keys).Concat(_applicationOverrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var plan = _applicationOverrides.TryGetValue(monitorId, out var applicationWallpaper)
                ? new WallpaperSchedulePlan(applicationWallpaper, WallpaperSchedulePlanSource.Application)
                : WallpaperSchedulePlanner.Select(schedule, monitorId, minuteOfDay, _rotationCursor.TryGetValue(monitorId, out int cursor) ? cursor : 0);
            if (plan is null)
            {
                // Nothing applies any more; hand the monitor back to the saved assignment.
                if (_applied.ContainsKey(monitorId))
                {
                    await RestoreMonitorAsync(settings, monitorId, ct).ConfigureAwait(false);
                    _applied.Remove(monitorId);
                }
                continue;
            }

            if (_applied.TryGetValue(monitorId, out var current) && current == plan.WallpaperId) continue;

            var wallpaper = await _libraryService.GetWallpaperByIdAsync(plan.WallpaperId, ct).ConfigureAwait(false);
            if (wallpaper is null || wallpaper.IsMissing)
            {
                _logger.LogWarning("Scheduled wallpaper {Id} is no longer in the library; skipping monitor {Monitor}.", plan.WallpaperId, monitorId);
                _applied.Remove(monitorId);
                continue;
            }

            wallpaper.FilePath = _storageManager.ResolveAbsolutePath(wallpaper.FilePath);
            if (!File.Exists(wallpaper.FilePath))
            {
                _logger.LogWarning("Scheduled wallpaper {Id} is missing on disk; skipping monitor {Monitor}.", plan.WallpaperId, monitorId);
                _applied.Remove(monitorId);
                continue;
            }

            await _orchestrator.ApplyWallpaperAsync(monitorId, wallpaper, settings.CreatePlaybackSettings(), ct).ConfigureAwait(false);
            _applied[monitorId] = plan.WallpaperId;
            _logger.LogInformation("Schedule applied '{Title}' to {Monitor} ({Source}).", wallpaper.Title, monitorId, plan.Source);
            WallpaperScheduled?.Invoke(this, plan.Source == WallpaperSchedulePlanSource.TimeWindow
                ? $"Schedule: '{wallpaper.Title}' is active for its time window."
                : $"Rotation: '{wallpaper.Title}' is now on your desktop.");
        }
    }

    /// <summary>Waits for the evaluation gate, tolerating cancellation while queued.</summary>
    private async Task<bool> WaitAsync(CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RestoreMonitorAsync(ApplicationSettings settings, string monitorId, CancellationToken ct)
    {
        if (settings.MonitorWallpaperAssignments.TryGetValue(monitorId, out var baseId))
        {
            var wallpaper = await _libraryService.GetWallpaperByIdAsync(baseId, ct).ConfigureAwait(false);
            if (wallpaper is not null && !wallpaper.IsMissing)
            {
                wallpaper.FilePath = _storageManager.ResolveAbsolutePath(wallpaper.FilePath);
                if (File.Exists(wallpaper.FilePath))
                {
                    await _orchestrator.ApplyWallpaperAsync(monitorId, wallpaper, settings.CreatePlaybackSettings(), ct).ConfigureAwait(false);
                    return;
                }
            }
        }
        await _orchestrator.StopAsync(monitorId, ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> Monitors(WallpaperSchedule schedule)
    {
        var monitors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in schedule.Entries ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entry.MonitorId)) monitors.Add(entry.MonitorId);
        }

        if (monitors.Count == 0) monitors.Add(WallpaperSchedule.PrimaryMonitorId);
        return monitors;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await ApplyAsync(ct).ConfigureAwait(false);

            TimeSpan delay = MaxIdleDelay;
            var schedule = _settingsService.CurrentSettings.Schedule;
            if (schedule is { Enabled: true, RotationIntervalMinutes: > 0 })
            {
                var interval = TimeSpan.FromMinutes(schedule.RotationIntervalMinutes);
                var since = _timeProvider.GetUtcNow() - _lastRotationUtc;
                delay = interval - since;
            }

            if (delay < MinIdleDelay) delay = MinIdleDelay;
            if (delay > MaxIdleDelay) delay = MaxIdleDelay;

            try
            {
                await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
