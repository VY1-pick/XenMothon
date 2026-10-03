namespace Lumen.Core.Models;

/// <summary>
/// Aggregated storage usage statistics for the Lumen library.
/// </summary>
public sealed record StorageUsageInfo(
    string StorageRoot,
    int WallpaperCount,
    long MediaSizeBytes,
    long ThumbnailSizeBytes,
    long TotalSizeBytes,
    long AvailableFreeSpaceBytes)
{
    public double MediaSizeMb => Math.Round(MediaSizeBytes / (1024.0 * 1024.0), 2);
    public double ThumbnailSizeMb => Math.Round(ThumbnailSizeBytes / (1024.0 * 1024.0), 2);
    public double TotalSizeMb => Math.Round(TotalSizeBytes / (1024.0 * 1024.0), 2);
    public double AvailableFreeSpaceGb => Math.Round(AvailableFreeSpaceBytes / (1024.0 * 1024.0 * 1024.0), 2);
}
