using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lumen.UI.ViewModels;

/// <summary>One selectable option in the Explore filter panel.</summary>
public sealed class FilterChip : INotifyPropertyChanged
{
    private bool _isSelected;

    public FilterChip(string label, object? value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public object? Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionRequested?.Invoke(this, value);
        }
    }

    /// <summary>Sets the flag without notifying the owning group (used while the group syncs itself).</summary>
    internal void SetSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }

    internal event Action<FilterChip, bool>? SelectionRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A set of chips. Single mode behaves like a radio group, multi mode like checkboxes.</summary>
public sealed class FilterChipGroup
{
    private readonly ObservableCollection<FilterChip> _items = [];
    private bool _syncing;

    public FilterChipGroup(bool multiSelect)
    {
        IsMultiSelect = multiSelect;
        Items = new ReadOnlyObservableCollection<FilterChip>(_items);
    }

    public bool IsMultiSelect { get; }
    public ReadOnlyObservableCollection<FilterChip> Items { get; }

    public event EventHandler? SelectionChanged;

    public FilterChip Add(string label, object? value, bool selected = false)
    {
        var chip = new FilterChip(label, value);
        chip.SelectionRequested += OnChipSelectionRequested;
        _items.Add(chip);
        chip.SetSilently(selected);
        return chip;
    }

    public void Clear()
    {
        foreach (var chip in _items) chip.SelectionRequested -= OnChipSelectionRequested;
        _items.Clear();
    }

    public FilterChip? Selected => _items.FirstOrDefault(c => c.IsSelected);
    public IEnumerable<FilterChip> SelectedChips => _items.Where(c => c.IsSelected);

    /// <summary>Selects the chip whose value equals <paramref name="value"/> (single mode) without raising SelectionChanged.</summary>
    public void SelectValue(object? value, bool raise = false)
    {
        _syncing = true;
        try
        {
            foreach (var chip in _items) chip.SetSilently(Equals(chip.Value, value));
        }
        finally { _syncing = false; }
        if (raise) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the first chip (single) or nothing (multi).</summary>
    public void Reset(bool raise = true)
    {
        _syncing = true;
        try
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].SetSilently(!IsMultiSelect && i == 0);
        }
        finally { _syncing = false; }
        if (raise) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChipSelectionRequested(FilterChip chip, bool selected)
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            if (!IsMultiSelect)
            {
                if (!selected)
                {
                    // A radio group always keeps one choice.
                    chip.SetSilently(true);
                    return;
                }
                foreach (var other in _items)
                    if (!ReferenceEquals(other, chip)) other.SetSilently(false);
            }
        }
        finally { _syncing = false; }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
