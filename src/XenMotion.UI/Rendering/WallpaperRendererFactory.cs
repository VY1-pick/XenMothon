using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace XenMotion.UI.Rendering;

/// <summary>
/// Factory creating concrete IWallpaperRenderer instances based on wallpaper content type.
/// </summary>
public static class WallpaperRendererFactory
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static IWallpaperRenderer CreateRenderer(WallpaperType type, ILoggerFactory loggerFactory, Action<string>? calibrationCompleted = null, double calibrationBudget = 250, Func<string, WallpaperSettings, MonitorBounds, CancellationToken, Task<WallpaperSettings>>? adaptiveQuality = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return type switch
        {
            WallpaperType.Image => new ImageWallpaperRenderer(loggerFactory.CreateLogger<ImageWallpaperRenderer>()),
            WallpaperType.Video => new VideoWallpaperRenderer(loggerFactory.CreateLogger<VideoWallpaperRenderer>(), calibrationCompleted, calibrationBudget, adaptiveQuality),
            _ => new VideoWallpaperRenderer(loggerFactory.CreateLogger<VideoWallpaperRenderer>(), calibrationCompleted, calibrationBudget, adaptiveQuality)
        };
    }
}
