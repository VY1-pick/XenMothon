namespace Lumen.UI.Services;

/// <summary>
/// Service abstraction for controlling main window visibility and focus without coupling to XAML elements.
/// </summary>
public interface IWindowManagementService
{
    bool IsVisible { get; }
    IntPtr MainWindowHwnd { get; }

    void RegisterMainWindow(Microsoft.UI.Xaml.Window window);
    void ShowAndActivate();
    void Hide();
}
