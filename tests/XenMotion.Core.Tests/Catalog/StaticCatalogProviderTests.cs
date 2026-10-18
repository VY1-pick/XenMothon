using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using XenMotion.Infrastructure.Persistence.Catalog.Static;
using XenMotion.Server;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

/// <summary>
/// Hosts the real catalog directory twice: once as the live API and once with `--static`, which is
/// exactly how the catalog would be published to a CDN. The static client is then driven against the
/// static host so manifest parsing, in-memory search parity and resumable media downloads are all
/// exercised over real HTTP.
/// </summary>
public sealed class StaticCatalogProviderTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "xenmotion-static-test-" + Guid.NewGuid().ToString("N"));
    private WebApplication _staticApp = null!;
    private WebApplication _apiApp = null!;
    private HttpClient _staticHttp = null!;
    private StaticCatalogProvider _client = null!;
    private RemoteCatalogProvider _apiClient = null!;
    private readonly Guid _imageId = Guid.NewGuid();
    private readonly Guid _videoId = Guid.NewGuid();
    private readonly byte[] _videoBytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
    private readonly byte[] _imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "media"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "media", "photo.png"), _imageBytes);
        await File.WriteAllBytesAsync(Path.Combine(_root, "media", "clip.mp4"), _videoBytes);
        await WriteManifestAsync(DefaultManifest());

        _staticApp = await CatalogServer.CreateAsync(["--urls", "http://127.0.0.1:0", "--environment", "Production", "--static", "--Logging:LogLevel:Default", "Warning"], _root);
        await _staticApp.StartAsync();
        _staticHttp = new HttpClient { BaseAddress = new Uri(_staticApp.Urls.Single()) };
        _client = CreateClient(_staticHttp.BaseAddress + "catalog.json", fallback: null);

        _apiApp = await CatalogServer.CreateAsync(["--urls", "http://127.0.0.1:0", "--environment", "Production", "--Logging:LogLevel:Default", "Warning"], _root);
        await _apiApp.StartAsync();
        var apiHttp = new HttpClient { BaseAddress = new Uri(_apiApp.Urls.Single()) };
        _apiClient = new RemoteCatalogProvider(apiHttp, new RemoteCatalogOptions
        {
            BaseUrl = apiHttp.BaseAddress!.ToString(),
            UseRemoteCatalog = true,
            UseLocalFallback = false
        }, NullLogger<RemoteCatalogProvider>.Instance);
    }

    private static StaticCatalogProvider CreateClient(string manifestUrl, ICatalogProvider? fallback, TimeSpan? cacheLifetime = null)
        => new(
            new HttpClient(),
            new StaticCatalogOptions
            {
                ManifestUrl = manifestUrl,
                UseLocalFallback = fallback is not null,
                ManifestCacheLifetime = cacheLifetime ?? TimeSpan.FromMinutes(10)
            },
            CreateDownloadTransport(),
            NullLogger<StaticCatalogProvider>.Instance,
            fallback);

    private static RemoteCatalogProvider CreateDownloadTransport()
        => new(
            new HttpClient(),
            new RemoteCatalogOptions { UseRemoteCatalog = true, BaseUrl = "https://media.invalid/", UseLocalFallback = false },
            NullLogger<RemoteCatalogProvider>.Instance);

    private Task WriteManifestAsync(string json) => File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), json);

    private string DefaultManifest() => Serialize(DefaultManifestModel());

    private CatalogManifest DefaultManifestModel() => new()
    {
        Categories =
        [
            new("all", "All Wallpapers", "Everything", "\uE71D", 0),
            new("nature", "Nature", "Landscapes", "\uE707", 0),
            new("cities", "Cities", "City scenes", "\uE80F", 0)
        ],
        Items =
        [
            new CatalogEntry
            {
                Id = _imageId, Title = "Quiet Lake", Author = "Test publisher", Description = "Nature still",
                Type = WallpaperType.Image, Width = 1920, Height = 1080, CategoryId = "nature", Tags = ["lake", "calm"],
                MediaPath = "media/photo.png", ThumbnailPath = "media/photo.png", IsFeatured = true, Rating = 4.5,
                DownloadsCount = 5, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new CatalogEntry
            {
                Id = _videoId, Title = "Neon Rooftop", Author = "Test publisher", Description = "City loop",
                Type = WallpaperType.Video, Width = 3840, Height = 2160, DurationSeconds = 15, CategoryId = "cities", Tags = ["neon", "city"],
                MediaPath = "media/clip.mp4", ThumbnailPath = "media/photo.png", PreviewPath = "media/clip.mp4", IsFeatured = false,
                Rating = 4.0, DownloadsCount = 120, CreatedAtUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        ]
    };

    private static string Serialize(CatalogManifest manifest) => JsonSerializer.Serialize(manifest, ManifestJson);

    [Fact]
    public async Task StaticManifestServesTheSameMetadataAsTheLiveApi()
    {
        var categories = await _client.GetCategoriesAsync();
        Assert.Equal(3, categories.Count);
        Assert.Equal(2, categories.Single(c => c.Id == "all").ItemCount);

        Assert.Equal(_imageId, (await _client.GetFeaturedAsync()).Single().Id);
        Assert.Equal(_videoId, (await _client.GetPopularAsync(1)).Single().Id);
        Assert.Equal(_videoId, (await _client.GetRecentAsync(1)).Single().Id);
        Assert.Equal(_videoId, (await _client.GetByIdAsync(_videoId))!.Id);
        Assert.Null(await _client.GetByIdAsync(Guid.NewGuid()));

        // Media paths resolve against the manifest's own directory.
        var video = (await _client.GetByIdAsync(_videoId))!;
        Assert.Equal(_staticHttp.BaseAddress + "media/clip.mp4", video.DownloadUrl);
        Assert.Equal("Cities", video.CategoryName);
        Assert.Equal(TimeSpan.FromSeconds(15), video.Duration);

        var image = (await _client.GetByIdAsync(_imageId))!;
        Assert.Equal(1920, image.Width);
        Assert.Equal(_staticHttp.BaseAddress + "media/photo.png", image.ThumbnailUrl);
    }

    [Theory]
    [InlineData("lake")]
    [InlineData("city")]
    [InlineData("")]
    public async Task SearchResultsMatchTheLiveApi(string term)
    {
        var query = new CatalogSearchQuery { SearchTerm = term, PageSize = 24, PageNumber = 1 };
        var staticPage = await _client.SearchAsync(query);
        var apiPage = await _apiClient.SearchAsync(query);

        Assert.Equal(apiPage.TotalCount, staticPage.TotalCount);
        Assert.Equal(apiPage.Items.Select(i => i.Id), staticPage.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task FilteredSearchResultsMatchTheLiveApi()
    {
        var query = new CatalogSearchQuery
        {
            Filter = new CatalogFilter
            {
                CategoryId = "cities",
                Type = WallpaperType.Video,
                Resolution = CatalogResolutionFilter.UHD_4K,
                Orientation = CatalogOrientationFilter.Landscape,
                Tags = ["neon"],
                MinRating = 4,
                MaxDuration = TimeSpan.FromSeconds(20)
            },
            SortBy = CatalogSortOption.Popular
        };

        var staticPage = await _client.SearchAsync(query);
        var apiPage = await _apiClient.SearchAsync(query);

        Assert.Equal(apiPage.TotalCount, staticPage.TotalCount);
        Assert.Equal(_videoId, Assert.Single(staticPage.Items).Id);
    }

    [Fact]
    public async Task MediaDownloadsResumeWithThePublishersValidator()
    {
        var video = (await _client.GetByIdAsync(_videoId))!;

        await using var initial = await _client.OpenDownloadStreamAsync(video, 0);
        Assert.False(initial.IsPartial);
        Assert.Equal(_videoBytes.Length, initial.ContentLength);
        Assert.False(string.IsNullOrWhiteSpace(initial.EntityTag));
        await initial.Content.DisposeAsync();

        await using var resumed = await _client.OpenDownloadStreamAsync(video, 123, entityTag: initial.EntityTag);
        Assert.True(resumed.IsPartial);
        await using (resumed.Content)
        {
            using var buffer = new MemoryStream();
            await resumed.Content.CopyToAsync(buffer);
            Assert.Equal(_videoBytes.Skip(123), buffer.ToArray());
        }

        // A stale validator must restart the file instead of appending mismatched bytes.
        await using var restarted = await _client.OpenDownloadStreamAsync(video, 123, entityTag: "\"stale-version\"");
        Assert.False(restarted.IsPartial);
        await using (restarted.Content)
        {
            using var buffer = new MemoryStream();
            await restarted.Content.CopyToAsync(buffer);
            Assert.Equal(_videoBytes, buffer.ToArray());
        }
    }

    [Fact]
    public async Task ManifestReloadsWhenThePublisherPublishesANewFile()
    {
        string path = Path.Combine(_root, "catalog.json");
        string original = await File.ReadAllTextAsync(path);
        try
        {
            await WriteManifestAsync(Serialize(new CatalogManifest()));
            var empty = CreateClient(_staticHttp.BaseAddress + "catalog.json", fallback: null, cacheLifetime: TimeSpan.Zero);
            Assert.Empty(await empty.GetFeaturedAsync());

            await WriteManifestAsync(original);
            var restored = CreateClient(_staticHttp.BaseAddress + "catalog.json", fallback: null, cacheLifetime: TimeSpan.Zero);
            Assert.Equal(2, (await restored.SearchAsync(new CatalogSearchQuery())).TotalCount);
        }
        finally
        {
            await WriteManifestAsync(original);
        }
    }

    [Fact]
    public async Task UnusableItemsAreSkippedInsteadOfBreakingTheCatalog()
    {
        string path = Path.Combine(_root, "catalog.json");
        string original = await File.ReadAllTextAsync(path);
        try
        {
            var manifest = JsonSerializer.Deserialize<CatalogManifest>(original, ManifestJson)!;
            manifest.Items.Add(new CatalogEntry
            {
                Id = Guid.NewGuid(), Title = "Escape attempt", Author = "bad", Type = WallpaperType.Image,
                Width = 100, Height = 100, CategoryId = "nature", MediaPath = "../secret.png",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await WriteManifestAsync(Serialize(manifest));

            var page = CreateClient(_staticHttp.BaseAddress + "catalog.json", fallback: null, cacheLifetime: TimeSpan.Zero);
            var results = await page.SearchAsync(new CatalogSearchQuery());
            Assert.Equal(2, results.TotalCount);
            Assert.DoesNotContain(results.Items, item => item.Title == "Escape attempt");
        }
        finally
        {
            await WriteManifestAsync(original);
        }
    }

    [Fact]
    public async Task UnreachableManifestFallsBackToTheOfflineCatalog()
    {
        var fallback = new StubCatalogProvider();
        var client = CreateClient("http://127.0.0.1:1/catalog.json", fallback);

        var categories = await client.GetCategoriesAsync();
        Assert.Equal("fallback", Assert.Single(categories).Id);
    }

    [Fact]
    public async Task UnreachableManifestWithoutFallbackSurfacesTheFailure()
    {
        var strict = CreateClient("http://127.0.0.1:1/catalog.json", fallback: null);
        await Assert.ThrowsAnyAsync<Exception>(() => strict.GetCategoriesAsync());
    }

    public async Task DisposeAsync()
    {
        _staticHttp?.Dispose();
        if (_staticApp is not null) { await _staticApp.StopAsync(); await _staticApp.DisposeAsync(); }
        if (_apiApp is not null) { await _apiApp.StopAsync(); await _apiApp.DisposeAsync(); }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class StubCatalogProvider : ICatalogProvider
    {
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CatalogCategory>>([new("fallback", "Offline", "", "", 1)]);

        public Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CatalogItem>>([]);

        public Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CatalogItem>>([]);

        public Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CatalogItem>>([]);

        public Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<CatalogItem?>(null);

        public Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default)
            => Task.FromResult(new CatalogPage<CatalogItem>([], 1, query.PageSize, 0));

        public Task<Stream> OpenDownloadStreamAsync(CatalogItem item, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());
    }
}
