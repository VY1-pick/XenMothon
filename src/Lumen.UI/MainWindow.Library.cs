using Lumen.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lumen.UI;

/// <summary>Library page: import, drag and drop, storage tools and per-wallpaper playback.</summary>
public sealed partial class MainWindow
{
    private async void OnImportButtonClicked(object sender, RoutedEventArgs e)
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

    private async void OnApplyButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WallpaperItemViewModel item })
        {
            await _viewModel.SetAsWallpaperAsync(item);
        }
    }

    private async void OnPauseResumeClicked(object sender, RoutedEventArgs e) => await _viewModel.TogglePauseResumeAsync();

    private async void OnMuteClicked(object sender, RoutedEventArgs e) => await _viewModel.ToggleMuteAsync();

    private async void OnStopClicked(object sender, RoutedEventArgs e) => await _viewModel.StopAllWallpapersAsync();

    private async void OnReconcileClicked(object sender, RoutedEventArgs e)
    {
        StorageFlyout.Hide();
        await _viewModel.ReconcileLibraryAsync();
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
        e.DragUIOverride.Caption = "Add to Lumen";
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
