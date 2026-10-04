using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lumen.Core.Enums;
using Lumen.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lumen.UI.ViewModels;

/// <summary>
/// ViewModel representing an individual wallpaper card in the gallery grid.
/// </summary>
public sealed class WallpaperItemViewModel : INotifyPropertyChanged
{
    private readonly Wallpaper _wallpaper;
    private readonly string _resolvedThumbnailPath;
    private readonly string _resolvedMediaPath;
    private bool _isApplying;

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

    public string ResolutionText => _wallpaper.ResolutionText;

    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(ResolutionText)) parts.Add(ResolutionText);
            if (IsVideo && _wallpaper.DurationMs > 0) parts.Add($"{_wallpaper.Duration.Minutes:D2}:{_wallpaper.Duration.Seconds:D2}");
            if (!string.IsNullOrEmpty(FormattedSize)) parts.Add(FormattedSize);
            return string.Join(" • ", parts);
        }
    }

    public string ThumbnailPath => !string.IsNullOrEmpty(_resolvedThumbnailPath) && File.Exists(_resolvedThumbnailPath)
        ? _resolvedThumbnailPath
        : _resolvedMediaPath;

    private BitmapImage? _thumbnailImage;
    private bool _isActive;
    private bool _isPaused;
    private bool _isMuted;

    public BitmapImage? ThumbnailImage
    {
        get
        {
            if (_thumbnailImage != null) return _thumbnailImage;
            string path = ThumbnailPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                _thumbnailImage = new BitmapImage { DecodePixelWidth = 640 };
                _thumbnailImage.UriSource = new Uri(path, UriKind.Absolute);
            }
            catch
            {
                _thumbnailImage = null;
            }
            return _thumbnailImage;
        }
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
