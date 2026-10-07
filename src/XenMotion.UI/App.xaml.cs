using System.Runtime.Versioning;
using System.Diagnostics;
using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Infrastructure.Persistence.Database;
using XenMotion.UI.Bootstrapper;
using XenMotion.UI.Services;
using XenMotion.UI.Tray;
using XenMotion.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace XenMotion.UI;

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
        var startupWatch = Stopwatch.StartNew();

        // 1. Build Composition Root with shared single instance coordinator
        _serviceProvider = AppCompositionRoot.BuildServiceProvider(singleInstanceService: _singleInstanceService);
        _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        _crashReportService = _serviceProvider.GetRequiredService<CrashReportService>();
        _releaseReadinessService = _serviceProvider.GetRequiredService<ReleaseReadinessService>();
        _updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        RegisterGlobalExceptionHandlers();
        _logger.LogInformation("XenMotion application launching with args: {Args}", string.Join(" ", _args));

        // 2. Initialize Database & Settings
        var migrator = _serviceProvider.GetRequiredService<DatabaseMigrator>();
        await migrator.MigrateAsync().ConfigureAwait(true);

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        await settingsService.LoadSettingsAsync().ConfigureAwait(true);
        if (settingsService.IsFirstRun)
        {
            var tuning = _serviceProvider.GetRequiredService<HardwareTuningService>();
            await settingsService.UpdateSettingsAsync(s => XenMotion.Core.Models.QualityPresets.Apply(s, tuning.RecommendedPreset)).ConfigureAwait(true);
            _logger.LogInformation("First-run quality recommendation: {Preset}; {Hardware}.", tuning.RecommendedPreset, tuning.Summary);
        }
        await _releaseReadinessService.ValidateAsync().ConfigureAwait(true);

        // 3. Resolve Services
        _windowManager = _serviceProvider.GetRequiredService<IWindowManagementService>();
        _orchestrator = _serviceProvider.GetRequiredService<IWallpaperEngineOrchestrator>();
        _trayManager = _serviceProvider.GetRequiredService<ISystemTrayManager>();
        _desktopHost = _serviceProvider.GetRequiredService<IDesktopWallpaperHost>();
        _performanceMonitor = _serviceProvider.GetRequiredService<IPerformanceMonitorService>();
        _monitorTopology = _serviceProvider.GetRequiredService<MonitorTopologyService>();
        _startupService = _serviceProvider.GetRequiredService<WindowsStartupService>();

        // Synchronize the logon task with the persisted startup preference.
        settingsService.SettingsChanged += OnSettingsChanged;

        // 4. Attach Desktop Shell & Start Background Performance Monitor
        _monitorTopology.Start();
        var monitors = _monitorTopology.CurrentMonitors;
        // A logon task may start before Explorer finishes creating its desktop.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (await _desktopHost.AttachToDesktopAsync().ConfigureAwait(true)) break;
            await Task.Delay(250).ConfigureAwait(true);
        }
        await _desktopHost.UpdateMonitorLayoutAsync(monitors).ConfigureAwait(true);
        _performanceMonitor.Start();

        // Keep the XAML window, catalog and thumbnails out of the background startup path.
        _windowManager.SetMainWindowFactory(CreateMainWindow);
        _trayManager.OpenRequested += (_, _) => _windowManager.ShowAndActivate();
        _trayManager.PauseResumeRequested += async (_, _) => await GetPlaybackViewModel().TogglePauseResumeAsync();
        _trayManager.StopRequested += async (_, _) =>
        {
            await GetPlaybackViewModel().StopAllWallpapersAsync();
        };
        _trayManager.PlayRequested += async (_, _) => await GetPlaybackViewModel().ResumeAllWallpapersAsync();
        _trayManager.MuteRequested += async (_, _) => await GetPlaybackViewModel().ToggleMuteAsync();
        _trayManager.NextWallpaperRequested += async (_, _) =>
        {
            var vm = GetPlaybackViewModel();
            if (vm.Wallpapers.Count == 0) await vm.RefreshLibraryAsync();
            await vm.NextWallpaperAsync();
        };
        _trayManager.ExitRequested += (_, _) => Shutdown();
        _trayManager.Initialize();
        try
        {
            await _serviceProvider.GetRequiredService<SavedWallpaperService>().RestorePrimaryAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Could not restore the saved wallpaper at startup."); }
        _logger.LogInformation("Background startup ready in {Elapsed} ms; UI created: {Created}.", startupWatch.ElapsedMilliseconds, _mainWindow is not null);
        _startupService.Apply(settingsService.CurrentSettings.StartWithWindows);
        _ = CheckForUpdatesInBackgroundAsync();

        // 7. Handle Launch Mode (--minimized)
        bool startMinimized = _args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
                           || settingsService.CurrentSettings.StartMinimizedToTray;

        if (startMinimized && _trayManager.IsInitialized)
        {
            _logger.LogInformation("Application started in --minimized mode. Remaining in System Tray.");
            _windowManager.Hide();
            _trayManager.ShowNotification("XenMotion Wallpaper Platform", "XenMotion is active in the system tray.");
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

    private void CreateMainWindow()
    {
        if (_mainWindow is not null || _serviceProvider is null || _windowManager is null || _trayManager is null) return;
        var vm = GetPlaybackViewModel();
        _mainWindow = new MainWindow(vm, _windowManager, _serviceProvider.GetRequiredService<SettingsViewModel>(), _trayManager);
    }

    private MainViewModel GetPlaybackViewModel()
    {
        var vm = ServiceProvider.GetRequiredService<MainViewModel>();
        var active = ServiceProvider.GetRequiredService<SavedWallpaperService>().ActiveWallpaper;
        if (active is not null && vm.ActiveWallpaperId != active.Id) vm.SetRestoredWallpaper(active.Id, active.Title);
        return vm;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        UnhandledException += OnUiUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryWriteCrashReport(new InvalidOperationException(e.Message, e.Exception), "WinUI.UnhandledException");
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
                _logger?.LogInformation("A new XenMotion release is available: {Version}", update.Version);
                _trayManager.ShowNotification("XenMotion update available", $"Version {update.Version} is available.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Update check failed; continuing without update notification.");
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private void OnSettingsChanged(object? sender, XenMotion.Core.Models.ApplicationSettings settings)
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
            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(true);
            }
            else if (_serviceProvider is IDisposable spDisposable)
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
