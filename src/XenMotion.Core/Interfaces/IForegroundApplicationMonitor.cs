namespace XenMotion.Core.Interfaces;

/// <summary>
/// Reports the process that currently owns the foreground window. Implemented on Windows; the
/// application layer consumes it to switch wallpapers for known applications.
/// </summary>
public interface IForegroundApplicationMonitor
{
    /// <summary>Raised with the normalized process name, or null when the desktop itself is focused.</summary>
    event EventHandler<string?>? ForegroundApplicationChanged;

    /// <summary>Process name of the current foreground application, without the .exe suffix.</summary>
    string? CurrentProcessName { get; }

    bool IsRunning { get; }

    void Start();
    void Stop();
}
