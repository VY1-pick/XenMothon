using XenMotion.Core.Models.Catalog;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Core contract for discovering wallpapers, categories, and media streams from a local mock or remote catalog.
/// The UI and application layers consume this abstraction to remain decoupled from the backing data source.
/// </summary>
public interface ICatalogProvider
{
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default);
    Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default);
    Task<Stream> OpenDownloadStreamAsync(CatalogItem item, CancellationToken ct = default);
}
