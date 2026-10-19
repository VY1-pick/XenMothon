using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Updates;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Updates;

/// <summary>
/// Reads GitHub releases or a custom JSON manifest over HTTPS. It performs no installation and rejects
/// non-HTTPS download/notes URLs. A missing endpoint simply means update checks are disabled.
/// </summary>
public sealed class RemoteUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;
    private readonly ILogger<RemoteUpdateService> _logger;
    private readonly string _currentVersion;

    // The 0.9.0 beta series predates the public numbering reset to 0.1.2 Beta.
    private static readonly IComparer<ReleaseVersion> GitHubVersionOrder = Comparer<ReleaseVersion>.Create((left, right) =>
    {
        static bool Legacy(ReleaseVersion version) => version.Major == 0 && version.Minor == 9 && version.Patch == 0 &&
            version.PreRelease.Count > 0 && version.PreRelease[0].Equals("beta", StringComparison.OrdinalIgnoreCase);
        bool leftLegacy = Legacy(left), rightLegacy = Legacy(right);
        return leftLegacy == rightLegacy ? left.CompareTo(right) : leftLegacy ? -1 : 1;
    });

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
            manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Update manifest URL must be an absolute HTTPS URI.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        request.Headers.UserAgent.ParseAdd("XenMotion");
        request.Headers.Accept.ParseAdd(manifestUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.github+json" : "application/json");
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        UpdateManifest? manifest;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // shortcut: inspect the latest 100 releases; paginate if the supported release window grows beyond that.
            manifest = (document.RootElement.Deserialize<List<GitHubRelease>>(JsonOptions) ?? [])
                .Where(release => !release.Draft)
                .Select(release => new { Release = release, Version = ReleaseVersion.TryParse(release.TagName, out var parsed) ? parsed : null, Asset = release.Assets?.FirstOrDefault(asset =>
                    asset.State == "uploaded" && string.Equals(asset.Name, $"XenMotion-{release.TagName?.TrimStart('v', 'V')}-Setup-x64.exe", StringComparison.OrdinalIgnoreCase)) })
                .Where(candidate => candidate.Version is not null && candidate.Asset is not null)
                .OrderByDescending(candidate => candidate.Version!, GitHubVersionOrder)
                .Select(candidate => new UpdateManifest
                {
                    Version = candidate.Release.TagName!.TrimStart('v', 'V'),
                    DownloadUrl = candidate.Asset!.DownloadUrl,
                    ReleaseNotesUrl = candidate.Release.HtmlUrl,
                    PublishedAtUtc = candidate.Release.PublishedAtUtc
                }).FirstOrDefault();
            if (manifest is null) return null;
        }
        else manifest = document.RootElement.Deserialize<UpdateManifest>(JsonOptions);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            throw new InvalidDataException("Update manifest is missing required version or downloadUrl fields.");
        }

        if (!ReleaseVersion.TryParse(manifest.Version, out var available) ||
            !ReleaseVersion.TryParse(_currentVersion, out var current))
        {
            throw new InvalidDataException("Update manifest or current application version is not a valid release version.");
        }

        // Pre-release labels are ordered too, so a private beta can supersede an earlier beta.
        int comparison = document.RootElement.ValueKind == JsonValueKind.Array
            ? GitHubVersionOrder.Compare(available!, current!) : available!.CompareTo(current);
        if (comparison <= 0)
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

    /// <summary>
    /// Prefers the informational version, which keeps the pre-release label (0.9.0-beta.3), over the
    /// numeric assembly version that would collapse every beta into 0.9.0.
    /// </summary>
    private static string GetEntryAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Trim();
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        public bool Draft { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAtUtc { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? Name { get; set; }
        public string? State { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
    }

    private static void ValidateOptionalHttpUrl(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"Update manifest field '{field}' must be an absolute HTTPS URL.");
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
