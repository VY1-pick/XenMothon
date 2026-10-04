using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.UI.Desktop.Win32;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Desktop;

/// <summary>
/// Native Windows desktop shell integration service.
/// Uses the 0x052C Progman message to spawn a dedicated WorkerW window directly behind desktop icons,
/// and manages desktop re-attachment upon Explorer shell restarts.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DesktopWallpaperHost : IDesktopWallpaperHost
{
    private readonly ILogger<DesktopWallpaperHost> _logger;
    private readonly object _syncLock = new();
    private IntPtr _workerwHwnd = IntPtr.Zero;
    private bool _isAttached;
    private bool _isDisposed;
    private IReadOnlyList<MonitorInfo> _monitors = [];

    public IntPtr DesktopWorkerWHwnd
    {
        get
        {
            lock (_syncLock)
            {
                return _workerwHwnd;
            }
        }
    }

    public bool IsAttached
    {
        get
        {
            lock (_syncLock)
            {
                return _isAttached;
            }
        }
    }

    public event EventHandler? Attached;
    public event EventHandler? Detached;
    public event EventHandler? ShellRestartDetected;

    public DesktopWallpaperHost(ILogger<DesktopWallpaperHost> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> AttachToDesktopAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            if (_isAttached && _workerwHwnd != IntPtr.Zero)
            {
                return Task.FromResult(true);
            }
        }

        try
        {
            IntPtr workerw = FindOrCreateWorkerW();
            if (workerw == IntPtr.Zero)
            {
                _logger.LogWarning("Failed to locate or spawn WorkerW window behind desktop icons.");
                return Task.FromResult(false);
            }

            lock (_syncLock)
            {
                _workerwHwnd = workerw;
                _isAttached = true;
            }

            _logger.LogInformation("Successfully docked to WorkerW desktop window (HWND: 0x{Hwnd:X8}).", workerw.ToInt64());
            Attached?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while attaching to desktop WorkerW.");
            return Task.FromResult(false);
        }
    }

    public Task<bool> EnsureAttachedAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        IntPtr worker;
        lock (_syncLock) worker = _workerwHwnd;
        if (_isAttached && worker != IntPtr.Zero && DesktopNativeMethods.IsWindow(worker))
            return Task.FromResult(true);

        _logger.LogWarning("Desktop WorkerW handle is no longer valid. Re-attaching to Explorer desktop shell.");
        return AttachToDesktopAsync(ct);
    }

    public Task DetachAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        bool wasAttached;
        lock (_syncLock)
        {
            wasAttached = _isAttached;
            _workerwHwnd = IntPtr.Zero;
            _isAttached = false;
        }

        if (wasAttached)
        {
            _logger.LogInformation("Detached from WorkerW desktop shell.");
            Detached?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public async Task HandleExplorerRestartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _logger.LogWarning("Windows Explorer shell restart detected. Re-acquiring WorkerW handle.");

        ShellRestartDetected?.Invoke(this, EventArgs.Empty);

        await DetachAsync(ct).ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false); // Allow Explorer to settle
        await AttachToDesktopAsync(ct).ConfigureAwait(false);
    }

    public Task UpdateMonitorLayoutAsync(IReadOnlyList<MonitorInfo> monitors, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(monitors);

        lock (_syncLock)
        {
            _monitors = monitors.ToList();
        }

        _logger.LogInformation("Updated desktop monitor layout: {Count} active displays registered.", monitors.Count);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return ValueTask.CompletedTask;
            }
            _isDisposed = true;
            _workerwHwnd = IntPtr.Zero;
            _isAttached = false;
        }

        Detached?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }

    private IntPtr FindOrCreateWorkerW()
    {
        IntPtr progman = DesktopNativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            _logger.LogWarning("Progman window not found.");
            return IntPtr.Zero;
        }

        // Send 0x052C to Progman to spawn WorkerW behind SHELLDLL_DefView
        DesktopNativeMethods.SendMessageTimeout(
            progman,
            DesktopNativeMethods.WM_SPAWN_WORKER,
            new IntPtr(0xD),
            new IntPtr(0x1),
            DesktopNativeMethods.SMTO_NORMAL,
            1000,
            out _);

        IntPtr workerw = IntPtr.Zero;

        // Iterate through all top-level windows to find the spawned WorkerW behind SHELLDLL_DefView
        DesktopNativeMethods.EnumWindows((hwnd, _) =>
        {
            IntPtr shellView = DesktopNativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                // Find next WorkerW window directly behind desktop icons
                workerw = DesktopNativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        // Fallback: If not found as next sibling, check all WorkerW windows with desktop-sized client rect
        if (workerw == IntPtr.Zero)
        {
            DesktopNativeMethods.EnumWindows((hwnd, _) =>
            {
                IntPtr shellView = DesktopNativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView == IntPtr.Zero)
                {
                    IntPtr defView = DesktopNativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (defView != IntPtr.Zero)
                    {
                        workerw = defView;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        // Windows 11 24H2+: the WorkerW is a child of Progman instead of a top-level sibling.
        if (workerw == IntPtr.Zero)
        {
            workerw = DesktopNativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        }

        return workerw;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(DesktopWallpaperHost));
        }
    }
}
