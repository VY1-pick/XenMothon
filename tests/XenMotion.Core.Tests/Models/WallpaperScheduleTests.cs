using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Models;

public class WallpaperScheduleTests
{
    private static WallpaperScheduleEntry Window(Guid id, string start, string end, string monitor = WallpaperSchedule.PrimaryMonitorId) => new()
    {
        WallpaperId = id,
        MonitorId = monitor,
        UseTimeWindow = true,
        StartMinutes = WallpaperSchedulePlanner.TryParseTime(start)!.Value,
        EndMinutes = WallpaperSchedulePlanner.TryParseTime(end)!.Value
    };

    private static WallpaperScheduleEntry Rotating(Guid id, string monitor = WallpaperSchedule.PrimaryMonitorId) => new()
    {
        WallpaperId = id,
        MonitorId = monitor
    };

    [Fact]
    public void DisabledScheduleSelectsNothing()
    {
        var schedule = new WallpaperSchedule { Enabled = false, RotationIntervalMinutes = 5, Entries = [Rotating(Guid.NewGuid())] };
        Assert.Null(WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 600, 0));
    }

    [Theory]
    [InlineData("07:00", "19:00", 8 * 60, true)]
    [InlineData("07:00", "19:00", 6 * 60, false)]
    [InlineData("07:00", "19:00", 19 * 60, false)]
    [InlineData("22:00", "06:00", 23 * 60, true)]
    [InlineData("22:00", "06:00", 2 * 60, true)]
    [InlineData("22:00", "06:00", 12 * 60, false)]
    public void TimeWindowsIncludeTheStartAndExcludeTheEnd(string start, string end, int minute, bool active)
    {
        var entry = Window(Guid.NewGuid(), start, end);
        Assert.Equal(active, WallpaperSchedulePlanner.IsWindowActive(entry, minute));
    }

    [Fact]
    public void EqualStartAndEndMeansAllDay()
    {
        var entry = Window(Guid.NewGuid(), "00:00", "00:00");
        Assert.True(WallpaperSchedulePlanner.IsWindowActive(entry, 0));
        Assert.True(WallpaperSchedulePlanner.IsWindowActive(entry, 1439));
    }

    [Fact]
    public void TimeWindowWinsOverRotation()
    {
        var windowId = Guid.NewGuid();
        var rotationId = Guid.NewGuid();
        var schedule = new WallpaperSchedule
        {
            Enabled = true,
            RotationIntervalMinutes = 30,
            Entries = [Rotating(rotationId), Window(windowId, "22:00", "06:00")]
        };

        var plan = WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 23 * 60, 0);
        Assert.NotNull(plan);
        Assert.Equal(windowId, plan!.WallpaperId);
        Assert.Equal(WallpaperSchedulePlanSource.TimeWindow, plan.Source);
    }

    [Fact]
    public void RotationCyclesThroughThePoolUsingTheCursor()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var schedule = new WallpaperSchedule
        {
            Enabled = true,
            RotationIntervalMinutes = 10,
            Entries = [Rotating(first), Rotating(second)]
        };

        Assert.Equal(first, WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 500, 0)!.WallpaperId);
        Assert.Equal(second, WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 500, 1)!.WallpaperId);
        Assert.Equal(first, WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 500, 2)!.WallpaperId);
        // Negative cursors wrap instead of throwing.
        Assert.Equal(second, WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 500, -1)!.WallpaperId);
    }

    [Fact]
    public void RotationWithoutAnIntervalStaysPut()
    {
        var schedule = new WallpaperSchedule { Enabled = true, RotationIntervalMinutes = 0, Entries = [Rotating(Guid.NewGuid())] };
        Assert.Null(WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 500, 0));
        Assert.Single(WallpaperSchedulePlanner.RotationPool(schedule, WallpaperSchedule.PrimaryMonitorId));
    }

    [Fact]
    public void MonitorsAreIsolated()
    {
        var primary = Guid.NewGuid();
        var secondary = Guid.NewGuid();
        var schedule = new WallpaperSchedule
        {
            Enabled = true,
            RotationIntervalMinutes = 5,
            Entries = [Rotating(primary), Rotating(secondary, @"\\.\DISPLAY2")]
        };

        Assert.Equal(primary, WallpaperSchedulePlanner.Select(schedule, WallpaperSchedule.PrimaryMonitorId, 100, 0)!.WallpaperId);
        Assert.Equal(secondary, WallpaperSchedulePlanner.Select(schedule, @"\\.\DISPLAY2", 100, 0)!.WallpaperId);
        Assert.Null(WallpaperSchedulePlanner.Select(schedule, @"\\.\DISPLAY3", 100, 0));
    }

    [Fact]
    public void NormalizeClampsAndDropsUnusableEntries()
    {
        var withoutMonitor = Rotating(Guid.NewGuid());
        withoutMonitor.MonitorId = "  ";
        var wrapped = Window(Guid.NewGuid(), "00:00", "12:00");
        wrapped.StartMinutes = 5000;
        wrapped.EndMinutes = -30;

        var schedule = new WallpaperSchedule
        {
            Enabled = true,
            RotationIntervalMinutes = 99999,
            Shuffle = true,
            Entries = [Rotating(Guid.Empty), withoutMonitor, wrapped]
        };

        WallpaperSchedulePlanner.Normalize(schedule);

        Assert.Equal(WallpaperSchedule.MinutesPerDay, schedule.RotationIntervalMinutes);
        Assert.Equal(2, schedule.Entries.Count);
        Assert.All(schedule.Entries, entry => Assert.Equal(WallpaperSchedule.PrimaryMonitorId, entry.MonitorId));
        Assert.Equal(5000 % WallpaperSchedule.MinutesPerDay, schedule.Entries[1].StartMinutes);
        Assert.Equal(WallpaperSchedule.MinutesPerDay - 30, schedule.Entries[1].EndMinutes);
    }

    [Theory]
    [InlineData("07:00", 420)]
    [InlineData("7:00", 420)]
    [InlineData("23:59", 1439)]
    [InlineData("24:00", null)]
    [InlineData("7:70", null)]
    [InlineData("noon", null)]
    [InlineData("", null)]
    public void TimeParsingRejectsInvalidInput(string text, int? expected)
        => Assert.Equal(expected, WallpaperSchedulePlanner.TryParseTime(text));

    [Fact]
    public void FormatRoundTripsThroughParse()
    {
        Assert.Equal("07:05", WallpaperSchedulePlanner.FormatTime(425));
        Assert.Equal(425, WallpaperSchedulePlanner.TryParseTime(WallpaperSchedulePlanner.FormatTime(425)));
    }
}
