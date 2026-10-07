using XenMotion.UI.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class SingleWindowControllerTests
{
    [Fact]
    public void ReentrantTraySelectionDuringConstructionAndActivation_CreatesOnlyOneWindow()
    {
        var windows = new SingleWindowController<object>();
        int creations = 0, activations = 0;
        object? expected = null;
        Action<object> activate = window =>
        {
            Assert.Same(expected, window);
            activations++;
            Assert.False(windows.Show(_ => throw new Exception("Nested activation")));
        };
        windows.Factory = () =>
        {
            creations++;
            Assert.False(windows.Show(activate)); // WinUI pumps a second click before registration.
            expected = new object();
            windows.Register(expected);
            Assert.False(windows.Show(activate)); // Also before the constructor finishes.
        };

        Assert.True(windows.Show(activate));
        for (int i = 0; i < 20; i++)
        {
            windows.Hide(window => Assert.Same(expected, window));
            Assert.True(windows.Show(activate));
        }
        Assert.Equal(1, creations);
        Assert.Equal(21, activations);
    }

    [Fact]
    public void FailedFirstConstruction_AllowsRetry()
    {
        var windows = new SingleWindowController<object> { Factory = () => throw new IOException("Construction failed") };
        Assert.Throws<IOException>(() => windows.Show(_ => { }));
        windows.Factory = () => windows.Register(new object());
        Assert.True(windows.Show(_ => { }));
    }

    [Fact]
    public void MainWindowCannotBeReplaced_HideStillTargetsOriginalWindow()
    {
        var windows = new SingleWindowController<object>();
        var original = new object();
        windows.Register(original);
        Assert.Throws<InvalidOperationException>(() => windows.Register(new object()));
        windows.Hide(window => Assert.Same(original, window));
    }
}
