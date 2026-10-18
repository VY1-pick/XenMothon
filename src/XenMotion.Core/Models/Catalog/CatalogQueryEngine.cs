using XenMotion.Core.Enums;

namespace XenMotion.Core.Models.Catalog;

/// <summary>
/// Filtering, sorting and paging over an in-memory catalog. The server API and the static-manifest
/// client provider both call this so the two catalog transports answer identical queries.
/// </summary>
public static class CatalogQueryEngine
{
    public static CatalogPage<CatalogItem> Search(IEnumerable<CatalogItem> items, CatalogSearchQuery? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        query ??= new CatalogSearchQuery();

        var source = items.Where(item => item is not null).ToList();
        var filter = query.Filter;
        string term = query.SearchTerm?.Trim() ?? string.Empty;
        string[] tokens = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] tags = filter?.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray() ?? [];
        string category = filter?.CategoryId ?? string.Empty;
        double? minRating = filter?.MinRating;
        double maxDurationSeconds = filter?.MaxDuration is { } duration ? Math.Max(0, duration.TotalSeconds) : 0;

        var matches = source.Where(item =>
            (string.IsNullOrWhiteSpace(category) || category.Equals("all", StringComparison.OrdinalIgnoreCase) || category.Equals(item.CategoryId, StringComparison.OrdinalIgnoreCase)) &&
            (filter?.Type is null || filter.Type == item.Type) &&
            (filter is null || filter.Resolution == CatalogResolutionFilter.All || filter.Resolution == item.ResolutionCategory) &&
            (filter is null || filter.Orientation == CatalogOrientationFilter.All || filter.Orientation == item.Orientation) &&
            (minRating is null || item.Rating >= minRating) &&
            (maxDurationSeconds == 0 || item.Type != WallpaperType.Video || item.Duration.TotalSeconds <= maxDurationSeconds) &&
            (tags.Length == 0 || item.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase))) &&
            tokens.All(token => SearchText(item).Contains(token, StringComparison.OrdinalIgnoreCase)));

        var ordered = query.SortBy switch
        {
            CatalogSortOption.Newest => matches.OrderByDescending(item => item.CreatedAtUtc),
            CatalogSortOption.Rating => matches.OrderByDescending(item => item.Rating).ThenByDescending(item => item.RatingCount),
            CatalogSortOption.TitleAsc => matches.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            CatalogSortOption.TitleDesc => matches.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase),
            CatalogSortOption.Relevance when tokens.Length > 0 => matches
                .OrderByDescending(item => item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    ? 2
                    : tokens.Count(token => item.Title.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(item => item.DownloadsCount),
            _ => matches.OrderByDescending(item => item.DownloadsCount).ThenByDescending(item => item.Rating)
        };

        var orderedList = ordered.ToList();
        int pageNumber = Math.Max(1, query.PageNumber);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        long skip = ((long)pageNumber - 1) * pageSize;
        var page = skip > int.MaxValue
            ? []
            : orderedList.Skip((int)skip).Take(pageSize).ToList();

        return new CatalogPage<CatalogItem>(page, pageNumber, pageSize, orderedList.Count);
    }

    /// <summary>Top-N ordering used by the featured/popular/recent shelves.</summary>
    public static IReadOnlyList<CatalogItem> Top(IEnumerable<CatalogItem> items, string shelf, int limit)
    {
        ArgumentNullException.ThrowIfNull(items);
        limit = Math.Clamp(limit, 1, 100);
        return shelf switch
        {
            "featured" => items.Where(item => item.IsFeatured).OrderByDescending(item => item.Rating).Take(limit).ToList(),
            "recent" => items.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToList(),
            // "popular" and anything unknown fall back to the popularity ordering.
            _ => items.OrderByDescending(item => item.DownloadsCount).ThenByDescending(item => item.Rating).Take(limit).ToList()
        };
    }

    private static string SearchText(CatalogItem item)
        => $"{item.Title} {item.Description} {item.Author} {item.CategoryName} {string.Join(' ', item.Tags)}";
}
