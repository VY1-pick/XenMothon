using System.Runtime.Versioning;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Rendering.Direct3D;
using XenMotion.UI.Rendering.Media;
using XenMotion.UI.Rendering.Video;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Rendering;

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
    private readonly WallpaperAudioPlayer _audio;
    private readonly VideoPlaybackClock _clock;
    private readonly PlaybackSignal _resumeEvent = new(true);

    private IntPtr _targetHwnd = IntPtr.Zero;
    private MonitorBounds _bounds;
    private WallpaperSettings _settings = new();
    private string _mediaPath = string.Empty;
    private WallpaperState _state = WallpaperState.Uninitialized;
    private float _volume = 1.0f;
    private int _fpsLimit = 60;
    private int _calibratedFps = int.MaxValue;
    private long _presentedFrames;
    internal long PresentedFrameCount => Interlocked.Read(ref _presentedFrames);
    private readonly Action<string>? _calibrationCompleted;
    private readonly double _calibrationBudget;
    private readonly Func<string, WallpaperSettings, MonitorBounds, CancellationToken, Task<WallpaperSettings>>? _adaptiveQuality;
    internal string? CalibrationSummary { get; private set; }

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
        get { lock (_syncLock) return _volume; }
    }

    public bool HasAudio => _audio.HasAudio;
    public long QueuedAudioBytes => _audio.QueuedBytes;
    public long DecodedFrameCount => _mediaEngine.DecodedFrameCount;
    internal long GpuDecodedFrameCount => _mediaEngine.GpuDecodedFrameCount;

    public int CurrentFpsLimit
    {
        get
        {
            lock (_syncLock)
            {
                return Math.Min(_fpsLimit, _calibratedFps);
            }
        }
    }

    public VideoWallpaperRenderer(ILogger<VideoWallpaperRenderer> logger, Action<string>? calibrationCompleted = null, double calibrationBudget = 250, Func<string, WallpaperSettings, MonitorBounds, CancellationToken, Task<WallpaperSettings>>? adaptiveQuality = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _calibrationCompleted = calibrationCompleted;
        _adaptiveQuality = adaptiveQuality;
        _calibrationBudget = Math.Clamp(calibrationBudget, 25, 250);
        _presenter = new Direct3D11VideoPresenter(logger);
        _mediaEngine = new MediaFoundationPlaybackEngine(logger);
        _audio = new WallpaperAudioPlayer(logger);
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

        if (settings.AutoTunePlayback && _adaptiveQuality is not null)
            settings = await _adaptiveQuality(mediaPath, settings.Clone(), _bounds, ct).ConfigureAwait(false);

        // 2. Stop and detach any active playback render loop
        await StopRenderLoopAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            ct.ThrowIfCancellationRequested();

            _mediaPath = mediaPath;
            _settings = settings.Clone();
            _calibratedFps = int.MaxValue;
            CalibrationSummary = null;
            _presenter.SetVisualAdjustments(settings);
            _volume = settings.IsMuted ? 0.0f : Math.Clamp(settings.Volume, 0.0f, 1.0f);
            _fpsLimit = settings.TargetFps > 0 ? settings.TargetFps : 60;
            _clock.FpsLimit = _fpsLimit;
            _clock.Reset();

            // 3. Open media via Media Foundation
            _mediaEngine.Open(mediaPath, settings.UseHardwareDecoding ? _presenter.Device : IntPtr.Zero, settings.VideoHeightLimit);
            // Decode on the source cadence, not the display cap (which used to change playback speed).
            _clock.FpsLimit = (int)Math.Clamp(Math.Round(_mediaEngine.NativeFps), 1, 240);

            _state = WallpaperState.Ready;
        }

        if (settings.AutoTunePlayback && _adaptiveQuality is null)
            await Task.Run(() => CalibratePlayback(ct), ct).ConfigureAwait(false);
        await _audio.OpenAsync(mediaPath, _volume, settings.PlaybackSpeed).ConfigureAwait(false);
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
                _audio.Play();
                _clock.Resume();
                _resumeEvent.Set();
                _state = WallpaperState.Playing;
                _logger.LogDebug("Video playback resumed for '{Path}'.", _mediaPath);
                return Task.CompletedTask;
            }

            _audio.Play();
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
    private void CalibratePlayback(CancellationToken ct)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        bool fits = false;
        double decode = 0, render = 0;
        int samples = 0;
        var original = _settings.Clone();
        try
        {
            // Measure the actual source, codec, output size and GPU completion, not just installed RAM.
            // ponytail: bounded loading calibration; continuous telemetry only if changing scene costs warrant it.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var decodeTimes = new List<double>();
                var renderTimes = new List<double>();
                for (int i = 0; i < 10 && total.ElapsedMilliseconds < 1200; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using var frame = _mediaEngine.ReadNextFrame(false);
                    if (frame == null) break;
                    double decoding = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    _presenter.PresentFrame(frame, _settings.ScalingMode);
                    try { _presenter.CompleteCalibrationFrame(ct); }
                    catch (TimeoutException) { decodeTimes.Add(decoding); renderTimes.Add(200); break; }
                    if (i >= 2) { decodeTimes.Add(decoding); renderTimes.Add(watch.Elapsed.TotalMilliseconds); }
                }
                samples = decodeTimes.Count;
                if (samples == 0) break;
                decode = PlaybackCalibration.Percentile90(decodeTimes);
                render = PlaybackCalibration.Percentile90(renderTimes);
                fits = PlaybackCalibration.FitsBudget(decode, render, _mediaEngine.NativeFps * _settings.PlaybackSpeed, _settings.TargetFps, _calibrationBudget);
                var lower = PlaybackCalibration.LowerBudget(_settings);
                if (fits || total.ElapsedMilliseconds >= 1200 || attempt == 3 ||
                    (lower.VideoHeightLimit == _settings.VideoHeightLimit && lower.RenderScale == _settings.RenderScale && lower.TargetFps == _settings.TargetFps)) break;
                _settings = lower;
                _presenter.SetVisualAdjustments(_settings);
                _mediaEngine.Open(_mediaPath, _settings.UseHardwareDecoding ? _presenter.Device : IntPtr.Zero, _settings.VideoHeightLimit);
            }
            _calibratedFps = _settings.TargetFps;
            CalibrationSummary = $"Measured playback: {_mediaEngine.VideoWidth}×{_mediaEngine.VideoHeight}, {CurrentFpsLimit} fps ceiling, {_settings.RenderScale * 100:0}% render; p90 decode {decode:0.0}ms, render/GPU completion {render:0.0}ms ({samples} samples). " +
                (samples == 0 ? "Insufficient samples; starting recommendation retained." : fits ? "Within background headroom budget." : "Above background budget; a lighter source video may be needed.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playback calibration unavailable; retain current playback settings.");
            _settings = original;
            _calibratedFps = int.MaxValue;
            _presenter.SetVisualAdjustments(original);
            _mediaEngine.Open(_mediaPath, original.UseHardwareDecoding ? _presenter.Device : IntPtr.Zero, original.VideoHeightLimit);
            CalibrationSummary = "Playback measurement unavailable; using the current settings.";
        }
        finally { _mediaEngine.SeekToStart(); }
        _logger.LogInformation("{Calibration}", CalibrationSummary);
        _calibrationCompleted?.Invoke(CalibrationSummary!);
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

            // Suspend audio output and video decoding while the desktop is covered.
            _audio.Pause();
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
                _audio.Pause(); _audio.Rewind();
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
            _audio.SetVolume(_volume);
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

        await _audio.DisposeAsync().ConfigureAwait(false);
        _mediaEngine.Dispose();
        _presenter.Dispose();

        _logger.LogDebug("Disposed VideoWallpaperRenderer.");
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting video render loop for '{Path}'.", _mediaPath);

        try
        {
            var schedule = new VideoPresentationSchedule();
            double lastFrameMs = 0;
            while (!ct.IsCancellationRequested)
            {
                // 1. Wait if paused or occluded (0% CPU / GPU decode)
                await _resumeEvent.WaitAsync(ct).ConfigureAwait(false);

                // 3. Read decoded frame from Media Foundation
                bool isLooping;
                PlaybackScalingMode scalingMode;
                double speed;

                lock (_syncLock)
                {
                    isLooping = _settings.Loop;
                    scalingMode = _settings.ScalingMode;
                    speed = Math.Clamp(_settings.PlaybackSpeed, 0.25f, 2f);
                }

                using var frame = _mediaEngine.ReadNextFrame(isLooping);
                if (frame == null)
                {
                    if (_mediaEngine.EndOfStream)
                    {
                        // Hold the final frame for its duration before rewinding audio.
                        // Resetting at its timestamp used to truncate the last ~one-frame of sound.
                        double cycleEndMs = Math.Max(_mediaEngine.Duration.TotalMilliseconds,
                            lastFrameMs * speed + 1000.0 / Math.Max(1, _mediaEngine.NativeFps)) / speed;
                        await _clock.WaitUntilAsync(cycleEndMs, ct, _resumeEvent.WaitAsync).ConfigureAwait(false);
                        await _resumeEvent.WaitAsync(ct).ConfigureAwait(false);
                        if (!isLooping)
                        {
                            _audio.Pause();
                            lock (_syncLock) _state = WallpaperState.Stopped;
                            break;
                        }
                        _audio.Rewind();
                        _clock.Reset();
                        schedule.Reset();
                        lastFrameMs = 0;
                    }
                    else await Task.Delay(10, ct).ConfigureAwait(false);
                    continue;
                }
                lastFrameMs = frame.Timestamp.TotalMilliseconds / speed;
                double elapsedMs = await _clock.WaitUntilAsync(lastFrameMs, ct, _resumeEvent.WaitAsync).ConfigureAwait(false);
                await _resumeEvent.WaitAsync(ct).ConfigureAwait(false);
                if (schedule.ShouldPresent(lastFrameMs, elapsedMs, _mediaEngine.NativeFps * speed, CurrentFpsLimit))
                {
                    // 4. Present frame to Direct3D 11 viewport
                    _presenter.PresentFrame(frame, scalingMode);
                    Interlocked.Increment(ref _presentedFrames);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Video render loop cancelled cooperatively for '{Path}'.", _mediaPath);
        }
        catch (Exception ex)
        {
            _audio.Pause();
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
