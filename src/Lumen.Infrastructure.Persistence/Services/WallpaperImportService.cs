using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Persistence.Services;

/// <summary>
/// Production wallpaper import pipeline.
/// Validates media formats, performs streaming SHA-256 duplicate detection, stages files atomically,
/// generates cached thumbnails, stores media in Lumen storage, and persists SQLite metadata.
/// </summary>
public sealed class WallpaperImportService : IWallpaperImportService
{
    private readonly IStorageManager _storageManager;
    private readonly IWallpaperRepository _repository;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<WallpaperImportService> _logger;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm", ".wmv", ".asf", ".avi", ".mov"
    };

    public WallpaperImportService(
        IStorageManager storageManager,
        IWallpaperRepository repository,
        IThumbnailService thumbnailService,
        ILogger<WallpaperImportService> logger)
    {
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportResult> ImportFileAsync(string sourceFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ct.ThrowIfCancellationRequested();

        // 1. Validate file extension and format
        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrEmpty(ext) || (!SupportedImageExtensions.Contains(ext) && !SupportedVideoExtensions.Contains(ext)))
        {
            return ImportResult.Failed(sourceFilePath, $"Unsupported file extension '{ext}'.");
        }

        if (!File.Exists(sourceFilePath))
        {
            return ImportResult.Failed(sourceFilePath, $"Source file not found at '{sourceFilePath}'.");
        }

        WallpaperType type = SupportedVideoExtensions.Contains(ext) ? WallpaperType.Video : WallpaperType.Image;
        string? stagedPath = null;
        Guid wallpaperId = Guid.NewGuid();

        try
        {
            _logger.LogInformation("Starting import for '{Path}' (Type: {Type}).", sourceFilePath, type);

            // 2. Calculate SHA-256 hash using streaming I/O (never buffer entire media in memory)
            string hash = await _storageManager.CalculateSha256HashAsync(sourceFilePath, ct).ConfigureAwait(false);

            // 3. Duplicate detection
            var existing = await _repository.GetByContentHashAsync(hash, ct).ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogInformation("Duplicate detected. File '{Path}' matches existing wallpaper '{Id}' ({Title}).",
                    sourceFilePath, existing.Id, existing.Title);
                return ImportResult.Duplicate(sourceFilePath, existing);
            }

            // 4. Stage to temporary directory
            stagedPath = await _storageManager.StageImportAsync(sourceFilePath, ct).ConfigureAwait(false);

            // 5. Sanitize filename to prevent directory traversal and Windows device name collisions
            string sanitizedFileName = PathSanitizer.SanitizeFileName(Path.GetFileName(sourceFilePath));
            string title = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Untitled Wallpaper";
            }

            // 6. Commit to final storage location: Media/<wallpaperId>/<sanitizedFileName>
            string finalMediaPath = await _storageManager.CommitImportAsync(
                stagedPath,
                wallpaperId,
                sanitizedFileName,
                ct).ConfigureAwait(false);

            stagedPath = null; // Successfully moved

            // 7. Generate cached thumbnail
            string thumbPath = await _thumbnailService.CreateThumbnailAsync(
                finalMediaPath,
                wallpaperId,
                type,
                ct).ConfigureAwait(false);

            // 8. Convert paths to storage-relative for robust storage root migrations
            string relativeMediaPath = _storageManager.MakeStorageRelativePath(finalMediaPath);
            string relativeThumbPath = string.IsNullOrEmpty(thumbPath)
                ? string.Empty
                : _storageManager.MakeStorageRelativePath(thumbPath);

            long fileSizeBytes = new FileInfo(finalMediaPath).Length;

            // 9. Build and persist Wallpaper domain entity
            var wallpaper = new Wallpaper
            {
                Id = wallpaperId,
                Title = title,
                Type = type,
                FilePath = relativeMediaPath,
                ThumbnailPath = relativeThumbPath,
                ContentHash = hash,
                FileSizeBytes = fileSizeBytes,
                Width = 1920,
                Height = 1080,
                Fps = type == WallpaperType.Video ? 30.0 : 0.0,
                DurationMs = type == WallpaperType.Video ? 15000 : 0,
                Creator = "Local User",
                Category = "General",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await _repository.InsertAsync(wallpaper, ct).ConfigureAwait(false);
            _logger.LogInformation("Successfully imported wallpaper '{Title}' ({Id}).", wallpaper.Title, wallpaper.Id);

            return ImportResult.Succeeded(sourceFilePath, wallpaper);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Import cancelled for '{Path}'. Cleaning up artifacts.", sourceFilePath);
            await CleanUpFailedImportAsync(stagedPath, wallpaperId).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import wallpaper '{Path}'. Cleaning up artifacts.", sourceFilePath);
            await CleanUpFailedImportAsync(stagedPath, wallpaperId).ConfigureAwait(false);
            return ImportResult.Failed(sourceFilePath, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ImportResult>> ImportFilesAsync(
        IEnumerable<string> sourceFilePaths,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);

        var pathsList = sourceFilePaths.ToList();
        var results = new List<ImportResult>(pathsList.Count);
        int completed = 0;

        _logger.LogInformation("Starting batch import of {Count} files.", pathsList.Count);

        foreach (string path in pathsList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ImportFileAsync(path, ct).ConfigureAwait(false);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(ImportResult.Failed(path, ex.Message));
            }

            completed++;
            progress?.Report(new ImportProgress(completed, pathsList.Count, Path.GetFileName(path)));
        }

        _logger.LogInformation("Completed batch import: {Success} succeeded, {Duplicate} duplicates, {Failed} failed.",
            results.Count(r => r.Success && !r.IsDuplicate),
            results.Count(r => r.IsDuplicate),
            results.Count(r => !r.Success));

        return results;
    }

    private async Task CleanUpFailedImportAsync(string? stagedPath, Guid wallpaperId)
    {
        if (!string.IsNullOrEmpty(stagedPath) && File.Exists(stagedPath))
        {
            try { File.Delete(stagedPath); } catch { }
        }

        try
        {
            await _storageManager.DeleteWallpaperStorageAsync(wallpaperId, CancellationToken.None).ConfigureAwait(false);
            await _thumbnailService.DeleteThumbnailAsync(wallpaperId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical error cleaning up failed import artifacts for '{Id}'.", wallpaperId);
        }
    }
}
