namespace XenMotion.Application.Services;

/// <summary>
/// Validates video wallpaper media files and container formats.
/// Supports MP4, WebM, WMV, and AVI containers, gracefully rejecting unsupported codecs/formats.
/// </summary>
public static class MediaFormatValidator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".webm",
        ".wmv",
        ".asf",
        ".avi",
        ".mov"
    };

    /// <summary>
    /// Checks whether the file extension belongs to a supported video container format.
    /// </summary>
    public static bool IsSupportedFormat(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Validates that the file path is non-empty, has a supported format, and exists on disk.
    /// Throws descriptive exceptions on validation failure.
    /// </summary>
    public static void Validate(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext) || !SupportedExtensions.Contains(ext))
        {
            throw new NotSupportedException(
                $"Media format '{ext}' is not supported. Supported video formats: MP4, WebM, WMV, AVI.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Media file not found at '{filePath}'.", filePath);
        }
    }
}
