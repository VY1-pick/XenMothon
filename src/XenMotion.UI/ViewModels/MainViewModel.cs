using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Services;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.ViewModels;

/// <summary>
/// Main application ViewModel powering the WinUI 3 Explore, Library, Gallery, Import, and Storage experiences.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SavedWallpaperService _savedWallpapers;
    public void SetRestoredWallpaper(Guid id, string title) => SetActiveWallpaper(id, title);
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly IWindowManagementService _windowManager;
    private readonly ILibraryService _libraryService;
    private readonly IWallpaperImportService _importService;
    private readonly IStorageManager _storageManager;
    private readonly ExploreViewModel _exploreViewModel;
    private readonly ILogger<MainViewModel> _logger;
    private readonly ISettingsService _settingsService;

    private readonly ObservableCollection<WallpaperItemViewModel> _wallpapers = [];
    private string _statusMessage = "XenMotion Wallpaper Platform";
    private string _activeMonitorsSummary = "Monitors: Ready";
    private string? _errorMessage;
    private bool _isLoading;
    private bool _isPaused;
    private int _selectedNavIndex = 0; // 0 = Explore, 1 = Library
    private StorageUsageInfo? _storageUsage;
    private WallpaperItemViewModel? _selectedWallpaper;
    private Guid? _activeWallpaperId;
    private string _activeWallpaperTitle = string.Empty;
    private bool _isMuted;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

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

    public Guid? ActiveWallpaperId => _activeWallpaperId;
    public string ActiveWallpaperTitle => _activeWallpaperTitle;
    public bool HasActiveWallpaper => _activeWallpaperId.HasValue;
    public bool IsMuted => _isMuted;
    public double VolumePercentage => _settingsService.CurrentSettings.DefaultVolume * 100;

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
        ISettingsService settingsService,
        SavedWallpaperService savedWallpapers)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _exploreViewModel = exploreViewModel ?? throw new ArgumentNullException(nameof(exploreViewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _savedWallpapers = savedWallpapers;

        Wallpapers = new ReadOnlyObservableCollection<WallpaperItemViewModel>(_wallpapers);

        _isMuted = _settingsService.CurrentSettings.DefaultMuted;
        _exploreViewModel.WallpaperApplied += (_, e) => SetActiveWallpaper(e.Id, e.Title);
        _isPaused = _orchestrator.IsGloballyPaused;
        _orchestrator.GlobalPauseChanged += OnGlobalPauseChanged;
        _orchestrator.MonitorStateChanged += OnMonitorStateChanged;
        _libraryService.LibraryChanged += (_, _) => { if (!_importing) RunOnUi(async () => await RefreshLibraryAsync()); };

        RefreshMonitorSummary();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            RefreshLibraryAsync(),
            _exploreViewModel.InitializeAsync());
    }

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private volatile bool _importing;

    public async Task RefreshLibraryAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            var wallpapers = await _libraryService.GetWallpapersAsync();
            var usage = await _libraryService.GetStorageUsageAsync();

            _wallpapers.Clear();
            foreach (var wp in wallpapers)
            {
                string resolvedMedia = _storageManager.ResolveAbsolutePath(wp.FilePath);
                string resolvedThumb = _storageManager.ResolveAbsolutePath(wp.ThumbnailPath);
                var itemVm = new WallpaperItemViewModel(wp, resolvedThumb, resolvedMedia)
                {
                    IsActive = _activeWallpaperId == wp.Id
                };
                itemVm.SetPlaybackState(_isPaused, _isMuted);
                _wallpapers.Add(itemVm);
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
            _refreshGate.Release();
            IsLoading = false;
            OnPropertyChanged(nameof(HasWallpapers));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var list = filePaths.ToList();
        if (list.Count == 0 || _importing) return;
        _importing = true;

        IsLoading = true;
        StatusMessage = $"Importing {list.Count} files...";

        try
        {
            var results = await _importService.ImportFilesAsync(list);
            int successCount = results.Count(r => r.Success && !r.IsDuplicate);
            int duplicateCount = results.Count(r => r.IsDuplicate);
            int failCount = results.Count(r => !r.Success);

            await RefreshLibraryAsync();

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
            _importing = false;
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

            var playbackSettings = _settingsService.CurrentSettings.CreatePlaybackSettings();

            await _savedWallpapers.ApplyAsync(
                "PRIMARY_MONITOR",
                activeWp,
                playbackSettings);
            SetActiveWallpaper(item.Id, item.Title);
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
            if (item.IsActive) await StopAllWallpapersAsync();
            var result = await _libraryService.DeleteWallpaperAsync(item.Id);
            if (result.Success)
            {
                _wallpapers.Remove(item);
                StorageUsage = await _libraryService.GetStorageUsageAsync();
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
            var result = await _libraryService.ReconcileLibraryAsync();
            await RefreshLibraryAsync();

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
            bool success = await _libraryService.MigrateStorageRootAsync(newRootPath);
            if (success)
            {
                await RefreshLibraryAsync();
                await _settingsService.UpdateSettingsAsync(s => s.StoragePath = _storageManager.RootStoragePath);
                StatusMessage = "Storage root saved. The previous folder is retained as a recovery copy.";
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
        await _orchestrator.StopAllAsync();
        await _savedWallpapers.ClearAsync();
        SetActiveWallpaper(null, string.Empty);
        StatusMessage = "All wallpapers stopped.";
    }

    public async Task ResumeAllWallpapersAsync()
    {
        await _orchestrator.ResumeAllAsync();
        StatusMessage = "Wallpaper playback resumed.";
    }

    public async Task SyncAudioSettingsAsync()
    {
        var settings = _settingsService.CurrentSettings;
        _isMuted = settings.DefaultMuted;
        await _orchestrator.SetVolumeAllAsync(_isMuted ? 0 : settings.DefaultVolume);
        SyncPlaybackState();
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercentage));
    }

    public async Task SetVolumeAsync(float volume)
    {
        volume = Math.Clamp(volume, 0, 1);
        await _orchestrator.SetVolumeAllAsync(volume);
        await _settingsService.UpdateSettingsAsync(s => { s.DefaultVolume = volume; s.DefaultMuted = volume <= 0; });
        _isMuted = volume <= 0;
        SyncPlaybackState();
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercentage));
    }

    public async Task ToggleMuteAsync()
    {
        _isMuted = !_isMuted;
        float volume = _settingsService.CurrentSettings.DefaultVolume;
        if (!_isMuted && volume <= 0) volume = 1;
        await _orchestrator.SetVolumeAllAsync(_isMuted ? 0 : volume);
        await _settingsService.UpdateSettingsAsync(s => { s.DefaultMuted = _isMuted; s.DefaultVolume = volume; });
        SyncPlaybackState();
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercentage));
        StatusMessage = _isMuted ? "Wallpaper audio muted." : "Wallpaper audio on.";
    }
    public async Task NextWallpaperAsync()
    {
        if (_wallpapers.Count == 0) return;
        int currentIndex = _selectedWallpaper == null ? -1 : _wallpapers.IndexOf(_selectedWallpaper);
        var next = _wallpapers[(currentIndex + 1) % _wallpapers.Count];
        SelectedWallpaper = next;
        await SetAsWallpaperAsync(next);
    }

    public async Task TogglePauseResumeAsync()
    {
        _logger.LogInformation("Toggling global pause from main window UI.");
        await _orchestrator.TogglePauseAllAsync();
    }

    public void HideToTray()
    {
        _logger.LogInformation("Minimizing main window to tray.");
        _windowManager.Hide();
    }

    private void OnGlobalPauseChanged(object? sender, bool isPaused)
    {
        RunOnUi(() =>
        {
            IsPaused = isPaused;
            SyncPlaybackState();
            OnPropertyChanged(nameof(PauseButtonText));
            StatusMessage = isPaused ? "Playback Paused" : "Playback Active";
        });
    }

    /// <summary>Orchestrator/library events may come from worker threads; UI-bound state must change on the UI thread.</summary>
    private void RunOnUi(Action action)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    private void SetActiveWallpaper(Guid? id, string title)
    {
        _activeWallpaperId = id;
        _activeWallpaperTitle = title;
        foreach (var wp in _wallpapers)
        {
            wp.IsActive = id.HasValue && wp.Id == id.Value;
        }
        SyncPlaybackState();
        OnPropertyChanged(nameof(ActiveWallpaperId));
        OnPropertyChanged(nameof(ActiveWallpaperTitle));
        OnPropertyChanged(nameof(HasActiveWallpaper));
    }

    private void SyncPlaybackState()
    {
        foreach (var wp in _wallpapers)
        {
            wp.SetPlaybackState(_isPaused, _isMuted);
        }
    }

    private void OnMonitorStateChanged(object? sender, (string MonitorDeviceId, Core.Enums.WallpaperState State) e)
    {
        RunOnUi(RefreshMonitorSummary);
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
