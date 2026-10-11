using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Services;

/// <summary>
/// Service managing the creation, persistent caching, and regeneration of wallpaper thumbnail assets.
/// Avoids UI blocking, generates deterministic thumbnails, and gracefully recovers from missing or corrupt thumbnails.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private readonly IStorageManager _storageManager;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly IThumbnailCodec _codec;

    public string ThumbnailsDirectory => _storageManager.ThumbnailsDirectory;

    public ThumbnailService(IStorageManager storageManager, ILogger<ThumbnailService> logger, IThumbnailCodec codec)
    {
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));

        if (!Directory.Exists(ThumbnailsDirectory))
        {
            Directory.CreateDirectory(ThumbnailsDirectory);
        }
    }

    public string GetThumbnailPath(Guid wallpaperId)
    {
        return Path.Combine(ThumbnailsDirectory, $"{wallpaperId:D}.jpg");
    }

    public bool ThumbnailExists(Guid wallpaperId)
    {
        string path = GetThumbnailPath(wallpaperId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return _codec.IsValid(path);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> CreateThumbnailAsync(
        string mediaPath,
        Guid wallpaperId,
        WallpaperType type,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ct.ThrowIfCancellationRequested();

        string finalThumbPath = GetThumbnailPath(wallpaperId);

        // Check cache: do not regenerate if a valid thumbnail already exists
        if (ThumbnailExists(wallpaperId))
        {
            return finalThumbPath;
        }

        string absoluteMediaPath = _storageManager.ResolveAbsolutePath(mediaPath);
        if (!File.Exists(absoluteMediaPath))
        {
            _logger.LogWarning("Cannot generate thumbnail. Media file not found at '{Path}'.", absoluteMediaPath);
            return string.Empty;
        }

        string tempThumbPath = Path.Combine(_storageManager.TempDirectory, $"{wallpaperId:N}_{Guid.NewGuid():N}_thumb.tmp");

        try
        {
            using (File.Create(tempThumbPath)) { }
            await _codec.GenerateAsync(absoluteMediaPath, tempThumbPath, type, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!_codec.IsValid(tempThumbPath)) throw new InvalidDataException("Thumbnail is not a readable, reduced-size JPEG.");

            // Atomic move into persistent thumbnail cache
            File.Move(tempThumbPath, finalThumbPath, overwrite: true);
            _logger.LogInformation("Generated thumbnail for wallpaper '{Id}' at '{Path}'.", wallpaperId, finalThumbPath);
            return finalThumbPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for wallpaper '{Id}'.", wallpaperId);
            if (File.Exists(tempThumbPath))
            {
                try { File.Delete(tempThumbPath); } catch { }
            }
            if (ex is OperationCanceledException) throw;
            return string.Empty;
        }
    }

    public Task<bool> DeleteThumbnailAsync(Guid wallpaperId, CancellationToken ct = default)
    {
        string path = GetThumbnailPath(wallpaperId);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
                _logger.LogInformation("Deleted thumbnail for wallpaper '{Id}'.", wallpaperId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail for wallpaper '{Id}'.", wallpaperId);
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public async Task<int> RegenerateMissingThumbnailsAsync(IEnumerable<Wallpaper> wallpapers, CancellationToken ct = default)
    {
        int regeneratedCount = 0;

        foreach (var wp in wallpapers)
        {
            ct.ThrowIfCancellationRequested();

            if (!ThumbnailExists(wp.Id))
            {
                string thumb = await CreateThumbnailAsync(wp.FilePath, wp.Id, wp.Type, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(thumb))
                {
                    regeneratedCount++;
                }
            }
        }

        if (regeneratedCount > 0)
        {
            _logger.LogInformation("Reconciliation: Regenerated {Count} missing thumbnails.", regeneratedCount);
        }

        return regeneratedCount;
    }

}
