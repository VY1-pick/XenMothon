using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Infrastructure.Persistence.Services;
using XenMotion.Infrastructure.Persistence.Storage;
using XenMotion.Server;
using Xunit;

namespace XenMotion.Core.Tests.Services;

/// <summary>
/// Drives the link-import path against a real loopback HTTP host so media types, staging names and
/// extension resolution are exercised over the wire while the library pipeline stays a mock.
/// </summary>
public sealed class UrlImportServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xenmotion-urlimport-test-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private LocalStorageManager _storage = null!;
    private Mock<IWallpaperImportService> _importService = null!;
    private UrlImportService _service = null!;
    private string? _stagedPath;
    private byte[] _stagedBytes = [];
    private byte[] _imageBytes = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "media"));
        _imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(_root, "media", "ocean_sunset.jpg"), _imageBytes);
        await File.WriteAllBytesAsync(Path.Combine(_root, "media", "no-extension-file"), _imageBytes);
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), "{\"categories\":[],\"items\":[]}");

        _app = await CatalogServer.CreateAsync(["--urls", "http://127.0.0.1:0", "--environment", "Production", "--static", "--Logging:LogLevel:Default", "Warning"], _root);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };

        _storage = new LocalStorageManager(Path.Combine(_root, "library"), NullLogger<LocalStorageManager>.Instance);
        _importService = new Mock<IWallpaperImportService>();
        _importService
            .Setup(service => service.ImportFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                _stagedPath = path;
                // The service deletes its staging folder afterwards, so capture the bytes now.
                _stagedBytes = File.ReadAllBytes(path);
                return ImportResult.Succeeded(path, new Wallpaper
                {
                    Title = Path.GetFileNameWithoutExtension(path),
                    Type = WallpaperType.Image,
                    FilePath = "Media/fake",
                    ContentHash = "hash"
                });
            });

        _service = new UrlImportService(new RemoteMediaFetcher(NullLogger<RemoteMediaFetcher>.Instance), _storage, _importService.Object, NullLogger<UrlImportService>.Instance);
    }

    [Fact]
    public async Task LinkImportDownloadsAndKeepsTheOriginalFileName()
    {
        var progress = new List<double>();
        var result = await _service.ImportFromUrlAsync(_http.BaseAddress + "media/ocean_sunset.jpg", new Progress<double>(progress.Add));

        Assert.True(result.Success);
        Assert.NotNull(_stagedPath);
        Assert.Equal("ocean_sunset.jpg", Path.GetFileName(_stagedPath));
        Assert.Equal("ocean_sunset", result.ImportedWallpaper!.Title);
        // The reported source is the user's link, not the transient staging path.
        Assert.Equal(_http.BaseAddress + "media/ocean_sunset.jpg", result.SourceFilePath);
        Assert.Equal(_imageBytes, _stagedBytes);
        Assert.Equal(100d, progress.Last(), 3);
    }

    [Fact]
    public async Task LinkImportUsesTheServerMediaTypeWhenTheLinkHasNoExtension()
    {
        var result = await _service.ImportFromUrlAsync(_http.BaseAddress + "media/no-extension-file");

        // The static host reports application/octet-stream for an unknown extension, which is not media.
        Assert.False(result.Success);
        Assert.Contains("supported image or video type", result.ErrorMessage);
    }

    [Theory]
    [InlineData("", "Enter a media link")]
    [InlineData("not a link", "complete link")]
    [InlineData("ftp://example.com/a.jpg", "http and https")]
    [InlineData("https://user:secret@example.com/a.jpg", "credentials")]
    public async Task InvalidLinksAreRejectedBeforeDownloading(string url, string expectedMessage)
    {
        var result = await _service.ImportFromUrlAsync(url);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.ErrorMessage);
        _importService.Verify(service => service.ImportFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingRemoteFileFailsWithoutTouchingTheLibrary()
    {
        var result = await _service.ImportFromUrlAsync(_http.BaseAddress + "media/absent.jpg");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        _importService.Verify(service => service.ImportFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StagingFolderIsRemovedAfterTheImport()
    {
        await _service.ImportFromUrlAsync(_http.BaseAddress + "media/ocean_sunset.jpg");

        string urlImportRoot = Path.Combine(_storage.TempDirectory, "url-import");
        Assert.True(!Directory.Exists(urlImportRoot) || Directory.GetDirectories(urlImportRoot).Length == 0);
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
