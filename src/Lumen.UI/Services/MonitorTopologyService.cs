using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Services;

/// <summary>
/// Polls the Windows display topology and publishes stable monitor snapshots.
/// This is intentionally independent of WinUI so topology changes can be handled while the
/// main window is hidden in the tray.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MonitorTopologyService : IAsyncDisposable
{
    private readonly ILogger<MonitorTopologyService> _logger;
    private readonly object _syncLock = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _disposed;

    public MonitorTopologyService(ILogger<MonitorTopologyService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<MonitorInfo> CurrentMonitors
    {
        get { lock (_syncLock) return _monitors; }
    }

    public event EventHandler<MonitorTopologyChangedEventArgs>? TopologyChanged;

    public IReadOnlyList<MonitorInfo> Refresh()
    {
        ThrowIfDisposed();
        var monitors = EnumerateMonitors();
        bool changed;
        lock (_syncLock)
        {
            changed = !AreEquivalent(_monitors, monitors);
            _monitors = monitors;
        }

        if (changed)
        {
            _logger.LogInformation("Display topology changed: {Count} monitor(s).", monitors.Count);
            TopologyChanged?.Invoke(this, new MonitorTopologyChangedEventArgs(monitors));
        }

        return monitors;
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            if (_pollTask != null) return;
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollAsync(_cts.Token));
        }
        Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        Task? poll;
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            poll = _pollTask;
            _pollTask = null;
            _cts = null;
        }

        if (poll != null)
        {
            try { await poll.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Refresh();
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh Windows display topology.");
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var result = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            if (!GetMonitorInfo(hMonitor, ref info)) return true;

            int refreshRate = 60;
            try
            {
                var mode = new DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
                if (EnumDisplaySettingsEx(info.szDevice, ENUM_CURRENT_SETTINGS, ref mode, 0) && mode.dmDisplayFrequency > 0)
                    refreshRate = mode.dmDisplayFrequency;
            }
            catch { }

            double dpiScale = 1.0;
            try
            {
                if (GetDpiForMonitor(hMonitor, MonitorDpiType.Effective, out uint dpiX, out _) == 0)
                    dpiScale = dpiX / 96.0;
            }
            catch { }

            result.Add(new MonitorInfo
            {
                DeviceId = info.szDevice,
                DeviceName = info.szDevice,
                DisplayIndex = result.Count,
                Bounds = new MonitorBounds(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
                WorkingArea = new MonitorBounds(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
                IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                RefreshRateHz = refreshRate,
                DpiScale = dpiScale
            });
            return true;
        }, IntPtr.Zero);

        return result.OrderBy(m => m.DeviceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool AreEquivalent(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (!string.Equals(x.DeviceId, y.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                x.Bounds != y.Bounds || x.WorkingArea != y.WorkingArea ||
                x.IsPrimary != y.IsPrimary || x.RefreshRateHz != y.RefreshRateHz ||
                Math.Abs(x.DpiScale - y.DpiScale) > 0.01) return false;
        }
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitorTopologyService));
    }

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint MONITORINFOF_PRIMARY = 1;

    private enum MonitorDpiType { Effective = 0, Angular = 1, Raw = 2, Default = 0 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        public MONITORINFOEX() { cbSize = 0; rcMonitor = default; rcWork = default; dwFlags = 0; szDevice = string.Empty; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string deviceName, int modeNum, ref DEVMODE devMode, uint flags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
}

public sealed class MonitorTopologyChangedEventArgs : EventArgs
{
    public MonitorTopologyChangedEventArgs(IReadOnlyList<MonitorInfo> monitors) => Monitors = monitors;
    public IReadOnlyList<MonitorInfo> Monitors { get; }
}
