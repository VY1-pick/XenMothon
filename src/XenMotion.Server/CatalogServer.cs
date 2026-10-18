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
        // The documented command line (--Catalog:DataRoot <path>) is parsed explicitly: the argument
        // is also readable through configuration, and reading it here keeps the tools working even if
        // the host's configuration sources change.
        string root = Path.GetFullPath(
            dataRoot
            ?? ReadArgument(args, "Catalog:DataRoot")
            ?? builder.Configuration["Catalog:DataRoot"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Catalog directory not found: {root}. Pass --Catalog:DataRoot <path> or set Catalog:DataRoot.");
        }

        bool staticMode = args.Any(arg => string.Equals(arg, "--static", StringComparison.OrdinalIgnoreCase))
            || builder.Configuration.GetValue("Catalog:Static", false);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-cache";
            await next(context);
        });

        if (staticMode)
        {
            // Static mode serves the catalog directory verbatim: catalog.json plus relative media.
            // That is the same layout a CDN, object store or GitHub Pages deployment exposes, and the
            // static file middleware supplies ETag, Last-Modified and HTTP range support for resume.
            string contentRoot = root;
            var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = provider,
                ServeUnknownFileTypes = true,
                ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(),
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=300"
            });
            app.MapGet("/health", () => Results.Ok(new { status = "ready", mode = "static", root = contentRoot }));
            return app;
        }

        var catalog = await CatalogSnapshot.LoadAsync(root);
        app.MapGet("/", () => Results.Ok(new { name = "XenMotion Catalog", version = "v1", wallpapers = catalog.Items.Count, catalog = "/api/v1/catalog/search", health = "/health" }));
        app.MapGet("/health", () => Results.Ok(new { status = "ready", wallpapers = catalog.Items.Count }));
        app.MapGet("/api/v1/catalog/categories", () => catalog.Categories);
        app.MapGet("/api/v1/catalog/{id:guid}", (Guid id) => catalog.Items.FirstOrDefault(i => i.Id == id) is { } item ? Results.Ok(CatalogSnapshot.ToWire(item)) : Results.NotFound());
        app.MapGet("/api/v1/catalog/featured", (HttpRequest request) => Top(request, catalog.Items, "featured"));
        app.MapGet("/api/v1/catalog/popular", (HttpRequest request) => Top(request, catalog.Items, "popular"));
        app.MapGet("/api/v1/catalog/recent", (HttpRequest request) => Top(request, catalog.Items, "recent"));
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

    /// <summary>Reads <c>--Name=value</c> or <c>--Name value</c> from the command line.</summary>
    private static string? ReadArgument(string[] args, string name)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.StartsWith("--" + name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(name.Length + 3)..];
            }

            if (string.Equals(argument, "--" + name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static IResult Top(HttpRequest request, IEnumerable<CatalogItem> items, string shelf)
    {
        try { return Results.Ok(CatalogQueryEngine.Top(items, shelf, ReadInt(request, "limit", 10, 1, 100)).Select(CatalogSnapshot.ToWire)); }
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
            // The client's static-manifest transport uses this same engine, so both transports agree.
            var result = CatalogQueryEngine.Search(items, new CatalogSearchQuery
            {
                SearchTerm = term,
                SortBy = sort,
                PageNumber = page,
                PageSize = pageSize,
                Filter = new CatalogFilter
                {
                    CategoryId = string.IsNullOrWhiteSpace(category) ? null : category,
                    Type = type,
                    Resolution = resolution,
                    Orientation = orientation,
                    MaxDuration = duration is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                    Tags = tags.Length == 0 ? null : tags,
                    MinRating = rating
                }
            });
            return Results.Ok(new { items = result.Items.Select(CatalogSnapshot.ToWire).ToArray(), pageNumber = result.PageNumber, pageSize = result.PageSize, totalCount = result.TotalCount });
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
