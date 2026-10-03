using Lumen.Core.Enums;

namespace Lumen.Core.Models.Catalog;

/// <summary>
/// Domain model representing a discoverable wallpaper item in the catalog.
/// Distinct from a local <see cref="Wallpaper"/> entity, which represents content installed on disk.
/// </summary>
public sealed class CatalogItem
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Author { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string PreviewUrl { get; init; }
    /// <summary>Absolute or API-relative URL used for subscription/download operations.</summary>
    public string DownloadUrl { get; init; } = string.Empty;
    public required WallpaperType Type { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public long FileSizeBytes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public double Rating { get; init; } = 5.0;
    public int RatingCount { get; init; } = 0;
    public int DownloadsCount { get; init; } = 0;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsFeatured { get; init; } = false;

    public string ResolutionText
    {
        get
        {
            if (Width <= 0 || Height <= 0) return string.Empty;
            if (Height >= 2160) return $"{Width}x{Height} (4K UHD)";
            if (Height >= 1440) return $"{Width}x{Height} (QHD)";
            if (Height >= 1080) return $"{Width}x{Height} (FHD)";
            return $"{Width}x{Height}";
        }
    }

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes <= 0) return string.Empty;
            double mb = FileSizeBytes / (1024.0 * 1024.0);
            return mb < 1.0 ? $"{FileSizeBytes / 1024.0:F0} KB" : $"{mb:F1} MB";
        }
    }

    public string DurationText
    {
        get
        {
            if (Type != WallpaperType.Video || Duration <= TimeSpan.Zero)
            {
                return string.Empty;
            }
            return $"{Duration.Minutes:D2}:{Duration.Seconds:D2}";
        }
    }

    public CatalogOrientationFilter Orientation
    {
        get
        {
            if (Width <= 0 || Height <= 0) return CatalogOrientationFilter.All;
            double aspect = (double)Width / Height;
            if (aspect >= 2.0) return CatalogOrientationFilter.Ultrawide;
            if (Width >= Height) return CatalogOrientationFilter.Landscape;
            return CatalogOrientationFilter.Portrait;
        }
    }

    public CatalogResolutionFilter ResolutionCategory
    {
        get
        {
            if (Height >= 2160) return CatalogResolutionFilter.UHD_4K;
            if (Height >= 1440) return CatalogResolutionFilter.QHD_1440p;
            if (Height >= 1080) return CatalogResolutionFilter.FHD_1080p;
            return CatalogResolutionFilter.All;
        }
    }
}
