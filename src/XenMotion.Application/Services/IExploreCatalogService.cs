using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.Core.Models.Catalog;

namespace XenMotion.Application.Services;

/// <summary>
/// Application service coordinating Explore/Catalog queries, latest-request-wins search concurrency,
/// local library download state synchronization, and wallpaper subscriptions.
/// </summary>
public interface IExploreCatalogService
{
    event EventHandler<(Guid CatalogItemId, CatalogDownloadState State)>? DownloadStateChanged;

    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default);
    Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default);

    Task<CatalogDownloadState> GetDownloadStateAsync(Guid catalogItemId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, CatalogDownloadState>> GetDownloadStatesAsync(IEnumerable<Guid> catalogItemIds, CancellationToken ct = default);

    Task<Wallpaper?> SubscribeAsync(CatalogItem item, IProgress<double>? progress = null, CancellationToken ct = default);
}
