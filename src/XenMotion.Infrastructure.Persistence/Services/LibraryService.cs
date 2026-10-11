using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Services;

/// <summary>
/// Production library management service coordinating SQLite metadata persistence,
/// filesystem media storage, thumbnail caching, safe deletions, and library reconciliation.
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly IWallpaperRepository _repository;
    private readonly IStorageManager _storageManager;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<LibraryService> _logger;

    public event EventHandler? LibraryChanged;

    public LibraryService(
        IWallpaperRepository repository,
        IStorageManager storageManager,
        IThumbnailService thumbnailService,
        ILogger<LibraryService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Wallpaper>> GetWallpapersAsync(CancellationToken ct = default)
    {
        var wallpapers = await _repository.GetAllAsync(ct).ConfigureAwait(false);

        foreach (var wp in wallpapers)
        {
            string absolutePath = _storageManager.ResolveAbsolutePath(wp.FilePath);
            wp.IsMissing = !File.Exists(absolutePath);
            if (!wp.IsMissing)
            {
                string thumbnail = await _thumbnailService.CreateThumbnailAsync(wp.FilePath, wp.Id, wp.Type, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(thumbnail))
                {
                    string relative = _storageManager.MakeStorageRelativePath(thumbnail);
                    if (wp.ThumbnailPath != relative)
                    {
                        wp.ThumbnailPath = relative;
                        await _repository.UpdateAsync(wp, ct).ConfigureAwait(false);
                    }
                }
            }
        }

        return wallpapers;
    }

    public async Task<Wallpaper?> GetWallpaperByIdAsync(Guid id, CancellationToken ct = default)
    {
        var wp = await _repository.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (wp != null)
        {
            string absolutePath = _storageManager.ResolveAbsolutePath(wp.FilePath);
            wp.IsMissing = !File.Exists(absolutePath);
        }

        return wp;
    }

    public async Task RenameWallpaperAsync(Guid id, string title, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        title = title.Trim();
        if (title.Length > 200) throw new ArgumentException("Title must be at most 200 characters.", nameof(title));
        var wallpaper = await _repository.GetByIdAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wallpaper is no longer in the library.");
        wallpaper.Title = title;
        wallpaper.UpdatedAtUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(wallpaper, ct).ConfigureAwait(false);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<DeleteResult> DeleteWallpaperAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var wp = await _repository.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (wp == null)
        {
            _logger.LogWarning("Delete request for wallpaper '{Id}' failed: record not found.", id);
            return DeleteResult.NotFound(id);
        }

        _logger.LogInformation("Deleting wallpaper '{Title}' ({Id}).", wp.Title, id);

        bool dbDeleted = false;
        bool mediaDeleted = false;
        bool thumbDeleted = false;

        try
        {
            // 1. Remove database record
            await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
            dbDeleted = true;

            // 2. Remove media directory from filesystem
            mediaDeleted = await _storageManager.DeleteWallpaperStorageAsync(id, ct).ConfigureAwait(false);

            // 3. Remove cached thumbnail
            thumbDeleted = await _thumbnailService.DeleteThumbnailAsync(id, ct).ConfigureAwait(false);

            LibraryChanged?.Invoke(this, EventArgs.Empty);

            if (!mediaDeleted)
            {
                return DeleteResult.PartialSuccess(id, "Wallpaper record deleted, but media directory could not be removed.");
            }

            return DeleteResult.Succeeded(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting wallpaper '{Id}'.", id);
            return new DeleteResult
            {
                WallpaperId = id,
                Success = dbDeleted,
                DatabaseDeleted = dbDeleted,
                MediaDeleted = mediaDeleted,
                ThumbnailDeleted = thumbDeleted,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ReconciliationResult> ReconcileLibraryAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting library reconciliation and repair scan.");

        var wallpapers = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var missingIds = new List<Guid>();
        int validCount = 0;

        foreach (var wp in wallpapers)
        {
            ct.ThrowIfCancellationRequested();

            string fullMediaPath = _storageManager.ResolveAbsolutePath(wp.FilePath);
            if (!File.Exists(fullMediaPath))
            {
                missingIds.Add(wp.Id);
                wp.IsMissing = true;
                _logger.LogWarning("Reconciliation: Wallpaper '{Title}' ({Id}) is missing media at '{Path}'.",
                    wp.Title, wp.Id, fullMediaPath);
            }
            else
            {
                validCount++;
                wp.IsMissing = false;
            }
        }

        // 2. Check and regenerate missing thumbnails for valid media
        int regeneratedThumbs = await _thumbnailService.RegenerateMissingThumbnailsAsync(
            wallpapers.Where(w => !w.IsMissing),
            ct).ConfigureAwait(false);

        // 3. Scan for orphaned directories in Media folder
        var orphanedDirs = new List<string>();
        var knownIds = wallpapers.Select(w => w.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_storageManager.MediaDirectory))
        {
            foreach (string dir in Directory.GetDirectories(_storageManager.MediaDirectory))
            {
                string folderName = Path.GetFileName(dir);
                if (!knownIds.Contains(folderName) && Guid.TryParse(folderName, out _))
                {
                    orphanedDirs.Add(dir);
                    _logger.LogInformation("Reconciliation: Detected orphaned media folder '{Path}'.", dir);
                }
            }
        }

        // 4. Clean old temp files
        await _storageManager.CleanTempFilesAsync(TimeSpan.FromHours(1), ct).ConfigureAwait(false);

        var result = new ReconciliationResult(
            wallpapers.Count,
            validCount,
            missingIds,
            regeneratedThumbs,
            orphanedDirs);

        _logger.LogInformation("Reconciliation completed: {Total} total, {Valid} valid, {Missing} missing, {Regen} thumbnails regenerated, {Orphan} orphaned dirs.",
            result.TotalWallpapers, result.ValidWallpapers, result.MissingMediaIds.Count, result.RegeneratedThumbnailsCount, result.OrphanedDirectories.Count);

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task<StorageUsageInfo> GetStorageUsageAsync(CancellationToken ct = default)
    {
        var wallpapers = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        return await _storageManager.GetStorageUsageAsync(wallpapers.Count, ct).ConfigureAwait(false);
    }

    public async Task<bool> MigrateStorageRootAsync(string newRootPath, CancellationToken ct = default)
    {
        bool success = await _storageManager.MigrateStorageRootAsync(newRootPath, ct).ConfigureAwait(false);
        if (success)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        return success;
    }
}
