using System.ComponentModel;
using System.Runtime.CompilerServices;
using XenMotion.Core.Enums;

namespace XenMotion.UI.ViewModels;

/// <summary>
/// ViewModel managing the detailed wallpaper inspection modal or drawer view.
/// </summary>
public sealed class CatalogDetailsViewModel : INotifyPropertyChanged
{
    private CatalogItemViewModel? _item;
    private bool _isOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CatalogItemViewModel? Item
    {
        get => _item;
        set
        {
            if (SetField(ref _item, value))
            {
                OnPropertyChanged(nameof(HasItem));
                OnPropertyChanged(nameof(FormattedTags));
                OnPropertyChanged(nameof(AspectRatioText));
            }
        }
    }

    public bool HasItem => _item != null;

    public bool IsOpen
    {
        get => _isOpen;
        set => SetField(ref _isOpen, value);
    }

    public string FormattedTags => _item != null && _item.Tags.Count > 0
        ? string.Join("  ", _item.Tags.Select(t => $"#{t}"))
        : "None";

    public string AspectRatioText
    {
        get
        {
            if (_item == null || _item.Model.Width <= 0 || _item.Model.Height <= 0) return string.Empty;
            return _item.Model.Orientation switch
            {
                CatalogOrientationFilter.Ultrawide => "21:9 Ultrawide",
                CatalogOrientationFilter.Landscape => "16:9 Landscape",
                CatalogOrientationFilter.Portrait => "9:16 Portrait",
                _ => "Standard"
            };
        }
    }

    public void Open(CatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Item = null;
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
