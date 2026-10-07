using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog;
using XenMotion.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public class CatalogSubscriptionTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;
    private readonly Mock<IWallpaperRepository> _mockRepo;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly LocalCatalogProvider _catalogProvider;
    private readonly ExploreCatalogService _service;

    public CatalogSubscriptionTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "XenMotionSubTests_" + Guid.NewGuid().ToString("N"));
        _storageManager = new LocalStorageManager(_testRoot, NullLogger<LocalStorageManager>.Instance);
        _mockRepo = new Mock<IWallpaperRepository>();
        _mockThumb = new Mock<IThumbnailService>();
        _catalogProvider = new LocalCatalogProvider(NullLogger<LocalCatalogProvider>.Instance);

        _mockThumb.Setup(t => t.CreateThumbnailAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string _, Guid id, WallpaperType _, CancellationToken _) => Path.Combine(_testRoot, "Thumbnails", $"{id:D}.jpg"));

        _service = new ExploreCatalogService(
            _catalogProvider,
            _mockRepo.Object,
            _storageManager,
            _mockThumb.Object,
            NullLogger<ExploreCatalogService>.Instance);
    }

    [Fact]
    public async Task Subscribe_NewItem_DownloadsStagesGeneratesThumbnailAndInsertsIntoDatabase()
    {
        Guid knownId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var item = await _catalogProvider.GetByIdAsync(knownId);
        Assert.NotNull(item);

        _mockRepo.Setup(r => r.GetByIdAsync(knownId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Wallpaper?)null);

        var progressList = new List<double>();
        var progress = new Progress<double>(p => progressList.Add(p));

        var wallpaper = await _service.SubscribeAsync(item, progress);

        Assert.NotNull(wallpaper);
        Assert.Equal(knownId, wallpaper.Id);
        Assert.Equal(item.Title, wallpaper.Title);
        Assert.True(wallpaper.IsSubscribed);
        Assert.StartsWith("Media/", wallpaper.FilePath);

        // Verify committed to storage and repository
        _mockRepo.Verify(r => r.InsertAsync(It.Is<Wallpaper>(w => w.Id == knownId), It.IsAny<CancellationToken>()), Times.Once);
        string absMedia = _storageManager.ResolveAbsolutePath(wallpaper.FilePath);
        Assert.True(File.Exists(absMedia));

        // State check
        var state = await _service.GetDownloadStateAsync(knownId);
        Assert.Equal(CatalogDownloadState.Downloaded, state);
    }

    [Fact]
    public async Task Subscribe_AlreadyInstalledItem_ReturnsExistingWithoutReDownloading()
    {
        Guid knownId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var item = await _catalogProvider.GetByIdAsync(knownId);
        Assert.NotNull(item);

        string dummyMedia = Path.Combine(_storageManager.MediaDirectory, knownId.ToString(), "forest.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(dummyMedia)!);
        await File.WriteAllTextAsync(dummyMedia, "media_bytes");

        var existing = new Wallpaper
        {
            Id = knownId,
            Title = item.Title,
            Type = item.Type,
            FilePath = _storageManager.MakeStorageRelativePath(dummyMedia),
            ContentHash = "hash123"
        };

        _mockRepo.Setup(r => r.GetByIdAsync(knownId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);

        var wallpaper = await _service.SubscribeAsync(item);

        Assert.Same(existing, wallpaper);
        // Must not call insert again
        _mockRepo.Verify(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Subscribe_DownloadFails_CleansUpArtifactsAndSetsFailedState()
    {
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = "Broken Wallpaper",
            Description = "",
            Author = "",
            ThumbnailUrl = "",
            PreviewUrl = "",
            Type = WallpaperType.Image,
            Width = 1920,
            Height = 1080,
            CategoryId = "abstract",
            CategoryName = "Abstract"
        };

        var mockFaultyProvider = new Mock<ICatalogProvider>();
        mockFaultyProvider.Setup(p => p.OpenDownloadStreamAsync(It.IsAny<CatalogItem>(), It.IsAny<CancellationToken>()))
                          .ThrowsAsync(new IOException("Simulated network stream timeout."));

        var faultyService = new ExploreCatalogService(
            mockFaultyProvider.Object,
            _mockRepo.Object,
            _storageManager,
            _mockThumb.Object,
            NullLogger<ExploreCatalogService>.Instance);

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await faultyService.SubscribeAsync(item);
        });

        // Verify state is Failed and temp files are clean
        var state = await faultyService.GetDownloadStateAsync(item.Id);
        Assert.Equal(CatalogDownloadState.Failed, state);
        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    [Fact]
    public async Task Subscribe_Cancellation_AbortsAndRemovesPartialStorage()
    {
        Guid knownId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var item = await _catalogProvider.GetByIdAsync(knownId);
        Assert.NotNull(item);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _service.SubscribeAsync(item, ct: cts.Token);
        });

        // Must clean up any temp files
        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    [Fact]
    public async Task GetDownloadStates_SynchronizesWithLocalDatabase()
    {
        Guid id1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid id2 = Guid.Parse("10000000-0000-0000-0000-000000000002");

        string media1 = Path.Combine(_storageManager.MediaDirectory, id1.ToString(), "test1.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(media1)!);
        await File.WriteAllTextAsync(media1, "data");

        var localWp = new Wallpaper
        {
            Id = id1,
            Title = "Installed",
            Type = WallpaperType.Video,
            FilePath = _storageManager.MakeStorageRelativePath(media1),
            ContentHash = "test-content-hash"
        };

        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([localWp]);

        var states = await _service.GetDownloadStatesAsync([id1, id2]);

        Assert.Equal(CatalogDownloadState.Downloaded, states[id1]);
        Assert.Equal(CatalogDownloadState.NotDownloaded, states[id2]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
