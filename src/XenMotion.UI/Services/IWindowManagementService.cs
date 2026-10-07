namespace XenMotion.UI.Services;

/// <summary>
/// Service abstraction for controlling main window visibility and focus without coupling to XAML elements.
/// </summary>
public interface IWindowManagementService
{
    bool IsVisible { get; }
    IntPtr MainWindowHwnd { get; }

    void RegisterMainWindow(Microsoft.UI.Xaml.Window window);
    void SetMainWindowFactory(Action factory);
    void ShowAndActivate();
    void Hide();
}
