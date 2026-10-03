using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lumen.Application.Services;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.UI.Services;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.ViewModels;

/// <summary>
/// Main application ViewModel powering the WinUI 3 Explore, Library, Gallery, Import, and Storage experiences.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly IWindowManagementService _windowManager;
    private readonly ILibraryService _libraryService;
    private readonly IWallpaperImportService _importService;
    private readonly IStorageManager _storageManager;
    private readonly ExploreViewModel _exploreViewModel;
    private readonly ILogger<MainViewModel> _logger;
    private readonly ISettingsService _settingsService;

    private readonly ObservableCollection<WallpaperItemViewModel> _wallpapers = [];
    private string _statusMessage = "Lumen Wallpaper Platform";
    private string _activeMonitorsSummary = "Monitors: Ready";
    private string? _errorMessage;
    private bool _isLoading;
    private bool _isPaused;
    private int _selectedNavIndex = 0; // 0 = Explore, 1 = Library
    private StorageUsageInfo? _storageUsage;
    private WallpaperItemViewModel? _selectedWallpaper;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExploreViewModel ExploreViewModel => _exploreViewModel;

    public ReadOnlyObservableCollection<WallpaperItemViewModel> Wallpapers { get; }

    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            if (SetField(ref _selectedNavIndex, value))
            {
                OnPropertyChanged(nameof(IsExploreSelected));
                OnPropertyChanged(nameof(IsLibrarySelected));
            }
        }
    }

    public bool IsExploreSelected => _selectedNavIndex == 0;
    public bool IsLibrarySelected => _selectedNavIndex == 1;

    public WallpaperItemViewModel? SelectedWallpaper
    {
        get => _selectedWallpaper;
        set => SetField(ref _selectedWallpaper, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string ActiveMonitorsSummary
    {
        get => _activeMonitorsSummary;
        private set => SetField(ref _activeMonitorsSummary, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasWallpapers => _wallpapers.Count > 0;
    public bool IsEmpty => !_isLoading && _wallpapers.Count == 0;

    public bool IsPaused
    {
        get => _isPaused;
        private set => SetField(ref _isPaused, value);
    }

    public string PauseButtonText => IsPaused ? "Resume All" : "Pause All";

    public StorageUsageInfo? StorageUsage
    {
        get => _storageUsage;
        private set
        {
            if (SetField(ref _storageUsage, value))
            {
                OnPropertyChanged(nameof(StorageSummary));
                OnPropertyChanged(nameof(StorageRootText));
            }
        }
    }

    public string StorageSummary => _storageUsage != null
        ? $"{_storageUsage.TotalSizeMb:F1} MB used ({_storageUsage.WallpaperCount} wallpapers)"
        : "Storage: Calculating...";

    public string StorageRootText => _storageUsage?.StorageRoot ?? _storageManager.RootStoragePath;

    public MainViewModel(
        IWallpaperEngineOrchestrator orchestrator,
        IWindowManagementService windowManager,
        ILibraryService libraryService,
        IWallpaperImportService importService,
        IStorageManager storageManager,
        ExploreViewModel exploreViewModel,
        ILogger<MainViewModel> logger,
        ISettingsService settingsService)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _exploreViewModel = exploreViewModel ?? throw new ArgumentNullException(nameof(exploreViewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        Wallpapers = new ReadOnlyObservableCollection<WallpaperItemViewModel>(_wallpapers);

        _isPaused = _orchestrator.IsGloballyPaused;
        _orchestrator.GlobalPauseChanged += OnGlobalPauseChanged;
        _orchestrator.MonitorStateChanged += OnMonitorStateChanged;
        _libraryService.LibraryChanged += async (_, _) => await RefreshLibraryAsync().ConfigureAwait(false);

        RefreshMonitorSummary();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            RefreshLibraryAsync(),
            _exploreViewModel.InitializeAsync()).ConfigureAwait(false);
    }

    public async Task RefreshLibraryAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var wallpapers = await _libraryService.GetWallpapersAsync().ConfigureAwait(false);
            var usage = await _libraryService.GetStorageUsageAsync().ConfigureAwait(false);

            _wallpapers.Clear();
            foreach (var wp in wallpapers)
            {
                string resolvedMedia = _storageManager.ResolveAbsolutePath(wp.FilePath);
                string resolvedThumb = _storageManager.ResolveAbsolutePath(wp.ThumbnailPath);
                _wallpapers.Add(new WallpaperItemViewModel(wp, resolvedThumb, resolvedMedia));
            }

            StorageUsage = usage;
            StatusMessage = $"Library loaded ({_wallpapers.Count} wallpapers)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load library items.");
            ErrorMessage = $"Failed to load library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasWallpapers));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var list = filePaths.ToList();
        if (list.Count == 0) return;

        IsLoading = true;
        StatusMessage = $"Importing {list.Count} files...";

        try
        {
            var results = await _importService.ImportFilesAsync(list).ConfigureAwait(false);
            int successCount = results.Count(r => r.Success && !r.IsDuplicate);
            int duplicateCount = results.Count(r => r.IsDuplicate);
            int failCount = results.Count(r => !r.Success);

            await RefreshLibraryAsync().ConfigureAwait(false);

            if (failCount > 0)
            {
                StatusMessage = $"Imported {successCount} files. {duplicateCount} duplicates, {failCount} failed.";
            }
            else if (duplicateCount > 0)
            {
                StatusMessage = $"Imported {successCount} files. {duplicateCount} files already in library.";
            }
            else
            {
                StatusMessage = $"Successfully imported {successCount} wallpapers.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch import encountered an unexpected error.");
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SetAsWallpaperAsync(WallpaperItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.IsApplying = true;
        StatusMessage = $"Applying '{item.Title}' as desktop wallpaper...";

        try
        {
            string absolutePath = _storageManager.ResolveAbsolutePath(item.Wallpaper.FilePath);
            if (!File.Exists(absolutePath))
            {
                ErrorMessage = $"Media file for '{item.Title}' is missing on disk.";
                return;
            }

            var activeWp = new Wallpaper
            {
                Id = item.Wallpaper.Id,
                Title = item.Wallpaper.Title,
                Type = item.Wallpaper.Type,
                FilePath = absolutePath,
                ThumbnailPath = _storageManager.ResolveAbsolutePath(item.Wallpaper.ThumbnailPath),
                ContentHash = item.Wallpaper.ContentHash,
                Width = item.Wallpaper.Width,
                Height = item.Wallpaper.Height
            };

            var appSettings = _settingsService.CurrentSettings;
            var playbackSettings = new WallpaperSettings
            {
                TargetFps = appSettings.GlobalTargetFps,
                ScalingMode = appSettings.DefaultScalingMode,
                Volume = appSettings.DefaultVolume,
                IsMuted = appSettings.DefaultMuted,
                PlaybackSpeed = appSettings.DefaultPlaybackSpeed,
                Loop = appSettings.DefaultLoop,
                Brightness = appSettings.DefaultBrightness,
                Contrast = appSettings.DefaultContrast,
                Saturation = appSettings.DefaultSaturation
            };

            await _orchestrator.ApplyWallpaperAsync(
                "PRIMARY_MONITOR",
                activeWp,
                playbackSettings).ConfigureAwait(false);
            StatusMessage = $"'{item.Title}' is now active on the desktop.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply wallpaper '{Title}'.", item.Title);
            ErrorMessage = $"Failed to set wallpaper: {ex.Message}";
        }
        finally
        {
            item.IsApplying = false;
        }
    }

    public async Task DeleteWallpaperAsync(WallpaperItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        StatusMessage = $"Deleting '{item.Title}'...";

        try
        {
            var result = await _libraryService.DeleteWallpaperAsync(item.Id).ConfigureAwait(false);
            if (result.Success)
            {
                _wallpapers.Remove(item);
                StorageUsage = await _libraryService.GetStorageUsageAsync().ConfigureAwait(false);
                StatusMessage = $"Deleted '{item.Title}' from library.";
                OnPropertyChanged(nameof(HasWallpapers));
                OnPropertyChanged(nameof(IsEmpty));
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to delete wallpaper.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting wallpaper '{Id}'.", item.Id);
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    public async Task ReconcileLibraryAsync()
    {
        IsLoading = true;
        StatusMessage = "Reconciling library files and thumbnails...";

        try
        {
            var result = await _libraryService.ReconcileLibraryAsync().ConfigureAwait(false);
            await RefreshLibraryAsync().ConfigureAwait(false);

            StatusMessage = $"Reconciliation: {result.ValidWallpapers} valid, {result.MissingMediaIds.Count} missing, {result.RegeneratedThumbnailsCount} thumbnails rebuilt.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed.");
            ErrorMessage = $"Reconciliation failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> MigrateStorageAsync(string newRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRootPath);

        IsLoading = true;
        StatusMessage = $"Migrating storage to '{newRootPath}'...";

        try
        {
            bool success = await _libraryService.MigrateStorageRootAsync(newRootPath).ConfigureAwait(false);
            if (success)
            {
                await RefreshLibraryAsync().ConfigureAwait(false);
                StatusMessage = "Storage root migrated successfully.";
                return true;
            }
            else
            {
                ErrorMessage = "Storage migration failed. Preserved original location.";
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage migration error.");
            ErrorMessage = $"Storage migration error: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task StopAllWallpapersAsync()
    {
        await _orchestrator.StopAllAsync().ConfigureAwait(false);
        StatusMessage = "All wallpapers stopped.";
    }

    public async Task ResumeAllWallpapersAsync()
    {
        await _orchestrator.ResumeAllAsync().ConfigureAwait(false);
        StatusMessage = "Wallpaper playback resumed.";
    }

    public async Task ToggleMuteAsync()
    {
        await _orchestrator.ToggleMuteAllAsync().ConfigureAwait(false);
        StatusMessage = "Wallpaper audio toggled.";
    }

    public async Task NextWallpaperAsync()
    {
        if (_wallpapers.Count == 0) return;
        int currentIndex = _selectedWallpaper == null ? -1 : _wallpapers.IndexOf(_selectedWallpaper);
        var next = _wallpapers[(currentIndex + 1) % _wallpapers.Count];
        SelectedWallpaper = next;
        await SetAsWallpaperAsync(next).ConfigureAwait(false);
    }

    public async Task TogglePauseResumeAsync()
    {
        _logger.LogInformation("Toggling global pause from main window UI.");
        await _orchestrator.TogglePauseAllAsync().ConfigureAwait(false);
    }

    public void HideToTray()
    {
        _logger.LogInformation("Minimizing main window to tray.");
        _windowManager.Hide();
    }

    private void OnGlobalPauseChanged(object? sender, bool isPaused)
    {
        IsPaused = isPaused;
        OnPropertyChanged(nameof(PauseButtonText));
        StatusMessage = isPaused ? "Playback Paused" : "Playback Active";
    }

    private void OnMonitorStateChanged(object? sender, (string MonitorDeviceId, Core.Enums.WallpaperState State) e)
    {
        RefreshMonitorSummary();
    }

    private void RefreshMonitorSummary()
    {
        var states = _orchestrator.GetActiveMonitorStates();
        ActiveMonitorsSummary = states.Count == 0
            ? "Monitors: Standby"
            : $"Monitors: {states.Count} Active";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
