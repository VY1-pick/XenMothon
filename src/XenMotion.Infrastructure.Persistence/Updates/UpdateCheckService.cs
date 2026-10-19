using System.Reflection;
using XenMotion.Application.Services;
using XenMotion.Core.Models.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace XenMotion.Infrastructure.Persistence.Updates;

/// <summary>
/// Runs an update check against the current settings. Building the transport per call keeps the
/// behaviour identical to the startup check while letting the user enable, disable or repoint the
/// manifest and immediately press "Check now" without restarting XenMotion.
/// </summary>
public sealed class UpdateCheckService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(ISettingsService settingsService, ILogger<UpdateCheckService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? NullLogger<UpdateCheckService>.Instance;
    }

    /// <summary>Current application version, including its pre-release label.</summary>
    public static string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational) ? assembly.GetName().Version?.ToString(3) ?? "0.0.0" : informational.Trim();
        }
    }

    /// <summary>Returns the newer release, or null when up to date. Throws on a malformed manifest.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var settings = _settingsService.CurrentSettings;
        if (!settings.CheckForUpdates) return null;

        if (!Uri.TryCreate(settings.UpdateManifestUrl?.Trim(), UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Update checks need an absolute HTTPS manifest URL.");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var service = new RemoteUpdateService(
            client,
            manifestUri.ToString(),
            NullLogger<RemoteUpdateService>.Instance,
            CurrentVersion);

        return await service.CheckForUpdateAsync(ct).ConfigureAwait(false);
    }
}
