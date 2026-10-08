using XenMotion.Core.Models;
using XenMotion.UI.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class WallpaperEditAutosaveTests
{
    [Fact]
    public void SavesLatestChangeAfterFiveQuietSeconds_AndOnlyOnce()
    {
        var save = new WallpaperEditAutosave();
        var id = Guid.NewGuid();
        save.Schedule(id, new WallpaperEdits { Contrast = .5f });
        Assert.Null(save.Tick());
        var latest = new WallpaperEdits { Contrast = 1.5f };
        save.Schedule(id, latest);
        Assert.Equal(5, save.RemainingSeconds);
        for (int i = 0; i < 4; i++) Assert.Null(save.Tick());
        var result = save.Tick();
        Assert.Equal(id, result!.Value.Id);
        Assert.Same(latest, result.Value.Edits);
        Assert.Equal(0, save.RemainingSeconds);
        Assert.Null(save.Tick());
    }

    [Fact]
    public void ClosingOrSwitchingFlushesOriginalWallpaperSnapshot()
    {
        var save = new WallpaperEditAutosave();
        var originalId = Guid.NewGuid();
        save.Schedule(originalId, new WallpaperEdits { Opacity = .6f });
        var pending = save.Take();
        save.Schedule(Guid.NewGuid(), new WallpaperEdits());
        Assert.Equal(originalId, pending!.Value.Id);
        Assert.Equal(.6f, pending.Value.Edits.Opacity);
        Assert.Equal(5, save.RemainingSeconds);
    }
}
