namespace XenMotion.UI.Rendering.Video;

/// <summary>Pause without occupying a ThreadPool worker for each audio/video stream.</summary>
internal sealed class PlaybackSignal
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool> _ready = Create();
    public PlaybackSignal(bool ready) { if (ready) _ready.SetResult(true); }
    private static TaskCompletionSource<bool> Create() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitAsync(CancellationToken ct) { lock (_gate) return _ready.Task.WaitAsync(ct); }
    public void Set() { lock (_gate) _ready.TrySetResult(true); }
    public void Reset() { lock (_gate) { if (_ready.Task.IsCompleted) _ready = Create(); } }
}
