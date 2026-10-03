using System.Runtime.Versioning;
using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.UI.Rendering.Direct3D;
using Lumen.UI.Rendering.Media;
using Lumen.UI.Rendering.Video;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Rendering;

/// <summary>
/// Production video wallpaper rendering engine.
/// Uses Windows Media Foundation decoding with a CPU-readable RGB32 presentation path.
/// Provides lifecycle safety, zero-busy-wait occlusion suspension, and resilient failure recovery.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class VideoWallpaperRenderer : IWallpaperRenderer
{
    private readonly ILogger<VideoWallpaperRenderer> _logger;
    private readonly object _syncLock = new();

    private readonly Direct3D11VideoPresenter _presenter;
    private readonly MediaFoundationPlaybackEngine _mediaEngine;
    private readonly VideoPlaybackClock _clock;
    private readonly ManualResetEventSlim _resumeEvent = new(true);

    private IntPtr _targetHwnd = IntPtr.Zero;
    private MonitorBounds _bounds;
    private WallpaperSettings _settings = new();
    private string _mediaPath = string.Empty;
    private WallpaperState _state = WallpaperState.Uninitialized;
    private float _volume = 1.0f;
    private int _fpsLimit = 60;

    private CancellationTokenSource? _renderLoopCts;
    private Task? _renderLoopTask;
    private bool _isDisposed;

    public WallpaperType SupportedType => WallpaperType.Video;

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

    public float CurrentVolume
    {
        get
        {
            lock (_syncLock)
            {
                return _volume;
            }
        }
    }

    public int CurrentFpsLimit
    {
        get
        {
            lock (_syncLock)
            {
                return _fpsLimit;
            }
        }
    }

    public VideoWallpaperRenderer(ILogger<VideoWallpaperRenderer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _presenter = new Direct3D11VideoPresenter(logger);
        _mediaEngine = new MediaFoundationPlaybackEngine(logger);
        _clock = new VideoPlaybackClock(_fpsLimit);
    }

    public Task InitializeAsync(IntPtr targetHwnd, MonitorBounds bounds, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            _targetHwnd = targetHwnd;
            _bounds = bounds;
            _presenter.Initialize(targetHwnd, bounds);

            if (_state == WallpaperState.Uninitialized)
            {
                _state = WallpaperState.Ready;
            }
        }

        _logger.LogDebug("Initialized VideoWallpaperRenderer for HWND 0x{Hwnd:X8} with bounds {Bounds}.",
            targetHwnd.ToInt64(), bounds);

        return Task.CompletedTask;
    }

    public async Task LoadAsync(string mediaPath, WallpaperSettings settings, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        // 1. Validate supported format and existence
        MediaFormatValidator.Validate(mediaPath);

        // 2. Stop and detach any active playback render loop
        await StopRenderLoopAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            ct.ThrowIfCancellationRequested();

            _mediaPath = mediaPath;
            _settings = settings;
            _volume = settings.IsMuted ? 0.0f : Math.Clamp(settings.Volume, 0.0f, 1.0f);
            _fpsLimit = settings.TargetFps > 0 ? settings.TargetFps : 60;
            _clock.FpsLimit = _fpsLimit;
            _clock.Reset();

            // 3. Open media via Media Foundation
            _mediaEngine.Open(mediaPath);

            _state = WallpaperState.Ready;
        }

        _logger.LogInformation("Loaded video wallpaper '{Path}' (FPS Limit: {Fps}, Volume: {Vol}, Scaling: {Scaling}).",
            mediaPath, _fpsLimit, _volume, settings.ScalingMode);
    }

    public Task PlayAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            if (_state == WallpaperState.Playing)
            {
                return Task.CompletedTask;
            }

            if (_state == WallpaperState.Paused)
            {
                // Resume existing render loop without creating a duplicate task
                _clock.Resume();
                _resumeEvent.Set();
                _state = WallpaperState.Playing;
                _logger.LogDebug("Video playback resumed for '{Path}'.", _mediaPath);
                return Task.CompletedTask;
            }

            // Start new playback loop if in Ready or Stopped state
            _clock.Reset();
            _resumeEvent.Set();
            _renderLoopCts = new CancellationTokenSource();
            var loopToken = _renderLoopCts.Token;

            _renderLoopTask = Task.Run(() => RenderLoopAsync(loopToken), CancellationToken.None);
            _state = WallpaperState.Playing;
        }

        _logger.LogDebug("Video playback started for '{Path}'.", _mediaPath);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            if (_state != WallpaperState.Playing)
            {
                return Task.CompletedTask;
            }

            // Block render loop: drops CPU and GPU decode consumption to 0%
            _resumeEvent.Reset();
            _clock.Pause();
            _state = WallpaperState.Paused;
        }

        _logger.LogDebug("Video playback paused for '{Path}'.", _mediaPath);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await StopRenderLoopAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            try
            {
                _mediaEngine.SeekToStart();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rewind media engine on stop.");
            }

            _state = WallpaperState.Stopped;
        }

        _logger.LogDebug("Video playback stopped for '{Path}'.", _mediaPath);
    }

    public Task SetVolumeAsync(float volume, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _volume = Math.Clamp(volume, 0.0f, 1.0f);
        }

        _logger.LogDebug("Video volume adjusted to {Volume}.", _volume);
        return Task.CompletedTask;
    }

    public Task SetFpsLimitAsync(int fps, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _fpsLimit = Math.Max(15, fps);
            _clock.FpsLimit = _fpsLimit;
        }

        _logger.LogDebug("Video FPS limit set to {Fps}.", _fpsLimit);
        return Task.CompletedTask;
    }

    public Task ResizeAsync(MonitorBounds newBounds, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _bounds = newBounds;
            _presenter.Resize(newBounds);
        }

        _logger.LogDebug("Video viewport bounds updated to {Bounds}.", newBounds);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _state = WallpaperState.Disposed;
        }

        // Cancel and await render loop
        await StopRenderLoopAsync().ConfigureAwait(false);

        _resumeEvent.Dispose();
        _mediaEngine.Dispose();
        _presenter.Dispose();

        _logger.LogDebug("Disposed VideoWallpaperRenderer.");
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting video render loop for '{Path}'.", _mediaPath);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 1. Wait if paused or occluded (0% CPU / GPU decode)
                _resumeEvent.Wait(ct);

                // 2. High-precision frame pacing
                await _clock.PacedWaitAsync(ct).ConfigureAwait(false);

                // 3. Read decoded frame from Media Foundation
                bool isLooping;
                PlaybackScalingMode scalingMode;

                lock (_syncLock)
                {
                    isLooping = _settings.Loop;
                    scalingMode = _settings.ScalingMode;
                }

                using var frame = _mediaEngine.ReadNextFrame(isLooping);
                if (frame != null)
                {
                    // 4. Present frame to Direct3D 11 viewport
                    _presenter.PresentFrame(
                        frame.DataPointer,
                        frame.Width,
                        frame.Height,
                        scalingMode);
                }
                else if (!isLooping)
                {
                    // Reached end of non-looping video: stop naturally
                    lock (_syncLock)
                    {
                        _state = WallpaperState.Stopped;
                    }
                    _logger.LogInformation("Playback completed for non-looping video '{Path}'.", _mediaPath);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Video render loop cancelled cooperatively for '{Path}'.", _mediaPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in video render loop for '{Path}'.", _mediaPath);
            lock (_syncLock)
            {
                _state = WallpaperState.Error;
            }
        }
    }

    private async Task StopRenderLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_syncLock)
        {
            cts = _renderLoopCts;
            loopTask = _renderLoopTask;
            _renderLoopCts = null;
            _renderLoopTask = null;
        }

        if (cts != null)
        {
            // Unblock resume event so loop can exit immediately
            _resumeEvent.Set();
            cts.Cancel();

            if (loopTask != null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception while awaiting render loop task completion.");
                }
            }

            cts.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(VideoWallpaperRenderer));
        }
    }
}
