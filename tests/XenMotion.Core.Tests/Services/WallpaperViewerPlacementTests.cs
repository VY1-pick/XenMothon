using XenMotion.UI.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class WallpaperViewerPlacementTests
{
    [Theory]
    [InlineData(100, 100, 1000, 700, 0, 0, 1920, 1080, 1100, 100, 700)]
    [InlineData(700, 100, 1000, 700, 0, 0, 1920, 1080, 320, 100, 700)]
    [InlineData(-1800, 100, 1000, 700, -1920, 0, 1920, 1080, -800, 100, 700)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 0, 0, 1040)]
    public void FitsBesideMainWindowOrStaysOnItsMonitor(int x, int y, int width, int height,
        int workX, int workY, int workWidth, int workHeight, int expectedX, int expectedY, int expectedHeight)
    {
        var bounds = WallpaperViewerPlacement.Beside(x, y, width, height, 380, workX, workY, workWidth, workHeight);
        Assert.Equal((expectedX, expectedY, expectedHeight), (bounds.X, bounds.Y, bounds.Height));
        Assert.Equal(x, bounds.MainX);
    }

    [Fact]
    public void FitsBothWindowsByMovingMainWithoutResizingIt()
    {
        var bounds = WallpaperViewerPlacement.Beside(267, 100, 1426, 813, 380, 0, 0, 1920, 1080);
        Assert.Equal(114, bounds.MainX);
        Assert.Equal(bounds.MainX + 1426, bounds.X);
        Assert.Equal(813, bounds.Height);
    }
}
