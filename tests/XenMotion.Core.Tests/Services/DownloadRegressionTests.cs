using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using XenMotion.Infrastructure.Persistence.Catalog.Static;
using XenMotion.Infrastructure.Persistence.Services;
using XenMotion.Infrastructure.Persistence.Storage;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public sealed class DownloadRegressionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xenmotion-download-regression-" + Guid.NewGuid().ToString("N"));
    private WebApplication _host = null!;
    private string _baseUrl = "";
    private readonly byte[] _bytes = Enumerable.Range(0, 10).Select(x => (byte)x).ToArray();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        _host = builder.Build();
        _host.MapGet("/chunks", async context =>
        {
            long start = context.Request.GetTypedHeaders().Range?.Ranges.Single().From ?? 0;
            int end = start == 4 ? 5 : 9;
            context.Response.StatusCode = 206;
            context.Response.Headers.ETag = "\"same\"";
            context.Response.Headers.ContentRange = $"bytes {start}-{end}/10";
            context.Response.ContentLength = end - start + 1;
            await context.Response.Body.WriteAsync(_bytes.AsMemory((int)start, (int)(end - start + 1)));
        });
        _host.MapGet("/stall/{error:bool}", async context =>
        {
            if (bool.Parse((string)context.Request.RouteValues["error"]!)) context.Response.StatusCode = 500;
            context.Response.ContentLength = 100;
            await context.Response.Body.WriteAsync(new byte[] { 1 }); await context.Response.Body.FlushAsync();
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); } catch (OperationCanceledException) { }
        });
        _host.MapGet("/image.jpg", () => Results.Bytes(_bytes, "image/jpeg"));
        await _host.StartAsync(); _baseUrl = _host.Urls.Single();
    }

    [Fact]
    public async Task ResumeDoesNotFinishUntilTheEntireFileIsPresent()
    {
        string path = Path.Combine(_root, "download.part");
        await File.WriteAllBytesAsync(path, _bytes[..4]); await File.WriteAllTextAsync(path + ".etag", "\"same\"");
        var fetcher = new RemoteMediaFetcher();
        var result = await fetcher.FetchAsync(new Uri(_baseUrl + "/chunks"), path);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(10, result.SizeBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledBodyTimesOutWithoutWaitingForUserCancellation(bool errorResponse)
    {
        var fetcher = new RemoteMediaFetcher(requestTimeout: TimeSpan.FromMilliseconds(100));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await fetcher.FetchAsync(new Uri(_baseUrl + "/stall/" + errorResponse), Path.Combine(_root, "stalled.part"), ct: cancel.Token)
            .WaitAsync(TimeSpan.FromSeconds(6));
        Assert.False(result.Success);
        Assert.False(cancel.IsCancellationRequested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogTimeoutUsesOfflineFallbackButUserCancellationDoesNot(bool staticManifest)
    {
        using var http = new HttpClient(new SlowHeaders());
        var fallback = new Mock<ICatalogProvider>();
        fallback.Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new CatalogCategory("offline", "Offline", "", "", 0) });
        ICatalogProvider provider = staticManifest
            ? new StaticCatalogProvider(http, new() { ManifestUrl = "https://catalog.invalid/catalog.json", RequestTimeout = TimeSpan.FromMilliseconds(50) }, Mock.Of<IResumableCatalogDownloadProvider>(), NullLogger<StaticCatalogProvider>.Instance, fallback.Object)
            : new RemoteCatalogProvider(http, new() { UseRemoteCatalog = true, BaseUrl = "https://catalog.invalid/", RequestTimeout = TimeSpan.FromMilliseconds(50), UseLocalFallback = true }, NullLogger<RemoteCatalogProvider>.Instance, fallback.Object);
        Assert.Equal("offline", Assert.Single(await provider.GetCategoriesAsync()).Id);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetCategoriesAsync(cancel.Token));
        fallback.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecodeFailureDoesNotCreateALibraryRecordOrLeaveMedia()
    {
        string file = Path.Combine(_root, "fake.jpg"); await File.WriteAllTextAsync(file, "not an image");
        var storage = new LocalStorageManager(Path.Combine(_root, "decode-library"), NullLogger<LocalStorageManager>.Instance);
        var repo = new Mock<IWallpaperRepository>(); var codec = new Mock<IThumbnailCodec>();
        codec.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("Invalid image"));
        var thumbs = new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance, codec.Object);
        var importer = new WallpaperImportService(storage, repo.Object, thumbs, NullLogger<WallpaperImportService>.Instance);
        var result = await importer.ImportFileAsync(file);
        Assert.False(result.Success);
        repo.Verify(x => x.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(Directory.GetDirectories(storage.MediaDirectory));
    }

    [Fact]
    public async Task CancellationAfterCatalogCommitCleansFinalFiles()
    {
        var storage = new LocalStorageManager(Path.Combine(_root, "cancel-library"), NullLogger<LocalStorageManager>.Instance);
        var provider = new Mock<ICatalogProvider>(); var repo = new Mock<IWallpaperRepository>(); var thumbnails = new Mock<IThumbnailService>();
        provider.Setup(x => x.OpenDownloadStreamAsync(It.IsAny<CatalogItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(_bytes));
        thumbnails.Setup(x => x.CreateThumbnailAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        var service = new ExploreCatalogService(provider.Object, repo.Object, storage, thumbnails.Object, NullLogger<ExploreCatalogService>.Instance);
        var item = new CatalogItem { Id = Guid.NewGuid(), Title = "Cancelled", Description = "", Author = "Test", ThumbnailUrl = "", PreviewUrl = "", Width = 10, Height = 10, Type = WallpaperType.Image, CategoryId = "all", CategoryName = "All" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubscribeAsync(item));
        Assert.Empty(Directory.GetDirectories(storage.MediaDirectory));
    }

    [Fact]
    public async Task PrivateUrlQueryDoesNotAppearInDownloadOrImportLogs()
    {
        var downloadLog = new CaptureLogger<RemoteMediaFetcher>(); var importLog = new CaptureLogger<UrlImportService>();
        var storage = new LocalStorageManager(Path.Combine(_root, "log-library"), NullLogger<LocalStorageManager>.Instance);
        var importer = new Mock<IWallpaperImportService>();
        importer.Setup(x => x.ImportFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ImportResult.Succeeded("", new Wallpaper { Title = "Image", FilePath = "test", ContentHash = "test" }));
        var service = new UrlImportService(new RemoteMediaFetcher(downloadLog), storage, importer.Object, importLog);
        Assert.True((await service.ImportFromUrlAsync(_baseUrl + "/image.jpg?token=private-download-token")).Success);
        Assert.DoesNotContain("private-download-token", string.Join("\n", downloadLog.Messages.Concat(importLog.Messages)));
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync(); await _host.DisposeAsync();
        Directory.Delete(_root, true);
    }

    private sealed class SlowHeaders : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
