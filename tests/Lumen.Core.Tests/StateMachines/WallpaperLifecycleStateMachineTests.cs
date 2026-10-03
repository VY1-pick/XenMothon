using Lumen.Core.Enums;
using Lumen.Core.StateMachines;
using Xunit;

namespace Lumen.Core.Tests.StateMachines;

public class WallpaperLifecycleStateMachineTests
{
    [Fact]
    public void NewStateMachine_ShouldBeInUninitializedState()
    {
        var sm = new WallpaperLifecycleStateMachine();
        Assert.Equal(WallpaperState.Uninitialized, sm.CurrentState);
    }

    [Fact]
    public void ValidTransitions_ShouldProgressThroughFullLifecycle()
    {
        var sm = new WallpaperLifecycleStateMachine();

        // Uninitialized -> Loading
        Assert.True(sm.CanTransitionTo(WallpaperState.Loading));
        sm.TransitionTo(WallpaperState.Loading);
        Assert.Equal(WallpaperState.Loading, sm.CurrentState);

        // Loading -> Ready
        Assert.True(sm.CanTransitionTo(WallpaperState.Ready));
        sm.TransitionTo(WallpaperState.Ready);
        Assert.Equal(WallpaperState.Ready, sm.CurrentState);

        // Ready -> Playing
        Assert.True(sm.CanTransitionTo(WallpaperState.Playing));
        sm.TransitionTo(WallpaperState.Playing);
        Assert.Equal(WallpaperState.Playing, sm.CurrentState);

        // Playing -> PartiallyVisible
        Assert.True(sm.CanTransitionTo(WallpaperState.PartiallyVisible));
        sm.TransitionTo(WallpaperState.PartiallyVisible);
        Assert.Equal(WallpaperState.PartiallyVisible, sm.CurrentState);

        // PartiallyVisible -> Paused
        Assert.True(sm.CanTransitionTo(WallpaperState.Paused));
        sm.TransitionTo(WallpaperState.Paused);
        Assert.Equal(WallpaperState.Paused, sm.CurrentState);

        // Paused -> Playing
        Assert.True(sm.CanTransitionTo(WallpaperState.Playing));
        sm.TransitionTo(WallpaperState.Playing);
        Assert.Equal(WallpaperState.Playing, sm.CurrentState);

        // Playing -> Stopping
        Assert.True(sm.CanTransitionTo(WallpaperState.Stopping));
        sm.TransitionTo(WallpaperState.Stopping);
        Assert.Equal(WallpaperState.Stopping, sm.CurrentState);

        // Stopping -> Stopped
        Assert.True(sm.CanTransitionTo(WallpaperState.Stopped));
        sm.TransitionTo(WallpaperState.Stopped);
        Assert.Equal(WallpaperState.Stopped, sm.CurrentState);

        // Stopped -> Disposed
        Assert.True(sm.CanTransitionTo(WallpaperState.Disposed));
        sm.TransitionTo(WallpaperState.Disposed);
        Assert.Equal(WallpaperState.Disposed, sm.CurrentState);
    }

    [Fact]
    public void InvalidTransition_ShouldThrowInvalidStateTransitionException()
    {
        var sm = new WallpaperLifecycleStateMachine();

        // Cannot jump directly from Uninitialized to Playing
        Assert.False(sm.CanTransitionTo(WallpaperState.Playing));
        var ex = Assert.Throws<InvalidStateTransitionException>(() => sm.TransitionTo(WallpaperState.Playing));

        Assert.Equal(WallpaperState.Uninitialized, ex.CurrentState);
        Assert.Equal(WallpaperState.Playing, ex.RequestedState);
    }

    [Fact]
    public void TryTransitionTo_InvalidTransition_ShouldReturnFalseWithoutThrowing()
    {
        var sm = new WallpaperLifecycleStateMachine();

        bool success = sm.TryTransitionTo(WallpaperState.Paused, out var previousState);

        Assert.False(success);
        Assert.Equal(WallpaperState.Uninitialized, previousState);
        Assert.Equal(WallpaperState.Uninitialized, sm.CurrentState);
    }

    [Fact]
    public void StateChangedEvent_ShouldFireOnValidTransition()
    {
        var sm = new WallpaperLifecycleStateMachine();
        StateChangedEventArgs? receivedEvent = null;

        sm.StateChanged += (_, e) => receivedEvent = e;

        sm.TransitionTo(WallpaperState.Loading);

        Assert.NotNull(receivedEvent);
        Assert.Equal(WallpaperState.Uninitialized, receivedEvent.PreviousState);
        Assert.Equal(WallpaperState.Loading, receivedEvent.NewState);
    }

    [Fact]
    public void ErrorAndRecoveryCycle_ShouldSucceed()
    {
        var sm = new WallpaperLifecycleStateMachine();

        sm.TransitionTo(WallpaperState.Loading);
        sm.TransitionTo(WallpaperState.Error);
        Assert.Equal(WallpaperState.Error, sm.CurrentState);

        // Error -> Recovering
        Assert.True(sm.CanTransitionTo(WallpaperState.Recovering));
        sm.TransitionTo(WallpaperState.Recovering);
        Assert.Equal(WallpaperState.Recovering, sm.CurrentState);

        // Recovering -> Ready
        Assert.True(sm.CanTransitionTo(WallpaperState.Ready));
        sm.TransitionTo(WallpaperState.Ready);
        Assert.Equal(WallpaperState.Ready, sm.CurrentState);
    }

    [Fact]
    public void DisposedState_IsTerminal_NoFurtherTransitionsAllowed()
    {
        var sm = new WallpaperLifecycleStateMachine();
        sm.TransitionTo(WallpaperState.Disposed);

        Assert.False(sm.CanTransitionTo(WallpaperState.Loading));
        Assert.False(sm.CanTransitionTo(WallpaperState.Ready));
        Assert.False(sm.CanTransitionTo(WallpaperState.Playing));
        Assert.False(sm.CanTransitionTo(WallpaperState.Uninitialized));

        Assert.Throws<InvalidStateTransitionException>(() => sm.TransitionTo(WallpaperState.Loading));
    }

    [Fact]
    public void ConcurrentTransitions_ShouldPreserveIntegrity()
    {
        var sm = new WallpaperLifecycleStateMachine();
        sm.TransitionTo(WallpaperState.Loading);
        sm.TransitionTo(WallpaperState.Ready);
        sm.TransitionTo(WallpaperState.Playing);

        int successfulTransitions = 0;

        Parallel.For(0, 50, i =>
        {
            if (sm.TryTransitionTo(WallpaperState.Paused, out var previousState))
            {
                Interlocked.Increment(ref successfulTransitions);
            }
        });

        // Exactly one transition to Paused should succeed from Playing
        Assert.Equal(1, successfulTransitions);
        Assert.Equal(WallpaperState.Paused, sm.CurrentState);
    }
}
