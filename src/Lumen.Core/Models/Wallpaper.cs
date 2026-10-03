using Lumen.Core.Enums;

namespace Lumen.Core.Models;

/// <summary>
/// Domain model representing a managed wallpaper asset with validated metadata.
/// </summary>
public sealed class Wallpaper
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public WallpaperType Type { get; init; }
    public required string FilePath { get; set; }
    public string ThumbnailPath { get; set; } = string.Empty;
    public string PreviewPath { get; set; } = string.Empty;
    public required string ContentHash { get; set; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public long DurationMs { get; init; }
    public long FileSizeBytes { get; init; }
    public string Creator { get; set; } = "Local";
    public string Category { get; set; } = "General";
    public List<string> Tags { get; init; } = [];
    public bool IsFavorite { get; set; }
    public bool IsSubscribed { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsVideo => Type == WallpaperType.Video;
    public bool IsAnimated => Type is WallpaperType.Video or WallpaperType.Gif;

    // Phase 4 Helper Properties
    public string FileName => Path.GetFileName(FilePath);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
    public string ResolutionText => Width > 0 && Height > 0 ? $"{Width}x{Height}" : string.Empty;
    public bool IsMissing { get; set; }
}
