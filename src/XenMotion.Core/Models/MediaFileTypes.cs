using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

/// <summary>
/// Single source of truth for the media extensions, content types and file names used by the local
/// import pipeline, the catalog download pipeline and link imports. Keeping the sets in one place
/// stops the three paths from drifting apart.
/// </summary>
public static class MediaFileTypes
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm", ".wmv", ".asf", ".avi", ".mov"
    };

    public static bool IsSupportedImage(string? extension) => !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension);
    public static bool IsSupportedVideo(string? extension) => !string.IsNullOrEmpty(extension) && VideoExtensions.Contains(extension);
    public static bool IsSupported(string? extension) => IsSupportedImage(extension) || IsSupportedVideo(extension);

    /// <summary>Wallpaper type for a supported extension, or null when the extension is not supported.</summary>
    public static WallpaperType? ResolveType(string? extension)
    {
        if (IsSupportedVideo(extension)) return WallpaperType.Video;
        if (IsSupportedImage(extension)) return WallpaperType.Image;
        return null;
    }

    /// <summary>Maps a download response's media type onto a supported extension.</summary>
    public static string? ExtensionForContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            contentType = parsed.MediaType;
        }

        return (contentType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" => ".png",
            "image/bmp" or "image/x-ms-bmp" => ".bmp",
            "image/webp" => ".webp",
            "video/mp4" or "video/x-m4v" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-msvideo" or "video/avi" => ".avi",
            "video/x-ms-wmv" or "video/x-ms-asf" => ".wmv",
            _ => null
        };
    }

    /// <summary>
    /// Extension to store a downloaded file under: the URL's own supported extension wins so an
    /// image keeps its real container, then the response content type, then a type-based default.
    /// </summary>
    public static string ResolveDownloadExtension(string? url, string? contentType, WallpaperType type)
    {
        string? fromUrl = ExtensionFromUrl(url);
        if (IsSupported(fromUrl)) return fromUrl!.ToLowerInvariant();

        string? fromContentType = ExtensionForContentType(contentType);
        if (fromContentType is not null) return fromContentType;

        return type == WallpaperType.Video ? ".mp4" : ".png";
    }

    /// <summary>Extension of the last path segment of an absolute or relative URL, if any.</summary>
    public static string? ExtensionFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;
        else
        {
            int query = path.IndexOf('?');
            if (query >= 0) path = path[..query];
            int hash = path.IndexOf('#');
            if (hash >= 0) path = path[..hash];
        }

        try { return Path.GetExtension(path); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// Last path segment of a URL used to name a downloaded file. Returns an empty string when the
    /// URL has no usable segment.
    /// </summary>
    public static string FileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        string path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;

        try
        {
            string decoded = Uri.UnescapeDataString(path);
            return Path.GetFileName(decoded).Trim();
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return string.Empty;
        }
    }
}
