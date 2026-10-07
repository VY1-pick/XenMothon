using System.ComponentModel;
using System.Numerics;
using XenMotion.Core.Enums;
using XenMotion.Core.Models.Catalog;
using XenMotion.UI.Services;
using XenMotion.UI.Tray;
using XenMotion.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace XenMotion.UI;

/// <summary>
/// Main window: Explore, Library, advanced filters, settings and dialogs, all in dark liquid glass.
/// This file holds construction, view-model wiring, navigation and the tray hooks.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _syncingPlaybackVolume;
    private readonly MainViewModel _viewModel;
    private readonly IWindowManagementService _windowManager;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ISystemTrayManager _trayManager;

    private string? _lastError;
    private string? _lastStatus;
    private int _uiUpdatePending;

    private void QueueUiUpdate()
    {
        if (Interlocked.Exchange(ref _uiUpdatePending, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _uiUpdatePending, 0);
            UpdateUiState();
        })) Interlocked.Exchange(ref _uiUpdatePending, 0);
    }

    public MainViewModel ViewModel => _viewModel;

    public MainWindow(MainViewModel viewModel, IWindowManagementService windowManager, SettingsViewModel settingsViewModel, ISystemTrayManager trayManager)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        _trayManager = trayManager ?? throw new ArgumentNullException(nameof(trayManager));

        Title = "XenMotion";

        // The app is dark-only. Keep the persisted preference in line so nothing else ever flips it.
        _settingsViewModel.Theme = "Dark";

        // Register the real window before tray callbacks can try to restore it.
        _windowManager.RegisterMainWindow(this);

        TrySetBackdrop();
        ConfigureWindowChrome();
        ResizeToComfortableSize();

        WireViewModels();
        BindCollections();

        // Closing the window hides it to the tray when the shell accepted our icon.
        AppWindow.Closing += (_, args) =>
        {
            if (_trayManager.IsInitialized)
            {
                args.Cancel = true;
                _windowManager.Hide();
                _trayManager.ShowNotification("XenMotion is still running", "The window was minimized to the system tray.");
            }
            else
            {
                App.Current.Shutdown();
            }
        };

        ShowFilterTab("Basics");
        ShowSettingsTab("General");
        UpdateSettingsUiState();
        UpdateUiState();
        EnforceMinimumWindowSize();
        RootGrid.SizeChanged += (_, _) => EnforceMinimumWindowSize();

        RootGrid.Loaded += (_, _) =>
        {
            BuildAmbientLight();
            UpdateMaximizeGlyph();
            UpdateInputRegions();
        };

        AddKeyboardShortcuts();

        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await _viewModel.InitializeAsync();
            UpdateUiState();
        });
    }

    // ------------------------------------------------------------------ wiring

    private void WireViewModels()
    {
        _viewModel.PropertyChanged += (_, _) => QueueUiUpdate();
        _viewModel.ExploreViewModel.PropertyChanged += (_, _) => QueueUiUpdate();
        _viewModel.ExploreViewModel.DetailsViewModel.PropertyChanged += (_, _) => QueueUiUpdate();
        _settingsViewModel.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateSettingsUiState);
        _viewModel.ExploreViewModel.WallpaperApplied += (_, e) =>
            DispatcherQueue.TryEnqueue(() => ShowToast($"“{e.Title}” is now on your desktop"));
    }


    private void BindCollections()
    {
        var explore = _viewModel.ExploreViewModel;

        WallpaperGridView.ItemsSource = _viewModel.Wallpapers;
        ExploreGridView.ItemsSource = explore.SearchResults;

        CategoryRepeater.ItemsSource = explore.Categories;
        CategoryRepeater.ElementPrepared += OnCategoryElementPrepared;
        TagsRepeater.ItemsSource = explore.PopularTags;

        TypeRepeater.ItemsSource = explore.TypeGroup.Items;
        SortRepeater.ItemsSource = explore.SortGroup.Items;
        ResolutionRepeater.ItemsSource = explore.ResolutionGroup.Items;
        OrientationRepeater.ItemsSource = explore.OrientationGroup.Items;
        DurationRepeater.ItemsSource = explore.DurationGroup.Items;
        TagFilterRepeater.ItemsSource = explore.TagGroup.Items;

        DetailsTagsRepeater.ItemsSource = Array.Empty<string>();
    }

    private void AddKeyboardShortcuts()
    {
        var focusSearch = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        focusSearch.Invoked += (_, args) =>
        {
            _viewModel.SelectedNavIndex = 0;
            UpdateUiState();
            ExploreSearchBox.Focus(FocusState.Programmatic);
            ExploreSearchBox.SelectAll();
            args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(focusSearch);

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            if (CloseTopmostOverlay()) args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(escape);
    }

    private bool CloseTopmostOverlay()
    {
        if (DownloadOverlay.Visibility == Visibility.Visible && DownloadOverlay.Opacity > 0) { CloseDownloadSheet(); return true; }
        if (SettingsOverlay.Visibility == Visibility.Visible && SettingsOverlay.Opacity > 0) { CloseSettingsSheet(); return true; }
        if (DetailsOverlay.Visibility == Visibility.Visible && DetailsOverlay.Opacity > 0) { _viewModel.ExploreViewModel.CloseDetails(); return true; }
        return false;
    }

    // ------------------------------------------------------------------ state -> UI

    private async void OnPlaybackVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingPlaybackVolume || _viewModel is null || NowPlayingVolumeText is null) return;
        NowPlayingVolumeText.Text = $"{e.NewValue:0}%";
        try { await _viewModel.SetVolumeAsync((float)(e.NewValue / 100)); }
        catch (Exception ex) { _viewModel.StatusMessage = $"Could not adjust audio: {ex.Message}"; }
    }
    private void UpdateUiState()
    {
        var explore = _viewModel.ExploreViewModel;

        // navigation
        bool exploreSelected = _viewModel.IsExploreSelected;
        NavExploreButton.IsChecked = exploreSelected;
        NavLibraryButton.IsChecked = !exploreSelected;
        ExploreContentGrid.Visibility = exploreSelected ? Visibility.Visible : Visibility.Collapsed;
        LibraryContentGrid.Visibility = exploreSelected ? Visibility.Collapsed : Visibility.Visible;
        ImportButton.Visibility = exploreSelected ? Visibility.Collapsed : Visibility.Visible;
        SyncFilterDockWithNavigation();

        // now playing dock
        NowPlayingPanel.Visibility = _viewModel.HasActiveWallpaper ? Visibility.Visible : Visibility.Collapsed;
        NowPlayingTitleTextBlock.Text = _viewModel.ActiveWallpaperTitle;
        NowPlayingPauseIcon.Glyph = _viewModel.IsPaused ? "" : "";
        _syncingPlaybackVolume = true;
        NowPlayingVolumeSlider.Value = _viewModel.VolumePercentage;
        NowPlayingVolumeText.Text = $"{_viewModel.VolumePercentage:0}%";
        _syncingPlaybackVolume = false;
        NowPlayingMuteIcon.Glyph = _viewModel.IsMuted ? "" : "";
        ToolTipService.SetToolTip(PauseResumeButton, _viewModel.IsPaused ? "Resume" : "Pause");

        // explore
        ExploreLoadingPanel.Visibility = explore.IsLoading && explore.SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExploreEmptyStatePanel.Visibility = explore.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        ExploreGridView.Visibility = explore.SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExploreCountTextBlock.Text = explore.ResultCountText;
        PaginationPill.Visibility = explore.HasMultiplePages ? Visibility.Visible : Visibility.Collapsed;
        PrevPageButton.IsEnabled = explore.HasPreviousPage;
        NextPageButton.IsEnabled = explore.HasNextPage;
        PageSummaryTextBlock.Text = explore.PageSummaryText;
        SearchClearButton.Visibility = string.IsNullOrEmpty(ExploreSearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        SyncCategoryChips();
        UpdateFilterUiState();

        // library
        LoadingPanel.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        WallpaperGridView.Visibility = _viewModel.HasWallpapers ? Visibility.Visible : Visibility.Collapsed;
        LibrarySubtitleText.Text = $"{_viewModel.Wallpapers.Count} wallpapers  •  {_viewModel.StorageSummary}";
        StorageUsageTextBlock.Text = _viewModel.StorageSummary;
        StorageRootTextBlock.Text = _viewModel.StorageRootText;

        UpdateDetailsState();
        UpdateToasts(explore);
    }

    private void UpdateToasts(ExploreViewModel explore)
    {
        string? error = _viewModel.ErrorMessage ?? explore.ErrorMessage;
        if (!string.IsNullOrEmpty(error))
        {
            if (error != _lastError)
            {
                _lastError = error;
                ShowToast(error, isError: true);
            }

            // The toast now owns the message; clearing it lets the same error be reported again later.
            _viewModel.ErrorMessage = null;
            explore.ClearError();
        }
        else
        {
            _lastError = null;
        }

        string status = _viewModel.StatusMessage ?? string.Empty;
        if (status != _lastStatus)
        {
            _lastStatus = status;
            if (IsToastWorthyStatus(status)) ShowToast(status);
        }
    }

    private static bool IsToastWorthyStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("active on the desktop", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Imported", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Successfully imported", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Deleted", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Storage root", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("All wallpapers stopped", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Wallpaper audio", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Wallpaper playback resumed", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Reconciliation", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ navigation

    private void OnNavExploreClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedNavIndex = 0;
        UpdateUiState();
    }

    private void OnNavLibraryClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedNavIndex = 1;
        UpdateUiState();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsExploreSelected)
        {
            await _viewModel.ExploreViewModel.ExecuteSearchAsync(resetPage: true);
        }
        else
        {
            await _viewModel.RefreshLibraryAsync();
        }
    }

    // ------------------------------------------------------------------ window size

    private void ResizeToComfortableSize()
    {
        try
        {
            var size = AppWindow.Size;
            if (size.Width < 1280 || size.Height < 820)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(1280, size.Width), Math.Max(820, size.Height)));
            }
        }
        catch
        {
        }
    }

    private void EnforceMinimumWindowSize()
    {
        try
        {
            const int minWidth = 1040;
            const int minHeight = 680;
            var size = AppWindow.Size;
            if (size.Width < minWidth || size.Height < minHeight)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(minWidth, size.Width), Math.Max(minHeight, size.Height)));
            }
        }
        catch
        {
            // Some shell states reject an immediate resize; keep the current size.
        }
    }
}
