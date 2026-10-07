using System.Runtime.Versioning;
using XenMotion.Application.Providers;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Infrastructure.Persistence.Catalog;
using XenMotion.Infrastructure.Persistence.Catalog.Remote;
using XenMotion.Infrastructure.Persistence.Creator;
using XenMotion.Infrastructure.Persistence.Database;
using XenMotion.Infrastructure.Persistence.Services;
using XenMotion.Infrastructure.Persistence.Storage;
using XenMotion.Infrastructure.Persistence.Updates;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering;
using XenMotion.UI.Services;
using XenMotion.UI.Tray;
using XenMotion.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Bootstrapper;

/// <summary>
/// Configures the Dependency Injection service container and builds the application composition root.
/// </summary>
public static class AppCompositionRoot
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static IServiceProvider BuildServiceProvider(
        string? customAppDataRoot = null,
        ISingleInstanceService? singleInstanceService = null)
    {
        var services = new ServiceCollection();

        // 1. Storage & AppData Path Resolution
        string appDataRoot = customAppDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XenMotion");

        // Reuse an existing installation's library without moving media or breaking absolute paths.
        string legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen");
        // A WebView/runtime cache directory is not a migrated installation. Keep using
        // the legacy library until the new root actually contains persisted app data.
        if (customAppDataRoot is null
            && !File.Exists(Path.Combine(appDataRoot, "settings.json"))
            && !File.Exists(Path.Combine(appDataRoot, "xenmotion_metadata.db"))
            && !File.Exists(Path.Combine(appDataRoot, "lumen_metadata.db"))
            && Directory.Exists(legacyRoot))
            appDataRoot = legacyRoot;

        string legacyDatabase = Path.Combine(appDataRoot, "lumen_metadata.db");
        string databasePath = File.Exists(legacyDatabase) ? legacyDatabase : Path.Combine(appDataRoot, "xenmotion_metadata.db");
        string settingsFilePath = Path.Combine(appDataRoot, "settings.json");
        string mediaStoragePath = Path.Combine(appDataRoot, "MediaStorage");

        if (!Directory.Exists(appDataRoot))
        {
            Directory.CreateDirectory(appDataRoot);
        }

        // 2. Structured Logging Infrastructure
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new LocalFileLoggerProvider(appDataRoot));
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
            });
        });

        // 3. Settings & Configuration
        services.AddSingleton<ISettingsService>(sp =>
            new SettingsService(settingsFilePath, sp.GetRequiredService<ILogger<SettingsService>>()));

        services.AddSingleton<CrashReportService>(sp =>
            new CrashReportService(appDataRoot, sp.GetRequiredService<ILogger<CrashReportService>>()));
        services.AddSingleton<ReleaseReadinessService>();

        services.AddSingleton<IUpdateService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>().CurrentSettings;
            if (!settings.CheckForUpdates || string.IsNullOrWhiteSpace(settings.UpdateManifestUrl))
            {
                return new DisabledUpdateService();
            }

            return new RemoteUpdateService(
                new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
                settings.UpdateManifestUrl,
                sp.GetRequiredService<ILogger<RemoteUpdateService>>());
        });

        // 4. Persistence & Database Infrastructure
        services.AddSingleton<IDbConnectionFactory>(sp =>
            new SqliteConnectionFactory(databasePath, sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<IWallpaperRepository, WallpaperRepository>();

        // 5. Storage Management & Media Services
        services.AddSingleton<IStorageManager>(sp =>
            new LocalStorageManager(mediaStoragePath, sp.GetRequiredService<ILogger<LocalStorageManager>>()));

        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IWallpaperImportService, WallpaperImportService>();
        services.AddSingleton<ILibraryService, LibraryService>();

        // 6. Catalog & Explore Services
        // The provider is selected after settings are loaded. Remote catalog is opt-in and
        // transparently falls back to the deterministic local catalog when configured.
        services.AddSingleton<LocalCatalogProvider>();
        services.AddSingleton<ICatalogProvider>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>().CurrentSettings;
            var localProvider = sp.GetRequiredService<LocalCatalogProvider>();

            if (!settings.UseRemoteCatalog || string.IsNullOrWhiteSpace(settings.CatalogBackendUrl))
            {
                return localProvider;
            }

            var options = new RemoteCatalogOptions
            {
                UseRemoteCatalog = true,
                BaseUrl = settings.CatalogBackendUrl,
                RequestTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.CatalogRequestTimeoutSeconds, 5, 120)),
                UseLocalFallback = settings.UseLocalCatalogFallback
            };

            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute)
            };
            return new RemoteCatalogProvider(
                httpClient,
                options,
                sp.GetRequiredService<ILogger<RemoteCatalogProvider>>(),
                options.UseLocalFallback ? localProvider : null);
        });
        services.AddSingleton<IExploreCatalogService, ExploreCatalogService>();
        services.AddSingleton<ICreatorService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>().CurrentSettings;
            if (string.IsNullOrWhiteSpace(settings.CreatorBackendUrl))
            {
                return new DisabledCreatorService();
            }

            if (!Uri.TryCreate(settings.CreatorBackendUrl, UriKind.Absolute, out var creatorUri) ||
                (creatorUri.Scheme != Uri.UriSchemeHttp && creatorUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("CreatorBackendUrl must be an absolute HTTP(S) URI.");
            }

            return new RemoteCreatorService(
                new HttpClient { BaseAddress = new Uri(creatorUri.AbsoluteUri.TrimEnd('/') + "/") },
                sp.GetRequiredService<ILogger<RemoteCreatorService>>());
        });
        services.AddSingleton<IWallpaperCatalogProvider, LocalWallpaperCatalogProvider>();

        // 7. Engine Orchestration & Renderer Factory
        services.AddSingleton<Func<WallpaperType, IWallpaperRenderer>>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return type =>
            {
                var inner = WallpaperRendererFactory.CreateRenderer(type, loggerFactory, sp.GetRequiredService<HardwareTuningService>().RecordPlaybackReport,
                    250.0 / Math.Max(1, sp.GetRequiredService<MonitorTopologyService>().CurrentMonitors.Count), sp.GetRequiredService<AdaptiveQualityService>().TuneAsync);
                return new DesktopWallpaperRenderer(
                    inner,
                    sp.GetRequiredService<IDesktopWallpaperHost>(),
                    sp.GetRequiredService<MonitorTopologyService>(),
                    loggerFactory.CreateLogger<DesktopWallpaperRenderer>());
            };
        });

        services.AddSingleton<IWallpaperEngineOrchestrator>(sp =>
            new WallpaperEngineOrchestrator(
                sp.GetRequiredService<ILogger<WallpaperEngineOrchestrator>>(),
                sp.GetRequiredService<Func<WallpaperType, IWallpaperRenderer>>()));
        services.AddSingleton<SavedWallpaperService>();

        if (singleInstanceService != null)
        {
            services.AddSingleton<ISingleInstanceService>(singleInstanceService);
        }
        else
        {
            services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        }

        // 8. Desktop Shell Integration & Performance Monitoring
        services.AddSingleton<IDesktopWallpaperHost, DesktopWallpaperHost>();
        services.AddSingleton<MonitorTopologyService>();
        services.AddSingleton<WindowsStartupService>();
        services.AddSingleton<HardwareTuningService>();
        services.AddSingleton<AdaptiveQualityService>(sp => new AdaptiveQualityService(appDataRoot,
            sp.GetRequiredService<HardwareTuningService>(), sp.GetRequiredService<ILogger<AdaptiveQualityService>>()));
        services.AddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();

        // 9. UI Management & ViewModels
        services.AddSingleton<IWindowManagementService, WindowManagementService>();
        services.AddSingleton<ISystemTrayManager, SystemTrayManager>();
        services.AddTransient<ExploreViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
