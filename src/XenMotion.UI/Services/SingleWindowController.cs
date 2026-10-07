namespace XenMotion.UI.Services;

/// <summary>Owns one window and suppresses nested activation while creation pumps native messages.</summary>
internal sealed class SingleWindowController<T> where T : class
{
    private int _showing;
    private T? _window;
    public Action? Factory { get; set; }

    public void Register(T window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_window is not null && !ReferenceEquals(_window, window))
            throw new InvalidOperationException("A different main window is already registered.");
        _window = window;
    }

    public bool Show(Action<T> activate)
    {
        if (Interlocked.CompareExchange(ref _showing, 1, 0) != 0) return false;
        try
        {
            if (_window is null) Factory?.Invoke();
            if (_window is null) return false;
            activate(_window);
            return true;
        }
        finally { Volatile.Write(ref _showing, 0); }
    }

    public void Hide(Action<T> hide)
    {
        if (_window is not null) hide(_window);
    }
}
