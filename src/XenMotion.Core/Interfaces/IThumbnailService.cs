using XenMotion.Core.Enums;
using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Contract for creating, caching, and validating wallpaper thumbnail assets.
/// </summary>
public interface IThumbnailService
{
    string ThumbnailsDirectory { get; }
    string GetThumbnailPath(Guid wallpaperId);
    bool ThumbnailExists(Guid wallpaperId);
    Task<string> CreateThumbnailAsync(string mediaPath, Guid wallpaperId, WallpaperType type, CancellationToken ct = default);
    Task<bool> DeleteThumbnailAsync(Guid wallpaperId, CancellationToken ct = default);
    Task<int> RegenerateMissingThumbnailsAsync(IEnumerable<Wallpaper> wallpapers, CancellationToken ct = default);
}
