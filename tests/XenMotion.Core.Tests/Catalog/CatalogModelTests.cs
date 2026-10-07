using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public class CatalogModelTests
{
    [Fact]
    public void CatalogItem_PropertiesAndResolutionText_CalculatedCorrectly()
    {
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = "Cyberpunk Neo",
            Description = "City in rain",
            Author = "Artist1",
            ThumbnailUrl = "thumb.jpg",
            PreviewUrl = "prev.mp4",
            Type = WallpaperType.Video,
            Width = 3840,
            Height = 2160,
            Duration = TimeSpan.FromSeconds(25),
            FileSizeBytes = 45 * 1024 * 1024,
            CategoryId = "cyberpunk",
            CategoryName = "Cyberpunk & Sci-Fi"
        };

        Assert.Equal("3840x2160 (4K UHD)", item.ResolutionText);
        Assert.Equal("45.0 MB", item.FormattedSize);
        Assert.Equal("00:25", item.DurationText);
        Assert.Equal(CatalogResolutionFilter.UHD_4K, item.ResolutionCategory);
        Assert.Equal(CatalogOrientationFilter.Landscape, item.Orientation);
    }

    [Theory]
    [InlineData(1920, 1080, "1920x1080 (FHD)", CatalogResolutionFilter.FHD_1080p)]
    [InlineData(2560, 1440, "2560x1440 (QHD)", CatalogResolutionFilter.QHD_1440p)]
    [InlineData(3840, 2160, "3840x2160 (4K UHD)", CatalogResolutionFilter.UHD_4K)]
    public void CatalogItem_ResolutionClassification_MatchesResolutionCategory(
        int width, int height, string expectedText, CatalogResolutionFilter expectedCategory)
    {
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = "Res Test",
            Description = "",
            Author = "",
            ThumbnailUrl = "",
            PreviewUrl = "",
            Type = WallpaperType.Image,
            Width = width,
            Height = height,
            CategoryId = "test",
            CategoryName = "Test"
        };

        Assert.Equal(expectedText, item.ResolutionText);
        Assert.Equal(expectedCategory, item.ResolutionCategory);
    }

    [Theory]
    [InlineData(3440, 1440, CatalogOrientationFilter.Ultrawide)]
    [InlineData(1920, 1080, CatalogOrientationFilter.Landscape)]
    [InlineData(1080, 1920, CatalogOrientationFilter.Portrait)]
    public void CatalogItem_Orientation_CalculatesAspectCorrectly(
        int width, int height, CatalogOrientationFilter expectedOrientation)
    {
        var item = new CatalogItem
        {
            Id = Guid.NewGuid(),
            Title = "Aspect Test",
            Description = "",
            Author = "",
            ThumbnailUrl = "",
            PreviewUrl = "",
            Type = WallpaperType.Image,
            Width = width,
            Height = height,
            CategoryId = "test",
            CategoryName = "Test"
        };

        Assert.Equal(expectedOrientation, item.Orientation);
    }

    [Fact]
    public void CatalogPage_PaginationMath_CalculatesTotalPagesAndNavigation()
    {
        var items = new List<string> { "item1", "item2", "item3" };
        var page = new CatalogPage<string>(items, PageNumber: 2, PageSize: 3, TotalCount: 10);

        Assert.Equal(4, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public void CatalogPage_EmptyPage_HasZeroPagesAndNoNavigation()
    {
        var page = new CatalogPage<string>([], PageNumber: 1, PageSize: 10, TotalCount: 0);

        Assert.Equal(0, page.TotalPages);
        Assert.False(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
    }

    [Fact]
    public void CatalogCategory_Initialization_StoresStableMetadata()
    {
        var cat = new CatalogCategory("nature", "Nature & Landscapes", "Scenic views", "\uE707", 15);

        Assert.Equal("nature", cat.Id);
        Assert.Equal("Nature & Landscapes", cat.DisplayName);
        Assert.Equal("Scenic views", cat.Description);
        Assert.Equal("\uE707", cat.IconGlyph);
        Assert.Equal(15, cat.ItemCount);
    }

    [Fact]
    public void CatalogSearchQuery_DefaultPagination_IsPageOneWithTwelveItems()
    {
        var query = new CatalogSearchQuery();

        Assert.Equal(1, query.PageNumber);
        Assert.Equal(12, query.PageSize);
        Assert.Equal(CatalogSortOption.Relevance, query.SortBy);
        Assert.Null(query.SearchTerm);
        Assert.Null(query.Filter);
    }
}
