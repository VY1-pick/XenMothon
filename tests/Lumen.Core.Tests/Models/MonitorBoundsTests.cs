using Lumen.Core.Models;
using Xunit;

namespace Lumen.Core.Tests.Models;

public class MonitorBoundsTests
{
    [Fact]
    public void Contains_InsidePoint_ShouldReturnTrue()
    {
        var bounds = new MonitorBounds(100, 100, 1920, 1080);

        Assert.True(bounds.Contains(500, 500));
        Assert.True(bounds.Contains(100, 100)); // Inclusive top-left
        Assert.False(bounds.Contains(2020, 1180)); // Exclusive bottom-right
        Assert.False(bounds.Contains(50, 50));
    }

    [Fact]
    public void Contains_NegativeVirtualCoordinates_ShouldWorkAccurately()
    {
        // Monitor positioned to the left of primary display
        var bounds = new MonitorBounds(-1920, 0, 1920, 1080);

        Assert.True(bounds.Contains(-1000, 500));
        Assert.True(bounds.Contains(-1920, 0));
        Assert.False(bounds.Contains(0, 0));
    }

    [Fact]
    public void IntersectsWith_OverlappingBounds_ShouldReturnTrue()
    {
        var monitorA = new MonitorBounds(0, 0, 1920, 1080);
        var window = new MonitorBounds(1000, 500, 1200, 800);

        Assert.True(monitorA.IntersectsWith(window));
    }

    [Fact]
    public void IntersectsWith_DisjointBounds_ShouldReturnFalse()
    {
        var monitorA = new MonitorBounds(0, 0, 1920, 1080);
        var monitorB = new MonitorBounds(1920, 0, 1920, 1080);

        Assert.False(monitorA.IntersectsWith(monitorB));
    }

    [Fact]
    public void Intersect_ComputesCorrectOverlapRegion()
    {
        var monitor = new MonitorBounds(0, 0, 1920, 1080);
        var window = new MonitorBounds(500, 500, 500, 500);

        var intersection = MonitorBounds.Intersect(monitor, window);

        Assert.Equal(500, intersection.X);
        Assert.Equal(500, intersection.Y);
        Assert.Equal(500, intersection.Width);
        Assert.Equal(500, intersection.Height);
        Assert.False(intersection.IsEmpty);
    }

    [Fact]
    public void Intersect_NoOverlap_ShouldReturnEmpty()
    {
        var a = new MonitorBounds(0, 0, 100, 100);
        var b = new MonitorBounds(200, 200, 100, 100);

        var intersection = MonitorBounds.Intersect(a, b);

        Assert.True(intersection.IsEmpty);
    }
}
