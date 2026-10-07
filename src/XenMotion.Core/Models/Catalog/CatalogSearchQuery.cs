using XenMotion.Core.Enums;

namespace XenMotion.Core.Models.Catalog;

/// <summary>
/// Query payload specifying search text, filter restrictions, sort order, and pagination parameters.
/// </summary>
public sealed record CatalogSearchQuery
{
    public string? SearchTerm { get; init; }
    public CatalogFilter? Filter { get; init; }
    public CatalogSortOption SortBy { get; init; } = CatalogSortOption.Relevance;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}
