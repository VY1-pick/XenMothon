using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.UI.Desktop;
using Lumen.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Lumen.UI.Rendering;

/// <summary>
/// Makes a renderer actually visible on the desktop.
/// Images are set as the real Windows wallpaper; videos get a surface window behind the icons
/// and the inner renderer is initialised with that window before it loads media.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DesktopWallpaperRenderer : IWallpaperRenderer
{
    private const uint SpiSetDeskWallpaper = 0x0014, SpifUpdate = 0x01, SpifSendChange = 0x02;

    private readonly IWallpaperRenderer _inner;
    private readonly IDesktopWallpaperHost _host;
    private readonly MonitorTopologyService _monitors;
    private readonly ILogger _logger;
    private DesktopSurface? _surface;

    public DesktopWallpaperRenderer(IWallpaperRenderer inner, IDesktopWallpaperHost host,
        MonitorTopologyService monitors, ILogger logger)
    {
        _inner = inner;
        _host = host;
        _monitors = monitors;
        _logger = logger;
    }

    public WallpaperType SupportedType => _inner.SupportedType;
    public WallpaperState CurrentState => _inner.CurrentState;

    public Task InitializeAsync(IntPtr targetHwnd, MonitorBounds bounds, CancellationToken ct = default)
        => _inner.InitializeAsync(targetHwnd, bounds, ct);

    public async Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default)
    {
        if (!File.Exists(mediaPath))
            throw new FileNotFoundException("Wallpaper file was not found.", mediaPath);

        if (_inner.SupportedType is WallpaperType.Image or WallpaperType.Gif)
        {
            await _inner.InitializeAsync(IntPtr.Zero, MonitorBounds.Empty, ct).ConfigureAwait(false);
            SetStaticDesktopWallpaper(mediaPath);
        }
        else
        {
            await _host.EnsureAttachedAsync(ct).ConfigureAwait(false);
            var monitor = _monitors.CurrentMonitors.FirstOrDefault(m => m.IsPrimary)
                          ?? _monitors.Refresh().FirstOrDefault();
            if (monitor == null) throw new InvalidOperationException("No display was found.");

            _surface?.Dispose();
            _surface = DesktopSurface.TryCreate(_host.DesktopWorkerWHwnd, monitor.Bounds);
            if (_surface == null)
                throw new InvalidOperationException("Could not create a render surface behind the desktop icons.");

            await _inner.InitializeAsync(_surface.Handle, _surface.LocalBounds, ct).ConfigureAwait(false);
        }

        await _inner.LoadAsync(mediaPath, settings, ct).ConfigureAwait(false);
    }

    public Task PlayAsync(CancellationToken ct = default) => _inner.PlayAsync(ct);
    public Task PauseAsync(CancellationToken ct = default) => _inner.PauseAsync(ct);
    public Task StopAsync(CancellationToken ct = default) => _inner.StopAsync(ct);
    public Task SetVolumeAsync(float volume, CancellationToken ct = default) => _inner.SetVolumeAsync(volume, ct);
    public Task SetFpsLimitAsync(int fps, CancellationToken ct = default) => _inner.SetFpsLimitAsync(fps, ct);
    public Task ResizeAsync(MonitorBounds newBounds, CancellationToken ct = default) => _inner.ResizeAsync(newBounds, ct);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        _surface?.Dispose();
        _surface = null;
    }

    private void SetStaticDesktopWallpaper(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            key?.SetValue("WallpaperStyle", "10"); // Fill
            key?.SetValue("TileWallpaper", "0");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set the wallpaper fit style.");
        }

        if (!SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdate | SpifSendChange))
            throw new InvalidOperationException($"Windows refused the wallpaper (error {Marshal.GetLastWin32Error()}).");

        _logger.LogInformation("Desktop wallpaper set to '{Path}'.", path);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);
}
