namespace XenMotion.Core.Models;

/// <summary>
/// User-defined daily wallpaper plan. Entries with a time window pin a wallpaper to a monitor inside
/// their local-time window; entries without a window form the rotation pool that the rotation
/// interval cycles through. The plan is per monitor and never touches the library itself.
/// </summary>
public sealed class WallpaperSchedule
{
    public const string PrimaryMonitorId = "PRIMARY_MONITOR";
    public const int MinutesPerDay = 24 * 60;

    public bool Enabled { get; set; }

    /// <summary>Minutes between automatic rotations. 0 disables rotation.</summary>
    public int RotationIntervalMinutes { get; set; }

    /// <summary>Reserved for a deterministic-but-varied rotation order; the planner stays deterministic.</summary>
    public bool Shuffle { get; set; }

    public List<WallpaperScheduleEntry> Entries { get; set; } = [];
}

/// <summary>
/// One wallpaper slot in the schedule. A windowed entry is active inside
/// <c>[StartMinutes, EndMinutes)</c> local time and may wrap past midnight.
/// </summary>
public sealed class WallpaperScheduleEntry
{
    public Guid WallpaperId { get; set; }
    public string MonitorId { get; set; } = WallpaperSchedule.PrimaryMonitorId;

    /// <summary>When true the entry only applies inside its daily window; otherwise it rotates.</summary>
    public bool UseTimeWindow { get; set; }

    /// <summary>Minutes from local midnight, inclusive.</summary>
    public int StartMinutes { get; set; }

    /// <summary>Minutes from local midnight, exclusive. Equal to StartMinutes means all day.</summary>
    public int EndMinutes { get; set; } = WallpaperSchedule.MinutesPerDay;
}

/// <summary>Why the planner chose a wallpaper; the UI reports it so the user can see the schedule working.</summary>
public enum WallpaperSchedulePlanSource
{
    TimeWindow,
    Rotation,
    Application
}

public sealed record WallpaperSchedulePlan(Guid WallpaperId, WallpaperSchedulePlanSource Source);

/// <summary>
/// Pure, deterministic schedule selection. Keeping it free of clocks and I/O makes the rotation and
/// window rules directly testable; the runtime service only supplies the current minute and cursor.
/// </summary>
public static class WallpaperSchedulePlanner
{
    /// <summary>True when the entry's daily window contains <paramref name="minuteOfDay"/>.</summary>
    public static bool IsWindowActive(WallpaperScheduleEntry entry, int minuteOfDay)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.UseTimeWindow) return false;

        int minute = NormalizeMinute(minuteOfDay);
        int start = NormalizeMinute(entry.StartMinutes);
        int end = NormalizeMinute(entry.EndMinutes);

        if (start == end) return true;
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;
    }

    /// <summary>Entries that rotate for a monitor: no time window, a real wallpaper and a matching monitor.</summary>
    public static IReadOnlyList<WallpaperScheduleEntry> RotationPool(WallpaperSchedule schedule, string monitorId)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return (schedule.Entries ?? [])
            .Where(entry => entry is not null && !entry.UseTimeWindow && entry.WallpaperId != Guid.Empty && IsForMonitor(entry, monitorId))
            .ToList();
    }

    /// <summary>
    /// Chooses the wallpaper for one monitor at a local minute. A matching time window always wins;
    /// otherwise the rotation pool is consulted when an interval is configured. Null means "leave the
    /// current wallpaper alone".
    /// </summary>
    public static WallpaperSchedulePlan? Select(WallpaperSchedule schedule, string monitorId, int minuteOfDay, int rotationIndex)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!schedule.Enabled) return null;

        foreach (var entry in (schedule.Entries ?? [])
                     .Where(entry => entry is not null && entry.UseTimeWindow && entry.WallpaperId != Guid.Empty && IsForMonitor(entry, monitorId)))
        {
            if (IsWindowActive(entry, minuteOfDay))
            {
                return new WallpaperSchedulePlan(entry.WallpaperId, WallpaperSchedulePlanSource.TimeWindow);
            }
        }

        var pool = RotationPool(schedule, monitorId);
        if (pool.Count > 0 && schedule.RotationIntervalMinutes > 0)
        {
            int index = ((rotationIndex % pool.Count) + pool.Count) % pool.Count;
            return new WallpaperSchedulePlan(pool[index].WallpaperId, WallpaperSchedulePlanSource.Rotation);
        }

        return null;
    }

    /// <summary>Clamps persisted values so a hand-edited settings file cannot break the timer.</summary>
    public static void Normalize(WallpaperSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        schedule.Entries ??= [];
        schedule.RotationIntervalMinutes = schedule.RotationIntervalMinutes <= 0
            ? 0
            : Math.Clamp(schedule.RotationIntervalMinutes, 1, WallpaperSchedule.MinutesPerDay);
        schedule.Entries.RemoveAll(entry => entry is null || entry.WallpaperId == Guid.Empty);
        foreach (var entry in schedule.Entries)
        {
            entry.MonitorId = string.IsNullOrWhiteSpace(entry.MonitorId) ? WallpaperSchedule.PrimaryMonitorId : entry.MonitorId.Trim();
            entry.StartMinutes = NormalizeMinute(entry.StartMinutes);
            entry.EndMinutes = NormalizeMinute(entry.EndMinutes);
            if (!entry.UseTimeWindow) continue;
        }

        if (schedule.RotationIntervalMinutes == 0) schedule.Shuffle = false;
    }

    private static int NormalizeMinute(int value)
    {
        int wrapped = value % WallpaperSchedule.MinutesPerDay;
        return wrapped < 0 ? wrapped + WallpaperSchedule.MinutesPerDay : wrapped;
    }

    private static bool IsForMonitor(WallpaperScheduleEntry entry, string monitorId)
        => entry.MonitorId is { Length: > 0 }
            ? string.Equals(entry.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(monitorId, WallpaperSchedule.PrimaryMonitorId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Formats a minute-of-day as the local hh:mm text used by the settings UI.</summary>
    public static string FormatTime(int minuteOfDay)
    {
        int minute = NormalizeMinute(minuteOfDay);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>Parses "hh:mm" (or "H:mm"); returns null when the text is not a valid time of day.</summary>
    public static int? TryParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out int hours) || !int.TryParse(parts[1], out int minutes)) return null;
        if (hours is < 0 or > 23 || minutes is < 0 or > 59) return null;
        return hours * 60 + minutes;
    }
}
