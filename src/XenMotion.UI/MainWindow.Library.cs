using XenMotion.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace XenMotion.UI;

/// <summary>Library page: import, drag and drop, storage tools and per-wallpaper playback.</summary>
public sealed partial class MainWindow
{
    private bool _pickingImport;
    private async void OnImportButtonClicked(object sender, RoutedEventArgs e)
    {
        if (_pickingImport) return;
        _pickingImport = true;
        try
        {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;

        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".mp4", ".webm", ".wmv", ".avi", ".mov" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            await _viewModel.ImportFilesAsync(files.Select(f => f.Path).ToList());
        }
        }
        catch (Exception ex) { ShowToast("Could not import: " + ex.Message, true); }
        finally { _pickingImport = false; }
    }

    private void OnLibrarySelectionClicked(object sender, RoutedEventArgs e)
    {
        bool selecting = LibrarySelectionToggle.IsChecked == true;
        WallpaperGridView.SelectionMode = selecting ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        WallpaperGridView.IsItemClickEnabled = !selecting;
        LibrarySelectAllButton.Visibility = LibraryDeleteSelectedButton.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnLibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null) return;
        foreach (var item in _viewModel.Wallpapers)
            item.IsSelected = LibrarySelectionToggle.IsChecked == true ? WallpaperGridView.SelectedItems.Contains(item) : item.Id == _viewerId;
        LibraryDeleteSelectedButton.Content = $"Remove selected ({WallpaperGridView.SelectedItems.Count})";
    }
    private void OnSelectAllWallpapersClicked(object sender, RoutedEventArgs e) => WallpaperGridView.SelectAll();
    private async void OnDeleteSelectedWallpapersClicked(object sender, RoutedEventArgs e)
    {
        var selected = WallpaperGridView.SelectedItems.OfType<WallpaperItemViewModel>().ToArray();
        if (selected.Length == 0) { ShowToast("Select wallpapers first."); return; }
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, RequestedTheme = ElementTheme.Dark,
            Title = $"Remove {selected.Length} wallpapers?", Content = "Only library copies are removed. Original imported files remain on your computer.",
            PrimaryButtonText = "Remove selected", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        LibraryDeleteSelectedButton.IsEnabled = false;
        try
        {
            await FlushWallpaperEdits();
            foreach (var item in selected) await _viewModel.DeleteWallpaperAsync(item);
            if (_viewerId is { } id && !_viewModel.Wallpapers.Any(w => w.Id == id)) CloseWallpaperViewer();
        }
        catch (Exception ex) { ShowToast("Could not remove wallpapers: " + ex.Message, true); }
        finally { LibraryDeleteSelectedButton.IsEnabled = true; }
    }

    private async void OnPauseResumeClicked(object sender, RoutedEventArgs e) => await _viewModel.TogglePauseResumeAsync();

    private async void OnDeleteWallpaperClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WallpaperItemViewModel item }) return;
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var details = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock { Text = item.Title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)resources["XenMotionTextBrush"] });
        details.Children.Add(new TextBlock { Text = $"{item.TypeBadge} · {item.DetailsText}", FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)resources["XenMotionMutedTextBrush"] });
        var preview = new Grid { ColumnSpacing = 16 };
        preview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        preview.Children.Add(new Border { Width = 88, Height = 66, CornerRadius = new CornerRadius(12),
            Background = (Brush)resources["XenMotionGlassDarkBrush"],
            Child = new Image { Source = item.ThumbnailImage, Stretch = Stretch.UniformToFill } });
        Grid.SetColumn(details, 1);
        preview.Children.Add(details);
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(16),
            Background = (Brush)resources["XenMotionSectionBrush"], BorderBrush = (Brush)resources["XenMotionRimSoftBrush"],
            BorderThickness = new Thickness(1), Child = preview });
        content.Children.Add(new TextBlock { Text = "This removes the wallpaper and its stored copy from your library.",
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = (Brush)resources["XenMotionMutedTextBrush"] });
        content.Children.Add(new TextBlock { Text = "Your original imported file stays on your computer.",
            TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = (Brush)resources["XenMotionMintTextBrush"] });
        var dialog = new ContentDialog
        {
            XamlRoot = (sender as FrameworkElement)?.XamlRoot ?? RootGrid.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = "Remove wallpaper?",
            Content = content,
            PrimaryButtonText = "Remove wallpaper",
            CloseButtonText = "Cancel",
            PrimaryButtonStyle = (Style)resources["XenMotionDangerButtonStyle"],
            CloseButtonStyle = (Style)resources["XenMotionButtonStyle"],
            DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogBackground"] = resources["XenMotionFlyoutBrush"];
        dialog.Resources["ContentDialogBorderBrush"] = resources["XenMotionRimBrush"];
        dialog.Resources["ContentDialogCornerRadius"] = new CornerRadius(24);
        dialog.Resources["ContentDialogMaxWidth"] = 520d;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteWallpaperAsync(item);
            if (_viewerId == item.Id && !_viewModel.Wallpapers.Any(w => w.Id == item.Id)) CloseWallpaperViewer();
        }
    }

    private async void OnMuteClicked(object sender, RoutedEventArgs e) => await _viewModel.ToggleMuteAsync();

    private async void OnStopClicked(object sender, RoutedEventArgs e) => await _viewModel.StopAllWallpapersAsync();

    private async void OnReconcileClicked(object sender, RoutedEventArgs e)
    {
        StorageFlyout.Hide();
        try { await _viewModel.ReconcileLibraryAsync(); }
        catch (Exception ex) { ShowToast("Could not rescan the library: " + ex.Message, true); }
    }

    private async void OnChangeStorageRootClicked(object sender, RoutedEventArgs e)
    {
        StorageFlyout.Hide();

        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            await _viewModel.MigrateStorageAsync(folder.Path);
        }
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to XenMotion";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.OfType<StorageFile>().Select(f => f.Path).ToList();
        if (paths.Count > 0)
        {
            await _viewModel.ImportFilesAsync(paths);
        }
    }
}
