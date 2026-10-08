using System.Security.Cryptography;
using XenMotion.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace XenMotion.Core.Tests.Storage;

public class LocalStorageManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;

    public LocalStorageManagerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "XenMotionStorageTests_" + Guid.NewGuid().ToString("N"));
        _storageManager = new LocalStorageManager(_testRoot, NullLogger<LocalStorageManager>.Instance);
    }

    [Fact]
    public async Task MigrationRejectsNestedDestinationWithoutDeletingSource()
    {
        var source = Path.Combine(_storageManager.MediaDirectory, "keep.txt");
        await File.WriteAllTextAsync(source, "keep");
        Assert.False(await _storageManager.MigrateStorageRootAsync(Path.Combine(_storageManager.MediaDirectory, "nested")));
        Assert.Equal("keep", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public void RelativePathCannotEscapeManagedLibrary()
    {
        Assert.Throws<UnauthorizedAccessException>(()=>_storageManager.ResolveAbsolutePath("../escape.mp4"));
        Assert.StartsWith(_testRoot,_storageManager.ResolveAbsolutePath("Media/video.mp4"));
    }

    [Fact]
    public async Task Storage_StreamingSha256_CalculatesAccurately()
    {
        string filePath = Path.Combine(_testRoot, "test_sha.dat");
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        await File.WriteAllBytesAsync(filePath, data);

        string computed = await _storageManager.CalculateSha256HashAsync(filePath);
        string expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        Assert.Equal(expected, computed);
    }

    [Fact]
    public async Task Storage_StorageUsage_CalculatesSizesAndFreeSpace()
    {
        string mediaFile = Path.Combine(_storageManager.MediaDirectory, "dummy_media.bin");
        await File.WriteAllBytesAsync(mediaFile, new byte[1024 * 50]); // 50 KB

        string thumbFile = Path.Combine(_storageManager.ThumbnailsDirectory, "dummy_thumb.jpg");
        await File.WriteAllBytesAsync(thumbFile, new byte[1024 * 10]); // 10 KB

        var usage = await _storageManager.GetStorageUsageAsync(1);

        Assert.Equal(1, usage.WallpaperCount);
        Assert.Equal(1024 * 50, usage.MediaSizeBytes);
        Assert.Equal(1024 * 10, usage.ThumbnailSizeBytes);
        Assert.Equal(1024 * 60, usage.TotalSizeBytes);
    }

    [Fact]
    public async Task Storage_MigrateStorageRoot_CopiesAllFilesAndUpdatesRoot()
    {
        Guid id = Guid.NewGuid();
        string wpDir = _storageManager.GetWallpaperDirectory(id);
        Directory.CreateDirectory(wpDir);
        string file = Path.Combine(wpDir, "test.mp4");
        await File.WriteAllTextAsync(file, "migration_test_data");

        string thumb = Path.Combine(_storageManager.ThumbnailsDirectory, $"{id:D}.jpg");
        await File.WriteAllTextAsync(thumb, "thumb_test_data");

        string newRoot = Path.Combine(Path.GetTempPath(), "XenMotionNewRoot_" + Guid.NewGuid().ToString("N"));

        try
        {
            bool success = await _storageManager.MigrateStorageRootAsync(newRoot);
            Assert.True(success);
            Assert.Equal(Path.GetFullPath(newRoot), _storageManager.RootStoragePath);

            string migratedMedia = Path.Combine(newRoot, "Media", id.ToString(), "test.mp4");
            string migratedThumb = Path.Combine(newRoot, "Thumbnails", $"{id:D}.jpg");

            Assert.True(File.Exists(migratedMedia));
            Assert.True(File.Exists(migratedThumb));
            Assert.Equal("migration_test_data", await File.ReadAllTextAsync(migratedMedia));
        }
        finally
        {
            if (Directory.Exists(newRoot))
            {
                Directory.Delete(newRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Storage_RelativePath_ConvertsBothWays()
    {
        string fullMedia = Path.Combine(_storageManager.MediaDirectory, "abc", "video.mp4");
        string relative = _storageManager.MakeStorageRelativePath(fullMedia);

        Assert.StartsWith("Media/", relative);
        Assert.EndsWith("video.mp4", relative);

        string resolved = _storageManager.ResolveAbsolutePath(relative);
        Assert.Equal(Path.GetFullPath(fullMedia), resolved);
    }

    [Fact]
    public async Task Storage_CleanTempFiles_DeletesOldFiles()
    {
        string oldTemp = Path.Combine(_storageManager.TempDirectory, "old.tmp");
        string newTemp = Path.Combine(_storageManager.TempDirectory, "new.tmp");

        await File.WriteAllTextAsync(oldTemp, "old");
        await File.WriteAllTextAsync(newTemp, "new");

        // Backdate old file creation time
        File.SetCreationTimeUtc(oldTemp, DateTime.UtcNow.AddHours(-3));

        await _storageManager.CleanTempFilesAsync(TimeSpan.FromHours(1));

        Assert.False(File.Exists(oldTemp));
        Assert.True(File.Exists(newTemp));
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
