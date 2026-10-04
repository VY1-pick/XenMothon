using System.Runtime.Versioning;
using Lumen.Application.Services;
using Lumen.Core.Interfaces;
using Lumen.Infrastructure.Persistence.Database;
using Lumen.UI.Bootstrapper;
using Lumen.UI.Services;
using Lumen.UI.Tray;
using Lumen.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Lumen.UI;

/// <summary>
/// Main application entry point for WinUI 3 lifecycle and dependency bootstrapping.
/// </summary>
public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly string[] _args;
    private readonly ISingleInstanceService? _singleInstanceService;
    private IServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private ILogger<App>? _logger;
    private ISystemTrayManager? _trayManager;
    private IWindowManagementService? _windowManager;
    private IWallpaperEngineOrchestrator? _orchestrator;
    private IDesktopWallpaperHost? _desktopHost;
    private IPerformanceMonitorService? _performanceMonitor;
    private MonitorTopologyService? _monitorTopology;
    private WindowsStartupService? _startupService;
    private CrashReportService? _crashReportService;
    private ReleaseReadinessService? _releaseReadinessService;
    private IUpdateService? _updateService;
    private bool _isShuttingDown;

    public static new App Current => (App)global::Microsoft.UI.Xaml.Application.Current;
    public IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("App is not initialized.");

    public App() : this([])
    {
    }

    public App(string[] args, ISingleInstanceService? singleInstanceService = null)
    {
        _args = args ?? [];
        _singleInstanceService = singleInstanceService;
        InitializeComponent();
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        // 1. Build Composition Root with shared single instance coordinator
        _serviceProvider = AppCompositionRoot.BuildServiceProvider(singleInstanceService: _singleInstanceService);
        _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        _crashReportService = _serviceProvider.GetRequiredService<CrashReportService>();
        _releaseReadinessService = _serviceProvider.GetRequiredService<ReleaseReadinessService>();
        _updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        RegisterGlobalExceptionHandlers();
        _logger.LogInformation("Lumen application launching with args: {Args}", string.Join(" ", _args));

        // 2. Initialize Database & Settings
        var migrator = _serviceProvider.GetRequiredService<DatabaseMigrator>();
        await migrator.MigrateAsync().ConfigureAwait(true);

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        await settingsService.LoadSettingsAsync().ConfigureAwait(true);
        await _releaseReadinessService.ValidateAsync().ConfigureAwait(true);

        // 3. Resolve Services
        _windowManager = _serviceProvider.GetRequiredService<IWindowManagementService>();
        _orchestrator = _serviceProvider.GetRequiredService<IWallpaperEngineOrchestrator>();
        _trayManager = _serviceProvider.GetRequiredService<ISystemTrayManager>();
        _desktopHost = _serviceProvider.GetRequiredService<IDesktopWallpaperHost>();
        _performanceMonitor = _serviceProvider.GetRequiredService<IPerformanceMonitorService>();
        _monitorTopology = _serviceProvider.GetRequiredService<MonitorTopologyService>();
        _startupService = _serviceProvider.GetRequiredService<WindowsStartupService>();

        // Synchronize the unpackaged HKCU startup entry with the persisted preference.
        _startupService.Apply(settingsService.CurrentSettings.StartWithWindows);
        settingsService.SettingsChanged += OnSettingsChanged;

        // 4. Attach Desktop Shell & Start Background Performance Monitor
        _monitorTopology.Start();
        var monitors = _monitorTopology.CurrentMonitors;
        await _desktopHost.AttachToDesktopAsync().ConfigureAwait(true);
        await _desktopHost.UpdateMonitorLayoutAsync(monitors).ConfigureAwait(true);
        _performanceMonitor.Start();

        // 5. Create Main Window
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        _mainWindow = new MainWindow(viewModel, _windowManager, settingsViewModel, _trayManager);

        // 6. Tray callbacks are wired once, inside MainWindow. Wiring them here as well made
        // Pause/Resume fire twice (pause then immediately resume) and Exit run twice.
        _trayManager.Initialize();
        _ = CheckForUpdatesInBackgroundAsync();

        // 7. Handle Launch Mode (--minimized)
        bool startMinimized = _args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
                           || settingsService.CurrentSettings.StartMinimizedToTray;

        if (startMinimized && _trayManager.IsInitialized)
        {
            _logger.LogInformation("Application started in --minimized mode. Remaining in System Tray.");
            _windowManager.Hide();
            _trayManager.ShowNotification("Lumen Wallpaper Platform", "Lumen is active in the system tray.");
        }
        else
        {
            if (startMinimized && !_trayManager.IsInitialized)
            {
                _logger.LogWarning("Tray initialization failed; showing the main window instead of starting invisibly.");
            }
            else
            {
                _logger.LogInformation("Application started in normal mode. Displaying main window.");
            }
            _windowManager.ShowAndActivate();
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        UnhandledException += OnUiUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryWriteCrashReport(e.Exception, "WinUI.UnhandledException");
    }

    private void OnAppDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryWriteCrashReport(exception, "AppDomain.UnhandledException");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryWriteCrashReport(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private void TryWriteCrashReport(Exception exception, string source)
    {
        try
        {
            _crashReportService?.WriteCrashReport(exception, source, _args);
        }
        catch (Exception reportException)
        {
            _logger?.LogError(reportException, "Failed to write crash report for {Source}.", source);
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (_updateService is null || _trayManager is null)
        {
            return;
        }

        try
        {
            var update = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);
            if (update is not null)
            {
                _logger?.LogInformation("A new Lumen release is available: {Version}", update.Version);
                _trayManager.ShowNotification("Lumen update available", $"Version {update.Version} is available.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Update check failed; continuing without update notification.");
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private void OnSettingsChanged(object? sender, Lumen.Core.Models.ApplicationSettings settings)
    {
        try
        {
            _startupService?.Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to synchronize Windows startup registration with settings.");
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    public async void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }
        _isShuttingDown = true;

        _logger?.LogInformation("Application clean shutdown initiated.");

        try
        {
            // 1. Remove tray icon immediately so Windows taskbar does not show ghost icon
            _trayManager?.Dispose();

            // 2. Stop performance monitor
            if (_performanceMonitor != null)
            {
                _performanceMonitor.Stop();
                await _performanceMonitor.DisposeAsync().ConfigureAwait(true);
            }

            // 3. Stop wallpaper orchestrator and release active sessions
            if (_orchestrator != null)
            {
                await _orchestrator.DisposeAsync().ConfigureAwait(true);
            }

            // 4. Stop monitor topology polling
            if (_monitorTopology != null)
            {
                await _monitorTopology.DisposeAsync().ConfigureAwait(true);
            }

            // 5. Detach desktop wallpaper host
            if (_desktopHost != null)
            {
                await _desktopHost.DisposeAsync().ConfigureAwait(true);
            }

            // 6. Dispose service provider (window is allowed to close when the process exits). to flush SQLite connections and persistent files
            if (_serviceProvider is IDisposable spDisposable)
            {
                spDisposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during application shutdown.");
        }
        finally
        {
            Exit();
        }
    }
}
