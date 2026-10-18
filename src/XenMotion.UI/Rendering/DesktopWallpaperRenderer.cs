using System.Runtime.Versioning;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Desktop;
using XenMotion.UI.Services;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Rendering;

/// <summary>Owns a removable desktop layer for both image and video renderers.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DesktopWallpaperRenderer : IWallpaperRenderer
{
    private readonly IWallpaperRenderer _inner;
    private readonly IDesktopWallpaperHost _host;
    private readonly MonitorTopologyService _monitors;
    private readonly ILogger _logger;
    private readonly string _monitorId;
    private DesktopSurface? _surface;
    private readonly SemaphoreSlim _surfaceGate = new(1, 1);
    private MonitorBounds _monitorBounds;
    private IntPtr _shellHwnd;
    private bool _disposed;
    public DesktopWallpaperRenderer(IWallpaperRenderer inner, IDesktopWallpaperHost host,
        MonitorTopologyService monitors, ILogger logger, string monitorId)
    {
        _inner = inner; _host = host; _monitors = monitors; _logger = logger; _monitorId = monitorId;
        _monitors.TopologyChanged += OnTopologyChanged;
    }
    public WallpaperType SupportedType => _inner.SupportedType;
    public WallpaperState CurrentState => _inner.CurrentState;
    public Task InitializeAsync(IntPtr hwnd, MonitorBounds bounds, CancellationToken ct = default)
        => _inner.InitializeAsync(hwnd, bounds, ct);
    public async Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default)
    {
        await _surfaceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(mediaPath)) throw new FileNotFoundException("Wallpaper file was not found.", mediaPath);
            if (!await _host.EnsureAttachedAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("The desktop shell is unavailable. Retry after Explorer has started.");
            var monitor = FindMonitor(_monitors.CurrentMonitors) ?? FindMonitor(_monitors.Refresh());
            if (monitor is null) throw new InvalidOperationException("No display was found.");
            if (_surface == null || _shellHwnd != _host.DesktopWorkerWHwnd)
            {
                _surface?.Dispose();
                _surface = await Task.Run(() => DesktopSurface.TryCreate(_host.DesktopWorkerWHwnd, monitor.Bounds), ct).ConfigureAwait(false);
                if (_surface is null) throw new InvalidOperationException("Could not create a desktop render surface.");
                _shellHwnd = _host.DesktopWorkerWHwnd;
                _logger.LogInformation("Wallpaper surface 0x{Surface:X} for {Type}; shell 0x{Shell:X}; monitor {Monitor}; bounds {Bounds}; file {Path}.",
                    _surface.Handle.ToInt64(), _inner.SupportedType, _shellHwnd.ToInt64(), monitor.DeviceId, monitor.Bounds, mediaPath);
                if (_inner is ImageWallpaperRenderer image) _surface.PaintRequested += (_, _) => image.Redraw();
                await _inner.InitializeAsync(_surface.Handle, _surface.LocalBounds, ct).ConfigureAwait(false);
            }
            else if (_monitorBounds != monitor.Bounds)
            {
                await _surface.ResizeAsync(monitor.Bounds, ct).ConfigureAwait(false);
                await _inner.ResizeAsync(_surface.LocalBounds, ct).ConfigureAwait(false);
            }
            await _inner.LoadAsync(mediaPath, settings, ct).ConfigureAwait(false);
            _surface.SetOpacity(settings.Opacity);
            _monitorBounds = monitor.Bounds;
        }
        finally { _surfaceGate.Release(); }
    }
    public Task PlayAsync(CancellationToken ct = default) => _inner.PlayAsync(ct);
    public Task PauseAsync(CancellationToken ct = default) => _inner.PauseAsync(ct);
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _surfaceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _inner.StopAsync(ct).ConfigureAwait(false);
            _surface?.Dispose(); _surface = null;
        }
        finally { _surfaceGate.Release(); }
    }
    public async Task<bool> TryApplySettingsAsync(WallpaperSettings settings, CancellationToken ct = default)
    {
        await _surfaceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || _surface == null || !await _inner.TryApplySettingsAsync(settings, ct).ConfigureAwait(false)) return false;
            _surface.SetOpacity(settings.Opacity);
            return true;
        }
        finally { _surfaceGate.Release(); }
    }
    public Task SetVolumeAsync(float volume, CancellationToken ct = default) => _inner.SetVolumeAsync(volume, ct);
    public Task SetFpsLimitAsync(int fps, CancellationToken ct = default) => _inner.SetFpsLimitAsync(fps, ct);
    public async Task ResizeAsync(MonitorBounds bounds, CancellationToken ct = default)
    {
        await _surfaceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || _surface is null || bounds.IsEmpty || bounds == _monitorBounds) return;
            await _surface.ResizeAsync(bounds, ct).ConfigureAwait(false);
            await _inner.ResizeAsync(_surface.LocalBounds, ct).ConfigureAwait(false);
            _monitorBounds = bounds;
        }
        finally { _surfaceGate.Release(); }
    }
    private async void OnTopologyChanged(object? sender, MonitorTopologyChangedEventArgs e)
    {
        var monitor = FindMonitor(e.Monitors);
        if (monitor is null) return;
        try { await ResizeAsync(monitor.Bounds).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogError(ex, "Could not resize wallpaper after display topology changed."); }
    }
    private MonitorInfo? FindMonitor(IReadOnlyList<MonitorInfo> monitors)
        => _monitorId == WallpaperSchedule.PrimaryMonitorId
            ? monitors.FirstOrDefault(m => m.IsPrimary)
            : monitors.FirstOrDefault(m => string.Equals(m.DeviceId, _monitorId, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        _monitors.TopologyChanged -= OnTopologyChanged;
        await _surfaceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { await _inner.DisposeAsync().ConfigureAwait(false); }
            finally { _surface?.Dispose(); _surface = null; }
        }
        finally { _surfaceGate.Release(); }
    }
}
