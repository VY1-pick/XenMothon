using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lumen.Core.Enums;
using Lumen.Core.Models.Catalog;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lumen.UI.ViewModels;

/// <summary>
/// ViewModel representing an individual wallpaper item within the Explore catalog grid.
/// </summary>
public sealed class CatalogItemViewModel : INotifyPropertyChanged
{
    private readonly CatalogItem _item;
    private CatalogDownloadState _downloadState;
    private double _downloadProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CatalogItem Model => _item;
    public Guid Id => _item.Id;
    public string Title => _item.Title;
    public string Author => _item.Author;
    public string Description => _item.Description;
    public string CategoryId => _item.CategoryId;
    public string CategoryName => _item.CategoryName;
    public WallpaperType Type => _item.Type;
    public bool IsVideo => _item.Type == WallpaperType.Video;
    public bool IsFeatured => _item.IsFeatured;
    public double Rating => _item.Rating;
    public int RatingCount => _item.RatingCount;
    public int DownloadsCount => _item.DownloadsCount;
    public IReadOnlyList<string> Tags => _item.Tags;

    public string ResolutionText => _item.ResolutionText;
    public string DurationText => _item.DurationText;
    public string FormattedSize => _item.FormattedSize;

    public string TypeBadgeText => _item.Type switch
    {
        WallpaperType.Video when _item.Height >= 2160 => "4K VIDEO",
        WallpaperType.Video => "VIDEO",
        WallpaperType.Image when _item.Height >= 2160 => "4K IMAGE",
        WallpaperType.Image => "IMAGE",
        _ => "MEDIA"
    };

    public string RatingText => $"★ {_item.Rating:F2} ({_item.RatingCount:N0})";
    public string DownloadsText => $"{_item.DownloadsCount:N0} downloads";

    public string ThumbnailUrl => _item.ThumbnailUrl;

    /// <summary>
    /// Resolves catalog-relative mock assets to packaged WinUI assets while leaving
    /// absolute remote URLs untouched for the production catalog backend.
    /// </summary>
    public string ThumbnailSource
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_item.ThumbnailUrl)) return string.Empty;
            if (_item.ThumbnailUrl.StartsWith("assets/catalog/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(_item.ThumbnailUrl);
                return $"ms-appx:///Assets/Catalog/{fileName}";
            }
            return _item.ThumbnailUrl;
        }
    }

    private BitmapImage? _thumbnailImage;

    /// <summary>Decoded at card size (not full size) so a grid of thumbnails stays light on memory.</summary>
    public BitmapImage? ThumbnailImage
    {
        get
        {
            if (_thumbnailImage != null) return _thumbnailImage;
            string source = ThumbnailSource;
            if (string.IsNullOrWhiteSpace(source)) return null;
            try
            {
                _thumbnailImage = new BitmapImage { DecodePixelWidth = 640 };
                _thumbnailImage.UriSource = new Uri(source, UriKind.Absolute);
            }
            catch
            {
                _thumbnailImage = null;
            }
            return _thumbnailImage;
        }
    }

    public string TypeGlyph => _item.Type == WallpaperType.Video ? "\uE714" : "\uEB9F";

    public string QualityBadgeText => _item.Height >= 2160 ? "4K" : (_item.Type == WallpaperType.Video ? "VIDEO" : "IMAGE");

    public string PreviewUrl => _item.PreviewUrl;

    public CatalogDownloadState DownloadState
    {
        get => _downloadState;
        set
        {
            if (SetField(ref _downloadState, value))
            {
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsDownloaded));
                OnPropertyChanged(nameof(CanSubscribe));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (SetField(ref _downloadProgress, value))
            {
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public bool IsDownloading => _downloadState == CatalogDownloadState.Downloading;
    public bool IsDownloaded => _downloadState == CatalogDownloadState.Downloaded;
    public bool CanSubscribe => _downloadState == CatalogDownloadState.NotDownloaded || _downloadState == CatalogDownloadState.Failed;

    public string ActionButtonText => _downloadState switch
    {
        CatalogDownloadState.Downloading => $"{DownloadProgress:F0}%",
        CatalogDownloadState.Downloaded => "Installed",
        CatalogDownloadState.Failed => "Retry",
        _ => "Download"
    };

    public CatalogItemViewModel(CatalogItem item, CatalogDownloadState initialState = CatalogDownloadState.NotDownloaded)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _downloadState = initialState;
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
