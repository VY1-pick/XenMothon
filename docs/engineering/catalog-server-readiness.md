# Wallpaper server handoff — 2026-10-07

The desktop client already has an HTTP catalog, streaming downloads, progress, cancellation,
partial-file resume and local fallback. General → Wallpaper server now exposes its settings.
Leave **Use online catalog** off until the backend is deployed; save and restart after changing
the server because the provider is selected once per process. No server has been deployed.

## Required API

Use HTTPS in production. HTTP localhost is allowed for development. No credentials belong in
the base URL. The current catalog is public/read-only; authentication and paid content need
an explicit server/client contract before enabling them.

| GET route | Response |
| --- | --- |
| `/api/v1/catalog/categories` | Array of `{id, displayName, description, iconGlyph, itemCount}` |
| `/api/v1/catalog/featured?limit=10` | Item array |
| `/api/v1/catalog/popular?limit=10` | Item array |
| `/api/v1/catalog/recent?limit=10` | Item array |
| `/api/v1/catalog/{guid}` | Item object, or HTTP 404 |
| `/api/v1/catalog/search` | `{items, pageNumber, pageSize, totalCount}` |

Search parameters: `q`, `page`, `pageSize`, `sort`, `category`, `type`, `resolution`,
`orientation`, `maxDurationSeconds`, comma-separated `tags`, and `minRating`.
Use the enum names defined in `src/XenMotion.Core/Enums/Catalog*.cs` and `WallpaperType.cs`.
Return JSON with `Content-Type: application/json`; timestamps use UTC ISO 8601.

An example search response is in `docs/engineering/catalog-search.example.json`.
Each item needs a stable GUID, title, description, author, thumbnail/preview/download URLs,
type, dimensions, duration in seconds, byte size, category, tags and timestamps. Thumbnail
URLs point to actual image bytes; previews/downloads point to the corresponding media.
Relative media paths are resolved against the configured base URL. Prefer root-host deployment
or explicit absolute CDN URLs. Keep download URLs immutable when bytes change. The client now
resumes only with a strong ETag and If-Range; without a validator it restarts safely.

A compatible local server is implemented in `src/XenMotion.Server`. See
[catalog-server.md](catalog-server.md) for setup, content layout and verification. No public deployment has been made.

## Media hosting and resume

- Initial GET: HTTP 200 and accurate `Content-Length`.
- Supply a strong `ETag` for resumable media. Match `If-Range` before returning 206;
  when the validator differs, return the full file with HTTP 200 and its current ETag.
- `Range: bytes=N-`: HTTP 206, `Content-Range: bytes N-END/TOTAL`, accurate remaining
  `Content-Length`, `Accept-Ranges: bytes`, and unchanged media bytes.
- If range is unsupported, return HTTP 200 with the entire file. The client restarts the file.
- Out-of-range: HTTP 416. A mismatched/missing 206 range is rejected to prevent file corruption.
- Use immutable/versioned media URLs, correct MIME types and CDN caching. Do not return HTML
  login/error pages with HTTP 200. Do not compress MP4 responses dynamically.
- Advertised `fileSizeBytes` must equal the media length. The client validates final size and
  imports through its normal media validation/hash pipeline. Server-provided digest verification
  is not part of the current wire contract.

## Tomorrow's deployment checks

1. Configure domain/TLS and publish one real licensed video plus its thumbnail and preview.
2. Implement the six catalog routes above; verify pagination and empty/404 responses.
3. Verify initial download and a 206 range request against the CDN, including exact byte offsets.
4. Configure the URL in General, enable online catalog, save and restart XenMotion.
5. Search, open details, download, cancel midway, resume, import and apply the real wallpaper.
6. Disconnect the server and verify local fallback; disable fallback to verify a visible error.

Local contract/resume checks:

```powershell
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj -c Release --filter FullyQualifiedName~RemoteCatalogProviderTests
```
