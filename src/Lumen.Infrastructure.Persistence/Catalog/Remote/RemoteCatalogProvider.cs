using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models.Catalog;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Persistence.Catalog.Remote;

/// <summary>
/// HTTP-backed catalog provider for the production catalog API.
/// The wire contract is intentionally DTO-based so server payload changes do not leak into the domain models.
/// </summary>
public sealed class RemoteCatalogProvider : ICatalogProvider, IResumableCatalogDownloadProvider
{
    private readonly HttpClient _httpClient;
    private readonly RemoteCatalogOptions _options;
    private readonly ICatalogProvider? _fallbackProvider;
    private readonly ILogger<RemoteCatalogProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static RemoteCatalogProvider()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public RemoteCatalogProvider(
        HttpClient httpClient,
        RemoteCatalogOptions options,
        ILogger<RemoteCatalogProvider> logger,
        ICatalogProvider? fallbackProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackProvider = fallbackProvider;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new ArgumentException("Remote catalog BaseUrl is required.", nameof(options));
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Remote catalog BaseUrl must be an absolute HTTP(S) URI.", nameof(options));
        }

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        }
        _httpClient.Timeout = _options.RequestTimeout;
    }

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => GetJsonAsync<List<CatalogCategoryDto>>("api/v1/catalog/categories", ct),
            dto => (dto ?? []).Select(MapCategory).ToList(),
            () => _fallbackProvider!.GetCategoriesAsync(ct),
            nameof(GetCategoriesAsync),
            ct);

    public Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => GetJsonAsync<List<CatalogItemDto>>($"api/v1/catalog/featured?limit={Math.Clamp(limit, 1, 100)}", ct),
            dto => (dto ?? []).Select(MapItem).ToList(),
            () => _fallbackProvider!.GetFeaturedAsync(limit, ct),
            nameof(GetFeaturedAsync),
            ct);

    public Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => GetJsonAsync<List<CatalogItemDto>>($"api/v1/catalog/popular?limit={Math.Clamp(limit, 1, 100)}", ct),
            dto => (dto ?? []).Select(MapItem).ToList(),
            () => _fallbackProvider!.GetPopularAsync(limit, ct),
            nameof(GetPopularAsync),
            ct);

    public Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => GetJsonAsync<List<CatalogItemDto>>($"api/v1/catalog/recent?limit={Math.Clamp(limit, 1, 100)}", ct),
            dto => (dto ?? []).Select(MapItem).ToList(),
            () => _fallbackProvider!.GetRecentAsync(limit, ct),
            nameof(GetRecentAsync),
            ct);

    public Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            async () =>
            {
                using var response = await _httpClient.GetAsync($"api/v1/catalog/{id:D}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                await EnsureSuccessAsync(response, "catalog item").ConfigureAwait(false);
                var dto = await response.Content.ReadFromJsonAsync<CatalogItemDto>(JsonOptions, ct).ConfigureAwait(false);
                return dto is null ? null : MapItem(dto);
            },
            value => value,
            () => _fallbackProvider!.GetByIdAsync(id, ct),
            nameof(GetByIdAsync),
            ct);

    public Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        string path = BuildSearchPath(query);

        return ExecuteWithFallbackAsync(
            async () =>
            {
                var dto = await GetJsonAsync<CatalogPageDto>(path, ct).ConfigureAwait(false)
                          ?? new CatalogPageDto();
                return new CatalogPage<CatalogItem>(
                    (dto.Items ?? []).Select(MapItem).ToList(),
                    Math.Max(1, dto.PageNumber),
                    Math.Max(1, dto.PageSize),
                    Math.Max(0, dto.TotalCount));
            },
            page => page,
            () => _fallbackProvider!.SearchAsync(query, ct),
            nameof(SearchAsync),
            ct);
    }

    public async Task<Stream> OpenDownloadStreamAsync(CatalogItem item, CancellationToken ct = default)
        => (await OpenDownloadStreamAsync(item, 0, ct).ConfigureAwait(false)).Content;

    public async Task<CatalogDownloadStream> OpenDownloadStreamAsync(CatalogItem item, long offset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ResolveUri(item.DownloadUrl));
                if (offset > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                }

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    await EnsureSuccessAsync(response, "catalog download").ConfigureAwait(false);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    response.Dispose();
                    throw new HttpRequestException("The requested download range is no longer valid.");
                }

                var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                bool partial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                return new CatalogDownloadStream(new ResponseOwnedStream(response, content), response.Content.Headers.ContentLength, partial);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (_options.UseLocalFallback && _fallbackProvider is not null)
            {
                _logger.LogWarning(ex, "Remote download failed for catalog item {CatalogItemId}; using local fallback.", item.Id);
                return new CatalogDownloadStream(await _fallbackProvider.OpenDownloadStreamAsync(item, ct).ConfigureAwait(false), null, false);
            }
        }

        if (_options.UseLocalFallback && _fallbackProvider is not null)
        {
            return new CatalogDownloadStream(await _fallbackProvider.OpenDownloadStreamAsync(item, ct).ConfigureAwait(false), null, false);
        }

        throw new InvalidOperationException($"Catalog item {item.Id} does not contain a download URL.");
    }

    private async Task<TResult> ExecuteWithFallbackAsync<TRemote, TResult>(
        Func<Task<TRemote>> remoteCall,
        Func<TRemote, TResult> map,
        Func<Task<TResult>> fallbackCall,
        string operation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.UseRemoteCatalog)
        {
            return await fallbackCall().ConfigureAwait(false);
        }

        try
        {
            var value = await remoteCall().ConfigureAwait(false);
            return map(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (_options.UseLocalFallback && _fallbackProvider is not null)
        {
            _logger.LogWarning(ex, "Remote catalog operation {Operation} failed; using local catalog fallback.", operation);
            return await fallbackCall().ConfigureAwait(false);
        }
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, relativePath).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preserve the HTTP status even if the error body cannot be read.
        }

        throw new HttpRequestException(
            $"Remote catalog {operation} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}",
            null,
            response.StatusCode);
    }

    private Uri ResolveUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Catalog URLs must use HTTP or HTTPS.");
            }
            return absolute;
        }

        return new Uri(_httpClient.BaseAddress!, url.TrimStart('/'));
    }

    private static string BuildSearchPath(CatalogSearchQuery query)
    {
        var parameters = new List<string>();
        Add(parameters, "q", query.SearchTerm);
        Add(parameters, "page", query.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(parameters, "pageSize", Math.Clamp(query.PageSize, 1, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(parameters, "sort", query.SortBy.ToString());

        if (query.Filter is { } filter)
        {
            Add(parameters, "category", filter.CategoryId);
            Add(parameters, "type", filter.Type?.ToString());
            Add(parameters, "resolution", filter.Resolution.ToString());
            Add(parameters, "orientation", filter.Orientation.ToString());
            if (filter.MaxDuration is { } duration)
            {
                Add(parameters, "maxDurationSeconds", ((long)Math.Max(0, duration.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (filter.Tags is { Count: > 0 })
            {
                Add(parameters, "tags", string.Join(',', filter.Tags.Where(t => !string.IsNullOrWhiteSpace(t))));
            }
            if (filter.MinRating is { } rating)
            {
                Add(parameters, "minRating", rating.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return $"api/v1/catalog/search{(parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters))}";
    }

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
    }

    private CatalogCategory MapCategory(CatalogCategoryDto dto)
        => new(dto.Id ?? string.Empty, dto.DisplayName ?? string.Empty, dto.Description ?? string.Empty, dto.IconGlyph ?? string.Empty, Math.Max(0, dto.ItemCount));

    private CatalogItem MapItem(CatalogItemDto dto)
    {
        if (!Guid.TryParse(dto.Id, out var id))
        {
            throw new JsonException("Remote catalog item contains an invalid GUID.");
        }

        return new CatalogItem
        {
            Id = id,
            Title = dto.Title ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Author = dto.Author ?? string.Empty,
            ThumbnailUrl = ResolveUri(dto.ThumbnailUrl ?? string.Empty).ToString(),
            PreviewUrl = ResolveUri(dto.PreviewUrl ?? string.Empty).ToString(),
            DownloadUrl = ResolveUri(dto.DownloadUrl ?? string.Empty).ToString(),
            Type = dto.Type,
            Width = Math.Max(0, dto.Width),
            Height = Math.Max(0, dto.Height),
            Duration = TimeSpan.FromSeconds(Math.Max(0, dto.DurationSeconds)),
            FileSizeBytes = Math.Max(0, dto.FileSizeBytes),
            Tags = dto.Tags ?? [],
            CategoryId = dto.CategoryId ?? string.Empty,
            CategoryName = dto.CategoryName ?? string.Empty,
            Rating = Math.Clamp(dto.Rating, 0, 5),
            RatingCount = Math.Max(0, dto.RatingCount),
            DownloadsCount = Math.Max(0, dto.DownloadsCount),
            CreatedAtUtc = dto.CreatedAtUtc == default ? DateTime.UtcNow : dto.CreatedAtUtc.ToUniversalTime(),
            UpdatedAtUtc = dto.UpdatedAtUtc == default ? DateTime.UtcNow : dto.UpdatedAtUtc.ToUniversalTime(),
            IsFeatured = dto.IsFeatured
        };
    }

    private sealed class CatalogCategoryDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? IconGlyph { get; set; }
        public int ItemCount { get; set; }
    }

    private sealed class CatalogItemDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? PreviewUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public WallpaperType Type { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public List<string>? Tags { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }
        public int DownloadsCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public bool IsFeatured { get; set; }
    }

    private sealed class CatalogPageDto
    {
        public List<CatalogItemDto>? Items { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }

    private sealed class ResponseOwnedStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public ResponseOwnedStream(HttpResponseMessage response, Stream inner)
        {
            _response = response;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
