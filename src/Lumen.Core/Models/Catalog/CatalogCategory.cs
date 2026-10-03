namespace Lumen.Core.Models.Catalog;

/// <summary>
/// Domain model representing a discoverable catalog category with a stable ID and presentation metadata.
/// </summary>
public sealed record CatalogCategory(
    string Id,
    string DisplayName,
    string Description,
    string IconGlyph,
    int ItemCount);
