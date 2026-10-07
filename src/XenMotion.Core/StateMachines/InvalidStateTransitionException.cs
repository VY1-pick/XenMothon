using XenMotion.Core.Enums;

namespace XenMotion.Core.StateMachines;

/// <summary>
/// Thrown when an illegal lifecycle transition is attempted on a wallpaper instance.
/// </summary>
public sealed class InvalidStateTransitionException : InvalidOperationException
{
    public WallpaperState CurrentState { get; }
    public WallpaperState RequestedState { get; }

    public InvalidStateTransitionException(WallpaperState current, WallpaperState requested)
        : base($"Cannot transition wallpaper lifecycle state from '{current}' to '{requested}'.")
    {
        CurrentState = current;
        RequestedState = requested;
    }
}
