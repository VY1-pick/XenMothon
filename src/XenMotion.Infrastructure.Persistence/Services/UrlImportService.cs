using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Services;

/// <summary>
/// Imports a wallpaper from an HTTP(S) link. The bytes are downloaded with resume support into a
/// private staging folder, then handed to the normal import pipeline so duplicate detection, SHA-256
/// hashing, thumbnails, storage layout and SQLite metadata behave exactly like a file import.
/// </summary>
public sealed class UrlImportService : IRemoteWallpaperImportService
{
    private readonly RemoteMediaFetcher _fetcher;
    private readonly IStorageManager _storageManager;
    private readonly IWallpaperImportService _importService;
    private readonly ILogger<UrlImportService> _logger;

    public UrlImportService(
        RemoteMediaFetcher fetcher,
        IStorageManager storageManager,
        IWallpaperImportService importService,
        ILogger<UrlImportService> logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportResult> ImportFromUrlAsync(string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string source = (url ?? string.Empty).Trim();
        if (!TryValidate(source, out var uri, out string validationError))
        {
            return ImportResult.Failed(source, validationError);
        }

        string workDirectory = Path.Combine(_storageManager.TempDirectory, "url-import", Guid.NewGuid().ToString("N"));
        string downloadPath = Path.Combine(workDirectory, "download.part");
        try
        {
            Directory.CreateDirectory(workDirectory);

            var downloadProgress = progress is null ? null : new Progress<double>(value => progress.Report(Math.Clamp(value * 0.85, 0, 85)));
            var fetch = await _fetcher.FetchAsync(uri!, downloadPath, downloadProgress, ct).ConfigureAwait(false);
            if (!fetch.Success)
            {
                return ImportResult.Failed(source, fetch.ErrorMessage ?? "The download failed.");
            }

            // The stored extension decides how the library validates the file, so prefer the link's own
            // supported extension and otherwise trust the server's media type.
            string? urlExtension = MediaFileTypes.ExtensionFromUrl(source);
            string extension = MediaFileTypes.IsSupported(urlExtension)
                ? urlExtension!.ToLowerInvariant()
                : MediaFileTypes.ExtensionForContentType(fetch.ContentType) ?? string.Empty;
            if (extension.Length == 0)
            {
                return ImportResult.Failed(source, "The link did not report a supported image or video type.");
            }

            progress?.Report(85);

            string requestedName = MediaFileTypes.FileNameFromUrl(source);
            string title = string.IsNullOrWhiteSpace(requestedName) ? "Downloaded wallpaper" : Path.GetFileNameWithoutExtension(requestedName);
            string stagedName = PathSanitizer.SanitizeFileName(title, extension);
            string stagedPath = Path.Combine(workDirectory, stagedName);

            File.Move(downloadPath, stagedPath, overwrite: true);
            _logger.LogInformation("Link import staged {Bytes} bytes as '{Name}'.", fetch.SizeBytes, stagedName);

            var result = await _importService.ImportFileAsync(stagedPath, ct).ConfigureAwait(false);
            // Report the user's link as the source rather than the transient staging path.
            result = result with { SourceFilePath = source };
            progress?.Report(100);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Link import cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Link import failed ({Error}).", ex.GetType().Name);
            return ImportResult.Failed(source, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static bool TryValidate(string url, out Uri? uri, out string error)
    {
        error = string.Empty;
        uri = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Enter a media link to import.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            error = "That is not a complete link. Include https:// at the start.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = "Only http and https links are supported.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Links that embed credentials are not supported.";
            return false;
        }

        return true;
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not remove the link-import staging folder {Path}.", path);
        }
    }
}
