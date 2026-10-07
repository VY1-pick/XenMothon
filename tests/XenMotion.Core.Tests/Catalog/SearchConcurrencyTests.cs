using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace XenMotion.Core.Tests.Catalog;

public class SearchConcurrencyTests
{
    private readonly Mock<ICatalogProvider> _mockProvider;
    private readonly Mock<IWallpaperRepository> _mockRepo;
    private readonly Mock<IStorageManager> _mockStorage;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly ExploreCatalogService _service;

    public SearchConcurrencyTests()
    {
        _mockProvider = new Mock<ICatalogProvider>();
        _mockRepo = new Mock<IWallpaperRepository>();
        _mockStorage = new Mock<IStorageManager>();
        _mockThumb = new Mock<IThumbnailService>();

        _service = new ExploreCatalogService(
            _mockProvider.Object,
            _mockRepo.Object,
            _mockStorage.Object,
            _mockThumb.Object,
            NullLogger<ExploreCatalogService>.Instance);
    }

    [Fact]
    public async Task Search_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockProvider.Setup(p => p.SearchAsync(It.IsAny<CatalogSearchQuery>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _service.SearchAsync(new CatalogSearchQuery { SearchTerm = "test" }, cts.Token);
        });
    }

    [Fact]
    public async Task Search_LatestRequestWins_SupersededRequestThrowsOperationCanceled()
    {
        var tcsFirst = new TaskCompletionSource<CatalogPage<CatalogItem>>();

        // First call hangs until second call triggers
        _mockProvider.SetupSequence(p => p.SearchAsync(It.IsAny<CatalogSearchQuery>(), It.IsAny<CancellationToken>()))
                     .Returns(tcsFirst.Task)
                     .ReturnsAsync(new CatalogPage<CatalogItem>([], 1, 10, 0));

        // Start request 1 (slow)
        var task1 = _service.SearchAsync(new CatalogSearchQuery { SearchTerm = "car" });

        // Start request 2 (fast, superseding request 1)
        var task2 = _service.SearchAsync(new CatalogSearchQuery { SearchTerm = "cars red" });

        var page2 = await task2;
        Assert.NotNull(page2);

        // Now resolve first task
        tcsFirst.SetResult(new CatalogPage<CatalogItem>([], 1, 10, 0));

        // Request 1 must throw OperationCanceledException because it was superseded
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task1);
    }

    [Fact]
    public async Task Search_SequentialRequests_AllCompleteSuccessfully()
    {
        _mockProvider.Setup(p => p.SearchAsync(It.IsAny<CatalogSearchQuery>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((CatalogSearchQuery q, CancellationToken _) =>
                         new CatalogPage<CatalogItem>([], q.PageNumber, q.PageSize, 0));

        var res1 = await _service.SearchAsync(new CatalogSearchQuery { SearchTerm = "query1" });
        var res2 = await _service.SearchAsync(new CatalogSearchQuery { SearchTerm = "query2" });

        Assert.NotNull(res1);
        Assert.NotNull(res2);
    }

    [Fact]
    public async Task Search_RapidConsecutiveQueries_OnlyLatestResultIsReturned()
    {
        int completedCount = 0;
        int canceledCount = 0;

        _mockProvider.Setup(p => p.SearchAsync(It.IsAny<CatalogSearchQuery>(), It.IsAny<CancellationToken>()))
                     .Returns(async (CatalogSearchQuery q, CancellationToken ct) =>
                     {
                         await Task.Delay(20, ct);
                         return new CatalogPage<CatalogItem>([], 1, 10, 0);
                     });

        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _service.SearchAsync(new CatalogSearchQuery { SearchTerm = $"token_{idx}" });
                    Interlocked.Increment(ref completedCount);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref canceledCount);
                }
            }));
            await Task.Delay(5);
        }

        await Task.WhenAll(tasks);

        // The final request must have completed
        Assert.True(completedCount >= 1);
        Assert.True(canceledCount >= 1);
    }
}
