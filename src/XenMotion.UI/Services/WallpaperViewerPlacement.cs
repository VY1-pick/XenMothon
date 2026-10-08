namespace XenMotion.UI.Services;

internal static class WallpaperViewerPlacement
{
    public static (int MainX, int X, int Y, int Height) Beside(int x, int y, int width, int height,
        int viewerWidth, int workX, int workY, int workWidth, int workHeight)
    {
        if (x + width + viewerWidth > workX + workWidth && x - viewerWidth < workX && width + viewerWidth <= workWidth)
            x = workX + workWidth - width - viewerWidth;
        int viewerX = x + width;
        if (viewerX + viewerWidth > workX + workWidth) viewerX = x - viewerWidth;
        viewerX = Math.Clamp(viewerX, workX, Math.Max(workX, workX + workWidth - viewerWidth));
        int viewerHeight = Math.Min(height, workHeight);
        return (x, viewerX, Math.Clamp(y, workY, workY + workHeight - viewerHeight), viewerHeight);
    }
}
