using Lumen.Core.Enums;

namespace Lumen.Contracts.IPC;

/// <summary>
/// Telemetry payload streamed from Render Host to UI for diagnostics and the debug panel.
/// </summary>
public sealed record HostTelemetryDto(
    string MonitorDeviceId,
    WallpaperState CurrentState,
    float ActualFps,
    float FrameTimeMs,
    long GpuMemoryBytes,
    long CpuMemoryBytes,
    DateTime SampleTimeUtc) : IpcMessage;
