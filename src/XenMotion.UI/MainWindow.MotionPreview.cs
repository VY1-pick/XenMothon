using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _motionPreviewCancellation;
    private MediaPlayer? _motionPlayer;

    private void StopMotionPreview()
    {
        _motionPreviewCancellation?.Cancel();
        _motionPreviewCancellation?.Dispose();
        _motionPreviewCancellation = null;
        DetailsMotionPreview.SetMediaPlayer(null);
        _motionPlayer?.Dispose(); _motionPlayer = null;
        DetailsMotionPreview.Visibility = Visibility.Collapsed;
        DetailsMotionButton.Visibility = Visibility.Collapsed;
    }

    private async Task LoadMotionPreviewAsync(Wallpaper wallpaper)
    {
        StopMotionPreview();
        var cancellation = new CancellationTokenSource();
        _motionPreviewCancellation = cancellation;
        var ct = cancellation.Token;
        string? temporary = null;
        try
        {
            DetailsMotionButton.Visibility = Visibility.Visible;
            DetailsMotionButton.Content = "Preparing small motion preview…";
            DetailsMotionButton.IsEnabled = false;
            var storage = App.Current.ServiceProvider.GetRequiredService<IStorageManager>();
            string source = storage.ResolveAbsolutePath(wallpaper.FilePath);
            var info = new FileInfo(source);
            string folder = Path.Combine(storage.RootStoragePath, "MotionPreviews");
            Directory.CreateDirectory(folder);
            string preview = Path.Combine(folder, $"{wallpaper.Id:N}-{info.Length}-{info.LastWriteTimeUtc.Ticks}-480.mp4");
            if (!File.Exists(preview))
            {
                temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".mp4");
                CreatePreviewFile(temporary);
                await Services.MotionPreviewEncoder.EncodeAsync(source, temporary, ct);
                File.Move(temporary, preview, true); temporary = null;
            }
            ct.ThrowIfCancellationRequested();
            if (_viewerId != wallpaper.Id) return;
            var previewFile = await StorageFile.GetFileFromPathAsync(preview);
            ct.ThrowIfCancellationRequested();
            if (_viewerId != wallpaper.Id) return;
            PlayMotionPreview(MediaSource.CreateFromStorageFile(previewFile), wallpaper.Id, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested && _viewerId == wallpaper.Id)
            {
                DetailsMotionButton.Content = "Motion preview unavailable · still preview shown";
                DetailsMotionButton.IsEnabled = false;
            }
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void PlayMotionPreview(MediaSource source, Guid id, CancellationToken ct)
    {
        _motionPlayer = new MediaPlayer { IsMuted = true, IsLoopingEnabled = true };
        _motionPlayer.Source = source;
        _motionPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested || _viewerId != id) return;
            DetailsMotionPreview.Visibility = Visibility.Collapsed;
            DetailsMotionButton.Content = "Motion preview unavailable · still preview shown";
            DetailsMotionButton.IsEnabled = false;
        });
        DetailsMotionPreview.SetMediaPlayer(_motionPlayer);
        DetailsMotionPreview.Visibility = Visibility.Visible;
        DetailsMotionButton.Visibility = Visibility.Visible;
        DetailsMotionButton.Content = "Pause motion preview";
        DetailsMotionButton.IsEnabled = true;
        _motionPlayer.Play();
    }

    private void LoadCatalogMotionPreview(Guid id)
    {
        if (_detailsItem?.Id != id || !_detailsItem.IsVideo) return;
        string url = _detailsItem.Model.PreviewUrl;
        if (url.StartsWith("assets/catalog/", StringComparison.OrdinalIgnoreCase))
        {
            string asset = Path.Combine(AppContext.BaseDirectory, "Assets", "Catalog", Path.GetFileName(url));
            if (!File.Exists(asset)) return;
            url = new Uri(asset).AbsoluteUri;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "file")) return;
        _motionPreviewCancellation = new CancellationTokenSource();
        PlayMotionPreview(MediaSource.CreateFromUri(uri), id, _motionPreviewCancellation.Token);
    }

    private static string CreatePreviewFile(string path) { using var file = File.Create(path); return path; }

    private async void OnMotionPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (_motionPlayer is null)
        {
            if (_viewerWallpaper is { IsVideo: true } wallpaper) await LoadMotionPreviewAsync(wallpaper);
            else if (_viewerId is { } id) LoadCatalogMotionPreview(id);
            return;
        }
        bool playing = DetailsMotionPreview.Visibility == Visibility.Visible;
        DetailsMotionPreview.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        if (playing) _motionPlayer.Pause(); else _motionPlayer.Play();
        DetailsMotionButton.Content = playing ? "Play motion preview" : "Pause motion preview";
    }
}
