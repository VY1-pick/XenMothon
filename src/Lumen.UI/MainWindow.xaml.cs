using System.ComponentModel;
using Lumen.Core.Enums;
using Lumen.Core.Models.Catalog;
using Lumen.UI.Services;
using Lumen.UI.Tray;
using Lumen.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Windows.System;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lumen.UI;

/// <summary>
/// Main application window providing Explore/Catalog discovery, local Library management,
/// drag-and-drop import, and detailed wallpaper inspection.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IWindowManagementService _windowManager;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ISystemTrayManager _trayManager;
    private Storyboard? _settingsStoryboard;
    private Guid? _shownDetailsId;
    private bool _syncingCategory;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow(MainViewModel viewModel, IWindowManagementService windowManager, SettingsViewModel settingsViewModel, ISystemTrayManager trayManager)
    {
        InitializeComponent();

        // Modern custom title bar. The content area now owns the visual header while
        // Windows still provides the native minimize/maximize/close caption buttons.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        _trayManager = trayManager ?? throw new ArgumentNullException(nameof(trayManager));

        // Register the actual WinUI window before tray callbacks can attempt to restore it.
        // Without this registration, the tray can receive OpenRequested but the window
        // manager has no HWND/window reference and ShowAndActivate() becomes a no-op.
        _windowManager.RegisterMainWindow(this);

        // Connect ViewModel event listeners
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _viewModel.ExploreViewModel.PropertyChanged += OnExploreViewModelPropertyChanged;
        _viewModel.ExploreViewModel.DetailsViewModel.PropertyChanged += OnDetailsViewModelPropertyChanged;
        _settingsViewModel.PropertyChanged += OnSettingsViewModelPropertyChanged;

        _trayManager.OpenRequested += OnTrayOpenRequested;
        _trayManager.PauseResumeRequested += OnTrayPauseResumeRequested;
        _trayManager.StopRequested += OnTrayStopRequested;
        _trayManager.PlayRequested += OnTrayPlayRequested;
        _trayManager.NextWallpaperRequested += OnTrayNextWallpaperRequested;
        _trayManager.MuteRequested += OnTrayMuteRequested;
        _trayManager.ExitRequested += OnTrayExitRequested;

        // Bind items sources
        WallpaperGridView.ItemsSource = _viewModel.Wallpapers;
        ExploreGridView.ItemsSource = _viewModel.ExploreViewModel.SearchResults;
        CategoryListView.ItemsSource = _viewModel.ExploreViewModel.Categories;
        TagsListView.ItemsSource = _viewModel.ExploreViewModel.PopularTags;
        DetailsTagsRepeater.ItemsSource = Array.Empty<string>();

        // Closing the window means "hide to tray" only when the shell accepted our tray icon.
        // If tray setup failed, shut down normally rather than leaving an unreachable hidden process.
        AppWindow.Closing += (sender, args) =>
        {
            if (_trayManager.IsInitialized)
            {
                args.Cancel = true;
                _windowManager.Hide();
                _trayManager.ShowNotification("Lumen is still running", "The window was minimized to the system tray.");
            }
            else
            {
                App.Current.Shutdown();
            }
        };

        ApplyRequestedTheme();
        ConfigureTitleBar();
        UpdateSettingsUiState();
        UpdateUiState();
        EnforceMinimumWindowSize();
        RootGrid.SizeChanged += OnRootGridSizeChanged;

        // Caption button colours depend on the *actual* theme, which is only final after load
        // and changes whenever the user switches theme (or Windows does, in System mode).
        RootGrid.Loaded += (_, _) =>
        {
            ApplyCaptionColors();
            if (RootGrid.Resources["OrbStoryboard"] is Storyboard orbs)
            {
                orbs.Begin();
            }
        };
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ApplyCaptionColors();
            UpdateUiState();
        };

        // Ctrl+K focuses the search box from anywhere.
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

        // Asynchronously load library and catalog items upon window creation
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await _viewModel.InitializeAsync();
            UpdateUiState();
        });
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUiState);
    }

    private void OnExploreViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUiState);
    }

    private void OnDetailsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUiState);
    }

    private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateSettingsUiState);
    }

    private void UpdateUiState()
    {
        // 1. Status bar & top indicators
        StatusTextBlock.Text = _viewModel.StatusMessage;
        StorageSummaryTextBlock.Text = _viewModel.StorageSummary;
        PauseResumeIcon.Glyph = _viewModel.IsPaused ? "\uE768" : "\uE769";
        ToolTipService.SetToolTip(PauseResumeButton, _viewModel.IsPaused ? "Resume playback" : "Pause playback");
        StorageRootTextBlock.Text = $"Location: {_viewModel.StorageRootText}";
        StorageUsageTextBlock.Text = _viewModel.StorageSummary;

        // 2. Now playing bar
        NowPlayingPanel.Visibility = _viewModel.HasActiveWallpaper ? Visibility.Visible : Visibility.Collapsed;
        NowPlayingTitleTextBlock.Text = _viewModel.ActiveWallpaperTitle;
        NowPlayingPauseIcon.Glyph = _viewModel.IsPaused ? "\uE768" : "\uE769";
        NowPlayingMuteIcon.Glyph = _viewModel.IsMuted ? "\uE74F" : "\uE767";

        // 3. Navigation
        bool exploreSelected = _viewModel.IsExploreSelected;
        ExploreContentGrid.Visibility = exploreSelected ? Visibility.Visible : Visibility.Collapsed;
        LibraryContentGrid.Visibility = exploreSelected ? Visibility.Collapsed : Visibility.Visible;
        SetNavigationVisualState(NavExploreButton, exploreSelected);
        SetNavigationVisualState(NavLibraryButton, !exploreSelected);

        // 4. Explore
        var explore = _viewModel.ExploreViewModel;
        ExploreLoadingPanel.Visibility = explore.IsLoading && explore.SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExploreEmptyStatePanel.Visibility = explore.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        ExploreGridView.Visibility = explore.SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExploreCountTextBlock.Text = explore.ResultCountText;
        PaginationPill.Visibility = explore.HasMultiplePages ? Visibility.Visible : Visibility.Collapsed;
        PrevPageButton.IsEnabled = explore.HasPreviousPage;
        NextPageButton.IsEnabled = explore.HasNextPage;
        PageSummaryTextBlock.Text = explore.PageSummaryText;
        SearchClearButton.Visibility = string.IsNullOrEmpty(ExploreSearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;

        if (!ReferenceEquals(CategoryListView.SelectedItem, explore.SelectedCategory) && explore.SelectedCategory != null)
        {
            _syncingCategory = true;
            CategoryListView.SelectedItem = explore.SelectedCategory;
            _syncingCategory = false;
        }

        // 5. Library
        LoadingPanel.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        WallpaperGridView.Visibility = _viewModel.HasWallpapers ? Visibility.Visible : Visibility.Collapsed;

        // 6. Details modal. Heavy content is rebuilt only when the shown item changes (no flicker).
        var details = explore.DetailsViewModel;
        if (details.IsOpen && details.Item != null)
        {
            var item = details.Item;
            DetailsModalOverlay.Visibility = Visibility.Visible;

            if (_shownDetailsId != item.Id)
            {
                _shownDetailsId = item.Id;
                DetailsTitleTextBlock.Text = item.Title;
                DetailsAuthorTextBlock.Text = $"by {item.Author}  •  {item.CategoryName}";
                DetailsDescriptionTextBlock.Text = item.Description;
                DetailsSpecsTextBlock.Text = $"{item.ResolutionText}  •  {item.FormattedSize}  •  {details.AspectRatioText}";
                DetailsStatsTextBlock.Text = $"{item.RatingText}   •   {item.DownloadsText}";
                DetailsTagsRepeater.ItemsSource = item.Tags.Select(t => $"#{t}").ToList();
                DetailsPreviewBrush.ImageSource = item.ThumbnailImage;
            }

            DetailsApplyButton.IsEnabled = item.IsDownloaded;
            DetailsSubscribeButton.Content = item.IsDownloaded ? "Downloaded ✓" : item.IsDownloading ? $"Downloading {item.DownloadProgress:F0}%" : item.CanSubscribe ? "Download" : "Unavailable";
            DetailsSubscribeButton.IsEnabled = item.CanSubscribe;
        }
        else
        {
            _shownDetailsId = null;
            DetailsModalOverlay.Visibility = Visibility.Collapsed;
        }

        // 7. Global error bar
        string? activeError = _viewModel.ErrorMessage ?? explore.ErrorMessage;
        if (!string.IsNullOrEmpty(activeError))
        {
            ErrorNotificationBar.Message = activeError;
            ErrorNotificationBar.IsOpen = true;
        }
        else
        {
            ErrorNotificationBar.IsOpen = false;
        }
    }

    private void UpdateSettingsUiState()
    {
        SettingsDisplayNameTextBox.Text = _settingsViewModel.DisplayName;
        SettingsStartWithWindowsToggle.IsOn = _settingsViewModel.StartWithWindows;
        SettingsStartMinimizedToggle.IsOn = _settingsViewModel.StartMinimizedToTray;
        SettingsFpsNumberBox.Value = _settingsViewModel.GlobalTargetFps;
        SettingsPauseFullscreenToggle.IsOn = _settingsViewModel.PauseOnFullscreen;
        SettingsPauseMaximizedToggle.IsOn = _settingsViewModel.PauseOnMaximized;
        SettingsPauseBatteryToggle.IsOn = _settingsViewModel.PauseOnBattery;
        SettingsPauseCoveredToggle.IsOn = _settingsViewModel.PauseWhenCovered;
        SettingsCoverageSlider.Value = _settingsViewModel.CoveredThresholdPercentage;
        SettingsMuteCoveredToggle.IsOn = _settingsViewModel.MuteAudioWhenCovered;
        SettingsHeartbeatNumberBox.Value = _settingsViewModel.HeartbeatIntervalMs;
        SettingsDefaultVolumeSlider.Value = _settingsViewModel.DefaultVolume * 100.0;
        SettingsDefaultMutedToggle.IsOn = _settingsViewModel.DefaultMuted;
        SettingsPlaybackSpeedNumberBox.Value = _settingsViewModel.DefaultPlaybackSpeed;
        SettingsLoopToggle.IsOn = _settingsViewModel.DefaultLoop;
        SettingsBrightnessSlider.Value = _settingsViewModel.DefaultBrightness * 100.0;
        SettingsContrastSlider.Value = _settingsViewModel.DefaultContrast * 100.0;
        SettingsSaturationSlider.Value = _settingsViewModel.DefaultSaturation * 100.0;

        SettingsThemeComboBox.SelectedIndex = _settingsViewModel.Theme switch
        {
            "Dark" => 1,
            "System" => 2,
            _ => 0
        };

        SettingsScalingComboBox.SelectedIndex = _settingsViewModel.DefaultScalingMode switch
        {
            PlaybackScalingMode.Fill => 0,
            PlaybackScalingMode.Fit => 1,
            PlaybackScalingMode.Stretch => 2,
            PlaybackScalingMode.Center => 3,
            PlaybackScalingMode.Span => 4,
            _ => 0
        };

        SettingsStatusTextBlock.Text = _settingsViewModel.StatusMessage ?? string.Empty;
    }

    #region Navigation Tab Handlers

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

    #endregion

    #region Explore Controls Handlers

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // InitializeComponent can raise TextChanged before the ViewModel is assigned.
        if (_viewModel is null || ExploreSearchBox is null) return;
        _viewModel.ExploreViewModel.SearchText = ExploreSearchBox.Text;
        SearchClearButton.Visibility = string.IsNullOrEmpty(ExploreSearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !string.IsNullOrEmpty(ExploreSearchBox.Text))
        {
            ExploreSearchBox.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void OnSearchClearClicked(object sender, RoutedEventArgs e)
    {
        ExploreSearchBox.Text = string.Empty;
        ExploreSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnTagItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string tag)
        {
            ExploreSearchBox.Text = tag;
            ExploreSearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("HoverLayer") is UIElement hover)
        {
            hover.Opacity = 1;
        }
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("HoverLayer") is UIElement hover)
        {
            hover.Opacity = 0;
        }
    }

    private void OnCatalogViewClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemViewModel item })
        {
            _viewModel.ExploreViewModel.OpenDetails(item);
        }
    }

    private async void OnCardPauseClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.TogglePauseResumeAsync();
    }

    private async void OnCardMuteClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleMuteAsync();
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopAllWallpapersAsync();
    }

    private void OnTypeFilterMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag?.ToString(), out var index))
        {
            _viewModel.ExploreViewModel.SelectedTypeFilter = index switch
            {
                1 => WallpaperType.Video,
                2 => WallpaperType.Image,
                _ => null
            };
            TypeFilterLabel.Text = index switch { 1 => "Videos", 2 => "Images", _ => "All types" };
        }
    }

    private void OnResolutionFilterMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag?.ToString(), out var index))
        {
            _viewModel.ExploreViewModel.SelectedResolutionFilter = index switch
            {
                1 => CatalogResolutionFilter.FHD_1080p,
                2 => CatalogResolutionFilter.QHD_1440p,
                3 => CatalogResolutionFilter.UHD_4K,
                _ => CatalogResolutionFilter.All
            };
            ResolutionFilterLabel.Text = index switch { 1 => "1080p FHD", 2 => "1440p QHD", 3 => "4K UHD", _ => "All sizes" };
        }
    }

    private void OnSortFilterMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag?.ToString(), out var index))
        {
            _viewModel.ExploreViewModel.SelectedSortOption = index switch
            {
                1 => CatalogSortOption.Popular,
                2 => CatalogSortOption.Newest,
                3 => CatalogSortOption.Rating,
                4 => CatalogSortOption.TitleAsc,
                _ => CatalogSortOption.Relevance
            };
            SortFilterLabel.Text = index switch { 1 => "Most popular", 2 => "Newest", 3 => "Top rated", 4 => "Title A–Z", _ => "Relevance" };
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCategory || _viewModel is null) return;
        if (CategoryListView.SelectedItem is CatalogCategory selected &&
            !ReferenceEquals(selected, _viewModel.ExploreViewModel.SelectedCategory))
        {
            _viewModel.ExploreViewModel.SelectedCategory = selected;
        }
    }

    private void OnClearFiltersClicked(object sender, RoutedEventArgs e)
    {
        ExploreSearchBox.Text = string.Empty;
        _viewModel.ExploreViewModel.SelectedTypeFilter = null;
        _viewModel.ExploreViewModel.SelectedResolutionFilter = CatalogResolutionFilter.All;
        _viewModel.ExploreViewModel.SelectedSortOption = CatalogSortOption.Relevance;
        TypeFilterLabel.Text = "All types";
        ResolutionFilterLabel.Text = "All sizes";
        SortFilterLabel.Text = "Relevance";
        if (_viewModel.ExploreViewModel.Categories.Count > 0)
        {
            _viewModel.ExploreViewModel.SelectedCategory = _viewModel.ExploreViewModel.Categories[0];
        }
    }

    private async void OnCatalogSubscribeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemViewModel item })
        {
            await ConfirmAndSubscribeAsync(item);
        }
    }

    private void OnCatalogCardTapped(object sender, TappedRoutedEventArgs e)
    {
        // The action button is an independent command; tapping it must never open the drawer.
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is Button) return;
            current = VisualTreeHelper.GetParent(current);
        }

        if (sender is FrameworkElement { DataContext: CatalogItemViewModel item })
        {
            _viewModel.ExploreViewModel.OpenDetails(item);
            e.Handled = true;
        }
    }

    private async void OnPrevPageClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.ExploreViewModel.PreviousPageAsync();
    }

    private async void OnNextPageClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.ExploreViewModel.NextPageAsync();
    }

    private void OnCloseDetailsClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ExploreViewModel.CloseDetails();
    }

    private async void OnDetailsSubscribeClicked(object sender, RoutedEventArgs e)
    {
        var item = _viewModel.ExploreViewModel.DetailsViewModel.Item;
        if (item != null)
        {
            await ConfirmAndSubscribeAsync(item);
        }
    }

    private async void OnDetailsApplyClicked(object sender, RoutedEventArgs e)
    {
        var item = _viewModel.ExploreViewModel.DetailsViewModel.Item;
        if (item != null)
        {
            await _viewModel.ExploreViewModel.ApplyItemToDesktopAsync(item);
        }
    }

    private void OnDetailsBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            _viewModel.ExploreViewModel.CloseDetails();
        }
    }

    private void OnDetailsPanelTapped(object sender, TappedRoutedEventArgs e)
    {
        // Taps inside the dialog must never fall through to the backdrop and close it.
        e.Handled = true;
    }

    private void OnSettingsBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        AnimateSettingsPanel(opening: false);
    }

    private void OnSettingsTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tab }) return;

        SettingsGeneralSection.Visibility = tab == "General" ? Visibility.Visible : Visibility.Collapsed;
        SettingsAppearanceSection.Visibility = tab == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPlaybackSection.Visibility = tab == "Playback" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPerformanceSection.Visibility = tab == "Performance" ? Visibility.Visible : Visibility.Collapsed;

        SetNavigationVisualState(SettingsTabGeneral, tab == "General");
        SetNavigationVisualState(SettingsTabAppearance, tab == "Appearance");
        SetNavigationVisualState(SettingsTabPlayback, tab == "Playback");
        SetNavigationVisualState(SettingsTabPerformance, tab == "Performance");
    }

    private async Task<bool> ConfirmAndSubscribeAsync(CatalogItemViewModel item)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Download wallpaper?",
            PrimaryButtonText = "Download",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = $"{item.ResolutionText} • {item.FormattedSize}{(item.IsVideo && !string.IsNullOrWhiteSpace(item.DurationText) ? $" • {item.DurationText}" : string.Empty)}", Foreground = new SolidColorBrush(Color.FromArgb(255, 97, 113, 108)), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "The wallpaper will be saved to your Lumen Library for offline use.", TextWrapping = TextWrapping.Wrap }
                }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        await _viewModel.ExploreViewModel.SubscribeItemAsync(item);
        return true;
    }

    #endregion

    #region Settings Handlers

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        UpdateSettingsUiState();
        SettingsOverlay.Visibility = Visibility.Visible;
        AnimateSettingsPanel(opening: true);
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
    {
        AnimateSettingsPanel(opening: false);
    }

    private void OnSettingsDisplayNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsDisplayNameTextBox is null) return;
        _settingsViewModel.DisplayName = SettingsDisplayNameTextBox.Text;
    }

    private void OnSettingsThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsThemeComboBox is null) return;
        _settingsViewModel.Theme = SettingsThemeComboBox.SelectedIndex switch
        {
            1 => "Dark",
            2 => "System",
            _ => "Light"
        };
    }

    private void OnSettingsScalingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsScalingComboBox is null) return;
        _settingsViewModel.DefaultScalingMode = SettingsScalingComboBox.SelectedIndex switch
        {
            1 => PlaybackScalingMode.Fit,
            2 => PlaybackScalingMode.Stretch,
            3 => PlaybackScalingMode.Center,
            4 => PlaybackScalingMode.Span,
            _ => PlaybackScalingMode.Fill
        };
    }

    private void OnSettingsStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsStartWithWindowsToggle is null) return;
        _settingsViewModel.StartWithWindows = SettingsStartWithWindowsToggle.IsOn;
    }

    private void OnSettingsStartMinimizedChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsStartMinimizedToggle is null) return;
        _settingsViewModel.StartMinimizedToTray = SettingsStartMinimizedToggle.IsOn;
    }

    private void OnSettingsFpsChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.GlobalTargetFps = (int)Math.Round(e.NewValue);
    }

    private void OnSettingsPauseFullscreenChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsPauseFullscreenToggle is null) return;
        _settingsViewModel.PauseOnFullscreen = SettingsPauseFullscreenToggle.IsOn;
    }

    private void OnSettingsPauseMaximizedChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsPauseMaximizedToggle is null) return;
        _settingsViewModel.PauseOnMaximized = SettingsPauseMaximizedToggle.IsOn;
    }

    private void OnSettingsPauseBatteryChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsPauseBatteryToggle is null) return;
        _settingsViewModel.PauseOnBattery = SettingsPauseBatteryToggle.IsOn;
    }

    private void OnSettingsPauseCoveredChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsPauseCoveredToggle is null) return;
        _settingsViewModel.PauseWhenCovered = SettingsPauseCoveredToggle.IsOn;
    }

    private void OnSettingsCoverageChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.CoveredThresholdPercentage = (int)Math.Round(e.NewValue);
        SettingsCoverageValueTextBlock.Text = $"{_settingsViewModel.CoveredThresholdPercentage}%";
    }

    private void OnSettingsMuteCoveredChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsMuteCoveredToggle is null) return;
        _settingsViewModel.MuteAudioWhenCovered = SettingsMuteCoveredToggle.IsOn;
    }

    private void OnSettingsHeartbeatChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.HeartbeatIntervalMs = (int)Math.Round(e.NewValue);
    }

    private void OnSettingsDefaultVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.DefaultVolume = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
    }

    private void OnSettingsDefaultMutedChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsDefaultMutedToggle is null) return;
        _settingsViewModel.DefaultMuted = SettingsDefaultMutedToggle.IsOn;
    }

    private void OnSettingsPlaybackSpeedChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.DefaultPlaybackSpeed = (float)Math.Clamp(e.NewValue, 0.25, 2.0);
    }

    private void OnSettingsLoopChanged(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || SettingsLoopToggle is null) return;
        _settingsViewModel.DefaultLoop = SettingsLoopToggle.IsOn;
    }

    private void OnSettingsBrightnessChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.DefaultBrightness = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
    }

    private void OnSettingsContrastChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.DefaultContrast = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
    }

    private void OnSettingsSaturationChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;
        _settingsViewModel.DefaultSaturation = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
    }

    private async void OnSaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _settingsViewModel.SaveAsync();
            ApplyRequestedTheme();
            UpdateSettingsUiState();
            AnimateSettingsPanel(opening: false);
            StatusTextBlock.Text = "Settings saved.";
        }
        catch
        {
            UpdateSettingsUiState();
        }
    }

    private void ApplyRequestedTheme()
    {
        RootGrid.RequestedTheme = _settingsViewModel.Theme switch
        {
            "Dark" => ElementTheme.Dark,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Light
        };
    }

    #endregion

    #region Library Controls Handlers

    private async void OnImportButtonClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;

        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".webm");
        picker.FileTypeFilter.Add(".wmv");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            var paths = files.Select(f => f.Path).ToList();
            await _viewModel.ImportFilesAsync(paths);
        }
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

    private async void OnReconcileClicked(object sender, RoutedEventArgs e)
    {
        StorageFlyout.Hide();
        await _viewModel.ReconcileLibraryAsync();
    }

    private async void OnChangeStorageRootClicked(object sender, RoutedEventArgs e)
    {
        StorageFlyout.Hide();

        var picker = new FolderPicker();
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            await _viewModel.MigrateStorageAsync(folder.Path);
        }
    }

    private async void OnApplyButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WallpaperItemViewModel item })
        {
            await _viewModel.SetAsWallpaperAsync(item);
        }
    }

    private async void OnPauseResumeClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.TogglePauseResumeAsync();
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Import to Lumen Library";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var filePaths = new List<string>();

            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    filePaths.Add(file.Path);
                }
            }

            if (filePaths.Count > 0)
            {
                await _viewModel.ImportFilesAsync(filePaths);
            }
        }
    }

    #endregion
    private static string ResolveCatalogAssetUri(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "ms-appx:///Assets/lumen-logo.png";
        }

        if (source.StartsWith("assets/catalog/", StringComparison.OrdinalIgnoreCase))
        {
            return $"ms-appx:///Assets/Catalog/{Path.GetFileName(source)}";
        }

        return Uri.TryCreate(source, UriKind.Absolute, out _) ? source : $"ms-appx:///{source.TrimStart('/')}";
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e)
    {
        _windowManager.ShowAndActivate();
    }

    private async void OnTrayPauseResumeRequested(object? sender, EventArgs e)
    {
        await _viewModel.TogglePauseResumeAsync();
    }

    private async void OnTrayStopRequested(object? sender, EventArgs e)
    {
        await _viewModel.StopAllWallpapersAsync();
    }

    private async void OnTrayPlayRequested(object? sender, EventArgs e)
    {
        await _viewModel.ResumeAllWallpapersAsync();
    }

    private async void OnTrayNextWallpaperRequested(object? sender, EventArgs e)
    {
        await _viewModel.NextWallpaperAsync();
    }

    private async void OnTrayMuteRequested(object? sender, EventArgs e)
    {
        await _viewModel.ToggleMuteAsync();
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        App.Current.Shutdown();
    }

    private void ConfigureTitleBar()
    {
        try
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }
        catch
        {
            // Not supported on every shell; the default height still works.
        }

        ApplyCaptionColors();
    }

    /// <summary>
    /// Colours the native Minimize/Maximize/Close glyphs for the *current* theme so they are always
    /// legible on top of the glass backplate (light: deep teal, dark: pale mint).
    /// </summary>
    private void ApplyCaptionColors()
    {
        try
        {
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Color.FromArgb(255, 234, 251, 246) : Color.FromArgb(255, 11, 43, 41);
            var fgInactive = dark ? Color.FromArgb(150, 234, 251, 246) : Color.FromArgb(150, 11, 43, 41);
            var clear = Color.FromArgb(0, 0, 0, 0);

            var titleBar = AppWindow.TitleBar;
            titleBar.BackgroundColor = clear;
            titleBar.InactiveBackgroundColor = clear;
            titleBar.ButtonBackgroundColor = clear;
            titleBar.ButtonInactiveBackgroundColor = clear;
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonInactiveForegroundColor = fgInactive;
            titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(70, 63, 240, 196) : Color.FromArgb(90, 46, 230, 184);
            titleBar.ButtonHoverForegroundColor = fg;
            titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(120, 63, 240, 196) : Color.FromArgb(150, 46, 230, 184);
            titleBar.ButtonPressedForegroundColor = fg;
        }
        catch
        {
            // Caption customization is best-effort and should never block startup.
        }
    }

    private static void SetNavigationVisualState(Button button, bool selected)
    {
        var resources = global::Microsoft.UI.Xaml.Application.Current.Resources;
        string key = selected ? "LumenNavButtonActiveStyle" : "LumenNavButtonStyle";
        if (resources.TryGetValue(key, out var style) && style is Style navStyle)
        {
            button.Style = navStyle;
        }
    }

    private void AnimateSettingsPanel(bool opening)
    {
        if (SettingsPanel.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            SettingsPanel.RenderTransform = transform;
        }

        if (opening)
        {
            transform.TranslateX = 24;
            SettingsPanel.Opacity = 0;
        }

        var opacity = new DoubleAnimation
        {
            To = opening ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
            EnableDependentAnimation = true
        };
        var translate = new DoubleAnimation
        {
            To = opening ? 0 : 38,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(opacity, SettingsPanel);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(translate, transform);
        Storyboard.SetTargetProperty(translate, "TranslateX");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translate);

        if (!opening)
        {
            storyboard.Completed += (_, _) => SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        _settingsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
        => EnforceMinimumWindowSize();

    private void EnforceMinimumWindowSize()
    {
        try
        {
            const int minWidth = 1040;
            const int minHeight = 680;
            var size = AppWindow.Size;
            if (size.Width < minWidth || size.Height < minHeight)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(
                    Math.Max(minWidth, size.Width),
                    Math.Max(minHeight, size.Height)));
            }
        }
        catch
        {
            // Some Windows shell states may reject an immediate resize; simply keep the current size.
        }
    }

}
