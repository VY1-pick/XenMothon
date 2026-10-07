using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// High-level application service coordinating wallpaper library management, deletion, reconciliation, and storage statistics.
/// </summary>
public interface ILibraryService
{
    event EventHandler? LibraryChanged;

    Task<IReadOnlyList<Wallpaper>> GetWallpapersAsync(CancellationToken ct = default);
    Task<Wallpaper?> GetWallpaperByIdAsync(Guid id, CancellationToken ct = default);
    Task<DeleteResult> DeleteWallpaperAsync(Guid id, CancellationToken ct = default);
    Task<ReconciliationResult> ReconcileLibraryAsync(CancellationToken ct = default);
    Task<StorageUsageInfo> GetStorageUsageAsync(CancellationToken ct = default);
    Task<bool> MigrateStorageRootAsync(string newRootPath, CancellationToken ct = default);
}
