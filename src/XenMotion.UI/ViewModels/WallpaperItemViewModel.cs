using System.ComponentModel;
using System.Runtime.CompilerServices;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace XenMotion.UI.ViewModels;

/// <summary>
/// ViewModel representing an individual wallpaper card in the gallery grid.
/// </summary>
public sealed class WallpaperItemViewModel : INotifyPropertyChanged
{
    private readonly Wallpaper _wallpaper;
    private readonly string _resolvedThumbnailPath;
    private readonly string _resolvedMediaPath;
    private bool _isApplying;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Wallpaper Wallpaper => _wallpaper;
    public Guid Id => _wallpaper.Id;
    public string Title => _wallpaper.Title;
    public WallpaperType Type => _wallpaper.Type;
    public bool IsVideo => _wallpaper.IsVideo;
    public bool IsMissing => _wallpaper.IsMissing;

    public string TypeBadge => _wallpaper.Type switch
    {
        WallpaperType.Video => "VIDEO",
        WallpaperType.Image => "IMAGE",
        WallpaperType.Gif => "GIF",
        _ => "MEDIA"
    };

    public string FormattedSize
    {
        get
        {
            if (_wallpaper.FileSizeBytes <= 0) return string.Empty;
            double mb = _wallpaper.FileSizeBytes / (1024.0 * 1024.0);
            return mb < 1.0 ? $"{_wallpaper.FileSizeBytes / 1024.0:F0} KB" : $"{mb:F1} MB";
        }
    }

    private string? _actualResolution;
    private TimeSpan? _actualDuration;
    public string ResolutionText => _actualResolution ?? _wallpaper.ResolutionText;

    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(ResolutionText)) parts.Add(ResolutionText);
            var duration = _actualDuration ?? _wallpaper.Duration;
            if (IsVideo && duration > TimeSpan.Zero) parts.Add($"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}");
            if (!string.IsNullOrEmpty(FormattedSize)) parts.Add(FormattedSize);
            return string.Join(" • ", parts);
        }
    }

    public string ThumbnailPath => !string.IsNullOrEmpty(_resolvedThumbnailPath) && File.Exists(_resolvedThumbnailPath)
        ? _resolvedThumbnailPath
        : _resolvedMediaPath;

    private BitmapImage? _thumbnailImage;
    private static readonly SemaphoreSlim ThumbnailSlots = new(2, 2);
    private bool _isActive;
    private bool _isPaused;
    private bool _isMuted;

    public BitmapImage? ThumbnailImage
    {
        get
        {
            if (_thumbnailImage != null) return _thumbnailImage;
            string path = _resolvedMediaPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                _thumbnailImage = new BitmapImage { DecodePixelWidth = 384 };
                if (IsVideo) _ = LoadVideoThumbnailAsync(_thumbnailImage, path);
                else { _thumbnailImage.UriSource = new Uri(path, UriKind.Absolute); _ = LoadImageMetadataAsync(path); }
            }
            catch
            {
                _thumbnailImage = null;
            }
            return _thumbnailImage;
        }
    }

    private async Task LoadImageMetadataAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetImagePropertiesAsync();
            _actualResolution = properties.Width > 0 && properties.Height > 0 ? $"{properties.Width}x{properties.Height}" : "Resolution unavailable";
            OnPropertyChanged(nameof(ResolutionText)); OnPropertyChanged(nameof(DetailsText));
        }
        catch { /* A missing codec must not crash the library. */ }
    }

    private async Task LoadVideoThumbnailAsync(BitmapImage image, string path)
    {
        await ThumbnailSlots.WaitAsync();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            _actualResolution = properties.Width > 0 && properties.Height > 0
                ? $"{properties.Width}x{properties.Height}" : "Preview unavailable";
            _actualDuration = properties.Duration;
            OnPropertyChanged(nameof(DetailsText));
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 384, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                await image.SetSourceAsync(thumbnail);
        }
        catch { /* Unsupported media keeps an empty preview and reports its error when applied. */ }
        finally { ThumbnailSlots.Release(); }
    }

    /// <summary>True while this wallpaper is the one currently running on the desktop.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public string PauseGlyph => _isPaused ? "\uE768" : "\uE769";
    public string MuteGlyph => _isMuted ? "\uE74F" : "\uE767";

    public void SetPlaybackState(bool isPaused, bool isMuted)
    {
        if (_isPaused != isPaused) { _isPaused = isPaused; OnPropertyChanged(nameof(PauseGlyph)); }
        if (_isMuted != isMuted) { _isMuted = isMuted; OnPropertyChanged(nameof(MuteGlyph)); }
    }

    public bool IsApplying
    {
        get => _isApplying;
        set
        {
            if (_isApplying != value)
            {
                _isApplying = value;
                OnPropertyChanged();
            }
        }
    }

    public WallpaperItemViewModel(Wallpaper wallpaper, string resolvedThumbnailPath, string resolvedMediaPath)
    {
        _wallpaper = wallpaper ?? throw new ArgumentNullException(nameof(wallpaper));
        _resolvedThumbnailPath = resolvedThumbnailPath ?? string.Empty;
        _resolvedMediaPath = resolvedMediaPath ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
