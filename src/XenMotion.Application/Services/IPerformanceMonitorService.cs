namespace XenMotion.Application.Services;

/// <summary>
/// Contract for monitoring foreground applications and fullscreen occlusion states to optimize power and hardware usage.
/// </summary>
public interface IPerformanceMonitorService : IAsyncDisposable
{
    bool IsRunning { get; }

    void Start();

    void Stop();

    Task CheckOcclusionAsync(CancellationToken ct = default);

    event EventHandler<bool>? FullscreenOcclusionChanged;
}
