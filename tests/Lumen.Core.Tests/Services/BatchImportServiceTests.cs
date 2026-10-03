using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.Infrastructure.Persistence.Services;
using Lumen.Infrastructure.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lumen.Core.Tests.Services;

public class BatchImportServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalStorageManager _storageManager;
    private readonly Mock<IWallpaperRepository> _mockRepo;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly WallpaperImportService _importService;
    private readonly List<string> _tempFiles = [];

    public BatchImportServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LumenBatchTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task ImportFiles_MultipleValidFiles_ImportsAllAndReportsProgress()
    {
        string f1 = CreateDummyFile("vid1.mp4", "content1");
        string f2 = CreateDummyFile("vid2.webm", "content2");
        string f3 = CreateDummyFile("img1.png", "content3");

        var progressList = new List<ImportProgress>();
        var progressMock = new Progress<ImportProgress>(p => progressList.Add(p));

        var results = await _importService.ImportFilesAsync([f1, f2, f3], progressMock);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        _mockRepo.Verify(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ImportFiles_PartialFailure_ContinuesRemainingImports()
    {
        string fValid1 = CreateDummyFile("valid1.mp4", "valid_one");
        string fInvalid = CreateDummyFile("broken.badext", "bad_content");
        string fValid2 = CreateDummyFile("valid2.png", "valid_two");

        var results = await _importService.ImportFilesAsync([fValid1, fInvalid, fValid2]);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.True(results[2].Success);

        // Valid ones should be committed
        _mockRepo.Verify(r => r.InsertAsync(It.IsAny<Wallpaper>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private string CreateDummyFile(string name, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumenBatchSources_" + Guid.NewGuid().ToString("N"));
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
        }
    }
}
