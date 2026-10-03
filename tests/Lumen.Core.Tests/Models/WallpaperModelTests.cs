using Lumen.Core.Enums;
using Lumen.Core.Models;
using Xunit;

namespace Lumen.Core.Tests.Models;

public class WallpaperModelTests
{
    [Fact]
    public void Wallpaper_Creation_PopulatesDefaultsCorrectly()
    {
        var wallpaper = new Wallpaper
        {
            Title = "Cyberpunk City",
            Type = WallpaperType.Video,
            FilePath = "C:\\Wallpapers\\cyberpunk.mp4",
            ContentHash = "abc123hash"
        };

        Assert.NotEqual(Guid.Empty, wallpaper.Id);
        Assert.Equal("Cyberpunk City", wallpaper.Title);
        Assert.Equal(WallpaperType.Video, wallpaper.Type);
        Assert.True(wallpaper.IsVideo);
        Assert.True(wallpaper.IsAnimated);
        Assert.False(wallpaper.IsFavorite);
        Assert.False(wallpaper.IsSubscribed);
        Assert.Equal("General", wallpaper.Category);
        Assert.Equal("Local", wallpaper.Creator);
    }

    [Fact]
    public void Wallpaper_ImageProperties_ShouldNotFlagAsVideo()
    {
        var wallpaper = new Wallpaper
        {
            Title = "Serene Forest",
            Type = WallpaperType.Image,
            FilePath = "C:\\Wallpapers\\forest.png",
            ContentHash = "def456hash"
        };

        Assert.False(wallpaper.IsVideo);
        Assert.False(wallpaper.IsAnimated);
    }
}
