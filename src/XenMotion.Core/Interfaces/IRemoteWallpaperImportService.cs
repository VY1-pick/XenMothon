using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Imports a wallpaper straight from an HTTP(S) media link: download, validate, store, thumbnail
/// and persist, using the same library pipeline as a local file import.
/// </summary>
public interface IRemoteWallpaperImportService
{
    Task<ImportResult> ImportFromUrlAsync(string url, IProgress<double>? progress = null, CancellationToken ct = default);
}
