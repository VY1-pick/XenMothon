namespace Lumen.UI.Tray;

/// <summary>
/// Contract for managing the Windows taskbar notification area (System Tray) icon and context menu.
/// </summary>
public interface ISystemTrayManager : IDisposable
{
    bool IsInitialized { get; }
    void Initialize();
    void ShowNotification(string title, string message);
    void UpdateToolTip(string tooltip);

    event EventHandler? OpenRequested;
    event EventHandler? PauseResumeRequested;
    event EventHandler? StopRequested;
    event EventHandler? PlayRequested;
    event EventHandler? NextWallpaperRequested;
    event EventHandler? MuteRequested;
    event EventHandler? ExitRequested;
}
