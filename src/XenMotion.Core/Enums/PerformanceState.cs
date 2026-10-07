namespace XenMotion.Core.Enums;

/// <summary>
/// Defines the system-level visibility and activity states evaluated by the activity manager.
/// </summary>
public enum PerformanceState
{
    ActiveVisible = 0,
    PartiallyVisible = 1,
    Covered = 2,
    FullscreenApplication = 3,
    Locked = 4,
    DisplayOff = 5,
    Sleep = 6,
    PausedByUser = 7,
    Error = 8,
    Stopped = 9
}
