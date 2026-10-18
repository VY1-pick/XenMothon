using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

/// <summary>
/// Polls the Win32 foreground window and reports the owning process name. Polling at a sub-second
/// interval costs far less than a hook plus its callbacks, and per-app wallpaper switching does not
/// need event-level latency.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class ForegroundApplicationMonitor : IForegroundApplicationMonitor, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly ILogger<ForegroundApplicationMonitor> _logger;
    private readonly object _sync = new();
    private Timer? _timer;
    private string? _current;
    private bool _disposed;

    public ForegroundApplicationMonitor(ILogger<ForegroundApplicationMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<string?>? ForegroundApplicationChanged;

    public string? CurrentProcessName
    {
        get { lock (_sync) return _current; }
    }

    public bool IsRunning
    {
        get { lock (_sync) return _timer is not null; }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _timer is not null) return;
            _current = ReadForegroundProcessName();
            _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }

        _logger.LogInformation("Foreground application monitor started (initial: {Process}).", _current ?? "desktop");
    }

    public void Stop()
    {
        Timer? timer;
        lock (_sync)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        Stop();
        GC.SuppressFinalize(this);
    }

    private void Poll()
    {
        if (_disposed) return;
        string? name = ReadForegroundProcessName();
        bool changed;
        lock (_sync)
        {
            changed = !string.Equals(_current, name, StringComparison.OrdinalIgnoreCase);
            if (changed) _current = name;
        }

        if (!changed) return;

        try
        {
            ForegroundApplicationChanged?.Invoke(this, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A foreground application handler failed.");
        }
    }

    private string? ReadForegroundProcessName()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;

            GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0) return null;

            using var process = Process.GetProcessById((int)processId);
            return ApplicationWallpaperRules.NormalizeProcessName(process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Explorer transitions, elevated processes and closing windows can all fail here.
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
