using XenMotion.Core.Models;

namespace XenMotion.UI.Services;

internal sealed class WallpaperEditAutosave
{
    public (Guid Id, WallpaperEdits Edits)? Pending { get; private set; }
    public int RemainingSeconds { get; private set; }

    public void Schedule(Guid id, WallpaperEdits edits)
    {
        Pending = (id, edits);
        RemainingSeconds = 5;
    }

    public (Guid Id, WallpaperEdits Edits)? Tick()
    {
        if (Pending == null || --RemainingSeconds > 0) return null;
        return Take();
    }

    public (Guid Id, WallpaperEdits Edits)? Take()
    {
        var pending = Pending;
        Pending = null;
        RemainingSeconds = 0;
        return pending;
    }
}
