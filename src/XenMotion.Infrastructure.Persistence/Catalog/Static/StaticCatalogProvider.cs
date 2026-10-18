using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Core.Models.Catalog;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Catalog.Static;

/// <summary>Configuration for the single-manifest catalog transport.</summary>
public sealed class StaticCatalogOptions
{
    /// <summary>Absolute HTTP(S) URL of the catalog.json document (loopback HTTP is allowed).</summary>
    public required string ManifestUrl { get; init; }

    /// <summary>How long a downloaded manifest stays usable before it is fetched again.</summary>
    public TimeSpan ManifestCacheLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool UseLocalFallback { get; init; } = true;
}

/// <summary>
/// Catalog transport for a single static <c>catalog.json</c> plus relative media, which is exactly
/// what a CDN, object store, GitHub Pages site or <c>XenMotion.Server --static</c> serves. Metadata
/// is filtered in memory with the same engine as the server API, so both transports answer
/// identical queries; media downloads reuse the HTTP range/resume implementation.
/// </summary>
public sealed class StaticCatalogProvider : ICatalogProvider, IResumableCatalogDownloadProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly Uri _contentBaseUri;
    private readonly IResumableCatalogDownloadProvider _downloadProvider;
    private readonly ICatalogProvider? _fallbackProvider;
    private readonly StaticCatalogOptions _options;
    private readonly ILogger<StaticCatalogProvider> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private StaticCatalogSnapshot? _snapshot;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static StaticCatalogProvider()
    {
        // Accept both "Image" and 1 so manifests written by either tool serialize identically.
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public StaticCatalogProvider(
        HttpClient httpClient,
        StaticCatalogOptions options,
        IResumableCatalogDownloadProvider downloadProvider,
        ILogger<StaticCatalogProvider> logger,
        ICatalogProvider? fallbackProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _downloadProvider = downloadProvider ?? throw new ArgumentNullException(nameof(downloadProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackProvider = fallbackProvider;

        if (!Uri.TryCreate(_options.ManifestUrl, UriKind.Absolute, out var manifestUri) || !IsAllowed(manifestUri))
        {
            throw new ArgumentException("Catalog manifest URL must be an absolute HTTP(S) URL.", nameof(options));
        }

        _manifestUri = manifestUri;
        _contentBaseUri = new Uri(manifestUri, ".");
        _httpClient.Timeout = _options.RequestTimeout;
    }

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
        => WithFallbackAsync(async () => (await LoadAsync(false, ct).ConfigureAwait(false)).Categories,
            () => _fallbackProvider!.GetCategoriesAsync(ct), nameof(GetCategoriesAsync), ct);

    public Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default)
        => ShelfAsync("featured", limit, ct);

    public Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default)
        => ShelfAsync("popular", limit, ct);

    public Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
        => ShelfAsync("recent", limit, ct);

    public Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => WithFallbackAsync(async () => (await LoadAsync(false, ct).ConfigureAwait(false)).Items.FirstOrDefault(item => item.Id == id),
            () => _fallbackProvider!.GetByIdAsync(id, ct), nameof(GetByIdAsync), ct);

    public Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return WithFallbackAsync(async () => CatalogQueryEngine.Search((await LoadAsync(false, ct).ConfigureAwait(false)).Items, query),
            () => _fallbackProvider!.SearchAsync(query, ct), nameof(SearchAsync), ct);
    }

    public async Task<Stream> OpenDownloadStreamAsync(CatalogItem item, CancellationToken ct = default)
        => (await OpenDownloadStreamAsync(item, 0, ct).ConfigureAwait(false)).Content;

    public Task<CatalogDownloadStream> OpenDownloadStreamAsync(CatalogItem item, long offset, CancellationToken ct = default, string? entityTag = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        // The underlying HTTP transport owns range validation, If-Range and ETag handling.
        return _downloadProvider.OpenDownloadStreamAsync(item, offset, ct, entityTag);
    }

    /// <summary>Drops the cached manifest so the next query re-reads it.</summary>
    public void Invalidate() => _snapshot = null;

    private async Task<IReadOnlyList<CatalogItem>> ShelfAsync(string shelf, int limit, CancellationToken ct)
        => await WithFallbackAsync(async () => CatalogQueryEngine.Top((await LoadAsync(false, ct).ConfigureAwait(false)).Items, shelf, limit),
            () => shelf switch
            {
                "featured" => _fallbackProvider!.GetFeaturedAsync(limit, ct),
                "recent" => _fallbackProvider!.GetRecentAsync(limit, ct),
                _ => _fallbackProvider!.GetPopularAsync(limit, ct)
            },
            shelf, ct).ConfigureAwait(false);

    private async Task<T> WithFallbackAsync<T>(Func<Task<T>> remoteCall, Func<Task<T>> fallbackCall, string operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return await remoteCall().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (_options.UseLocalFallback && _fallbackProvider is not null)
        {
            _logger.LogWarning(ex, "Static catalog operation {Operation} failed; using the local catalog fallback.", operation);
            return await fallbackCall().ConfigureAwait(false);
        }
    }

    private async Task<StaticCatalogSnapshot> LoadAsync(bool force, CancellationToken ct)
    {
        if (!force && _snapshot is { } cached && DateTimeOffset.UtcNow - cached.LoadedAtUtc < _options.ManifestCacheLifetime)
        {
            return cached;
        }

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!force && _snapshot is { } fresh && DateTimeOffset.UtcNow - fresh.LoadedAtUtc < _options.ManifestCacheLifetime)
            {
                return fresh;
            }

            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(_options.RequestTimeout);
                using var response = await _httpClient.GetAsync(_manifestUri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Catalog manifest request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", null, response.StatusCode);
                }

                var manifest = await response.Content.ReadFromJsonAsync<CatalogManifestDto>(JsonOptions, deadline.Token).ConfigureAwait(false)
                               ?? throw new InvalidDataException("Catalog manifest is empty.");
                var snapshot = Map(manifest);
                _snapshot = snapshot;
                if (snapshot.SkippedItems > 0)
                {
                    _logger.LogWarning("Static catalog manifest skipped {Skipped} invalid item(s) of {Total}.", snapshot.SkippedItems, manifest.Items?.Count ?? 0);
                }

                _logger.LogInformation("Static catalog loaded: {Count} wallpapers.", snapshot.Items.Count);
                return snapshot;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_snapshot is { } stale)
                {
                    // A reachable-but-failing host should not empty the Explore page.
                    _logger.LogWarning(ex, "Static catalog refresh failed; keeping the previously loaded manifest.");
                    return stale;
                }

                throw;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private StaticCatalogSnapshot Map(CatalogManifestDto manifest)
    {
        if (manifest.Categories is null) throw new InvalidDataException("Catalog manifest is missing its categories collection.");

        var categories = new List<CatalogCategory>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in manifest.Categories)
        {
            if (category is null || string.IsNullOrWhiteSpace(category.Id) || string.IsNullOrWhiteSpace(category.DisplayName)) continue;
            if (!names.TryAdd(category.Id, category.DisplayName)) continue;
            categories.Add(new CatalogCategory(category.Id, category.DisplayName, category.Description ?? string.Empty, category.IconGlyph ?? string.Empty, Math.Max(0, category.ItemCount)));
        }

        var items = new List<CatalogItem>();
        int skipped = 0;
        var ids = new HashSet<Guid>();
        foreach (var entry in manifest.Items ?? [])
        {
            if (entry is null || entry.Id == Guid.Empty || Guid.Empty.Equals(entry.Id) || !ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.MediaPath))
            {
                skipped++;
                continue;
            }

            if (!TryResolve(entry.MediaPath, out var media) ||
                !TryResolve(string.IsNullOrWhiteSpace(entry.ThumbnailPath) ? entry.MediaPath : entry.ThumbnailPath, out var thumbnail) ||
                !TryResolve(string.IsNullOrWhiteSpace(entry.PreviewPath) ? entry.MediaPath : entry.PreviewPath, out var preview))
            {
                _logger.LogWarning("Static catalog item {Id} has a media path that is not a resolvable HTTP(S) URL.", entry.Id);
                skipped++;
                continue;
            }

            string categoryId = entry.CategoryId ?? string.Empty;
            items.Add(new CatalogItem
            {
                Id = entry.Id,
                Title = entry.Title.Trim(),
                Description = entry.Description ?? string.Empty,
                Author = string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author,
                Type = entry.Type is WallpaperType.Image or WallpaperType.Video or WallpaperType.Gif ? entry.Type : WallpaperType.Image,
                Width = Math.Max(0, entry.Width),
                Height = Math.Max(0, entry.Height),
                Duration = TimeSpan.FromSeconds(Math.Clamp(entry.DurationSeconds, 0, 86400)),
                FileSizeBytes = Math.Max(0, entry.FileSizeBytes),
                ThumbnailUrl = thumbnail,
                PreviewUrl = preview,
                DownloadUrl = media,
                CategoryId = categoryId,
                CategoryName = names.TryGetValue(categoryId, out var name) ? name : categoryId,
                Tags = entry.Tags ?? [],
                IsFeatured = entry.IsFeatured,
                Rating = Math.Clamp(entry.Rating, 0, 5),
                RatingCount = Math.Max(0, entry.RatingCount),
                DownloadsCount = Math.Max(0, entry.DownloadsCount),
                CreatedAtUtc = entry.CreatedAtUtc == default ? DateTime.UtcNow : entry.CreatedAtUtc.ToUniversalTime(),
                UpdatedAtUtc = entry.UpdatedAtUtc == default ? DateTime.UtcNow : entry.UpdatedAtUtc.ToUniversalTime()
            });
        }

        // The API recomputes category counts from the published items, so do the same here and never
        // trust a stale itemCount written into the manifest.
        var withCounts = categories
            .Select(category => category with
            {
                ItemCount = category.Id.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? items.Count
                    : items.Count(item => item.CategoryId.Equals(category.Id, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        return new StaticCatalogSnapshot(items, withCounts, DateTimeOffset.UtcNow, skipped);
    }

    private bool TryResolve(string? path, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            if (!IsAllowed(absolute)) return false;
            url = absolute.ToString();
            return true;
        }

        // A manifest must not point above its own directory; the server rejects such manifests too.
        if (path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..")) return false;

        if (Uri.TryCreate(_contentBaseUri, path, out var relative) && IsAllowed(relative))
        {
            url = relative.ToString();
            return true;
        }

        return false;
    }

    private static bool IsAllowed(Uri uri)
        => (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.UserInfo);

    private sealed record StaticCatalogSnapshot(
        IReadOnlyList<CatalogItem> Items,
        IReadOnlyList<CatalogCategory> Categories,
        DateTimeOffset LoadedAtUtc,
        int SkippedItems);

    private sealed class CatalogManifestDto
    {
        public List<CatalogCategoryDto>? Categories { get; set; }
        public List<CatalogEntryDto>? Items { get; set; }
    }

    private sealed class CatalogCategoryDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? IconGlyph { get; set; }
        public int ItemCount { get; set; }
    }

    private sealed class CatalogEntryDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public WallpaperType Type { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public string? CategoryId { get; set; }
        public List<string>? Tags { get; set; }
        public bool IsFeatured { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }
        public int DownloadsCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? MediaPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? PreviewPath { get; set; }
    }
}
