namespace Lumen.Core.Models;

/// <summary>
/// Represents a connected physical or virtual display device detected by the Windows subsystem.
/// </summary>
public sealed class MonitorInfo
{
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public int DisplayIndex { get; init; }
    public MonitorBounds Bounds { get; init; }
    public MonitorBounds WorkingArea { get; init; }
    public bool IsPrimary { get; init; }
    public int RefreshRateHz { get; init; }
    public double DpiScale { get; init; } = 1.0;

    public override string ToString() =>
        $"{DeviceName} ({DeviceId}) [{Bounds.Width}x{Bounds.Height} @ {RefreshRateHz}Hz, Primary: {IsPrimary}]";
}
