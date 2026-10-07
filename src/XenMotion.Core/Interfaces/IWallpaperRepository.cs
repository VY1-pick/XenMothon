using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Data access abstraction for persisting and querying wallpaper metadata.
/// Decouples application logic from specific database engines or storage implementations.
/// </summary>
public interface IWallpaperRepository
{
    Task<IReadOnlyList<Wallpaper>> GetAllAsync(CancellationToken ct = default);
    Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Wallpaper?> GetByContentHashAsync(string contentHash, CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> SearchAsync(string query, string? category = null, IEnumerable<string>? tags = null, CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> GetSubscribedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Wallpaper>> GetFavoritesAsync(CancellationToken ct = default);
    Task InsertAsync(Wallpaper wallpaper, CancellationToken ct = default);
    Task UpdateAsync(Wallpaper wallpaper, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
