using System.Globalization;

namespace XenMotion.Core.Models.Updates;

/// <summary>
/// Semver-lite release version used by the update check. Numeric components compare first and a
/// pre-release label (for example <c>-beta.3</c>) always sorts below its final release, which is what
/// lets a private beta notice that beta.4 supersedes beta.3.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, IReadOnlyList<string> preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> PreRelease { get; }
    public bool IsPreRelease => PreRelease.Count > 0;

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (value.Length == 0) return false;

        string? preReleaseText = null;
        int dash = value.IndexOf('-');
        if (dash >= 0)
        {
            preReleaseText = value[(dash + 1)..];
            value = value[..dash];
        }

        // Ignore build metadata, which never affects ordering.
        int plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];
        if (preReleaseText is not null)
        {
            int prePlus = preReleaseText.IndexOf('+');
            if (prePlus >= 0) preReleaseText = preReleaseText[..prePlus];
        }

        var parts = value.Split('.');
        if (parts.Length is 0 or > 4) return false;

        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return false;
            if (i < numbers.Length) numbers[i] = number;
        }

        var preRelease = string.IsNullOrWhiteSpace(preReleaseText)
            ? Array.Empty<string>()
            : preReleaseText.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (preReleaseText is not null && preRelease.Length == 0) return false;

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;

        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (PreRelease.Count == 0 && other.PreRelease.Count == 0) return 0;
        if (PreRelease.Count == 0) return 1;
        if (other.PreRelease.Count == 0) return -1;

        int length = Math.Max(PreRelease.Count, other.PreRelease.Count);
        for (int i = 0; i < length; i++)
        {
            if (i >= PreRelease.Count) return -1;
            if (i >= other.PreRelease.Count) return 1;

            string left = PreRelease[i];
            string right = other.PreRelease[i];
            bool leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
            bool rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);

            result = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric ? -1
                : rightNumeric ? 1
                : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
        }

        return 0;
    }

    /// <summary>Compares two version strings; unparseable text falls back to ordinal comparison.</summary>
    public static int Compare(string? left, string? right)
    {
        bool hasLeft = TryParse(left, out var a);
        bool hasRight = TryParse(right, out var b);
        if (hasLeft && hasRight) return a!.CompareTo(b);
        if (hasLeft) return 1;
        if (hasRight) return -1;
        return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease).ToLowerInvariant());

    public override string ToString()
    {
        string value = $"{Major}.{Minor}.{Patch}";
        return PreRelease.Count == 0 ? value : $"{value}-{string.Join('.', PreRelease)}";
    }
}
