namespace XenMotion.UI.Rendering.Video;

/// <summary>Caps presentations on the media timeline, independent of OS wake-up jitter.</summary>
public sealed class VideoPresentationSchedule
{
    private double _nextMs, _lastMs;
    private int _fps;
    private bool _hasFrame;
    public void Reset() { _nextMs = _lastMs = 0; _fps = 0; _hasFrame = false; }

    public bool ShouldPresent(double mediaMs, double elapsedMs, double sourceFps, int fpsLimit)
    {
        int fps = Math.Max(1, fpsLimit);
        double interval = 1000.0 / fps;
        if (_hasFrame && elapsedMs - mediaMs > Math.Max(2000.0 / Math.Max(1, sourceFps), interval))
            return false;
        if (_fps != fps)
        {
            _nextMs = _hasFrame ? _lastMs + interval : mediaMs;
            _fps = fps;
        }
        if (mediaMs + 0.5 < _nextMs) return false;
        _nextMs += (Math.Floor(Math.Max(0, mediaMs + 0.5 - _nextMs) / interval) + 1) * interval;
        _lastMs = mediaMs;
        _hasFrame = true;
        return true;
    }
}
