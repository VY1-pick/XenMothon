# XenMotion catalog server

The first server is a public, read-only ASP.NET Core 8 service matching the desktop client's existing API. It serves catalog metadata, thumbnails, previews and downloadable image/GIF/video files. It has no user accounts or upload endpoints.

## Run locally

From the repository root in PowerShell:

```powershell
./tools/Start-CatalogServer.ps1
```

Open **http://127.0.0.1:5188/** in a browser. The root shows the server name, catalog count and API links. `/health` checks readiness; `/api/v1/catalog/search` lists wallpapers. Keep the terminal running; Ctrl+C stops it.

The script prepares two real bundled sample **images** in `artifacts/catalog-demo`, labelled as demo artwork. It never overwrites an existing manifest. These are local test assets, not a production collection. The generated manifest contains their actual image dimensions; served file sizes are derived from their bytes.

For your own catalog:

```powershell
./tools/Start-CatalogServer.ps1 -DataRoot 'D:\Catalog' -Port 5188
```

In XenMotion Settings → General → Wallpaper server, use `http://127.0.0.1:5188/`, enable the online catalog, save, then restart XenMotion. Disable local fallback when verifying the server so bundled results cannot mask a connection failure. Restore your preferred fallback setting afterwards.

## Content layout

```text
catalog-directory/
  catalog.json
  media/
    ocean.mp4
    ocean-preview.mp4
    ocean-thumbnail.jpg
  .published/                  # generated immutable copies; never edit manually
```

`catalog.json` uses this shape:

```json
{
  "categories": [
    { "id": "all", "displayName": "All Wallpapers", "description": "All wallpapers", "iconGlyph": "\uE71D", "itemCount": 0 },
    { "id": "nature", "displayName": "Nature", "description": "Natural scenes", "iconGlyph": "\uE707", "itemCount": 0 }
  ],
  "items": [
    {
      "id": "30000000-0000-0000-0000-000000000001",
      "title": "Ocean",
      "description": "Ocean waves at sunset.",
      "author": "Your creator name",
      "type": "Video",
      "width": 1920,
      "height": 1080,
      "durationSeconds": 20,
      "categoryId": "nature",
      "tags": ["ocean", "sunset"],
      "isFeatured": true,
      "rating": 0,
      "ratingCount": 0,
      "downloadsCount": 0,
      "createdAtUtc": "2026-10-09T00:00:00Z",
      "updatedAtUtc": "2026-10-09T00:00:00Z",
      "mediaPath": "media/ocean.mp4",
      "thumbnailPath": "media/ocean-thumbnail.jpg",
      "previewPath": "media/ocean-preview.mp4"
    }
  ]
}
```

This example requires your own real files. IDs must be unique and stable. Keep accurate dimensions/duration; the server validates metadata shape, file existence, nonempty bytes, supported extensions and media-type consistency, but does not transcode or decode your media. The desktop importer retains its playback/media checks. Thumbnail files must be images. An omitted/empty `previewPath` uses the original media. Category counts and download byte sizes are calculated automatically. Ratings/download counts are publisher-supplied metadata in this version, not collected analytics.

The manifest is loaded and validated **at startup**. Edit source files/metadata, then restart the server. Startup fails clearly for invalid content instead of publishing a partial catalog. Paths must stay within the catalog directory; symbolic links/junctions are rejected. Only explicitly listed files become accessible; the manifest and other files have no public static route.

## Downloads and caching

At startup the server streams each referenced source into `.published/` and computes SHA-256. Media URLs contain that hash; changing the source produces a new URL on restart. The previous published copy remains unchanged, so existing downloads can finish against their original version. These copies require additional disk space; old versions are retained, and cleanup is manual while the server is stopped.

Files support GET/HEAD, accurate byte lengths, strong ETag, Range/If-Range, conditional requests and immutable caching. A stale If-Range returns the whole file rather than an incompatible partial response. An invalid range returns 416. The implementation uses [ASP.NET Core file results](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-8.0) for HTTP protocol handling. Do not edit `.published` files while the server is running; detected cache damage returns 503 rather than serving changed bytes under an old validator.

## API

All six routes and filter names in [catalog-server-readiness.md](catalog-server-readiness.md) are supported. Search page size/featured limits are 1–100; malformed enum/numeric filters return 400. Pagination past the final page returns an empty array while keeping the total. Categories/tags/text matching is case-insensitive. Tag filters match any selected tag, consistent with the bundled catalog. Relevance favors title matches; Popular uses the manifest's download counts.

## Validation

```powershell
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj -c Release --filter FullyQualifiedName~CatalogServerTests
```

Tests start real Kestrel on an ephemeral loopback port and use the desktop's actual `RemoteCatalogProvider`. They cover every catalog route, search/filter/pagination, exact resume bytes, strong ETag, changed If-Range, HEAD, 416, file-version changes, invalid queries and path isolation.

## Deployment boundary

No public server is deployed yet. Deploy the server separately from the Windows application. Use HTTPS and a reverse proxy on your chosen host; configure its domain, request limits and monitoring there. The catalog directory must be private to the server process, with writable `.published` storage. Do not expose a file editor or a raw catalog-directory static file share. Preserve Range/If-Range and ETag through the proxy/CDN and avoid transforming/compressing media bytes. The default script binds loopback only; public hosting is a separate step after a domain and host are selected.
