using Lumen.Core.Models;

namespace Lumen.Core.Interfaces;

/// <summary>
/// Contract for single and batch wallpaper import pipelines.
/// </summary>
public interface IWallpaperImportService
{
    Task<ImportResult> ImportFileAsync(string sourceFilePath, CancellationToken ct = default);
    Task<IReadOnlyList<ImportResult>> ImportFilesAsync(
        IEnumerable<string> sourceFilePaths,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
