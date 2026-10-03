using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.Infrastructure.Persistence.Services;
using Lumen.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lumen.Core.Tests.Services;

public class LibraryServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;
    private readonly Mock<IWallpaperRepository> _mockRepo;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly LibraryService _libraryService;

    public LibraryServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LumenLibTests_" + Guid.NewGuid().ToString("N"));
        _storageManager = new LocalStorageManager(_testRoot, NullLogger<LocalStorageManager>.Instance);
        _mockRepo = new Mock<IWallpaperRepository>();
        _mockThumb = new Mock<IThumbnailService>();

        _libraryService = new LibraryService(
            _mockRepo.Object,
            _storageManager,
            _mockThumb.Object,
            NullLogger<LibraryService>.Instance);
    }

    [Fact]
    public async Task Library_GetWallpapers_MarksMissingIfFileDeletedOnDisk()
    {
        Guid id = Guid.NewGuid();
        var wp = new Wallpaper
        {
            Id = id,
            Title = "Ghost Wallpaper",
            Type = WallpaperType.Video,
            FilePath = $"Media/{id}/nonexistent.mp4",
            ContentHash = "hash1"
        };

        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([wp]);

        var list = await _libraryService.GetWallpapersAsync();

        Assert.Single(list);
        Assert.True(list[0].IsMissing);
    }

    [Fact]
    public async Task Library_DeleteWallpaper_RemovesDatabaseMediaAndThumbnail()
    {
        Guid id = Guid.NewGuid();
        string wpDir = _storageManager.GetWallpaperDirectory(id);
        Directory.CreateDirectory(wpDir);
        string mediaFile = Path.Combine(wpDir, "test.mp4");
        File.WriteAllText(mediaFile, "content");

        var wp = new Wallpaper
        {
            Id = id,
            Title = "To Delete",
            Type = WallpaperType.Video,
            FilePath = _storageManager.MakeStorageRelativePath(mediaFile),
            ContentHash = "hash_del"
        };

        _mockRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(wp);

        _mockThumb.Setup(t => t.DeleteThumbnailAsync(id, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var result = await _libraryService.DeleteWallpaperAsync(id);

        Assert.True(result.Success);
        Assert.True(result.DatabaseDeleted);
        Assert.True(result.MediaDeleted);
        Assert.False(Directory.Exists(wpDir));
        _mockRepo.Verify(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _mockThumb.Verify(t => t.DeleteThumbnailAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Library_DeleteWallpaper_MissingMediaOnDisk_HandlesGracefully()
    {
        Guid id = Guid.NewGuid();
        var wp = new Wallpaper
        {
            Id = id,
            Title = "Missing on Disk",
            Type = WallpaperType.Video,
            FilePath = $"Media/{id}/missing.mp4",
            ContentHash = "hash_missing"
        };

        _mockRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(wp);

        var result = await _libraryService.DeleteWallpaperAsync(id);

        Assert.True(result.Success);
        _mockRepo.Verify(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Library_Reconcile_DetectsMissingMediaAndOrphanedFolders()
    {
        Guid validId = Guid.NewGuid();
        string validDir = _storageManager.GetWallpaperDirectory(validId);
        Directory.CreateDirectory(validDir);
        string validMedia = Path.Combine(validDir, "valid.mp4");
        File.WriteAllText(validMedia, "data");

        Guid missingId = Guid.NewGuid();

        // Create an orphaned directory with random GUID that is NOT in DB
        Guid orphanId = Guid.NewGuid();
        string orphanDir = _storageManager.GetWallpaperDirectory(orphanId);
        Directory.CreateDirectory(orphanDir);
        File.WriteAllText(Path.Combine(orphanDir, "orphan.mp4"), "orphan_data");

        var wpValid = new Wallpaper { Id = validId, Title = "Valid", Type = WallpaperType.Video, FilePath = _storageManager.MakeStorageRelativePath(validMedia), ContentHash = "v" };
        var wpMissing = new Wallpaper { Id = missingId, Title = "Missing", Type = WallpaperType.Video, FilePath = $"Media/{missingId}/missing.mp4", ContentHash = "m" };

        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([wpValid, wpMissing]);

        var report = await _libraryService.ReconcileLibraryAsync();

        Assert.Equal(2, report.TotalWallpapers);
        Assert.Equal(1, report.ValidWallpapers);
        Assert.Single(report.MissingMediaIds);
        Assert.Equal(missingId, report.MissingMediaIds[0]);
        Assert.Single(report.OrphanedDirectories);
        Assert.Equal(orphanDir, report.OrphanedDirectories[0]);
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
