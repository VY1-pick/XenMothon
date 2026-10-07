using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class SavedWallpaperServiceTests
{
    [Fact]
    public async Task SelectionPersistsAndRestoresAcrossEngineRestart()
    {
        string file = Path.GetTempFileName();
        try
        {
            var config = new ApplicationSettings { GlobalTargetFps = 24, DefaultMuted = true };
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(x => x.CurrentSettings).Returns(config);
            settings.Setup(x => x.UpdateSettingsAsync(It.IsAny<Action<ApplicationSettings>>(), It.IsAny<CancellationToken>()))
                .Returns<Action<ApplicationSettings>, CancellationToken>((update, _) => { update(config); return Task.CompletedTask; });
            var wallpaper = new Wallpaper { Id = Guid.NewGuid(), Title = "Saved", Type = WallpaperType.Video, FilePath = file, ContentHash = "test" };
            var library = new Mock<ILibraryService>();
            library.Setup(x => x.GetWallpaperByIdAsync(wallpaper.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallpaper);
            var storage = new Mock<IStorageManager>();
            storage.Setup(x => x.ResolveAbsolutePath(file)).Returns(file);
            var renderer = new Mock<IWallpaperRenderer>();
            await using (var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => renderer.Object))
            {
                var service = new SavedWallpaperService(engine, settings.Object, library.Object, storage.Object, NullLogger<SavedWallpaperService>.Instance);
                await service.ApplyAsync("PRIMARY_MONITOR", wallpaper);
                Assert.Equal(wallpaper.Id, config.MonitorWallpaperAssignments["PRIMARY_MONITOR"]);
            }
            var restoredRenderer = new Mock<IWallpaperRenderer>();
            await using var restarted = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => restoredRenderer.Object);
            var restoredService = new SavedWallpaperService(restarted, settings.Object, library.Object, storage.Object, NullLogger<SavedWallpaperService>.Instance);
            Assert.Equal(wallpaper.Id, (await restoredService.RestorePrimaryAsync())?.Id);
            restoredRenderer.Verify(x => x.LoadAsync(file, It.Is<WallpaperSettings>(s => s.TargetFps == 24 && s.IsMuted), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(WallpaperState.Playing, restarted.GetMonitorState("PRIMARY_MONITOR"));
            await restoredService.ClearAsync();
            Assert.Null(await restoredService.RestorePrimaryAsync());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task FailedSelectionDoesNotReplaceSavedWallpaper()
    {
        var config = new ApplicationSettings();
        Guid previous = Guid.NewGuid();
        config.MonitorWallpaperAssignments["PRIMARY_MONITOR"] = previous;
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(x => x.CurrentSettings).Returns(config);
        var renderer = new Mock<IWallpaperRenderer>();
        renderer.Setup(x => x.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("decode failed"));
        await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => renderer.Object);
        var service = new SavedWallpaperService(engine, settings.Object, Mock.Of<ILibraryService>(), Mock.Of<IStorageManager>(), NullLogger<SavedWallpaperService>.Instance);
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync("PRIMARY_MONITOR", new Wallpaper { Id = Guid.NewGuid(), Type = WallpaperType.Video, FilePath = "bad.mp4", Title = "Bad", ContentHash = "test" }));
        Assert.Equal(previous, config.MonitorWallpaperAssignments["PRIMARY_MONITOR"]);
    }
}
