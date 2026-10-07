using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Rendering.Direct3D;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace XenMotion.UI.Rendering;

/// <summary>A single decoded bitmap, repainted on demand in the app-owned desktop surface.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class ImageWallpaperRenderer : IWallpaperRenderer
{
    private readonly object _syncLock = new();
    private readonly Direct3D11VideoPresenter _presenter;
    private GCHandle _pixels;
    private int _width, _height;
    private MonitorBounds _bounds;
    private PlaybackScalingMode _scaling;
    private WallpaperState _state;
    private bool _disposed;
    public WallpaperType SupportedType => WallpaperType.Image;
    public WallpaperState CurrentState { get { lock (_syncLock) return _state; } }
    public ImageWallpaperRenderer(ILogger<ImageWallpaperRenderer> logger) => _presenter = new(logger);
    public Task InitializeAsync(IntPtr hwnd, MonitorBounds bounds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _bounds = bounds; _presenter.Initialize(hwnd, bounds); _state = WallpaperState.Ready;
        }
        return Task.CompletedTask;
    }
    public async Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(mediaPath).AsTask(ct).ConfigureAwait(false);
        using var stream = await file.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        uint width = decoder.OrientedPixelWidth;
        uint height = decoder.OrientedPixelHeight;
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pixels.IsAllocated) _pixels.Free();
            _pixels = GCHandle.Alloc(data.DetachPixelData(), GCHandleType.Pinned);
            _width = (int)width; _height = (int)height; _scaling = settings.ScalingMode; _state = WallpaperState.Ready;
            _presenter.SetVisualAdjustments(settings);
        }
    }
    public void Redraw()
    {
        lock (_syncLock)
        {
            if (!_disposed && _pixels.IsAllocated && _state is WallpaperState.Playing or WallpaperState.Paused)
                _presenter.PresentFrame(_pixels.AddrOfPinnedObject(), _width, _height, _scaling);
        }
    }
    public Task PlayAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_syncLock) { ObjectDisposedException.ThrowIf(_disposed, this); _state = WallpaperState.Playing; }
        Redraw(); return Task.CompletedTask;
    }
    public Task PauseAsync(CancellationToken ct = default)
    {
        lock (_syncLock) { if (!_disposed) _state = WallpaperState.Paused; }
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_syncLock) { _state = WallpaperState.Stopped; if (_pixels.IsAllocated) _pixels.Free(); }
        return Task.CompletedTask;
    }
    public Task SetVolumeAsync(float volume, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetFpsLimitAsync(int fps, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResizeAsync(MonitorBounds bounds, CancellationToken ct = default)
    {
        lock (_syncLock) { _bounds = bounds; _presenter.Resize(bounds); }
        Redraw(); return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true; _state = WallpaperState.Disposed;
            if (_pixels.IsAllocated) _pixels.Free();
            _presenter.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
