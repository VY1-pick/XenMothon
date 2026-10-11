using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;

namespace XenMotion.Server;

public sealed class CatalogManifest
{
    public List<CatalogCategory> Categories { get; init; } = [];
    public List<CatalogEntry> Items { get; init; } = [];
}

public sealed class CatalogEntry
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public WallpaperType Type { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DurationSeconds { get; init; }
    public string CategoryId { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public bool IsFeatured { get; init; }
    public double Rating { get; init; }
    public int RatingCount { get; init; }
    public int DownloadsCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string MediaPath { get; init; } = "";
    public string ThumbnailPath { get; init; } = "";
    public string PreviewPath { get; init; } = "";
}
