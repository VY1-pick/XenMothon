using System.Security.Cryptography;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.Infrastructure.Persistence.Services;
using Lumen.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lumen.Core.Tests.Services;

public class WallpaperImportServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;
    private readonly Mock<IWallpaperRepository> _mockRepo;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly WallpaperImportService _importService;
    private readonly List<string> _tempFiles = [];

    public WallpaperImportServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LumenImportTests_" + Guid.NewGuid().ToString("N"));
        _storageManager = new LocalStorageManager(_testRoot, NullLogger<LocalStorageManager>.Instance);
        _mockRepo = new Mock<IWallpaperRepository>();
        _mockThumb = new Mock<IThumbnailService>();

        _mockThumb.Setup(t => t.CreateThumbnailAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WallpaperType>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string _, Guid id, WallpaperType _, CancellationToken _) => Path.Combine(_testRoot, "Thumbnails", $"{id:D}.jpg"));

        _importService = new WallpaperImportService(
            _storageManager,
            _mockRepo.Object,
            _mockThumb.Object,
            NullLogger<WallpaperImportService>.Instance);
    }

    [Fact]
    public async Task Import_ImageFile_SuccessfullyPersistsMetadataAndThumbnail()
    {
        string sourceFile = CreateDummyFile("landscape.png", "image_data_bytes_123");
        var result = await _importService.ImportFileAsync(sourceFile);

        Assert.True(result.Success);
        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.ImportedWallpaper);
        Assert.Equal("landscape", result.ImportedWallpaper.Title);
        Assert.Equal(WallpaperType.Image, result.ImportedWallpaper.Type);
        Assert.StartsWith("Media/", result.ImportedWallpaper.FilePath);
        Assert.StartsWith("Thumbnails/", result.ImportedWallpaper.ThumbnailPath);

        // Verify inserted into database
        _mockRepo.Verify(r => r.InsertAsync(It.Is<Wallpaper>(w => w.Id == result.ImportedWallpaper.Id), It.IsAny<CancellationToken>()), Times.Once);

        // Verify media committed to storage
        string absoluteMedia = _storageManager.ResolveAbsolutePath(result.ImportedWallpaper.FilePath);
        Assert.True(File.Exists(absoluteMedia));
    }

    [Fact]
    public async Task Import_VideoFile_ExtractsMetadataAndCalculatesSha256()
    {
        string sourceFile = CreateDummyFile("cyberpunk.mp4", "video_bytes_test_content");
        var result = await _importService.ImportFileAsync(sourceFile);

        Assert.True(result.Success);
        Assert.NotNull(result.ImportedWallpaper);
        Assert.Equal(WallpaperType.Video, result.ImportedWallpaper.Type);
        Assert.True(result.ImportedWallpaper.IsVideo);
        Assert.NotEmpty(result.ImportedWallpaper.ContentHash);

        // Verify SHA-256 accuracy
        byte[] expectedHashBytes = SHA256.HashData(File.ReadAllBytes(sourceFile));
        string expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();
        Assert.Equal(expectedHash, result.ImportedWallpaper.ContentHash);
    }

    [Fact]
    public async Task Import_DuplicateFile_ReturnsDuplicateResultWithoutDuplicateStorage()
    {
        string sourceFile = CreateDummyFile("sample.mp4", "duplicate_detection_content");
        byte[] hashBytes = SHA256.HashData(File.ReadAllBytes(sourceFile));
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var existingWallpaper = new Wallpaper
        {
            Id = Guid.NewGuid(),
            Title = "Existing Sample",
            Type = WallpaperType.Video,
            FilePath = "Media/existing/sample.mp4",
            ContentHash = hash
        };

        _mockRepo.Setup(r => r.GetByContentHashAsync(hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existingWallpaper);

        var result = await _importService.ImportFileAsync(sourceFile);

        Assert.True(result.Success);
        Assert.True(result.IsDuplicate);
        Assert.Null(result.ImportedWallpaper);
        Assert.Equal(existingWallpaper.Id, result.ExistingWallpaper?.Id);

        // Must not insert a second record into database
        _mockRepo.Verify(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_FilenameCollision_DifferentContent_AssignsUniquePaths()
    {
        string sourceFile1 = CreateDummyFile("nature.mp4", "content_1");
        string sourceFile2 = CreateDummyFile("nature.mp4", "content_2");

        var result1 = await _importService.ImportFileAsync(sourceFile1);
        var result2 = await _importService.ImportFileAsync(sourceFile2);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.NotEqual(result1.ImportedWallpaper!.Id, result2.ImportedWallpaper!.Id);
        Assert.NotEqual(result1.ImportedWallpaper.FilePath, result2.ImportedWallpaper.FilePath);

        string absPath1 = _storageManager.ResolveAbsolutePath(result1.ImportedWallpaper.FilePath);
        string absPath2 = _storageManager.ResolveAbsolutePath(result2.ImportedWallpaper.FilePath);
        Assert.True(File.Exists(absPath1));
        Assert.True(File.Exists(absPath2));
    }

    [Fact]
    public async Task Import_InvalidExtension_FailsGracefullyWithoutBrokenDbRecord()
    {
        string sourceFile = CreateDummyFile("document.pdf", "pdf_content");
        var result = await _importService.ImportFileAsync(sourceFile);

        Assert.False(result.Success);
        Assert.Contains("Unsupported file extension", result.ErrorMessage);
        _mockRepo.Verify(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_MissingSourceFile_FailsGracefully()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N") + ".mp4");
        var result = await _importService.ImportFileAsync(missingPath);

        Assert.False(result.Success);
        Assert.Contains("Source file not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Import_FailureDuringCommit_CleansUpTempAndPartialStorage()
    {
        string sourceFile = CreateDummyFile("crash.mp4", "content_to_fail");

        _mockRepo.Setup(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Simulated SQLite constraint failure."));

        var result = await _importService.ImportFileAsync(sourceFile);

        Assert.False(result.Success);
        Assert.Contains("Simulated SQLite constraint failure", result.ErrorMessage);

        // Staging directory must be clean (no orphaned .tmp files left)
        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    [Fact]
    public async Task Import_Cancellation_AbortsAndCleansUp()
    {
        string sourceFile = CreateDummyFile("cancel.mp4", "large_cancel_test");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _importService.ImportFileAsync(sourceFile, cts.Token);
        });

        Assert.Empty(Directory.GetFiles(_storageManager.TempDirectory));
    }

    private string CreateDummyFile(string name, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumenSources_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        _tempFiles.Add(dir);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }

            foreach (var d in _tempFiles)
            {
                if (Directory.Exists(d))
                {
                    Directory.Delete(d, recursive: true);
                }
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
