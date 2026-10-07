namespace XenMotion.Application.Services;

/// <summary>
/// Security utility for sanitizing user-provided filenames and preventing directory traversal vulnerabilities.
/// </summary>
public static class PathSanitizer
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Sanitizes an input filename by stripping path components, removing invalid characters,
    /// avoiding Windows device names, and preventing path traversal attacks.
    /// </summary>
    public static string SanitizeFileName(string? rawFileName, string defaultExtension = ".bin")
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return $"wallpaper_{Guid.NewGuid():N}{defaultExtension}";
        }

        // 1. Extract purely the filename component (strips any leading paths/directories)
        string name = Path.GetFileName(rawFileName);

        // 2. Remove all invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }

        // 3. Remove relative path navigation fragments
        name = name.Replace("..", "_").Replace('/', '_').Replace('\\', '_').Trim();

        // 4. Strip trailing dots or spaces which are problematic on Windows
        name = name.TrimEnd('.', ' ');

        string baseName = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"wallpaper_{Guid.NewGuid():N}";
        }

        // 5. Prevent Windows reserved device names (CON, NUL, AUX, etc.)
        if (ReservedDeviceNames.Contains(baseName))
        {
            baseName = $"wp_{baseName}";
        }

        if (string.IsNullOrEmpty(extension))
        {
            extension = defaultExtension;
        }

        return $"{baseName}{extension}";
    }

    /// <summary>
    /// Validates that a resolved path is strictly contained within the designated root directory,
    /// preventing directory traversal outside the storage boundary.
    /// </summary>
    public static bool IsPathWithinRoot(string path, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(rootDirectory);

        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
