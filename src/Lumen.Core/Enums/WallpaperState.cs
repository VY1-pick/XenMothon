namespace Lumen.Core.Enums;

/// <summary>
/// Defines the explicit lifecycle states of a wallpaper instance.
/// </summary>
public enum WallpaperState
{
    Uninitialized = 0,
    Loading = 1,
    Ready = 2,
    Playing = 3,
    PartiallyVisible = 4,
    Paused = 5,
    Stopping = 6,
    Stopped = 7,
    Error = 8,
    Recovering = 9,
    Disposed = 10
}
