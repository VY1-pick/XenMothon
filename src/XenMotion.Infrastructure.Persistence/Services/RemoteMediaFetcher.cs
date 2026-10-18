using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace XenMotion.Infrastructure.Persistence.Services;

public sealed record RemoteMediaFetchResult(
    bool Success,
    string? FilePath,
    long SizeBytes,
    string? ContentType,
    string? EntityTag,
    string? ErrorMessage)
{
    public static RemoteMediaFetchResult Failed(string message) => new(false, null, 0, null, null, message);
}

/// <summary>
/// Downloads one absolute media URL to a file with retry, progress and HTTP-range resume.
/// It deliberately reuses <see cref="RemoteCatalogProvider"/> as the transport so link imports and
/// catalog subscriptions share the same range validation, If-Range and ETag rules.
/// </summary>
public sealed class RemoteMediaFetcher
{
    public const int MaxAttempts = 3;

    private readonly RemoteCatalogProvider _downloads;
    private readonly ILogger<RemoteMediaFetcher> _logger;

    public RemoteMediaFetcher(ILogger<RemoteMediaFetcher>? logger = null, TimeSpan? requestTimeout = null)
    {
        _logger = logger ?? NullLogger<RemoteMediaFetcher>.Instance;
        _downloads = new RemoteCatalogProvider(
            new HttpClient(),
            new RemoteCatalogOptions
            {
                UseRemoteCatalog = true,
                // Absolute item URLs bypass this base address; it only satisfies transport validation.
                BaseUrl = "https://media.invalid/",
                UseLocalFallback = false,
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30)
            },
            NullLogger<RemoteCatalogProvider>.Instance);
    }

    /// <summary>
    /// Fetches <paramref name="url"/> into <paramref name="destinationPath"/>. An existing partial
    /// file is resumed when the server keeps a strong ETag; otherwise the file restarts safely.
    /// </summary>
    public async Task<RemoteMediaFetchResult> FetchAsync(Uri url, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ct.ThrowIfCancellationRequested();

        string etagPath = destinationPath + ".etag";
        Exception? last = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long offset = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
                string? entityTag = File.Exists(etagPath) ? await File.ReadAllTextAsync(etagPath, ct).ConfigureAwait(false) : null;

                await using var source = await _downloads.OpenDownloadStreamAsync(BuildItem(url), offset, ct, entityTag).ConfigureAwait(false);
                if (offset > 0 && !source.IsPartial)
                {
                    offset = 0;
                    TryDelete(destinationPath);
                }

                var mode = offset > 0 ? FileMode.Append : FileMode.Create;
                await using var target = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                if (!string.IsNullOrEmpty(source.EntityTag)) await File.WriteAllTextAsync(etagPath, source.EntityTag, ct).ConfigureAwait(false);
                else TryDelete(etagPath);

                long? expected = source.ContentLength;
                long total = expected is { } length ? offset + length : 0;
                byte[] buffer = new byte[128 * 1024];
                long written = offset;
                int read;
                while ((read = await source.Content.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    written += read;
                    if (total > 0) progress?.Report(Math.Clamp(written * 100d / total, 0, 100));
                }

                await target.FlushAsync(ct).ConfigureAwait(false);

                if (expected is { } awaited && written - offset != awaited)
                {
                    throw new IOException("The download ended before the announced content length.");
                }
                if (source.TotalLength is { } fullLength && written != fullLength)
                    throw new IOException("The complete media file has not been downloaded yet.");

                if (written == 0) throw new IOException("The link returned an empty file.");

                progress?.Report(100);
                _logger.LogInformation("Downloaded {Bytes} bytes in {Attempts} attempt(s).", written, attempt);
                return new RemoteMediaFetchResult(true, destinationPath, written, source.ContentType, source.EntityTag, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (NotSupportedException ex)
            {
                // A redirect to a non-HTTP scheme (for example a local file path) is not retryable.
                return RemoteMediaFetchResult.Failed(ex.Message);
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning("Download attempt {Attempt} of {Max} failed ({Error}).", attempt, MaxAttempts, ex.GetType().Name);
                if (attempt == MaxAttempts) break;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct).ConfigureAwait(false);
            }
        }

        return RemoteMediaFetchResult.Failed(last?.Message ?? "The download failed.");
    }

    private static CatalogItem BuildItem(Uri url)
    {
        string text = url.ToString();
        return new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = string.Empty,
            Description = string.Empty,
            Author = string.Empty,
            ThumbnailUrl = text,
            PreviewUrl = text,
            DownloadUrl = text,
            Type = WallpaperType.Image,
            Width = 0,
            Height = 0,
            CategoryId = string.Empty,
            CategoryName = string.Empty
        };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
