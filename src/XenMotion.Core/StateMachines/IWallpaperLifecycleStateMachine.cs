using XenMotion.Core.Enums;

namespace XenMotion.Core.StateMachines;

/// <summary>
/// Contract for observing and driving thread-safe wallpaper lifecycle state transitions.
/// </summary>
public interface IWallpaperLifecycleStateMachine
{
    WallpaperState CurrentState { get; }
    bool CanTransitionTo(WallpaperState targetState);
    WallpaperState TransitionTo(WallpaperState targetState);
    bool TryTransitionTo(WallpaperState targetState, out WallpaperState previousState);

    event EventHandler<StateChangedEventArgs>? StateChanged;
}

public sealed class StateChangedEventArgs : EventArgs
{
    public WallpaperState PreviousState { get; }
    public WallpaperState NewState { get; }
    public DateTime TimestampUtc { get; }

    public StateChangedEventArgs(WallpaperState previousState, WallpaperState newState)
    {
        PreviousState = previousState;
        NewState = newState;
        TimestampUtc = DateTime.UtcNow;
    }
}
