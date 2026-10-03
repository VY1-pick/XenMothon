using Lumen.Core.Models;

namespace Lumen.Core.Interfaces;

/// <summary>
/// Contract for managing wallpaper media directories, atomic file imports, and storage migrations.
/// </summary>
public interface IStorageManager
{
    string RootStoragePath { get; }
    string MediaDirectory { get; }
    string ThumbnailsDirectory { get; }
    string TempDirectory { get; }

    string GetWallpaperDirectory(Guid wallpaperId);
    string ResolveAbsolutePath(string relativeOrAbsolutePath);
    string MakeStorageRelativePath(string fullPath);

    Task<string> StageImportAsync(string sourceFilePath, CancellationToken ct = default);
    Task<string> CommitImportAsync(string stagedFilePath, Guid wallpaperId, string fileName, CancellationToken ct = default);
    Task<bool> DeleteWallpaperStorageAsync(Guid wallpaperId, CancellationToken ct = default);
    Task<bool> ValidateStoragePathAsync(string targetPath, CancellationToken ct = default);
    Task<string> CalculateSha256HashAsync(string filePath, CancellationToken ct = default);

    Task<StorageUsageInfo> GetStorageUsageAsync(int wallpaperCount = 0, CancellationToken ct = default);
    Task<bool> MigrateStorageRootAsync(string newRootPath, CancellationToken ct = default);
    Task CleanTempFilesAsync(TimeSpan? olderThan = null, CancellationToken ct = default);
}
