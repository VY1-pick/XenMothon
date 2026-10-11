using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;

namespace XenMotion.Server;

public sealed record MediaAsset(string Path, string ContentType, long Length, DateTime LastWriteUtc, string Hash)
{
    public string Url => $"media/{Hash}/{Uri.EscapeDataString(System.IO.Path.GetFileName(Path))}";
}

public sealed class CatalogSnapshot
{
    public IReadOnlyList<CatalogItem> Items { get; private set; } = [];
    public IReadOnlyList<CatalogCategory> Categories { get; private set; } = [];
    public Dictionary<string, MediaAsset> Assets { get; } = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".bmp"] = "image/bmp", [".webp"] = "image/webp", [".gif"] = "image/gif",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mov"] = "video/quicktime",
        [".mkv"] = "video/x-matroska", [".wmv"] = "video/x-ms-wmv", [".avi"] = "video/x-msvideo"
    };

    public static async Task<CatalogSnapshot> LoadAsync(string dataRoot, CancellationToken ct = default)
    {
        dataRoot = System.IO.Path.GetFullPath(dataRoot);
        RejectLinks(dataRoot);
        await using var manifestStream = File.OpenRead(System.IO.Path.Combine(dataRoot, "catalog.json"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var manifest = await JsonSerializer.DeserializeAsync<CatalogManifest>(manifestStream, options, ct)
            ?? throw new InvalidDataException("Catalog manifest is empty.");
        if (manifest.Categories is null || manifest.Items is null || manifest.Categories.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.DisplayName)) ||
            manifest.Categories.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Categories.Count)
            throw new InvalidDataException("Catalog categories must have unique, nonempty IDs and names.");
        var categories = manifest.Categories.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<CatalogItem>();
        var ids = new HashSet<Guid>();
        var snapshot = new CatalogSnapshot();
        var publishedRoot = System.IO.Path.Combine(dataRoot, ".published");
        Directory.CreateDirectory(publishedRoot);
        RejectLinks(publishedRoot);
        var sources = new Dictionary<string, MediaAsset>(StringComparer.Ordinal);

        async Task<MediaAsset> PublishAsync(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || System.IO.Path.IsPathRooted(relativePath))
                throw new InvalidDataException("Media paths must be relative to the catalog directory.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dataRoot, relativePath));
            var relative = System.IO.Path.GetRelativePath(dataRoot, path);
            if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar) || System.IO.Path.IsPathRooted(relative))
                throw new InvalidDataException("Media path escapes the catalog directory.");
            RejectLinks(path);
            if (sources.TryGetValue(path, out var existing)) return existing;
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (!ContentTypes.TryGetValue(extension, out var mime)) throw new InvalidDataException("Unsupported catalog media extension.");
            string temp = System.IO.Path.Combine(publishedRoot, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                string hash;
                long length;
                await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        digest.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    length = output.Length;
                    hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                }
                if (length == 0) throw new InvalidDataException("Catalog media cannot be empty.");
                string target = System.IO.Path.Combine(publishedRoot, hash + extension);
                if (File.Exists(target))
                {
                    RejectLinks(target);
                    await using var cached = File.OpenRead(target);
                    if (cached.Length != length || Convert.ToHexString(await SHA256.HashDataAsync(cached, ct)).ToLowerInvariant() != hash)
                        throw new InvalidDataException("Published media was modified. Remove the damaged cached file before restarting.");
                }
                else File.Move(temp, target);
                var asset = new MediaAsset(target, mime, length, File.GetLastWriteTimeUtc(target), hash);
                sources.Add(path, asset);
                snapshot.Assets.TryAdd(asset.Url, asset);
                return asset;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        foreach (var entry in manifest.Items)
        {
            if (entry is null || entry.Id == Guid.Empty || !ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.Title) ||
                string.IsNullOrWhiteSpace(entry.Author) || entry.CategoryId is null || !categories.ContainsKey(entry.CategoryId) ||
                entry.Width <= 0 || entry.Height <= 0 || !double.IsFinite(entry.DurationSeconds) || entry.DurationSeconds < 0 || entry.DurationSeconds > 86400 ||
                !double.IsFinite(entry.Rating) || entry.Rating < 0 || entry.Rating > 5 || entry.RatingCount < 0 || entry.DownloadsCount < 0 ||
                entry.Type is not (WallpaperType.Image or WallpaperType.Video or WallpaperType.Gif) ||
                entry.CreatedAtUtc == default || entry.CreatedAtUtc.Kind != DateTimeKind.Utc || entry.UpdatedAtUtc.Kind != DateTimeKind.Utc ||
                entry.UpdatedAtUtc < entry.CreatedAtUtc || entry.Tags is null || entry.Tags.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Invalid catalog item metadata.");
            var media = await PublishAsync(entry.MediaPath);
            var thumbnail = await PublishAsync(entry.ThumbnailPath);
            var preview = await PublishAsync(string.IsNullOrWhiteSpace(entry.PreviewPath) ? entry.MediaPath : entry.PreviewPath);
            if (!thumbnail.ContentType.StartsWith("image/") || (entry.Type == WallpaperType.Video) != media.ContentType.StartsWith("video/") ||
                (entry.Type == WallpaperType.Video) != preview.ContentType.StartsWith("video/") ||
                (entry.Type == WallpaperType.Gif && media.ContentType != "image/gif"))
                throw new InvalidDataException("Media type does not match the catalog metadata.");
            items.Add(new CatalogItem
            {
                Id = entry.Id, Title = entry.Title.Trim(), Description = entry.Description ?? "", Author = entry.Author,
                Type = entry.Type, Width = entry.Width, Height = entry.Height, Duration = TimeSpan.FromSeconds(entry.DurationSeconds),
                FileSizeBytes = media.Length, ThumbnailUrl = thumbnail.Url, PreviewUrl = preview.Url, DownloadUrl = media.Url,
                CategoryId = entry.CategoryId, CategoryName = categories[entry.CategoryId].DisplayName, Tags = entry.Tags,
                IsFeatured = entry.IsFeatured, Rating = entry.Rating, RatingCount = entry.RatingCount, DownloadsCount = entry.DownloadsCount,
                CreatedAtUtc = entry.CreatedAtUtc.ToUniversalTime(), UpdatedAtUtc = entry.UpdatedAtUtc.ToUniversalTime()
            });
        }
        snapshot.Items = items;
        snapshot.Categories = manifest.Categories.Select(c => c with { ItemCount = c.Id.Equals("all", StringComparison.OrdinalIgnoreCase) ? items.Count : items.Count(i => i.CategoryId.Equals(c.Id, StringComparison.OrdinalIgnoreCase)) }).ToArray();
        return snapshot;
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links and junctions are not allowed in catalog paths.");
    }

    public static object ToWire(CatalogItem item) => new
    {
        item.Id, item.Title, item.Description, item.Author, item.Type, item.Width, item.Height,
        DurationSeconds = item.Duration.TotalSeconds, item.FileSizeBytes, item.ThumbnailUrl, item.PreviewUrl, item.DownloadUrl,
        item.CategoryId, item.CategoryName, item.Tags, item.IsFeatured, item.Rating, item.RatingCount, item.DownloadsCount,
        item.CreatedAtUtc, item.UpdatedAtUtc
    };
}
