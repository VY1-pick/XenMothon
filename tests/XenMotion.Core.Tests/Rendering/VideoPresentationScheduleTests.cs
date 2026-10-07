using XenMotion.UI.Rendering.Video;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class VideoPresentationScheduleTests
{
    [Fact]
    public void ThirtyFpsWithTimerJitterDoesNotLoseEveryOtherFrame()
    {
        var schedule = new VideoPresentationSchedule();
        for (int i = 0; i < 300; i++)
        {
            double timestamp = i * 1000.0 / 30;
            double jitter = i % 2 == 0 ? 12 : 1;
            Assert.True(schedule.ShouldPresent(timestamp, timestamp + jitter, 30, 30));
        }
    }
    [Theory]
    [InlineData(60, 24)]
    [InlineData(30, 24)]
    [InlineData(60, 30)]
    public void FractionalCadencePreservesRequestedFrameCount(int sourceFps, int cap)
    {
        var schedule = new VideoPresentationSchedule();
        int count = 0;
        for (int i = 0; i < sourceFps * 10; i++)
            if (schedule.ShouldPresent(i * 1000.0 / sourceFps, i * 1000.0 / sourceFps + i % 13, sourceFps, cap)) count++;
        Assert.Equal(cap * 10, count);
    }
    [Fact]
    public void StallSkipsObsoleteFramesAndLoopResetShowsFirstFrame()
    {
        var schedule = new VideoPresentationSchedule();
        Assert.True(schedule.ShouldPresent(0, 0, 30, 30));
        Assert.False(schedule.ShouldPresent(33.333, 250, 30, 30));
        Assert.True(schedule.ShouldPresent(233.333, 250, 30, 30));
        schedule.Reset();
        Assert.True(schedule.ShouldPresent(0, 0, 30, 30));
    }
}
