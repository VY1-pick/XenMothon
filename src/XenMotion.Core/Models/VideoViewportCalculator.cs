using XenMotion.Core.Enums;

namespace XenMotion.Core.Models;

/// <summary>
/// Represents a 2D rendering viewport rectangle in screen space.
/// </summary>
public readonly record struct ViewportRectangle(int X, int Y, int Width, int Height)
{
    public static readonly ViewportRectangle Empty = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// High-performance geometry calculator for video and image wallpaper presentation viewports.
/// Supports Fill, Fit, Stretch, Center, and multi-monitor Span scaling modes.
/// </summary>
public static class VideoViewportCalculator
{
    /// <summary>
    /// Computes the target destination rectangle on screen for the specified source media dimensions,
    /// monitor bounds, and scaling mode.
    /// </summary>
    public static ViewportRectangle Calculate(
        int sourceWidth,
        int sourceHeight,
        MonitorBounds targetBounds,
        PlaybackScalingMode scalingMode,
        MonitorBounds? virtualDesktopBounds = null)
    {
        if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            return ViewportRectangle.Empty;
        }

        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new ViewportRectangle(targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height);
        }

        return scalingMode switch
        {
            PlaybackScalingMode.Stretch => new ViewportRectangle(targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height),
            PlaybackScalingMode.Fit => CalculateFit(sourceWidth, sourceHeight, targetBounds),
            PlaybackScalingMode.Fill => CalculateFill(sourceWidth, sourceHeight, targetBounds),
            PlaybackScalingMode.Center => CalculateCenter(sourceWidth, sourceHeight, targetBounds),
            PlaybackScalingMode.Span => CalculateSpan(sourceWidth, sourceHeight, targetBounds, virtualDesktopBounds ?? targetBounds),
            _ => CalculateFill(sourceWidth, sourceHeight, targetBounds)
        };
    }

    private static ViewportRectangle CalculateFit(int sourceWidth, int sourceHeight, MonitorBounds bounds)
    {
        double scaleX = (double)bounds.Width / sourceWidth;
        double scaleY = (double)bounds.Height / sourceHeight;
        double scale = Math.Min(scaleX, scaleY);

        int destWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int destHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        int destX = bounds.X + (bounds.Width - destWidth) / 2;
        int destY = bounds.Y + (bounds.Height - destHeight) / 2;

        return new ViewportRectangle(destX, destY, destWidth, destHeight);
    }

    private static ViewportRectangle CalculateFill(int sourceWidth, int sourceHeight, MonitorBounds bounds)
    {
        double scaleX = (double)bounds.Width / sourceWidth;
        double scaleY = (double)bounds.Height / sourceHeight;
        double scale = Math.Max(scaleX, scaleY);

        int destWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int destHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        int destX = bounds.X + (bounds.Width - destWidth) / 2;
        int destY = bounds.Y + (bounds.Height - destHeight) / 2;

        return new ViewportRectangle(destX, destY, destWidth, destHeight);
    }

    private static ViewportRectangle CalculateCenter(int sourceWidth, int sourceHeight, MonitorBounds bounds)
    {
        int destX = bounds.X + (bounds.Width - sourceWidth) / 2;
        int destY = bounds.Y + (bounds.Height - sourceHeight) / 2;

        return new ViewportRectangle(destX, destY, sourceWidth, sourceHeight);
    }

    private static ViewportRectangle CalculateSpan(
        int sourceWidth,
        int sourceHeight,
        MonitorBounds monitorBounds,
        MonitorBounds virtualDesktopBounds)
    {
        if (virtualDesktopBounds.Width <= 0 || virtualDesktopBounds.Height <= 0)
        {
            return CalculateFill(sourceWidth, sourceHeight, monitorBounds);
        }

        // 1. Calculate how source spans across the unified virtual desktop bounds (Fill across whole desktop)
        double scaleX = (double)virtualDesktopBounds.Width / sourceWidth;
        double scaleY = (double)virtualDesktopBounds.Height / sourceHeight;
        double scale = Math.Max(scaleX, scaleY);

        int totalSpanWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int totalSpanHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        int totalSpanX = virtualDesktopBounds.X + (virtualDesktopBounds.Width - totalSpanWidth) / 2;
        int totalSpanY = virtualDesktopBounds.Y + (virtualDesktopBounds.Height - totalSpanHeight) / 2;

        // 2. Map coordinates relative to target monitor
        // In multi-monitor rendering, if each monitor has its own renderer window positioned at monitorBounds.X, monitorBounds.Y,
        // the content within that window needs to be offset by (totalSpanX - monitorBounds.X, totalSpanY - monitorBounds.Y).
        int offsetX = totalSpanX - monitorBounds.X;
        int offsetY = totalSpanY - monitorBounds.Y;

        return new ViewportRectangle(offsetX, offsetY, totalSpanWidth, totalSpanHeight);
    }
}
