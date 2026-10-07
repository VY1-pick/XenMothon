namespace XenMotion.Core.Models;

/// <summary>
/// Result of a library reconciliation operation checking for missing files, orphaned directories, and missing thumbnails.
/// </summary>
public sealed record ReconciliationResult(
    int TotalWallpapers,
    int ValidWallpapers,
    IReadOnlyList<Guid> MissingMediaIds,
    int RegeneratedThumbnailsCount,
    IReadOnlyList<string> OrphanedDirectories);
