using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

public sealed class SavedWallpaperService
{
    private readonly IWallpaperEngineOrchestrator engine;
    private readonly ISettingsService settings;
    private readonly ILibraryService library;
    private readonly IStorageManager storage;
    private readonly ILogger<SavedWallpaperService> logger;

    public SavedWallpaperService(IWallpaperEngineOrchestrator engine, ISettingsService settings,
        ILibraryService library, IStorageManager storage, ILogger<SavedWallpaperService> logger)
    {
        this.engine = engine; this.settings = settings; this.library = library; this.storage = storage; this.logger = logger;
        engine.MonitorStateChanged += (_, change) =>
        {
            if (change.MonitorDeviceId == WallpaperSchedule.PrimaryMonitorId &&
                change.State is WallpaperState.Playing or WallpaperState.Paused or WallpaperState.PartiallyVisible or WallpaperState.Stopped)
                SetActiveWallpaper(engine.GetActiveWallpaper(change.MonitorDeviceId));
        };
    }

    public Wallpaper? ActiveWallpaper { get; private set; }
    public event EventHandler? ActiveWallpaperChanged;
    private void SetActiveWallpaper(Wallpaper? wallpaper)
    {
        if (ActiveWallpaper?.Id == wallpaper?.Id) return;
        ActiveWallpaper = wallpaper;
        ActiveWallpaperChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ApplyAsync(string monitor, Wallpaper wallpaper, WallpaperSettings? playback = null,
        CancellationToken ct = default)
    {
        await engine.ApplyWallpaperAsync(monitor, wallpaper, playback ?? PlaybackSettings(), ct).ConfigureAwait(false);
        if (engine.GetMonitorState(monitor) is not (WallpaperState.Playing or WallpaperState.Paused))
            throw new OperationCanceledException("The wallpaper selection was superseded or cancelled.", ct);
        SetActiveWallpaper(wallpaper);
        await settings.UpdateSettingsAsync(s => s.MonitorWallpaperAssignments[monitor] = wallpaper.Id, ct).ConfigureAwait(false);
    }

    public async Task<Wallpaper?> RestorePrimaryAsync(CancellationToken ct = default)
    {
        if (!settings.CurrentSettings.MonitorWallpaperAssignments.TryGetValue("PRIMARY_MONITOR", out var id)) return null;
        var wallpaper = await library.GetWallpaperByIdAsync(id, ct).ConfigureAwait(false);
        if (wallpaper is null) return null;
        wallpaper.FilePath = storage.ResolveAbsolutePath(wallpaper.FilePath);
        if (!File.Exists(wallpaper.FilePath))
        {
            logger.LogWarning("Saved wallpaper {Id} is missing; startup will continue without it.", id);
            return null;
        }
        await engine.ApplyWallpaperAsync("PRIMARY_MONITOR", wallpaper, PlaybackSettings(), ct).ConfigureAwait(false);
        SetActiveWallpaper(wallpaper);
        logger.LogInformation("Restored saved wallpaper {Id} on startup.", id);
        return wallpaper;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        SetActiveWallpaper(null);
        return settings.UpdateSettingsAsync(s => s.MonitorWallpaperAssignments.Clear(), ct);
    }

    private WallpaperSettings PlaybackSettings()
    {
        return settings.CurrentSettings.CreatePlaybackSettings();
    }
}
