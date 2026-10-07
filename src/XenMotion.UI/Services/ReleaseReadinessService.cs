using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

/// <summary>
/// Performs cheap, non-destructive checks before the production shell starts wallpaper activity.
/// </summary>
public sealed class ReleaseReadinessService
{
    private readonly ISettingsService _settings;
    private readonly IStorageManager _storage;
    private readonly ILogger<ReleaseReadinessService> _logger;

    public ReleaseReadinessService(ISettingsService settings, IStorageManager storage, ILogger<ReleaseReadinessService> logger)
    {
        _settings = settings;
        _storage = storage;
        _logger = logger;
    }

    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var settings = _settings.CurrentSettings;
        if (settings.GlobalTargetFps is < 15 or > 240)
        {
            _logger.LogWarning("Invalid persisted FPS {Fps}; application will continue using the validated UI range.", settings.GlobalTargetFps);
        }

        if (settings.Performance.HeartbeatIntervalMs is < 250 or > 5000)
        {
            _logger.LogWarning("Invalid persisted heartbeat interval {Interval} ms.", settings.Performance.HeartbeatIntervalMs);
        }

        var storagePath = _storage.RootStoragePath;
        Directory.CreateDirectory(storagePath);
        var probe = Path.Combine(storagePath, $".xenmotion-write-test-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(probe, "ok", ct).ConfigureAwait(false);
        File.Delete(probe);
    }
}
