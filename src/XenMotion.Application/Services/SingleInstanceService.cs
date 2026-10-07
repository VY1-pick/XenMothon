using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>
/// Robust Windows single-instance coordinator using a named Mutex and named EventWaitHandle.
/// Prevents multiple engines from competing for desktop resources and brings existing instance to focus.
/// </summary>
public sealed class SingleInstanceService : ISingleInstanceService
{
    private readonly string MutexName;
    private readonly string EventName;

    private readonly ILogger<SingleInstanceService> _logger;
    private Mutex? _mutex;
    private EventWaitHandle? _eventWaitHandle;
    private RegisteredWaitHandle? _registeredWait;
    private bool _hasOwnership;
    private bool _isDisposed;

    public bool IsFirstInstance => _hasOwnership;

    public event EventHandler? SecondInstanceLaunched;

    public SingleInstanceService(ILogger<SingleInstanceService> logger, string instanceKey = "Lumen_SingleInstance")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Retain the legacy IPC identity so an older installation cannot run a second engine.
        MutexName = $@"Local\{instanceKey}_Mutex_B6A58F92";
        EventName = $@"Local\{instanceKey}_Event_B6A58F92";
    }

    public bool TryAcquireOwnership()
    {
        if (_hasOwnership)
        {
            return true;
        }

        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                _hasOwnership = false;
                _logger.LogInformation("Another application instance already holds the single-instance lock.");
                return false;
            }

            _eventWaitHandle = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: EventName);

            // Register background wait callback without dedicating an entire thread
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _eventWaitHandle,
                OnSignalReceived,
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);

            _hasOwnership = true;
            _logger.LogInformation("Single-instance ownership successfully acquired.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize single-instance mutex. Refusing a competing engine.");
            _hasOwnership = false;
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    public void SignalExistingInstance()
    {
        try
        {
            _logger.LogInformation("Signaling existing instance to activate window.");
            if (OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not signal existing instance via EventWaitHandle.");
        }
    }

    private void OnSignalReceived(object? state, bool timedOut)
    {
        if (!timedOut && !_isDisposed)
        {
            _logger.LogInformation("Second instance detected. Raising activation event.");
            SecondInstanceLaunched?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;

        _registeredWait?.Unregister(null);
        _registeredWait = null;

        if (_eventWaitHandle != null)
        {
            _eventWaitHandle.Dispose();
            _eventWaitHandle = null;
        }

        if (_mutex != null)
        {
            if (_hasOwnership)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned
                }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
