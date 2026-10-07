using System.Diagnostics;

namespace XenMotion.UI.Rendering.Video;

/// <summary>
/// High-resolution playback clock and frame pacer for video wallpaper rendering.
/// Regulates frame rate presentation, avoids busy loops, and maintains synchronization across pause/resume cycles.
/// </summary>
public sealed class VideoPlaybackClock
{
    private static readonly double StopwatchTickFrequencyMs = 1000.0 / Stopwatch.Frequency;
    private readonly object _syncLock = new();
    private long _baseTimestamp;
    private long _pausedTimestamp;
    private long _totalPausedDurationTicks;
    private long _lastFrameTimestamp;
    private double _targetFrameIntervalMs;
    private int _fpsLimit = 60;
    private bool _isPaused;

    public int FpsLimit
    {
        get
        {
            lock (_syncLock)
            {
                return _fpsLimit;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _fpsLimit = Math.Clamp(value, 1, 240);
                _targetFrameIntervalMs = 1000.0 / _fpsLimit;
            }
        }
    }

    public VideoPlaybackClock(int fpsLimit = 60)
    {
        FpsLimit = fpsLimit;
        Reset();
    }

    /// <summary>
    /// Resets the clock to zero elapsed time.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            _baseTimestamp = Stopwatch.GetTimestamp();
            _pausedTimestamp = 0;
            _totalPausedDurationTicks = 0;
            _lastFrameTimestamp = _baseTimestamp;
            _isPaused = false;
        }
    }

    /// <summary>
    /// Freezes the clock at the current position.
    /// </summary>
    public void Pause()
    {
        lock (_syncLock)
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _pausedTimestamp = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>
    /// Resumes the clock from the paused position, compensating for the pause duration.
    /// </summary>
    public void Resume()
    {
        lock (_syncLock)
        {
            if (_isPaused)
            {
                _isPaused = false;
                long now = Stopwatch.GetTimestamp();
                _totalPausedDurationTicks += (now - _pausedTimestamp);
                _lastFrameTimestamp = now;
            }
        }
    }

    /// <summary>
    /// Asynchronously waits until the next frame's target presentation time has arrived.
    /// Returns the elapsed playback time since start in milliseconds.
    /// </summary>
    public async Task<double> PacedWaitAsync(CancellationToken ct = default)
    {
        double targetIntervalMs;
        long lastFrame;

        lock (_syncLock)
        {
            targetIntervalMs = _targetFrameIntervalMs;
            lastFrame = _lastFrameTimestamp;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedSinceLastFrameMs = (now - lastFrame) * StopwatchTickFrequencyMs;
        double remainingMs = targetIntervalMs - elapsedSinceLastFrameMs;

        // Use asynchronous delays only. The previous implementation used a tight
        // Thread.Yield loop for sub-millisecond pacing, which still consumed CPU while
        // a wallpaper was visible. A small final delay trades a tiny amount of timing
        // precision for substantially lower CPU overhead.
        while (remainingMs > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (remainingMs > 2.0)
            {
                int delayMs = Math.Max(1, (int)Math.Floor(remainingMs - 1.0));
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
            }

            now = Stopwatch.GetTimestamp();
            remainingMs = targetIntervalMs - (now - lastFrame) * StopwatchTickFrequencyMs;
        }

        lock (_syncLock)
        {
            _lastFrameTimestamp = now;
            long activeTicks = now - _baseTimestamp - _totalPausedDurationTicks;
            return Math.Max(0.0, activeTicks * StopwatchTickFrequencyMs);
        }
    }

    /// <summary>Waits for the media timestamp without accumulating per-frame delay drift.</summary>
    public async Task<double> WaitUntilAsync(double mediaTimeMs, CancellationToken ct, Action<CancellationToken>? waitForResume = null)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            waitForResume?.Invoke(ct);
            double elapsed;
            lock (_syncLock)
            {
                long now = _isPaused ? _pausedTimestamp : Stopwatch.GetTimestamp();
                elapsed = (now - _baseTimestamp - _totalPausedDurationTicks) * StopwatchTickFrequencyMs;
            }
            double remaining = mediaTimeMs - elapsed;
            if (remaining <= 0) return elapsed;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(remaining, 1, 100)), ct).ConfigureAwait(false);
        }
    }
}
