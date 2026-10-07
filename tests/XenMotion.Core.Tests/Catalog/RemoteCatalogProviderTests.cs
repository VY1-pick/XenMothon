using System.Net;
using System.Net.Http;
using System.Text;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public sealed class RemoteCatalogProviderTests
{
    [Fact]
    public async Task Search_MapsRemotePayloadAndBuildsExpectedQuery()
    {
        string? requestedUri = null;
        using var client = CreateClient((request, _) =>
        {
            requestedUri = request.RequestUri?.ToString();
            return Json("""
            {
              "items": [{
                "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "title": "Remote Aurora",
                "description": "Remote description",
                "author": "RemoteCreator",
                "thumbnailUrl": "/media/aurora.jpg",
                "previewUrl": "/media/aurora.mp4",
                "downloadUrl": "/downloads/aurora.mp4",
                "type": "Video",
                "width": 3840,
                "height": 2160,
                "durationSeconds": 12.5,
                "fileSizeBytes": 123456,
                "tags": ["aurora", "4k"],
                "categoryId": "nature",
                "categoryName": "Nature",
                "rating": 4.8,
                "ratingCount": 100,
                "downloadsCount": 2000,
                "createdAtUtc": "2026-08-01T10:00:00Z",
                "updatedAtUtc": "2026-08-02T10:00:00Z",
                "isFeatured": true
              }],
              "pageNumber": 2,
              "pageSize": 12,
              "totalCount": 25
            }
            """);
        });

        var provider = CreateProvider(client);
        var page = await provider.SearchAsync(new CatalogSearchQuery
        {
            SearchTerm = "aurora lights",
            SortBy = CatalogSortOption.Newest,
            PageNumber = 2,
            PageSize = 12,
            Filter = new CatalogFilter
            {
                CategoryId = "nature",
                Resolution = CatalogResolutionFilter.UHD_4K,
                Orientation = CatalogOrientationFilter.Landscape,
                Tags = ["aurora", "night"]
            }
        });

        Assert.NotNull(requestedUri);
        Assert.Contains("/api/v1/catalog/search?", requestedUri!);
        Assert.Contains("q=" + Uri.EscapeDataString("aurora lights"), new Uri(requestedUri!).Query);
        Assert.Contains("page=2", requestedUri!);
        Assert.Contains("pageSize=12", requestedUri!);
        Assert.Contains("sort=Newest", requestedUri!);
        Assert.Contains("category=nature", requestedUri!);
        Assert.Contains("resolution=UHD_4K", requestedUri!);
        Assert.Contains("orientation=Landscape", requestedUri!);
        Assert.Contains("tags=" + Uri.EscapeDataString("aurora,night"), new Uri(requestedUri!).Query);

        Assert.Equal(2, page.PageNumber);
        Assert.Equal(25, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("Remote Aurora", page.Items[0].Title);
        Assert.Equal("https://catalog.test/media/aurora.jpg", page.Items[0].ThumbnailUrl);
        Assert.Equal("https://catalog.test/downloads/aurora.mp4", page.Items[0].DownloadUrl);
        Assert.Equal(TimeSpan.FromSeconds(12.5), page.Items[0].Duration);
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNullWithoutFallback()
    {
        using var client = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fallback = new LocalCatalogProvider(NullLogger<LocalCatalogProvider>.Instance);
        var provider = CreateProvider(client, fallback);

        var result = await provider.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoteFailure_UsesLocalFallback()
    {
        using var client = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var fallback = new LocalCatalogProvider(NullLogger<LocalCatalogProvider>.Instance);
        var provider = CreateProvider(client, fallback);

        var result = await provider.GetFeaturedAsync(3);

        Assert.NotEmpty(result);
        Assert.True(result.Count <= 3);
        Assert.All(result, item => Assert.True(item.IsFeatured));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowedByFallback()
    {
        using var client = CreateClient(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var fallback = new LocalCatalogProvider(NullLogger<LocalCatalogProvider>.Instance);
        var provider = CreateProvider(client, fallback);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRecentAsync(5, cts.Token));
    }

    [Fact]
    public async Task Download_StreamsRemoteContentAndResolvesRelativeUrl()
    {
        byte[] payload = Encoding.UTF8.GetBytes("remote-media");
        using var client = CreateClient((request, _) =>
        {
            Assert.Equal("https://catalog.test/downloads/wallpaper.bin", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        var provider = CreateProvider(client);
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = "Remote",
            Description = "",
            Author = "Creator",
            ThumbnailUrl = "https://catalog.test/thumb.jpg",
            PreviewUrl = "https://catalog.test/preview.mp4",
            DownloadUrl = "/downloads/wallpaper.bin",
            Type = WallpaperType.Video,
            Width = 1920,
            Height = 1080,
            CategoryId = "test",
            CategoryName = "Test"
        };

        await using var stream = await provider.OpenDownloadStreamAsync(item);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Assert.Equal(payload, memory.ToArray());
    }

    [Fact]
    public async Task Download_WithOffset_SendsRangeAndMarksPartialResponse()
    {
        using var client = CreateClient((request, _) =>
        {
            Assert.Equal("bytes=128-", request.Headers.Range!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tail"))
            };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(128, 131, 132);
            return response;
        });
        var provider = CreateProvider(client);
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(), Title = "Resume", Description = "", Author = "Creator",
            ThumbnailUrl = "https://catalog.test/thumb.jpg", PreviewUrl = "https://catalog.test/preview.mp4",
            DownloadUrl = "/downloads/resume.mp4", Type = WallpaperType.Video, Width = 1920, Height = 1080,
            CategoryId = "test", CategoryName = "Test"
        };

        var resumable = (XenMotion.Core.Interfaces.IResumableCatalogDownloadProvider)provider;
        await using var stream = await resumable.OpenDownloadStreamAsync(item, 128);
        Assert.True(stream.IsPartial);
        Assert.Equal(4, stream.ContentLength);
    }

    private static RemoteCatalogProvider CreateProvider(HttpClient client, ICatalogProvider? fallback = null)
        => new(
            client,
            new RemoteCatalogOptions
            {
                UseRemoteCatalog = true,
                BaseUrl = "https://catalog.test/",
                RequestTimeout = TimeSpan.FromSeconds(5),
                UseLocalFallback = fallback is not null
            },
            NullLogger<RemoteCatalogProvider>.Instance,
            fallback);

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(new DelegateHandler(handler)) { BaseAddress = new Uri("https://catalog.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        => new(new DelegateHandler((request, _) => Task.FromResult(handler(request, CancellationToken.None))))
        {
            BaseAddress = new Uri("https://catalog.test/")
        };

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
