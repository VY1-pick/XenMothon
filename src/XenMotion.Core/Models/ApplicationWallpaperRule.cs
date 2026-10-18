namespace XenMotion.Core.Models;

/// <summary>
/// Pins a wallpaper to a monitor while one application is in the foreground. Rules never modify the
/// saved startup wallpaper; the switcher restores the base assignment when the rule stops matching.
/// </summary>
public sealed class ApplicationWallpaperRule
{
    /// <summary>Executable name with or without the .exe suffix, for example "code" or "chrome.exe".</summary>
    public string ProcessName { get; set; } = string.Empty;
    public Guid WallpaperId { get; set; }
    public string MonitorId { get; set; } = WallpaperSchedule.PrimaryMonitorId;
}

/// <summary>Pure matching helper shared by the runtime switcher and its tests.</summary>
public static class ApplicationWallpaperRules
{
    /// <summary>Strips directories and the executable suffix so "C:\x\Code.exe" matches "code".</summary>
    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
        string name = processName.Trim();
        int separator = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (separator >= 0) name = name[(separator + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Trim();
    }

    /// <summary>Returns the wallpaper for the first rule that matches the foreground process and monitor.</summary>
    public static Guid? Select(IReadOnlyList<ApplicationWallpaperRule>? rules, string? processName, string monitorId)
    {
        string name = NormalizeProcessName(processName);
        if (name.Length == 0 || rules is null || rules.Count == 0) return null;

        foreach (var rule in rules)
        {
            if (rule is null || rule.WallpaperId == Guid.Empty) continue;
            if (!string.Equals(NormalizeProcessName(rule.ProcessName), name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!MatchesMonitor(rule.MonitorId, monitorId)) continue;
            return rule.WallpaperId;
        }

        return null;
    }

    /// <summary>Removes empty/duplicate rules and rejects entries that cannot ever match.</summary>
    public static void NormalizeRules(List<ApplicationWallpaperRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.RemoveAll(rule => rule is null || rule.WallpaperId == Guid.Empty || NormalizeProcessName(rule.ProcessName).Length == 0);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        rules.RemoveAll(rule =>
        {
            rule.ProcessName = NormalizeProcessName(rule.ProcessName);
            rule.MonitorId = string.IsNullOrWhiteSpace(rule.MonitorId) ? WallpaperSchedule.PrimaryMonitorId : rule.MonitorId.Trim();
            return !seen.Add($"{rule.ProcessName}|{rule.MonitorId}");
        });
    }

    private static bool MatchesMonitor(string? ruleMonitor, string monitorId)
        => string.IsNullOrWhiteSpace(ruleMonitor)
            ? string.Equals(monitorId, WallpaperSchedule.PrimaryMonitorId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(ruleMonitor.Trim(), monitorId, StringComparison.OrdinalIgnoreCase);
}
