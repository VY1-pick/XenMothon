using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.Core.StateMachines;

namespace XenMotion.Application.Services;

/// <summary>
/// Orchestrates wallpaper lifecycle, per-monitor rendering sessions, and rapid switching semantics.
/// </summary>
public interface IWallpaperEngineOrchestrator : IAsyncDisposable
{
    IReadOnlyDictionary<string, WallpaperState> GetActiveMonitorStates();
    WallpaperState GetMonitorState(string monitorDeviceId);
    Wallpaper? GetActiveWallpaper(string monitorDeviceId);
    bool IsGloballyPaused { get; }

    Task ApplyWallpaperAsync(string monitorDeviceId, Wallpaper wallpaper, WallpaperSettings? settings = null, CancellationToken ct = default);
    Task PauseAsync(string monitorDeviceId, CancellationToken ct = default);
    Task ResumeAsync(string monitorDeviceId, CancellationToken ct = default);
    Task PauseAllAsync(CancellationToken ct = default);
    Task ResumeAllAsync(CancellationToken ct = default);
    Task TogglePauseAllAsync(CancellationToken ct = default);
    Task SetVolumeAllAsync(float volume, CancellationToken ct = default);
    Task ToggleMuteAllAsync(CancellationToken ct = default);
    Task StopAsync(string monitorDeviceId, CancellationToken ct = default);
    Task StopAllAsync(CancellationToken ct = default);
    Task UpdatePerformanceStateAsync(string monitorDeviceId, PerformanceState performanceState, CancellationToken ct = default);
    Task SetGlobalFpsLimitAsync(int fps, CancellationToken ct = default);
    Task ApplyPlaybackSettingsAllAsync(WallpaperSettings settings, CancellationToken ct = default);
    Task<bool> RecoverMonitorAsync(string monitorDeviceId, CancellationToken ct = default);

    event EventHandler<(string MonitorDeviceId, WallpaperState State)>? MonitorStateChanged;
    event EventHandler<bool>? GlobalPauseChanged;
}
