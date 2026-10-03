using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Lumen.UI.Rendering;

/// <summary>
/// Factory creating concrete IWallpaperRenderer instances based on wallpaper content type.
/// </summary>
public static class WallpaperRendererFactory
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static IWallpaperRenderer CreateRenderer(WallpaperType type, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return type switch
        {
            WallpaperType.Image => new ImageWallpaperRenderer(loggerFactory.CreateLogger<ImageWallpaperRenderer>()),
            WallpaperType.Video => new VideoWallpaperRenderer(loggerFactory.CreateLogger<VideoWallpaperRenderer>()),
            _ => new VideoWallpaperRenderer(loggerFactory.CreateLogger<VideoWallpaperRenderer>())
        };
    }
}
