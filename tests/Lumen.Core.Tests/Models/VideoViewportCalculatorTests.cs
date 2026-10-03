using Lumen.Core.Enums;
using Lumen.Core.Models;
using Xunit;

namespace Lumen.Core.Tests.Models;

public class VideoViewportCalculatorTests
{
    [Fact]
    public void Calculate_Stretch_MatchesTargetBoundsExactly()
    {
        var bounds = new MonitorBounds(1920, 0, 1920, 1080);
        var vp = VideoViewportCalculator.Calculate(1280, 720, bounds, PlaybackScalingMode.Stretch);

        Assert.Equal(1920, vp.X);
        Assert.Equal(0, vp.Y);
        Assert.Equal(1920, vp.Width);
        Assert.Equal(1080, vp.Height);
    }

    [Fact]
    public void Calculate_Fit_Letterbox_WhenSourceTallerThanTarget()
    {
        // Target is 16:9 (1920x1080), Source is 4:3 (1440x1080) -> Pillarbox (bars on left/right)
        var bounds = new MonitorBounds(0, 0, 1920, 1080);
        var vp = VideoViewportCalculator.Calculate(1440, 1080, bounds, PlaybackScalingMode.Fit);

        Assert.Equal(1080, vp.Height);
        Assert.Equal(1440, vp.Width);
        Assert.Equal(240, vp.X); // (1920 - 1440) / 2
        Assert.Equal(0, vp.Y);
    }

    [Fact]
    public void Calculate_Fit_Letterbox_WhenSourceWiderThanTarget()
    {
        // Target is 16:10 (1920x1200), Source is 16:9 (1920x1080) -> Letterbox (bars on top/bottom)
        var bounds = new MonitorBounds(0, 0, 1920, 1200);
        var vp = VideoViewportCalculator.Calculate(1920, 1080, bounds, PlaybackScalingMode.Fit);

        Assert.Equal(1920, vp.Width);
        Assert.Equal(1080, vp.Height);
        Assert.Equal(0, vp.X);
        Assert.Equal(60, vp.Y); // (1200 - 1080) / 2
    }

    [Fact]
    public void Calculate_Fill_PreservesAspect_CoversTargetCompletely()
    {
        // Target is 16:10 (1920x1200), Source is 16:9 (1920x1080) -> Fill scales height to 1200, width to ~2133 (cropped)
        var bounds = new MonitorBounds(0, 0, 1920, 1200);
        var vp = VideoViewportCalculator.Calculate(1920, 1080, bounds, PlaybackScalingMode.Fill);

        Assert.Equal(1200, vp.Height);
        Assert.True(vp.Width >= 1920);
        Assert.True(vp.X <= 0); // centered crop
    }

    [Fact]
    public void Calculate_Center_MaintainsNative1to1Resolution()
    {
        var bounds = new MonitorBounds(0, 0, 1920, 1080);
        var vp = VideoViewportCalculator.Calculate(800, 600, bounds, PlaybackScalingMode.Center);

        Assert.Equal(800, vp.Width);
        Assert.Equal(600, vp.Height);
        Assert.Equal((1920 - 800) / 2, vp.X);
        Assert.Equal((1080 - 600) / 2, vp.Y);
    }

    [Fact]
    public void Calculate_Span_MultiMonitor_OffsetsCoordinatesCorrectly()
    {
        // Two 1920x1080 monitors side by side: Virtual Desktop = 3840x1080
        var virtualDesktop = new MonitorBounds(0, 0, 3840, 1080);
        var monitor2Bounds = new MonitorBounds(1920, 0, 1920, 1080);

        // Source video is 3840x1080 (32:9 dual monitor video)
        var vp = VideoViewportCalculator.Calculate(3840, 1080, monitor2Bounds, PlaybackScalingMode.Span, virtualDesktop);

        // Offset inside Monitor 2 should be -1920 relative to monitor 2 origin, spanning full 3840 width
        Assert.Equal(-1920, vp.X);
        Assert.Equal(0, vp.Y);
        Assert.Equal(3840, vp.Width);
        Assert.Equal(1080, vp.Height);
    }

    [Fact]
    public void Calculate_InvalidDimensions_ReturnsSafeFallback()
    {
        var bounds = new MonitorBounds(0, 0, 1920, 1080);
        var vp = VideoViewportCalculator.Calculate(0, 0, bounds, PlaybackScalingMode.Fill);

        Assert.Equal(0, vp.X);
        Assert.Equal(0, vp.Y);
        Assert.Equal(1920, vp.Width);
        Assert.Equal(1080, vp.Height);

        var emptyBounds = new MonitorBounds(0, 0, 0, 0);
        var vpEmpty = VideoViewportCalculator.Calculate(1920, 1080, emptyBounds, PlaybackScalingMode.Fill);
        Assert.True(vpEmpty.IsEmpty);
    }
}
