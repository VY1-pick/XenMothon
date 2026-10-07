using XenMotion.UI.Rendering.Video;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class PlaybackSignalTests
{
    [Fact]
    public async Task PausedWaitsReturnImmediatelyAndResumeAndCancelAcrossCycles()
    {
        var signal = new PlaybackSignal(false);
        var pending = Enumerable.Range(0, 1000).Select(_ => signal.WaitAsync(default)).ToArray();
        Assert.All(pending, task => Assert.False(task.IsCompleted));
        signal.Set();
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(1));
        signal.Reset();
        using var cancellation = new CancellationTokenSource();
        var canceledWait = signal.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        var resumed = signal.WaitAsync(default);
        Assert.False(resumed.IsCompleted);
        signal.Set();
        await resumed.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
