using XenMotion.Core.Models;

namespace XenMotion.Application.Services;

/// <summary>
/// Service contract for loading, accessing, and persisting application configuration.
/// </summary>
public interface ISettingsService
{
    ApplicationSettings CurrentSettings { get; }
    bool IsFirstRun { get; }
    Task LoadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(Action<ApplicationSettings> updateAction, CancellationToken ct = default);

    event EventHandler<ApplicationSettings>? SettingsChanged;
}
