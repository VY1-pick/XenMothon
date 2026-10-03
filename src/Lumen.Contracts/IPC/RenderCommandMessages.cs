using Lumen.Core.Enums;
using Lumen.Core.Models;

namespace Lumen.Contracts.IPC;

public sealed record LoadWallpaperCommand(
    string MonitorDeviceId,
    Guid WallpaperId,
    string FilePath,
    WallpaperType Type,
    WallpaperSettings Settings,
    MonitorBounds TargetBounds) : IpcMessage;

public sealed record PlayCommand(string MonitorDeviceId) : IpcMessage;

public sealed record PauseCommand(string MonitorDeviceId) : IpcMessage;

public sealed record StopCommand(string MonitorDeviceId) : IpcMessage;

public sealed record UpdateBoundsCommand(string MonitorDeviceId, MonitorBounds Bounds) : IpcMessage;

public sealed record SetAudioCommand(string MonitorDeviceId, float Volume, bool IsMuted) : IpcMessage;

public sealed record SetPerformanceStateCommand(string MonitorDeviceId, PerformanceState State) : IpcMessage;

public sealed record CommandResponse(Guid RequestMessageId, bool Success, string? ErrorMessage = null) : IpcMessage;
