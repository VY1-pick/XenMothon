using Lumen.Core.Models;

namespace Lumen.Core.Interfaces;

/// <summary>
/// Abstraction for discovering wallpapers from local storage or future remote services.
/// The UI consumes this interface without awareness of the underlying data source.
/// </summary>
public interface IWallpaperCatalogProvider
{
    Task<IReadOnlyList<Wallpaper>> GetAllAsync(CancellationToken ct = default);
    Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> SearchAsync(string query, string? category = null, IEnumerable<string>? tags = null, CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> GetFeaturedAsync(int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> GetSubscribedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> GetFavoritesAsync(CancellationToken ct = default);
}
