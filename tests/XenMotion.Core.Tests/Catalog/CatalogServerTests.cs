using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using XenMotion.Server;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public sealed class CatalogServerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xenmotion-server-test-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private RemoteCatalogProvider _client = null!;
    private readonly Guid _imageId = Guid.NewGuid();
    private readonly Guid _videoId = Guid.NewGuid();
    private readonly byte[] _videoBytes = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        // HTTP-range fixture bytes; playback/codec validation belongs to the media importer.
        await File.WriteAllBytesAsync(Path.Combine(_root, "video.mp4"), _videoBytes);
        await File.WriteAllBytesAsync(Path.Combine(_root, "image.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII="));
        var manifest = new CatalogManifest
        {
            Categories = [new("all", "All Wallpapers", "", "\uE71D", 999), new("cities", "Cities", "", "\uE80F", 999)],
            Items =
            [
                Entry(_imageId, "Tokyo Still", WallpaperType.Image, "image.png", 1080, 1920, 0),
                Entry(_videoId, "Tokyo Night", WallpaperType.Video, "video.mp4", 3840, 2160, 15)
            ]
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(manifest, options));
        _app = await CatalogServer.CreateAsync(["--urls", "http://127.0.0.1:0", "--environment", "Production", "--Logging:LogLevel:Default", "Warning"], _root);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
        _client = new RemoteCatalogProvider(_http, new RemoteCatalogOptions
        {
            BaseUrl = _http.BaseAddress.ToString(), UseRemoteCatalog = true, UseLocalFallback = false
        }, NullLogger<RemoteCatalogProvider>.Instance);
    }

    private static CatalogEntry Entry(Guid id, string title, WallpaperType type, string path, int width, int height, double seconds) => new()
    {
        Id = id, Title = title, Author = "Test publisher", Description = "City test fixture", Type = type,
        Width = width, Height = height, DurationSeconds = seconds, CategoryId = "cities", Tags = ["night", "tokyo"],
        MediaPath = path, ThumbnailPath = "image.png", IsFeatured = true, Rating = 4.5, DownloadsCount = type == WallpaperType.Video ? 100 : 10,
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task DesktopClientReadsEveryCatalogRouteAndFilters()
    {
        var categories = await _client.GetCategoriesAsync();
        Assert.All(categories, c => Assert.Equal(2, c.ItemCount));
        Assert.Equal(2, (await _client.GetFeaturedAsync()).Count);
        Assert.Equal(_videoId, (await _client.GetPopularAsync(1)).Single().Id);
        Assert.Equal(2, (await _client.GetRecentAsync()).Count);
        Assert.Null(await _client.GetByIdAsync(Guid.NewGuid()));
        var page = await _client.SearchAsync(new CatalogSearchQuery { SearchTerm = "Tokyo", PageSize = 1, PageNumber = 2 });
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(_imageId, page.Items.Single().Id);
        var filtered = await _client.SearchAsync(new CatalogSearchQuery
        {
            Filter = new CatalogFilter { Type = WallpaperType.Video, Resolution = CatalogResolutionFilter.UHD_4K, Orientation = CatalogOrientationFilter.Landscape, Tags = ["night"], CategoryId = "cities", MinRating = 4, MaxDuration = TimeSpan.FromSeconds(20) }
        });
        Assert.Equal(_videoId, filtered.Items.Single().Id);
        Assert.Empty((await _client.SearchAsync(new CatalogSearchQuery { SearchTerm = "absent" })).Items);
    }

    [Fact]
    public async Task DownloadsResumeWithStrongIdentityAndMismatchedIfRangeRestarts()
    {
        var video = (await _client.GetByIdAsync(_videoId))!;
        var initial = await _client.OpenDownloadStreamAsync(video, 0);
        Assert.False(initial.IsPartial);
        Assert.Equal(_videoBytes.Length, initial.ContentLength);
        Assert.NotNull(initial.EntityTag);
        await initial.Content.DisposeAsync();
        var resumed = await _client.OpenDownloadStreamAsync(video, 123, entityTag: initial.EntityTag);
        Assert.True(resumed.IsPartial);
        await using (resumed.Content)
        {
            using var buffer = new MemoryStream();
            await resumed.Content.CopyToAsync(buffer);
            Assert.Equal(_videoBytes.Skip(123), buffer.ToArray());
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, video.DownloadUrl);
        request.Headers.Range = new RangeHeaderValue(123, null);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"different-version\""));
        using var full = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal(_videoBytes, await full.Content.ReadAsByteArrayAsync());
        using var invalid = new HttpRequestMessage(HttpMethod.Get, video.DownloadUrl);
        invalid.Headers.Range = new RangeHeaderValue(_videoBytes.Length + 1, null);
        using var rangeResponse = await _http.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, rangeResponse.StatusCode);
        using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, video.DownloadUrl));
        Assert.Equal(_videoBytes.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SourceReplacementKeepsPublishedBytesAndCreatesNewVersionOnReload()
    {
        var before = (await _client.GetByIdAsync(_videoId))!;
        await File.WriteAllBytesAsync(Path.Combine(_root, "video.mp4"), [7, 8, 9]);
        Assert.Equal(_videoBytes, await _http.GetByteArrayAsync(before.DownloadUrl));
        var reloaded = await CatalogSnapshot.LoadAsync(_root);
        var after = reloaded.Items.Single(i => i.Id == _videoId);
        Assert.NotEqual(before.DownloadUrl, after.DownloadUrl);
        Assert.Equal(3, after.FileSizeBytes);
    }

    [Fact]
    public async Task MatchingCacheValidatorReturns304AndDamagedCacheReturns503()
    {
        var video = (await _client.GetByIdAsync(_videoId))!;
        using var initial = await _http.GetAsync(video.DownloadUrl);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, video.DownloadUrl);
        conditional.Headers.IfNoneMatch.Add(initial.Headers.ETag!);
        using var cached = await _http.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        string fileName = new Uri(video.DownloadUrl).Segments.Last();
        await File.WriteAllBytesAsync(Path.Combine(_root, ".published", fileName), [1]);
        using var changed = await _http.GetAsync(video.DownloadUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, changed.StatusCode);
    }

    [Theory]
    [InlineData("\"items\":null")]
    [InlineData("\"categories\":null")]
    public async Task NullManifestCollectionsAreRejected(string field)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), "{" + field + "}");
        await Assert.ThrowsAsync<InvalidDataException>(() => CatalogSnapshot.LoadAsync(_root));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sort=unknown")]
    [InlineData("type=999")]
    [InlineData("minRating=NaN")]
    [InlineData("maxDurationSeconds=-1")]
    public async Task InvalidQueriesAreRejected(string query)
    {
        using var response = await _http.GetAsync("api/v1/catalog/search?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HugePageDoesNotOverflowOrExposeUnlistedFiles()
    {
        using var response = await _http.GetAsync("api/v1/catalog/search?page=2147483647&pageSize=100");
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, page.RootElement.GetProperty("items").GetArrayLength());
        using var secret = await _http.GetAsync("catalog.json");
        Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
        using var guessed = await _http.GetAsync("media/unknown/catalog.json");
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
    }

    [Fact]
    public async Task ManifestCannotPublishFilesOutsideItsDirectory()
    {
        string path = Path.Combine(_root, "catalog.json");
        string json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("video.mp4", "../private.mp4"));
        await Assert.ThrowsAsync<InvalidDataException>(() => CatalogSnapshot.LoadAsync(_root));
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        Directory.Delete(_root, recursive: true);
    }
}
