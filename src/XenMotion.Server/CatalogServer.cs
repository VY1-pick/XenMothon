using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Net.Http.Headers;
using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;

namespace XenMotion.Server;

public static class CatalogServer
{
    public static async Task<WebApplication> CreateAsync(string[] args, string? dataRoot = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        var catalog = await CatalogSnapshot.LoadAsync(dataRoot ?? builder.Configuration["Catalog:DataRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-cache";
            await next(context);
        });
        app.MapGet("/", () => Results.Ok(new { name = "XenMotion Catalog", version = "v1", wallpapers = catalog.Items.Count, catalog = "/api/v1/catalog/search", health = "/health" }));
        app.MapGet("/health", () => Results.Ok(new { status = "ready", wallpapers = catalog.Items.Count }));
        app.MapGet("/api/v1/catalog/categories", () => catalog.Categories);
        app.MapGet("/api/v1/catalog/{id:guid}", (Guid id) => catalog.Items.FirstOrDefault(i => i.Id == id) is { } item ? Results.Ok(CatalogSnapshot.ToWire(item)) : Results.NotFound());
        app.MapGet("/api/v1/catalog/featured", (HttpRequest request) => Top(request, catalog.Items.Where(i => i.IsFeatured).OrderByDescending(i => i.Rating)));
        app.MapGet("/api/v1/catalog/popular", (HttpRequest request) => Top(request, catalog.Items.OrderByDescending(i => i.DownloadsCount).ThenByDescending(i => i.Rating)));
        app.MapGet("/api/v1/catalog/recent", (HttpRequest request) => Top(request, catalog.Items.OrderByDescending(i => i.CreatedAtUtc)));
        app.MapGet("/api/v1/catalog/search", (HttpRequest request) => Search(request, catalog.Items));
        app.MapMethods("/media/{hash}/{fileName}", ["GET", "HEAD"], (HttpContext context, string hash, string fileName) =>
        {
            if (!catalog.Assets.TryGetValue($"media/{hash}/{Uri.EscapeDataString(fileName)}", out var asset)) return Results.NotFound();
            var info = new FileInfo(asset.Path);
            // Published copies are immutable; changed cache files must never retain their old ETag.
            if (!info.Exists || info.Length != asset.Length || info.LastWriteTimeUtc != asset.LastWriteUtc)
                return Results.Problem("Published media is unavailable. Restart after repairing the catalog cache.", statusCode: 503);
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(asset.Path, asset.ContentType, entityTag: new EntityTagHeaderValue($"\"{asset.Hash}\""), enableRangeProcessing: true);
        });
        return app;
    }

    private static IResult Top(HttpRequest request, IEnumerable<CatalogItem> items)
    {
        try { return Results.Ok(items.Take(ReadInt(request, "limit", 10, 1, 100)).Select(CatalogSnapshot.ToWire)); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static IResult Search(HttpRequest request, IReadOnlyList<CatalogItem> items)
    {
        try
        {
            int page = ReadInt(request, "page", 1, 1, int.MaxValue), pageSize = ReadInt(request, "pageSize", 12, 1, 100);
            string term = request.Query["q"].ToString().Trim();
            if (term.Length > 256) throw new ArgumentException("q must be at most 256 characters.");
            var sort = ReadEnum(request, "sort", CatalogSortOption.Relevance);
            var resolution = ReadEnum(request, "resolution", CatalogResolutionFilter.All);
            var orientation = ReadEnum(request, "orientation", CatalogOrientationFilter.All);
            WallpaperType? type = request.Query.ContainsKey("type") ? ReadEnum(request, "type", WallpaperType.Image) : null;
            string category = request.Query["category"].ToString();
            var tags = request.Query["tags"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tags.Length > 20 || tags.Any(t => t.Length > 64)) throw new ArgumentException("Use at most 20 tags of up to 64 characters.");
            double? rating = ReadNumber(request, "minRating", 5), duration = ReadNumber(request, "maxDurationSeconds", 86400);
            var tokens = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<CatalogItem> matches = items.Where(i =>
                (string.IsNullOrWhiteSpace(category) || category.Equals("all", StringComparison.OrdinalIgnoreCase) || category.Equals(i.CategoryId, StringComparison.OrdinalIgnoreCase)) &&
                (type is null || type == i.Type) && (resolution == CatalogResolutionFilter.All || resolution == i.ResolutionCategory) &&
                (orientation == CatalogOrientationFilter.All || orientation == i.Orientation) && (rating is null || i.Rating >= rating) &&
                (duration is null || duration == 0 || i.Type != WallpaperType.Video || i.Duration.TotalSeconds <= duration) &&
                (tags.Length == 0 || i.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase))) &&
                tokens.All(t => $"{i.Title} {i.Description} {i.Author} {i.CategoryName} {string.Join(' ', i.Tags)}".Contains(t, StringComparison.OrdinalIgnoreCase)));
            var ordered = sort switch
            {
                CatalogSortOption.Newest => matches.OrderByDescending(i => i.CreatedAtUtc),
                CatalogSortOption.Rating => matches.OrderByDescending(i => i.Rating).ThenByDescending(i => i.RatingCount),
                CatalogSortOption.TitleAsc => matches.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
                CatalogSortOption.TitleDesc => matches.OrderByDescending(i => i.Title, StringComparer.OrdinalIgnoreCase),
                CatalogSortOption.Relevance when tokens.Length > 0 => matches.OrderByDescending(i => i.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : tokens.Count(t => i.Title.Contains(t, StringComparison.OrdinalIgnoreCase))).ThenByDescending(i => i.DownloadsCount),
                _ => matches.OrderByDescending(i => i.DownloadsCount).ThenByDescending(i => i.Rating)
            };
            long skip = ((long)page - 1) * pageSize;
            var selected = skip > int.MaxValue ? [] : ordered.Skip((int)skip).Take(pageSize).Select(CatalogSnapshot.ToWire).ToArray();
            return Results.Ok(new { items = selected, pageNumber = page, pageSize, totalCount = matches.Count() });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static int ReadInt(HttpRequest request, string name, int fallback, int min, int max)
    {
        if (!request.Query.ContainsKey(name)) return fallback;
        if (!int.TryParse(request.Query[name], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new ArgumentException($"{name} must be an integer between {min} and {max}.");
        return value;
    }

    private static T ReadEnum<T>(HttpRequest request, string name, T fallback) where T : struct, Enum
    {
        if (!request.Query.ContainsKey(name)) return fallback;
        string value = request.Query[name].ToString();
        if (!Enum.TryParse<T>(value, true, out var parsed) || !Enum.IsDefined(parsed)) throw new ArgumentException($"Invalid {name}.");
        return parsed;
    }

    private static double? ReadNumber(HttpRequest request, string name, double max)
    {
        if (!request.Query.ContainsKey(name)) return null;
        if (!double.TryParse(request.Query[name], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0 || value > max)
            throw new ArgumentException($"{name} must be between 0 and {max}.");
        return value;
    }
}
