using XenMotion.Core.Enums;
using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Core contract implemented by all rendering implementations (Image, Video, Web, Shader).
/// Controls surface lifecycle, playback pacing, volume, and viewport configuration.
/// </summary>
public interface IWallpaperRenderer : IAsyncDisposable
{
    WallpaperType SupportedType { get; }
    WallpaperState CurrentState { get; }

    Task InitializeAsync(IntPtr targetHwnd, MonitorBounds bounds, CancellationToken ct = default);
    Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default);
    Task PlayAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SetVolumeAsync(float volume, CancellationToken ct = default);
    Task SetFpsLimitAsync(int fps, CancellationToken ct = default);
    Task ResizeAsync(MonitorBounds newBounds, CancellationToken ct = default);
}
