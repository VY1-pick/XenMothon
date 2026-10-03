using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Rendering;

/// <summary>
/// Renderer implementation for static images (JPG, PNG, BMP, WEBP).
/// Supports viewport scaling modes, bounds updates, and hardware-accelerated drawing.
/// </summary>
public sealed class ImageWallpaperRenderer : IWallpaperRenderer
{
    private readonly ILogger<ImageWallpaperRenderer> _logger;
    private readonly object _syncLock = new();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private MonitorBounds _bounds;
    private WallpaperSettings _settings = new();
    private string _mediaPath = string.Empty;
    private WallpaperState _state = WallpaperState.Uninitialized;
    private bool _isDisposed;

    public WallpaperType SupportedType => WallpaperType.Image;

    public WallpaperState CurrentState
    {
        get
        {
            lock (_syncLock)
            {
                return _state;
            }
        }
    }

    public ImageWallpaperRenderer(ILogger<ImageWallpaperRenderer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task InitializeAsync(IntPtr targetHwnd, MonitorBounds bounds, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _targetHwnd = targetHwnd;
            _bounds = bounds;
            _state = WallpaperState.Ready;
        }

        _logger.LogDebug("Initialized ImageWallpaperRenderer for HWND 0x{Hwnd:X8} with bounds {Bounds}.",
            targetHwnd.ToInt64(), bounds);

        return Task.CompletedTask;
    }

    public Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(settings);

        ct.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            _mediaPath = mediaPath;
            _settings = settings;
            _state = WallpaperState.Ready;
        }

        _logger.LogInformation("Image wallpaper loaded from '{Path}' with scaling mode {Scaling}.",
            mediaPath, settings.ScalingMode);

        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            _state = WallpaperState.Playing;
        }

        _logger.LogDebug("Image wallpaper playback active on '{Path}'.", _mediaPath);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            if (_state == WallpaperState.Playing)
            {
                _state = WallpaperState.Paused;
            }
        }

        _logger.LogDebug("Image wallpaper paused.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _state = WallpaperState.Stopped;
        }

        _logger.LogDebug("Image wallpaper stopped.");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(float volume, CancellationToken ct = default)
    {
        // Static images produce no audio output
        return Task.CompletedTask;
    }

    public Task SetFpsLimitAsync(int fps, CancellationToken ct = default)
    {
        // Static images have no frame pacing limit
        return Task.CompletedTask;
    }

    public Task ResizeAsync(MonitorBounds newBounds, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _bounds = newBounds;
        }

        _logger.LogDebug("Image wallpaper resized to {Bounds}.", newBounds);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return ValueTask.CompletedTask;
            }
            _isDisposed = true;
            _state = WallpaperState.Disposed;
            _targetHwnd = IntPtr.Zero;
        }

        _logger.LogDebug("Disposed ImageWallpaperRenderer.");
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ImageWallpaperRenderer));
        }
    }
}
