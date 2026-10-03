using Lumen.Core.Enums;

namespace Lumen.Core.StateMachines;

/// <summary>
/// Thread-safe implementation of the explicit wallpaper lifecycle state machine.
/// Enforces valid transition paths, isolates race conditions during rapid switching,
/// and publishes state-change notifications.
/// </summary>
public sealed class WallpaperLifecycleStateMachine : IWallpaperLifecycleStateMachine
{
    private readonly object _syncLock = new();
    private WallpaperState _currentState = WallpaperState.Uninitialized;

    public WallpaperState CurrentState
    {
        get
        {
            lock (_syncLock)
            {
                return _currentState;
            }
        }
    }

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public bool CanTransitionTo(WallpaperState targetState)
    {
        lock (_syncLock)
        {
            return IsValidTransition(_currentState, targetState);
        }
    }

    public WallpaperState TransitionTo(WallpaperState targetState)
    {
        WallpaperState previous;
        lock (_syncLock)
        {
            if (!IsValidTransition(_currentState, targetState))
            {
                throw new InvalidStateTransitionException(_currentState, targetState);
            }

            previous = _currentState;
            _currentState = targetState;
        }

        OnStateChanged(previous, targetState);
        return previous;
    }

    public bool TryTransitionTo(WallpaperState targetState, out WallpaperState previousState)
    {
        lock (_syncLock)
        {
            if (!IsValidTransition(_currentState, targetState))
            {
                previousState = _currentState;
                return false;
            }

            previousState = _currentState;
            _currentState = targetState;
        }

        OnStateChanged(previousState, targetState);
        return true;
    }

    private static bool IsValidTransition(WallpaperState current, WallpaperState target)
    {
        if (current == target)
        {
            return false;
        }

        if (current == WallpaperState.Disposed)
        {
            return false;
        }

        // Disposal is permitted from any non-disposed state.
        if (target == WallpaperState.Disposed)
        {
            return true;
        }

        return current switch
        {
            WallpaperState.Uninitialized => target is WallpaperState.Loading or WallpaperState.Ready,
            
            WallpaperState.Loading => target is WallpaperState.Ready
                                             or WallpaperState.Error
                                             or WallpaperState.Stopping
                                             or WallpaperState.Stopped,

            WallpaperState.Ready => target is WallpaperState.Playing
                                           or WallpaperState.Paused
                                           or WallpaperState.Stopping
                                           or WallpaperState.Stopped
                                           or WallpaperState.Error,

            WallpaperState.Playing => target is WallpaperState.PartiallyVisible
                                             or WallpaperState.Paused
                                             or WallpaperState.Stopping
                                             or WallpaperState.Stopped
                                             or WallpaperState.Error,

            WallpaperState.PartiallyVisible => target is WallpaperState.Playing
                                                      or WallpaperState.Paused
                                                      or WallpaperState.Stopping
                                                      or WallpaperState.Stopped
                                                      or WallpaperState.Error,

            WallpaperState.Paused => target is WallpaperState.Playing
                                            or WallpaperState.PartiallyVisible
                                            or WallpaperState.Stopping
                                            or WallpaperState.Stopped
                                            or WallpaperState.Error,

            WallpaperState.Stopping => target is WallpaperState.Stopped
                                              or WallpaperState.Error,

            WallpaperState.Stopped => target is WallpaperState.Loading
                                             or WallpaperState.Ready
                                             or WallpaperState.Playing
                                             or WallpaperState.Error,

            WallpaperState.Error => target is WallpaperState.Recovering
                                           or WallpaperState.Stopping
                                           or WallpaperState.Stopped,

            WallpaperState.Recovering => target is WallpaperState.Ready
                                                or WallpaperState.Loading
                                                or WallpaperState.Error
                                                or WallpaperState.Stopping
                                                or WallpaperState.Stopped,

            WallpaperState.Disposed => false,
            _ => false
        };
    }

    private void OnStateChanged(WallpaperState previous, WallpaperState current)
    {
        StateChanged?.Invoke(this, new StateChangedEventArgs(previous, current));
    }
}
