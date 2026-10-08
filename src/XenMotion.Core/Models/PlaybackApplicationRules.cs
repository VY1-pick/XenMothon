namespace XenMotion.Core.Models;

public static class PlaybackApplicationRules
{
    public static bool Matches(string? rules, string processName) =>
        (rules ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(rule => string.Equals(Path.GetFileNameWithoutExtension(rule), processName, StringComparison.OrdinalIgnoreCase));
}
