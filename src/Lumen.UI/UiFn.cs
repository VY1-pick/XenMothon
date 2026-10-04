using Microsoft.UI.Xaml;

namespace Lumen.UI;

/// <summary>Tiny static helpers used by x:Bind function bindings in MainWindow.xaml.</summary>
public static class UiFn
{
    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvVis(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string Join(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return $"{a}  •  {b}";
    }

    /// <summary>Glyph for the primary action on a catalog card: download, wait, or set-as-wallpaper.</summary>
    public static string ActionGlyph(bool isDownloaded, bool isDownloading)
        => isDownloaded ? "\uE768" : isDownloading ? "\uE916" : "\uE896";
}
