namespace Lumen.Core.Models.Catalog;

/// <summary>
/// Generic paginated envelope containing a slice of catalog items and total page metadata.
/// Designed for local providers and direct compatibility with future REST endpoints.
/// </summary>
public sealed record CatalogPage<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1 && TotalPages > 0;
}
