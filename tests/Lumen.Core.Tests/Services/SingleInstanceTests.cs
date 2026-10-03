using Lumen.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lumen.Core.Tests.Services;

public class SingleInstanceTests
{
    [Fact]
    public void FirstInstance_ShouldAcquireOwnership()
    {
        using var service = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        bool acquired = service.TryAcquireOwnership();

        Assert.True(acquired);
        Assert.True(service.IsFirstInstance);
    }

    [Fact]
    public void SecondInstance_ShouldFailToAcquireOwnership_WhenFirstInstanceActive()
    {
        using var firstInstance = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        bool firstAcquired = firstInstance.TryAcquireOwnership();
        Assert.True(firstAcquired);

        using var secondInstance = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        bool secondAcquired = secondInstance.TryAcquireOwnership();

        Assert.False(secondAcquired);
        Assert.False(secondInstance.IsFirstInstance);
    }

    [Fact]
    public void DisposedInstance_ShouldReleaseOwnership_ForSubsequentInstance()
    {
        var firstInstance = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        bool firstAcquired = firstInstance.TryAcquireOwnership();
        Assert.True(firstAcquired);

        // Dispose first instance, releasing the mutex
        firstInstance.Dispose();

        using var secondInstance = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        bool secondAcquired = secondInstance.TryAcquireOwnership();

        Assert.True(secondAcquired);
        Assert.True(secondInstance.IsFirstInstance);
    }
}
