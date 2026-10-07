using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Providers;

/// <summary>
/// Local filesystem/SQLite implementation of the wallpaper catalog provider.
/// Supplies wallpaper lists, search results, and category filters to the UI layer.
/// </summary>
public sealed class LocalWallpaperCatalogProvider : IWallpaperCatalogProvider
{
    private readonly IWallpaperRepository _repository;
    private readonly ILogger<LocalWallpaperCatalogProvider> _logger;

    public LocalWallpaperCatalogProvider(
        IWallpaperRepository repository,
        ILogger<LocalWallpaperCatalogProvider> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Wallpaper>> GetAllAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(ct).ConfigureAwait(false);
    }

    public async Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Wallpaper>> SearchAsync(
        string query,
        string? category = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        return await _repository.SearchAsync(query, category, tags, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetFeaturedAsync(int limit = 20, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        return all.Take(limit).ToList();
    }

    public async Task<IReadOnlyList<Wallpaper>> GetSubscribedAsync(CancellationToken ct = default)
    {
        return await _repository.GetSubscribedAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetFavoritesAsync(CancellationToken ct = default)
    {
        return await _repository.GetFavoritesAsync(ct).ConfigureAwait(false);
    }
}
