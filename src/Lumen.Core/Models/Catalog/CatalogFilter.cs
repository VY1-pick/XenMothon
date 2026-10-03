using Lumen.Core.Enums;

namespace Lumen.Core.Models.Catalog;

/// <summary>
/// Criteria for filtering catalog wallpapers by type, category, resolution, orientation, duration, and tags.
/// </summary>
public sealed record CatalogFilter
{
    public WallpaperType? Type { get; init; }
    public string? CategoryId { get; init; }
    public CatalogResolutionFilter Resolution { get; init; } = CatalogResolutionFilter.All;
    public CatalogOrientationFilter Orientation { get; init; } = CatalogOrientationFilter.All;
    public TimeSpan? MaxDuration { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public double? MinRating { get; init; }
}
