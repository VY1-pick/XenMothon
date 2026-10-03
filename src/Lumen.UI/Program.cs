using Lumen.Application.Services;
using Lumen.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Lumen.UI;

/// <summary>
/// Application executable entry point implementing Single-Instance gating and WinUI 3 bootstrapping.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Single-Instance Check
        using var singleInstance = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);
        if (!singleInstance.TryAcquireOwnership())
        {
            // Another instance is already running; signal it to activate and terminate immediately
            singleInstance.SignalExistingInstance();
            return;
        }

        // 2. WinUI 3 STA & ComWrappers Initialization
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 3. Start Application via fully-qualified Microsoft.UI.Xaml.Application to avoid Lumen.Application namespace collision
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            // Capture the UI dispatcher here. The single-instance signal is raised by a
            // ThreadPool wait callback, where GetForCurrentThread() would normally return null.
            var uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var context = new DispatcherQueueSynchronizationContext(uiDispatcherQueue);
            SynchronizationContext.SetSynchronizationContext(context);

            var app = new App(args, singleInstance);

            // Hook second-instance signal to restore the existing window on the UI thread.
            singleInstance.SecondInstanceLaunched += (s, e) =>
            {
                uiDispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        if (app.ServiceProvider.GetService(typeof(IWindowManagementService)) is IWindowManagementService windowManager)
                        {
                            windowManager.ShowAndActivate();
                        }
                    }
                    catch
                    {
                        // In case activation occurs before composition root initialization finishes
                    }
                });
            };
        });
    }
}
