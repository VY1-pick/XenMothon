using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class PlaybackCalibrationTests
{
    [Fact]
    public void BudgetAccountsForAllDecodedFramesAndNeverRaisesUserLimits()
    {
        Assert.False(PlaybackCalibration.FitsBudget(6, .1, 60, 15));
        Assert.True(PlaybackCalibration.FitsBudget(.5, 2, 30, 15));
        var economy = new WallpaperSettings { VideoHeightLimit = 720, TargetFps = 15, RenderScale = .5f, Sharpness = .15f };
        var lower = PlaybackCalibration.LowerBudget(economy);
        Assert.Equal(720, lower.VideoHeightLimit);
        Assert.Equal(15, lower.TargetFps);
        Assert.Equal(.5f, lower.RenderScale);
        Assert.Equal(.15f, lower.Sharpness);
        var constrained = PlaybackCalibration.LowerBudget(new WallpaperSettings { VideoHeightLimit = 1080, TargetFps = 20, RenderScale = .4f });
        Assert.True(constrained.VideoHeightLimit <= 1080 && constrained.TargetFps <= 20 && constrained.RenderScale <= .4f);
        Assert.Equal(9, PlaybackCalibration.Percentile90(Enumerable.Range(1, 10).Select(value => (double)value)));
        Assert.False(PlaybackCalibration.FitsBudget(PlaybackCalibration.Percentile90([]), 1, 30, 30));
    }
}
