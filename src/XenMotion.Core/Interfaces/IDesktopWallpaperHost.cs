using XenMotion.Core.Models;

namespace XenMotion.Core.Interfaces;

/// <summary>
/// Abstraction for Windows desktop shell integration (WorkerW/Progman docking and lifecycle).
/// </summary>
public interface IDesktopWallpaperHost : IAsyncDisposable
{
    IntPtr DesktopWorkerWHwnd { get; }
    bool IsAttached { get; }

    Task<bool> AttachToDesktopAsync(CancellationToken ct = default);
    Task<bool> EnsureAttachedAsync(CancellationToken ct = default);
    Task DetachAsync(CancellationToken ct = default);
    Task HandleExplorerRestartAsync(CancellationToken ct = default);
    Task UpdateMonitorLayoutAsync(IReadOnlyList<MonitorInfo> monitors, CancellationToken ct = default);

    event EventHandler? Attached;
    event EventHandler? Detached;
    event EventHandler? ShellRestartDetected;
}
