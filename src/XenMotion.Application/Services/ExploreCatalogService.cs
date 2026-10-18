using System.Collections.Concurrent;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Core.Models.Catalog;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>
/// Application service coordinating Explore/Catalog operations, latest-request-wins search concurrency,
/// local library download state synchronization, and wallpaper subscriptions.
/// </summary>
public sealed class ExploreCatalogService : IExploreCatalogService
{
    private readonly ICatalogProvider _catalogProvider;
    private readonly IWallpaperRepository _repository;
    private readonly IStorageManager _storageManager;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<ExploreCatalogService> _logger;

    private readonly ConcurrentDictionary<Guid, CatalogDownloadState> _activeStates = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _downloadLocks = new();
    private const int MaxDownloadAttempts = 3;
    private readonly object _searchLock = new();
    private long _activeSearchRequestId = 0;
    private CancellationTokenSource? _activeSearchCts;

    public event EventHandler<(Guid CatalogItemId, CatalogDownloadState State)>? DownloadStateChanged;

    public ExploreCatalogService(
        ICatalogProvider catalogProvider,
        IWallpaperRepository repository,
        IStorageManager storageManager,
        IThumbnailService thumbnailService,
        ILogger<ExploreCatalogService> logger)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return _catalogProvider.GetCategoriesAsync(ct);
    }

    public Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default)
    {
        return _catalogProvider.GetFeaturedAsync(limit, ct);
    }

    public Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default)
    {
        return _catalogProvider.GetPopularAsync(limit, ct);
    }

    public Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        return _catalogProvider.GetRecentAsync(limit, ct);
    }

    public Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _catalogProvider.GetByIdAsync(id, ct);
    }

    public async Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        long requestId;
        CancellationTokenSource? previousCts = null;
        CancellationTokenSource currentCts;

        lock (_searchLock)
        {
            requestId = ++_activeSearchRequestId;
            previousCts = _activeSearchCts;
            _activeSearchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            currentCts = _activeSearchCts;
        }

        // Cancel superseded request outside the lock. The superseded request owns
        // disposal of its CTS in its own finally block; disposing it here can race
        // with the request reading currentCts.Token and produce ObjectDisposedException.
        if (previousCts != null)
        {
            try { previousCts.Cancel(); } catch (ObjectDisposedException) { }
        }

        try
        {
            _logger.LogInformation("Initiating catalog search request #{RequestId} for query '{Query}'.",
                requestId, query.SearchTerm ?? "*");

            var page = await _catalogProvider.SearchAsync(query, currentCts.Token).ConfigureAwait(false);

            lock (_searchLock)
            {
                if (requestId != _activeSearchRequestId)
                {
                    _logger.LogInformation("Search request #{RequestId} superseded by #{Active}. Discarding stale results.",
                        requestId, _activeSearchRequestId);
                    throw new OperationCanceledException("Search request superseded by a newer query.");
                }
            }

            return page;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Search request #{RequestId} was cancelled.", requestId);
            throw;
        }
        finally
        {
            lock (_searchLock)
            {
                if (ReferenceEquals(_activeSearchCts, currentCts))
                {
                    _activeSearchCts = null;
                }
            }

            currentCts.Dispose();
        }
    }

    public async Task<CatalogDownloadState> GetDownloadStateAsync(Guid catalogItemId, CancellationToken ct = default)
    {
        if (_activeStates.TryGetValue(catalogItemId, out var trackedState) &&
            trackedState != CatalogDownloadState.NotDownloaded)
        {
            // Preserve the terminal state of the current subscription operation.
            // This is especially important after a failed/cancelled provider call,
            // before the local repository has any record to reconcile.
            if (trackedState == CatalogDownloadState.Downloading ||
                trackedState == CatalogDownloadState.Failed ||
                trackedState == CatalogDownloadState.Downloaded)
            {
                return trackedState;
            }
        }

        var existing = await _repository.GetByIdAsync(catalogItemId, ct).ConfigureAwait(false);
        if (existing != null)
        {
            string absolutePath = _storageManager.ResolveAbsolutePath(existing.FilePath);
            if (File.Exists(absolutePath))
            {
                return CatalogDownloadState.Downloaded;
            }
        }

        return CatalogDownloadState.NotDownloaded;
    }

    public async Task<IReadOnlyDictionary<Guid, CatalogDownloadState>> GetDownloadStatesAsync(
        IEnumerable<Guid> catalogItemIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, CatalogDownloadState>();
        var idList = catalogItemIds.Distinct().ToList();

        var localWallpapers = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var existingMap = localWallpapers.ToDictionary(w => w.Id);

        foreach (var id in idList)
        {
            if (_activeStates.TryGetValue(id, out var inFlight) && inFlight == CatalogDownloadState.Downloading)
            {
                result[id] = CatalogDownloadState.Downloading;
                continue;
            }

            if (existingMap.TryGetValue(id, out var localWp))
            {
                string abs = _storageManager.ResolveAbsolutePath(localWp.FilePath);
                result[id] = File.Exists(abs) ? CatalogDownloadState.Downloaded : CatalogDownloadState.NotDownloaded;
            }
            else
            {
                result[id] = CatalogDownloadState.NotDownloaded;
            }
        }

        return result;
    }

    public async Task<Wallpaper?> SubscribeAsync(
        CatalogItem item,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();

        var gate = _downloadLocks.GetOrAdd(item.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await _repository.GetByIdAsync(item.Id, ct).ConfigureAwait(false);
            if (existing != null)
            {
                string abs = _storageManager.ResolveAbsolutePath(existing.FilePath);
                if (File.Exists(abs))
                {
                    SetState(item.Id, CatalogDownloadState.Downloaded);
                    progress?.Report(100);
                    return existing;
                }
            }

            SetState(item.Id, CatalogDownloadState.Downloading);
            string stagedPath = Path.Combine(_storageManager.TempDirectory, $"{item.Id:N}_sub.tmp");

            try
            {
                await DownloadWithRetryAsync(item, stagedPath, progress, ct).ConfigureAwait(false);

                string ext = item.Type == WallpaperType.Video ? ".mp4" : ".png";
                string safeFileName = PathSanitizer.SanitizeFileName($"{item.Title}{ext}");
                string finalMediaPath = await _storageManager.CommitImportAsync(stagedPath, item.Id, safeFileName, ct).ConfigureAwait(false);
                TryDelete(stagedPath + ".etag");
                stagedPath = string.Empty;

                progress?.Report(80);
                string thumbPath = await _thumbnailService.CreateThumbnailAsync(finalMediaPath, item.Id, item.Type, ct).ConfigureAwait(false);
                string hash = await _storageManager.CalculateSha256HashAsync(finalMediaPath, ct).ConfigureAwait(false);

                var wallpaper = new Wallpaper
                {
                    Id = item.Id,
                    Title = item.Title,
                    Description = item.Description,
                    Type = item.Type,
                    FilePath = _storageManager.MakeStorageRelativePath(finalMediaPath),
                    ThumbnailPath = string.IsNullOrEmpty(thumbPath) ? string.Empty : _storageManager.MakeStorageRelativePath(thumbPath),
                    ContentHash = hash,
                    FileSizeBytes = new FileInfo(finalMediaPath).Length,
                    Width = item.Width,
                    Height = item.Height,
                    Fps = item.Type == WallpaperType.Video ? 30.0 : 0.0,
                    DurationMs = (long)item.Duration.TotalMilliseconds,
                    Creator = item.Author,
                    Category = item.CategoryName,
                    Tags = item.Tags.ToList(),
                    IsSubscribed = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await _repository.InsertAsync(wallpaper, ct).ConfigureAwait(false);
                progress?.Report(100);
                SetState(item.Id, CatalogDownloadState.Downloaded);
                return wallpaper;
            }
            catch (OperationCanceledException)
            {
                SetState(item.Id, CatalogDownloadState.NotDownloaded);
                // Keep a resumable partial file when the provider supports ranges.
                if (string.IsNullOrEmpty(stagedPath))
                    await CleanUpArtifactsAsync(null, item.Id).ConfigureAwait(false);
                else if (_catalogProvider is not IResumableCatalogDownloadProvider && File.Exists(stagedPath))
                {
                    TryDelete(stagedPath);
                    TryDelete(stagedPath + ".etag");
                }
                throw;
            }
            catch
            {
                TryDelete(stagedPath);
                TryDelete(stagedPath + ".etag");
                await CleanUpArtifactsAsync(null, item.Id).ConfigureAwait(false);
                SetState(item.Id, CatalogDownloadState.Failed);
                throw;
            }
        }
        finally
        {
            // Keep the per-item semaphore for the lifetime of the service. Removing and
            // disposing it here could race with another waiter that already captured it.
            gate.Release();
        }
    }

    private async Task DownloadWithRetryAsync(CatalogItem item, string stagedPath, IProgress<double>? progress, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long offset = File.Exists(stagedPath) ? new FileInfo(stagedPath).Length : 0;
                string? entityTag = File.Exists(stagedPath + ".etag")
                    ? await File.ReadAllTextAsync(stagedPath + ".etag", ct).ConfigureAwait(false) : null;
                await using var source = await OpenDownloadSourceAsync(item, offset, entityTag, ct).ConfigureAwait(false);

                if (offset > 0 && !source.IsPartial)
                {
                    offset = 0;
                    TryDelete(stagedPath);
                }

                var mode = offset > 0 ? FileMode.Append : FileMode.Create;
                await using var target = new FileStream(stagedPath, mode, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                if (!string.IsNullOrEmpty(source.EntityTag))
                    await File.WriteAllTextAsync(stagedPath + ".etag", source.EntityTag, ct).ConfigureAwait(false);
                else TryDelete(stagedPath + ".etag");
                byte[] buffer = new byte[128 * 1024];
                long written = offset;
                int read;
                while ((read = await source.Content.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    written += read;
                    double total = item.FileSizeBytes > 0 ? item.FileSizeBytes : (source.ContentLength.HasValue ? offset + source.ContentLength.Value : 0);
                    if (total > 0) progress?.Report(Math.Clamp(written * 75d / total, 0, 75));
                }
                await target.FlushAsync(ct).ConfigureAwait(false);
                if (source.ContentLength is { } expected && written - offset != expected)
                    throw new IOException("Catalog download ended before the expected content length.");
                if (source.TotalLength is { } totalLength && written != totalLength)
                    throw new IOException("Catalog download has not reached the end of the media file.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt == MaxDownloadAttempts) break;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct).ConfigureAwait(false);
            }
        }
        throw new IOException($"Catalog download failed after {MaxDownloadAttempts} attempts.", last);
    }

    private async Task<CatalogDownloadStream> OpenDownloadSourceAsync(CatalogItem item, long offset, string? entityTag, CancellationToken ct)
    {
        if (_catalogProvider is IResumableCatalogDownloadProvider resumable)
        {
            return await resumable.OpenDownloadStreamAsync(item, offset, ct, entityTag).ConfigureAwait(false);
        }

        if (offset > 0)
        {
            TryDelete(Path.Combine(_storageManager.TempDirectory, $"{item.Id:N}_sub.tmp"));
        }

        var content = await _catalogProvider.OpenDownloadStreamAsync(item, ct).ConfigureAwait(false);
        return new CatalogDownloadStream(content, content.CanSeek ? content.Length - content.Position : null, false);
    }

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private void SetState(Guid itemId, CatalogDownloadState state)
    {
        _activeStates[itemId] = state;
        DownloadStateChanged?.Invoke(this, (itemId, state));
    }

    private async Task CleanUpArtifactsAsync(string? stagedPath, Guid itemId)
    {
        if (!string.IsNullOrEmpty(stagedPath) && File.Exists(stagedPath))
        {
            try { File.Delete(stagedPath); } catch { }
        }

        try
        {
            await _storageManager.DeleteWallpaperStorageAsync(itemId, CancellationToken.None).ConfigureAwait(false);
            await _thumbnailService.DeleteThumbnailAsync(itemId, CancellationToken.None).ConfigureAwait(false);
            await _repository.DeleteAsync(itemId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
