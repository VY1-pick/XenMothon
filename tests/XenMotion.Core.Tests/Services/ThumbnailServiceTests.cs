using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.Infrastructure.Persistence.Services;
using XenMotion.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;
using XenMotion.Core.Interfaces;

namespace XenMotion.Core.Tests.Services;

public class ThumbnailServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;
    private readonly ThumbnailService _thumbnailService;
    private readonly Mock<IThumbnailCodec> _codec = new();

    public ThumbnailServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "XenMotionThumbTests_" + Guid.NewGuid().ToString("N"));
        _storageManager = new LocalStorageManager(_testRoot, NullLogger<LocalStorageManager>.Instance);
        _codec.Setup(c => c.IsValid(It.IsAny<string>())).Returns((string path) => File.ReadAllText(path) == "decoded-thumbnail");
        _codec.Setup(c => c.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>()))
            .Returns((string source, string destination, WallpaperType type, CancellationToken ct) => File.WriteAllTextAsync(destination, "decoded-thumbnail", ct));
        _thumbnailService = new ThumbnailService(_storageManager, NullLogger<ThumbnailService>.Instance, _codec.Object);
    }

    [Fact]
    public async Task GenerationUsesCodecInsteadOfCopyingSource()
    {
        var path = Path.Combine(_testRoot, "large.png");
        var bytes = new byte[600_000]; new Random(42).NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        var thumbnail = await _thumbnailService.CreateThumbnailAsync(path, Guid.NewGuid(), WallpaperType.Image);
        Assert.Equal("decoded-thumbnail", await File.ReadAllTextAsync(thumbnail));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Thumbnail_CreateAndCache_AvoidsRegenerationWhenCached()
    {
        Guid id = Guid.NewGuid();
        string dummyMedia = Path.Combine(_testRoot, "test.png");
        File.WriteAllText(dummyMedia, "image_bytes");

        string thumb1 = await _thumbnailService.CreateThumbnailAsync(dummyMedia, id, WallpaperType.Image);
        Assert.True(File.Exists(thumb1));
        var writeTime1 = File.GetLastWriteTimeUtc(thumb1);

        await Task.Delay(15);

        // Calling again should return cached path without overwriting
        string thumb2 = await _thumbnailService.CreateThumbnailAsync(dummyMedia, id, WallpaperType.Image);
        Assert.Equal(thumb1, thumb2);
        var writeTime2 = File.GetLastWriteTimeUtc(thumb2);
        Assert.Equal(writeTime1, writeTime2);
    }

    [Fact]
    public async Task Thumbnail_CorruptFile_IsRegenerated()
    {
        Guid id = Guid.NewGuid();
        string dummyMedia = Path.Combine(_testRoot, "test_vid.mp4");
        File.WriteAllText(dummyMedia, "video_bytes");

        string thumbPath = _thumbnailService.GetThumbnailPath(id);
        // Nonempty corrupt files must not pass validation either.
        File.WriteAllText(thumbPath, "corrupt image");

        Assert.False(_thumbnailService.ThumbnailExists(id));

        string regenerated = await _thumbnailService.CreateThumbnailAsync(dummyMedia, id, WallpaperType.Video);
        Assert.True(File.Exists(regenerated));
        Assert.True(new FileInfo(regenerated).Length > 0);
        Assert.True(_thumbnailService.ThumbnailExists(id));
    }

    [Fact]
    public async Task CancellationDoesNotLeaveTemporaryFiles()
    {
        string source = Path.Combine(_testRoot, "source.png");
        await File.WriteAllTextAsync(source, "source");
        _codec.Setup(c => c.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _thumbnailService.CreateThumbnailAsync(source, Guid.NewGuid(), WallpaperType.Image));
        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    [Fact]
    public async Task FailedGenerationDoesNotPublishInvalidOutput()
    {
        string source = Path.Combine(_testRoot, "source.png");
        await File.WriteAllTextAsync(source, "source");
        _codec.Setup(c => c.IsValid(It.IsAny<string>())).Returns(false);
        Guid id = Guid.NewGuid();
        Assert.Empty(await _thumbnailService.CreateThumbnailAsync(source, id, WallpaperType.Image));
        Assert.False(File.Exists(_thumbnailService.GetThumbnailPath(id)));
        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    [Fact]
    public async Task Thumbnail_Delete_RemovesFile()
    {
        Guid id = Guid.NewGuid();
        string dummyMedia = Path.Combine(_testRoot, "test_del.png");
        File.WriteAllText(dummyMedia, "image_bytes");

        string thumbPath = await _thumbnailService.CreateThumbnailAsync(dummyMedia, id, WallpaperType.Image);
        Assert.True(File.Exists(thumbPath));

        bool deleted = await _thumbnailService.DeleteThumbnailAsync(id);
        Assert.True(deleted);
        Assert.False(File.Exists(thumbPath));
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
