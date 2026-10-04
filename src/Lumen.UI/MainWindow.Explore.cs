using System.ComponentModel;
using System.Numerics;
using Lumen.Core.Enums;
using Lumen.Core.Models.Catalog;
using Lumen.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Lumen.UI;

/// <summary>Explore page: search, categories, cards, advanced filter dock, details and download dialogs.</summary>
public sealed partial class MainWindow
{
    private bool _filtersOpen;
    private bool _syncingFilterUi;
    private Guid? _shownDetailsId;
    private CatalogItemViewModel? _detailsItem;
    private CatalogItemViewModel? _downloadItem;

    // ------------------------------------------------------------------ search

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // InitializeComponent can raise TextChanged before the view model exists.
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

    private void OnTagPillClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            ExploreSearchBox.Text = tag;
            ExploreSearchBox.Focus(FocusState.Programmatic);
        }
    }

    // ------------------------------------------------------------------ categories

    private void OnCategoryElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is ToggleButton toggle && args.Index >= 0 && args.Index < _viewModel.ExploreViewModel.Categories.Count)
        {
            var selected = _viewModel.ExploreViewModel.SelectedCategory;
            toggle.IsChecked = selected != null && _viewModel.ExploreViewModel.Categories[args.Index].Id == selected.Id;
        }
    }

    private void SyncCategoryChips()
    {
        var explore = _viewModel.ExploreViewModel;
        var selected = explore.SelectedCategory;
        for (int i = 0; i < explore.Categories.Count; i++)
        {
            if (CategoryRepeater.TryGetElement(i) is ToggleButton toggle)
            {
                bool shouldCheck = selected != null && explore.Categories[i].Id == selected.Id;
                if (toggle.IsChecked != shouldCheck) toggle.IsChecked = shouldCheck;
            }
        }
    }

    private void OnCategoryChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string id }) return;
        var explore = _viewModel.ExploreViewModel;
        var category = explore.Categories.FirstOrDefault(c => c.Id == id);
        if (category != null && !ReferenceEquals(category, explore.SelectedCategory))
        {
            explore.SelectedCategory = category;
        }

        SyncCategoryChips();
    }

    // ------------------------------------------------------------------ pagination

    private async void OnPrevPageClicked(object sender, RoutedEventArgs e) => await _viewModel.ExploreViewModel.PreviousPageAsync();

    private async void OnNextPageClicked(object sender, RoutedEventArgs e) => await _viewModel.ExploreViewModel.NextPageAsync();

    // ------------------------------------------------------------------ cards

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("HoverLayer") is UIElement hover) hover.Opacity = 1;
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("HoverLayer") is UIElement hover) hover.Opacity = 0;
    }

    private void OnCatalogCardTapped(object sender, TappedRoutedEventArgs e)
    {
        // Buttons on the card are independent commands; tapping them must not also open the details.
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is ButtonBase) return;
            current = VisualTreeHelper.GetParent(current);
        }

        if (sender is FrameworkElement { DataContext: CatalogItemViewModel item })
        {
            _viewModel.ExploreViewModel.OpenDetails(item);
            e.Handled = true;
        }
    }

    private void OnCatalogViewClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemViewModel item }) _viewModel.ExploreViewModel.OpenDetails(item);
    }

    /// <summary>The round button on a card: download when new, watch progress when busy, set wallpaper when installed.</summary>
    private async void OnCatalogActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemViewModel item })
        {
            await RunCatalogPrimaryActionAsync(item, fromSheet: false);
        }
    }

    private async Task RunCatalogPrimaryActionAsync(CatalogItemViewModel item, bool fromSheet)
    {
        if (item.IsDownloaded)
        {
            await _viewModel.ExploreViewModel.ApplyItemToDesktopAsync(item);
            if (fromSheet) CloseDownloadSheet();
            return;
        }

        if (item.IsDownloading)
        {
            OpenDownloadSheet(item);
            return;
        }

        if (!fromSheet)
        {
            OpenDownloadSheet(item);
            return;
        }

        await _viewModel.ExploreViewModel.SubscribeItemAsync(item);
    }

    // ------------------------------------------------------------------ advanced filter dock

    private void OnFiltersToggleClicked(object sender, RoutedEventArgs e) => SetFiltersOpen(!_filtersOpen);

    private void SetFiltersOpen(bool open)
    {
        _filtersOpen = open;
        FiltersToggleButton.IsChecked = open;

        if (open)
        {
            FilterDock.Translation = new Vector3(44, 0, 0);
            FilterDock.Opacity = 0;
            FilterDock.Visibility = Visibility.Visible;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                FilterDock.Opacity = 1;
                FilterDock.Translation = Vector3.Zero;
            });
        }
        else
        {
            FilterDock.Opacity = 0;
            FilterDock.Translation = new Vector3(44, 0, 0);
            DelayCollapse(FilterDock, 300);
        }
    }

    private void SyncFilterDockWithNavigation()
    {
        if (!_viewModel.IsExploreSelected)
        {
            FilterDock.Visibility = Visibility.Collapsed;
        }
        else if (_filtersOpen && FilterDock.Visibility != Visibility.Visible)
        {
            FilterDock.Visibility = Visibility.Visible;
            FilterDock.Opacity = 1;
            FilterDock.Translation = Vector3.Zero;
        }
    }

    private void OnFilterTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tab }) ShowFilterTab(tab);
    }

    private void ShowFilterTab(string tab)
    {
        FilterBasicsSection.Visibility = tab == "Basics" ? Visibility.Visible : Visibility.Collapsed;
        FilterQualitySection.Visibility = tab == "Quality" ? Visibility.Visible : Visibility.Collapsed;
        FilterLengthSection.Visibility = tab == "Length" ? Visibility.Visible : Visibility.Collapsed;
        FilterTagsSection.Visibility = tab == "Tags" ? Visibility.Visible : Visibility.Collapsed;
        FilterTabBasics.IsChecked = tab == "Basics";
        FilterTabQuality.IsChecked = tab == "Quality";
        FilterTabLength.IsChecked = tab == "Length";
        FilterTabTags.IsChecked = tab == "Tags";
    }

    /// <summary>Keeps a chip's visual state equal to the model after the click toggled it (radio chips can't be unchecked).</summary>
    private void OnFilterChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: FilterChip chip } toggle && toggle.IsChecked != chip.IsSelected)
        {
            toggle.IsChecked = chip.IsSelected;
        }
    }

    private void OnMinRatingChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingFilterUi || _viewModel is null) return;
        _viewModel.ExploreViewModel.MinRating = e.NewValue;
    }

    private void OnClearTagsClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ExploreViewModel.TagGroup.Reset(raise: true);
    }

    private void OnResetFiltersClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ExploreViewModel.ResetAdvancedFilters();
    }

    private void OnClearFiltersClicked(object sender, RoutedEventArgs e)
    {
        var explore = _viewModel.ExploreViewModel;
        ExploreSearchBox.Text = string.Empty;
        explore.ResetAdvancedFilters();
        if (explore.Categories.Count > 0) explore.SelectedCategory = explore.Categories[0];
    }

    private void UpdateFilterUiState()
    {
        var explore = _viewModel.ExploreViewModel;

        int count = explore.ActiveFilterCount;
        FilterCountText.Text = count.ToString();
        FilterCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearFiltersLink.Visibility = count > 0 || !string.IsNullOrEmpty(explore.SearchText) ? Visibility.Visible : Visibility.Collapsed;
        FilterSummaryText.Text = count == 0 ? "No filters applied" : count == 1 ? "1 filter applied" : $"{count} filters applied";
        MinRatingText.Text = explore.MinRatingText;

        if (Math.Abs(MinRatingSlider.Value - explore.MinRating) > 0.01)
        {
            _syncingFilterUi = true;
            try { MinRatingSlider.Value = explore.MinRating; }
            finally { _syncingFilterUi = false; }
        }

        TagsHintText.Text = explore.TagGroup.Items.Count == 0
            ? "Tags will appear once the catalog has loaded."
            : "Pick several. A wallpaper must carry every tag you choose.";
    }

    // ------------------------------------------------------------------ details dialog

    private void UpdateDetailsState()
    {
        var details = _viewModel.ExploreViewModel.DetailsViewModel;

        if (details.IsOpen && details.Item != null)
        {
            var item = details.Item;
            if (_shownDetailsId != item.Id)
            {
                _shownDetailsId = item.Id;
                TrackDetailsItem(item);

                DetailsCategoryTextBlock.Text = item.CategoryName.ToUpperInvariant();
                DetailsTitleTextBlock.Text = item.Title;
                DetailsAuthorTextBlock.Text = $"by {item.Author}";
                DetailsResolutionText.Text = item.ResolutionText;
                DetailsSizeText.Text = item.FormattedSize;
                DetailsAspectText.Text = string.IsNullOrWhiteSpace(details.AspectRatioText) ? item.TypeBadgeText : details.AspectRatioText;
                string duration = item.IsVideo && !string.IsNullOrWhiteSpace(item.DurationText) ? $"   •   {item.DurationText}" : string.Empty;
                DetailsStatsTextBlock.Text = $"{item.RatingText}   •   {item.DownloadsText}{duration}";
                DetailsDescriptionTextBlock.Text = item.Description;
                DetailsTagsRepeater.ItemsSource = item.Tags.Select(t => $"#{t}").ToList();
                DetailsPreviewBrush.ImageSource = item.ThumbnailImage;

                ShowOverlay(DetailsOverlay, DetailsPanel, new Vector3(0, 26, 0));
            }

            RefreshDetailsButtons(item);
        }
        else if (_shownDetailsId != null)
        {
            _shownDetailsId = null;
            TrackDetailsItem(null);
            HideOverlay(DetailsOverlay, DetailsPanel, new Vector3(0, 26, 0));
        }
    }

    private void RefreshDetailsButtons(CatalogItemViewModel item)
    {
        DetailsProgressBar.Visibility = item.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        DetailsProgressBar.Value = item.DownloadProgress;

        if (item.IsDownloaded)
        {
            DetailsPrimaryButton.Content = "Set as wallpaper";
            DetailsPrimaryButton.IsEnabled = true;
        }
        else if (item.IsDownloading)
        {
            DetailsPrimaryButton.Content = $"Downloading  {item.DownloadProgress:F0}%";
            DetailsPrimaryButton.IsEnabled = false;
        }
        else
        {
            DetailsPrimaryButton.Content = item.DownloadState == CatalogDownloadState.Failed ? "Try again" : "Download";
            DetailsPrimaryButton.IsEnabled = true;
        }
    }

    private void TrackDetailsItem(CatalogItemViewModel? item)
    {
        if (_detailsItem != null) _detailsItem.PropertyChanged -= OnTrackedItemChanged;
        _detailsItem = item;
        if (_detailsItem != null) _detailsItem.PropertyChanged += OnTrackedItemChanged;
    }

    private void TrackDownloadItem(CatalogItemViewModel? item)
    {
        if (_downloadItem != null) _downloadItem.PropertyChanged -= OnTrackedItemChanged;
        _downloadItem = item;
        if (_downloadItem != null) _downloadItem.PropertyChanged += OnTrackedItemChanged;
    }

    private void OnTrackedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_detailsItem != null) RefreshDetailsButtons(_detailsItem);
            if (_downloadItem != null) RefreshDownloadSheet();
        });
    }

    private void OnCloseDetailsClicked(object sender, RoutedEventArgs e) => _viewModel.ExploreViewModel.CloseDetails();

    private void OnDetailsBackdropTapped(object sender, TappedRoutedEventArgs e) => _viewModel.ExploreViewModel.CloseDetails();

    private async void OnDetailsPrimaryClicked(object sender, RoutedEventArgs e)
    {
        var item = _viewModel.ExploreViewModel.DetailsViewModel.Item;
        if (item != null) await RunCatalogPrimaryActionAsync(item, fromSheet: false);
    }

    // ------------------------------------------------------------------ download sheet

    private void OpenDownloadSheet(CatalogItemViewModel item)
    {
        TrackDownloadItem(item);
        DownloadTitleText.Text = item.Title;
        string duration = item.IsVideo && !string.IsNullOrWhiteSpace(item.DurationText) ? item.DurationText : string.Empty;
        DownloadSpecsText.Text = UiFn.Join(UiFn.Join(item.ResolutionText, item.FormattedSize), duration);
        DownloadLocationText.Text = _viewModel.StorageRootText;
        DownloadPreviewBrush.ImageSource = item.ThumbnailImage;
        RefreshDownloadSheet();
        ShowOverlay(DownloadOverlay, DownloadPanel, new Vector3(0, 30, 0));
    }

    private void RefreshDownloadSheet()
    {
        var item = _downloadItem;
        if (item is null) return;

        DownloadProgressBar.Value = item.DownloadProgress;

        if (item.IsDownloaded)
        {
            DownloadHeadlineText.Text = "READY";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadStateText.Visibility = Visibility.Visible;
            DownloadStateText.Text = "Saved to your library. Put it on your desktop whenever you like.";
            DownloadPrimaryButton.Content = "Set as wallpaper";
            DownloadPrimaryButton.IsEnabled = true;
            DownloadSecondaryButton.Content = "Close";
        }
        else if (item.IsDownloading)
        {
            DownloadHeadlineText.Text = "DOWNLOADING";
            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadStateText.Visibility = Visibility.Visible;
            DownloadStateText.Text = $"{item.DownloadProgress:F0}% — you can close this, it keeps going in the background.";
            DownloadPrimaryButton.Content = "Downloading...";
            DownloadPrimaryButton.IsEnabled = false;
            DownloadSecondaryButton.Content = "Hide";
        }
        else if (item.DownloadState == CatalogDownloadState.Failed)
        {
            DownloadHeadlineText.Text = "DOWNLOAD FAILED";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadStateText.Visibility = Visibility.Visible;
            DownloadStateText.Text = "That didn't work. Check your connection and try again.";
            DownloadPrimaryButton.Content = "Try again";
            DownloadPrimaryButton.IsEnabled = true;
            DownloadSecondaryButton.Content = "Not now";
        }
        else
        {
            DownloadHeadlineText.Text = "DOWNLOAD WALLPAPER";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadStateText.Visibility = Visibility.Collapsed;
            DownloadPrimaryButton.Content = "Download";
            DownloadPrimaryButton.IsEnabled = true;
            DownloadSecondaryButton.Content = "Not now";
        }
    }

    private void CloseDownloadSheet()
    {
        HideOverlay(DownloadOverlay, DownloadPanel, new Vector3(0, 30, 0));
        TrackDownloadItem(null);
    }

    private void OnDownloadBackdropTapped(object sender, TappedRoutedEventArgs e) => CloseDownloadSheet();

    private void OnDownloadSecondaryClicked(object sender, RoutedEventArgs e) => CloseDownloadSheet();

    private async void OnDownloadPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_downloadItem != null) await RunCatalogPrimaryActionAsync(_downloadItem, fromSheet: true);
    }
}
