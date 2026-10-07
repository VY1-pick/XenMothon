using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public class LocalCatalogProviderTests
{
    [Fact]
    public async Task PreviewVideo_DoesNotReturnFakeBitmapAsVideo()
    {
        var item = await _provider.GetByIdAsync(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        await Assert.ThrowsAsync<NotSupportedException>(() => _provider.OpenDownloadStreamAsync(item!));
    }
    private readonly LocalCatalogProvider _provider;

    public LocalCatalogProviderTests()
    {
        _provider = new LocalCatalogProvider(NullLogger<LocalCatalogProvider>.Instance);
    }

    [Fact]
    public async Task GetCategories_ReturnsExpectedCategoriesWithPositiveCounts()
    {
        var categories = await _provider.GetCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.Contains(categories, c => c.Id == "nature");
        Assert.Contains(categories, c => c.Id == "cyberpunk");
        Assert.Contains(categories, c => c.Id == "space");
        Assert.All(categories, c => Assert.True(c.ItemCount > 0));
    }

    [Fact]
    public async Task GetFeatured_ReturnsCuratedFeaturedItemsOnly()
    {
        var featured = await _provider.GetFeaturedAsync(limit: 5);

        Assert.NotEmpty(featured);
        Assert.True(featured.Count <= 5);
        Assert.All(featured, item => Assert.True(item.IsFeatured));
    }

    [Fact]
    public async Task GetPopular_OrdersByDownloadsDescending()
    {
        var popular = await _provider.GetPopularAsync(limit: 10);

        Assert.NotEmpty(popular);
        for (int i = 0; i < popular.Count - 1; i++)
        {
            Assert.True(popular[i].DownloadsCount >= popular[i + 1].DownloadsCount);
        }
    }

    [Fact]
    public async Task GetRecent_OrdersByCreationDateDescending()
    {
        var recent = await _provider.GetRecentAsync(limit: 10);

        Assert.NotEmpty(recent);
        for (int i = 0; i < recent.Count - 1; i++)
        {
            Assert.True(recent[i].CreatedAtUtc >= recent[i + 1].CreatedAtUtc);
        }
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsMatchingItem()
    {
        Guid knownId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var item = await _provider.GetByIdAsync(knownId);

        Assert.NotNull(item);
        Assert.Equal(knownId, item.Id);
        Assert.Equal("Alpine Dawn in 4K", item.Title);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        var item = await _provider.GetByIdAsync(Guid.NewGuid());
        Assert.Null(item);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsAllItemsPaginated()
    {
        var query = new CatalogSearchQuery { PageNumber = 1, PageSize = 10 };
        var page = await _provider.SearchAsync(query);

        Assert.Equal(10, page.Items.Count);
        Assert.True(page.TotalCount > 10);
        Assert.Equal(1, page.PageNumber);
        Assert.True(page.HasNextPage);
    }

    [Theory]
    [InlineData("alpine")]
    [InlineData("ALPINE")]
    [InlineData("AlPiNe")]
    public async Task Search_CaseInsensitiveQuery_MatchesItem(string term)
    {
        var query = new CatalogSearchQuery { SearchTerm = term };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.Contains(page.Items, item => item.Title.Contains("Alpine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_WhitespaceNormalization_HandlesTrailingAndMultipleSpaces()
    {
        var query = new CatalogSearchQuery { SearchTerm = "   neo    tokyo   " };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.Contains(page.Items, item => item.Title.Contains("Neo Tokyo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_NoMatchingResults_ReturnsEmptyPageWithZeroCount()
    {
        var query = new CatalogSearchQuery { SearchTerm = "nonexistent_term_xyz_12345" };
        var page = await _provider.SearchAsync(query);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task Search_FilterByCategory_FiltersAccurately()
    {
        var query = new CatalogSearchQuery
        {
            Filter = new CatalogFilter { CategoryId = "cyberpunk" }
        };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal("cyberpunk", item.CategoryId));
    }

    [Fact]
    public async Task Search_FilterByType_ReturnsOnlyRequestedType()
    {
        var query = new CatalogSearchQuery
        {
            Filter = new CatalogFilter { Type = WallpaperType.Video },
            PageSize = 20
        };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal(WallpaperType.Video, item.Type));
    }

    [Fact]
    public async Task Search_FilterByResolution_ReturnsOnlyTargetResolution()
    {
        var query = new CatalogSearchQuery
        {
            Filter = new CatalogFilter { Resolution = CatalogResolutionFilter.UHD_4K },
            PageSize = 20
        };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.True(item.Height >= 2160));
    }

    [Fact]
    public async Task Search_FilterByOrientation_ReturnsOnlyTargetOrientation()
    {
        var query = new CatalogSearchQuery
        {
            Filter = new CatalogFilter { Orientation = CatalogOrientationFilter.Ultrawide },
            PageSize = 20
        };
        var page = await _provider.SearchAsync(query);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal(CatalogOrientationFilter.Ultrawide, item.Orientation));
    }

    [Fact]
    public async Task Search_SortByPopular_OrdersByDownloads()
    {
        var query = new CatalogSearchQuery { SortBy = CatalogSortOption.Popular, PageSize = 20 };
        var page = await _provider.SearchAsync(query);

        for (int i = 0; i < page.Items.Count - 1; i++)
        {
            Assert.True(page.Items[i].DownloadsCount >= page.Items[i + 1].DownloadsCount);
        }
    }

    [Fact]
    public async Task Search_SortByNewest_OrdersByDate()
    {
        var query = new CatalogSearchQuery { SortBy = CatalogSortOption.Newest, PageSize = 20 };
        var page = await _provider.SearchAsync(query);

        for (int i = 0; i < page.Items.Count - 1; i++)
        {
            Assert.True(page.Items[i].CreatedAtUtc >= page.Items[i + 1].CreatedAtUtc);
        }
    }

    [Fact]
    public async Task Search_SortByRating_OrdersByRating()
    {
        var query = new CatalogSearchQuery { SortBy = CatalogSortOption.Rating, PageSize = 20 };
        var page = await _provider.SearchAsync(query);

        for (int i = 0; i < page.Items.Count - 1; i++)
        {
            Assert.True(page.Items[i].Rating >= page.Items[i + 1].Rating);
        }
    }

    [Fact]
    public async Task Search_SortByTitleAscAndDesc_OrdersAlphabetically()
    {
        var queryAsc = new CatalogSearchQuery { SortBy = CatalogSortOption.TitleAsc, PageSize = 20 };
        var pageAsc = await _provider.SearchAsync(queryAsc);

        var titlesAsc = pageAsc.Items.Select(x => x.Title).ToList();
        var sortedTitles = titlesAsc.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sortedTitles, titlesAsc);

        var queryDesc = new CatalogSearchQuery { SortBy = CatalogSortOption.TitleDesc, PageSize = 20 };
        var pageDesc = await _provider.SearchAsync(queryDesc);

        var titlesDesc = pageDesc.Items.Select(x => x.Title).ToList();
        var sortedTitlesDesc = titlesDesc.OrderByDescending(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sortedTitlesDesc, titlesDesc);
    }

    [Fact]
    public async Task Search_Pagination_PageOneAndTwoHaveNoDuplicates()
    {
        var query1 = new CatalogSearchQuery { PageNumber = 1, PageSize = 6 };
        var page1 = await _provider.SearchAsync(query1);

        var query2 = new CatalogSearchQuery { PageNumber = 2, PageSize = 6 };
        var page2 = await _provider.SearchAsync(query2);

        Assert.Equal(6, page1.Items.Count);
        Assert.Equal(6, page2.Items.Count);

        var ids1 = page1.Items.Select(x => x.Id).ToHashSet();
        var ids2 = page2.Items.Select(x => x.Id).ToHashSet();

        Assert.Empty(ids1.Intersect(ids2));
    }

    [Fact]
    public async Task Search_Pagination_BeyondLastPage_ReturnsEmptyItems()
    {
        var query = new CatalogSearchQuery { PageNumber = 999, PageSize = 10 };
        var page = await _provider.SearchAsync(query);

        Assert.Empty(page.Items);
        Assert.Equal(999, page.PageNumber);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task OpenDownloadStream_ReturnsValidReadableStream()
    {
        Guid knownId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var item = await _provider.GetByIdAsync(knownId);
        Assert.NotNull(item);

        await using var stream = await _provider.OpenDownloadStreamAsync(item);
        Assert.NotNull(stream);
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }
}
