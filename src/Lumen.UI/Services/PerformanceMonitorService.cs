using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.UI.Desktop.Win32;
using Lumen.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Services;

/// <summary>
/// Windows activity monitor for fullscreen/maximized windows, coverage percentage,
/// battery state, display topology and desktop-shell recovery.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class PerformanceMonitorService : IPerformanceMonitorService
{
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly ILogger<PerformanceMonitorService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly MonitorTopologyService _topology;
    private readonly IDesktopWallpaperHost _desktopHost;
    private readonly object _syncLock = new();
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _lastOcclusionState;
    private readonly Dictionary<string, DateTimeOffset> _lastRecoveryAttempts = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning { get { lock (_syncLock) return _isRunning; } }
    public event EventHandler<bool>? FullscreenOcclusionChanged;

    public PerformanceMonitorService(
        IWallpaperEngineOrchestrator orchestrator,
        ISettingsService settingsService,
        MonitorTopologyService topology,
        IDesktopWallpaperHost desktopHost,
        ILogger<PerformanceMonitorService> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _desktopHost = desktopHost ?? throw new ArgumentNullException(nameof(desktopHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _pollingCts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollingLoopAsync(_pollingCts.Token));
        }
        _logger.LogInformation("PerformanceMonitorService started.");
    }

    public void Stop()
    {
        ThrowIfDisposed();
        CancellationTokenSource? cts;
        lock (_syncLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            cts = _pollingCts;
            _pollingCts = null;
        }
        cts?.Cancel();
        cts?.Dispose();
        _logger.LogInformation("PerformanceMonitorService stopped.");
    }

    public async Task CheckOcclusionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var monitors = _topology.Refresh();
        await _desktopHost.EnsureAttachedAsync(ct).ConfigureAwait(false);
        await _desktopHost.UpdateMonitorLayoutAsync(monitors, ct).ConfigureAwait(false);

        IntPtr foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == IntPtr.Zero || foregroundHwnd == GetShellWindow() || foregroundHwnd == GetDesktopWindow())
        {
            await RestoreActiveVisibleAsync(ct).ConfigureAwait(false);
            return;
        }

        if (!GetWindowRect(foregroundHwnd, out var windowRect)) return;

        var performance = _settingsService.CurrentSettings.Performance;
        bool onBattery = IsOnBattery();
        bool anyOccluded = false;
        bool isMaximized = IsZoomed(foregroundHwnd);

        foreach (var (monitorId, lifecycleState) in _orchestrator.GetActiveMonitorStates())
        {
            if (lifecycleState == Lumen.Core.Enums.WallpaperState.Error)
            {
                var now = DateTimeOffset.UtcNow;
                if (!_lastRecoveryAttempts.TryGetValue(monitorId, out var last) || now - last >= TimeSpan.FromSeconds(10))
                {
                    _lastRecoveryAttempts[monitorId] = now;
                    await _orchestrator.RecoverMonitorAsync(monitorId, ct).ConfigureAwait(false);
                }
                continue;
            }

            var monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceId, monitorId, StringComparison.OrdinalIgnoreCase));
            if (monitor == null) continue;

            double coverage = CalculateCoveragePercentage(windowRect, monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Right, monitor.Bounds.Bottom);
            bool fullyCovered = coverage >= 99.9;
            bool covered = coverage >= Math.Clamp(performance.CoveredThresholdPercentage, 1, 100);
            bool fullscreen = fullyCovered && IsFullscreenWindow(windowRect, monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Right, monitor.Bounds.Bottom);

            PerformanceState state = PerformanceState.ActiveVisible;
            if (onBattery && performance.PauseOnBattery)
                state = PerformanceState.PausedByUser;
            else if (fullscreen && performance.PauseOnFullscreen)
                state = PerformanceState.FullscreenApplication;
            else if (isMaximized && coverage >= 99.0 && performance.PauseOnMaximized)
                state = PerformanceState.FullscreenApplication;
            else if (covered && performance.PauseWhenCovered)
                state = PerformanceState.Covered;
            else if (coverage > 0)
                state = PerformanceState.PartiallyVisible;

            anyOccluded |= state is PerformanceState.Covered or PerformanceState.FullscreenApplication or PerformanceState.PausedByUser;
            await _orchestrator.UpdatePerformanceStateAsync(monitorId, state, ct).ConfigureAwait(false);
        }

        if (_lastOcclusionState != anyOccluded)
        {
            _lastOcclusionState = anyOccluded;
            FullscreenOcclusionChanged?.Invoke(this, anyOccluded);
            _logger.LogInformation("Performance occlusion state changed: {Occluded}.", anyOccluded);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? polling;
        CancellationTokenSource? cts;
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isRunning = false;
            cts = _pollingCts;
            _pollingCts = null;
            polling = _pollingTask;
            _pollingTask = null;
        }
        cts?.Cancel();
        cts?.Dispose();
        if (polling != null)
        {
            try { await polling.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckOcclusionAsync(ct).ConfigureAwait(false);
                int interval = Math.Clamp(_settingsService.CurrentSettings.Performance.HeartbeatIntervalMs, 250, 5000);
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during performance/topology polling.");
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RestoreActiveVisibleAsync(CancellationToken ct)
    {
        foreach (var (monitorId, _) in _orchestrator.GetActiveMonitorStates())
            await _orchestrator.UpdatePerformanceStateAsync(monitorId, PerformanceState.ActiveVisible, ct).ConfigureAwait(false);

        if (_lastOcclusionState)
        {
            _lastOcclusionState = false;
            FullscreenOcclusionChanged?.Invoke(this, false);
        }
    }

    private static double CalculateCoveragePercentage(DesktopNativeMethods.RECT window, int left, int top, int right, int bottom)
    {
        long iw = Math.Max(0, Math.Min(window.Right, right) - Math.Max(window.Left, left));
        long ih = Math.Max(0, Math.Min(window.Bottom, bottom) - Math.Max(window.Top, top));
        long intersection = iw * ih;
        long area = Math.Max(1L, (long)(right - left) * (bottom - top));
        return intersection * 100.0 / area;
    }

    private static bool IsFullscreenWindow(DesktopNativeMethods.RECT window, int left, int top, int right, int bottom) =>
        window.Left <= left && window.Top <= top && window.Right >= right && window.Bottom >= bottom;

    private static bool IsOnBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return false;
        return status.ACLineStatus == 0;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(PerformanceMonitorService));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out DesktopNativeMethods.RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);
}
