using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.UI.Desktop.Win32;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

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
    private readonly SemaphoreSlim _activityChanged = new(0, 1);
    private readonly WinEventProc _foregroundCallback;
    private IntPtr _foregroundHook;
    private IntPtr _windowHook, _minimizeHook;
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
        _foregroundCallback = (_, eventType, window, objectId, _, _, _) =>
        {
            if (eventType >= 0x8000 && (window == IntPtr.Zero || objectId != 0 || eventType is not (0x8002 or 0x8003 or 0x800B))) return;
            if (_activityChanged.CurrentCount == 0)
                try { _activityChanged.Release(); } catch (SemaphoreFullException) { }
        };
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;
            _pollingTask = Task.Run(() => PollingLoopAsync(token));
        }
        _logger.LogInformation("PerformanceMonitorService started.");
        _foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, _foregroundCallback, 0, 0, 0);
        _windowHook = SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, _foregroundCallback, 0, 0, 0);
        _minimizeHook = SetWinEventHook(0x16, 0x17, IntPtr.Zero, _foregroundCallback, 0, 0, 0);
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
        if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        if (_windowHook != IntPtr.Zero) { UnhookWinEvent(_windowHook); _windowHook = IntPtr.Zero; }
        if (_minimizeHook != IntPtr.Zero) { UnhookWinEvent(_minimizeHook); _minimizeHook = IntPtr.Zero; }
        cts?.Dispose();
        _logger.LogInformation("PerformanceMonitorService stopped.");
    }

    public async Task CheckOcclusionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var activeStates = _orchestrator.GetActiveMonitorStates();
        if (activeStates.Count == 0) return;
        var monitors = _topology.CurrentMonitors;
        await _desktopHost.EnsureAttachedAsync(ct).ConfigureAwait(false);

        var visibleWindows = new List<VisibleDesktopWindow>();
        // Desktop focus does not remove visible windows; custom low thresholds must still see them.
        DesktopNativeMethods.EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window) || GetClass(window) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if ((GetWindowLongPtr(window, -20).ToInt64() & 0x20) != 0) return true;
            if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (TryGetPhysicalBounds(window, out var bounds)) visibleWindows.Add(new(
                new MonitorBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height), IsZoomed(window)));
            return true;
        }, IntPtr.Zero);

        var performance = _settingsService.CurrentSettings.Performance;
        var foreground = GetForegroundWindow();
        bool applicationFocused = foreground != IntPtr.Zero && IsWindowVisible(foreground) &&
            GetClass(foreground) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
        bool pauseForFocus = applicationFocused && performance.PauseOnApplicationFocus;
        if (applicationFocused && !string.IsNullOrWhiteSpace(performance.PauseApplications))
        {
            GetWindowThreadProcessId(foreground, out uint processId);
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                pauseForFocus |= PlaybackApplicationRules.Matches(performance.PauseApplications, process.ProcessName);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        bool onBattery = IsOnBattery();
        bool saver = SystemParametersInfo(0x0072, 0, out bool runningSaver, 0) && runningSaver;
        bool powerSaver = GetSystemPowerStatus(out var power) && power.SystemStatusFlag == 1;
        pauseForFocus |= performance.PauseOnScreenSaver && saver || performance.PauseOnPowerSaver && powerSaver
            || performance.PauseOnRemoteSession && GetSystemMetrics(0x1000) != 0;
        int fps = onBattery ? Math.Min(_settingsService.CurrentSettings.GlobalTargetFps, Math.Clamp(performance.BatteryFps, 15, 240)) : _settingsService.CurrentSettings.GlobalTargetFps;
        // Reapply to newly loaded renderers too; changing an FPS limit is an inexpensive field update.
        await _orchestrator.SetGlobalFpsLimitAsync(fps, ct).ConfigureAwait(false);
        bool anyOccluded = false;

        foreach (var (monitorId, lifecycleState) in activeStates)
        {
            if (lifecycleState == XenMotion.Core.Enums.WallpaperState.Error)
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
            if (monitor is null && monitorId == "PRIMARY_MONITOR")
                monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            if (monitor == null) continue;

            PerformanceState state = DesktopVisibilityEvaluator.Evaluate(monitor.Bounds, visibleWindows, performance, onBattery);
            if (pauseForFocus) state = PerformanceState.PausedByUser;

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
        if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        if (_windowHook != IntPtr.Zero) { UnhookWinEvent(_windowHook); _windowHook = IntPtr.Zero; }
        if (_minimizeHook != IntPtr.Zero) { UnhookWinEvent(_minimizeHook); _minimizeHook = IntPtr.Zero; }
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
                await _activityChanged.WaitAsync(interval, ct).ConfigureAwait(false);
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

    private static string GetClass(IntPtr window)
    {
        var name = new System.Text.StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static bool TryGetPhysicalBounds(IntPtr window, out DesktopNativeMethods.RECT bounds) =>
        DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf<DesktopNativeMethods.RECT>()) == 0 || GetWindowRect(window, out bounds);

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out DesktopNativeMethods.RECT value, int size);

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

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
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

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
