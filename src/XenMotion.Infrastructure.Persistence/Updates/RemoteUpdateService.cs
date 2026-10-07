using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Updates;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Updates;

/// <summary>
/// Reads a small JSON release manifest over HTTPS. It performs no installation and rejects
/// non-HTTP(S) download/notes URLs. A missing endpoint simply means update checks are disabled.
/// </summary>
public sealed class RemoteUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;
    private readonly ILogger<RemoteUpdateService> _logger;
    private readonly string _currentVersion;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public RemoteUpdateService(HttpClient httpClient, string manifestUrl, ILogger<RemoteUpdateService> logger, string? currentVersion = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _manifestUrl = manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentVersion = string.IsNullOrWhiteSpace(currentVersion) ? GetEntryAssemblyVersion() : currentVersion.Trim();
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!Uri.TryCreate(_manifestUrl, UriKind.Absolute, out var manifestUri) ||
            (manifestUri.Scheme != Uri.UriSchemeHttp && manifestUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Update manifest URL must be an absolute HTTP(S) URI.");
        }

        var manifest = await _httpClient.GetFromJsonAsync<UpdateManifest>(manifestUri, JsonOptions, ct).ConfigureAwait(false);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            throw new InvalidDataException("Update manifest is missing required version or downloadUrl fields.");
        }

        if (!Version.TryParse(NormalizeVersion(manifest.Version), out var available) ||
            !Version.TryParse(NormalizeVersion(_currentVersion), out var current))
        {
            throw new InvalidDataException("Update manifest or current application version is not a valid numeric version.");
        }

        if (available <= current)
        {
            return null;
        }

        ValidateOptionalHttpUrl(manifest.DownloadUrl, "downloadUrl");
        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl))
        {
            ValidateOptionalHttpUrl(manifest.ReleaseNotesUrl!, "releaseNotesUrl");
        }

        var result = new UpdateInfo(
            manifest.Version.Trim(),
            manifest.DownloadUrl.Trim(),
            string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl) ? null : manifest.ReleaseNotesUrl.Trim(),
            manifest.PublishedAtUtc,
            manifest.IsMandatory);

        _logger.LogInformation("Update available: {Version} (current {CurrentVersion}).", result.Version, _currentVersion);
        return result;
    }

    private static string GetEntryAssemblyVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static string NormalizeVersion(string version)
        => version.Trim().TrimStart('v', 'V');

    private static void ValidateOptionalHttpUrl(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException($"Update manifest field '{field}' must be an absolute HTTP(S) URL.");
        }
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public DateTimeOffset? PublishedAtUtc { get; set; }
        public bool IsMandatory { get; set; }
    }
}
