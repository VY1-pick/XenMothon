using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Services;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private readonly WallpaperEditAutosave _editAutosave = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _editSaveTimer;
    private TextBlock? _editSaveStatus;
    private int _editSaveRevision;
    private Task _editSaveTask = Task.CompletedTask;

    private void QueueWallpaperEdits()
    {
        if (!_viewerFromLibrary || _viewerWallpaper is not { IsMissing: false } wallpaper) return;
        var normalized = App.Current.ServiceProvider.GetRequiredService<ISettingsService>().CurrentSettings.CreatePlaybackSettings();
        _viewerEdits.ApplyTo(normalized);
        var snapshot = new WallpaperEdits { Opacity = normalized.Opacity, Brightness = normalized.Brightness,
            Contrast = normalized.Contrast, Saturation = normalized.Saturation, Red = normalized.Red, Green = normalized.Green, Blue = normalized.Blue,
            PlaybackSpeed = normalized.PlaybackSpeed, Volume = normalized.Volume, Sharpness = normalized.Sharpness,
            Loop = normalized.Loop, IsMuted = normalized.IsMuted, ScalingMode = normalized.ScalingMode, FlipHorizontal = normalized.FlipHorizontal };
        _editAutosave.Schedule(wallpaper.Id, snapshot);
        _editSaveRevision++;
        if (_editSaveStatus != null) _editSaveStatus.Text = "Saving in 5s";
        if (_editSaveTimer == null)
        {
            _editSaveTimer = DispatcherQueue.CreateTimer();
            _editSaveTimer.Interval = TimeSpan.FromSeconds(1);
            _editSaveTimer.Tick += (_, _) =>
            {
                var pending = _editAutosave.Tick();
                if (pending is { } edit)
                {
                    _editSaveTimer.Stop();
                    _editSaveTask = SaveAfterPreviousAsync(_editSaveTask, edit.Id, edit.Edits, _editSaveRevision);
                }
                else if (_editSaveStatus != null) _editSaveStatus.Text = $"Saving in {_editAutosave.RemainingSeconds}s";
            };
        }
        _editSaveTimer.Stop();
        _editSaveTimer.Start();
    }

    private Task FlushWallpaperEdits()
    {
        _editSaveTimer?.Stop();
        if (_editAutosave.Take() is { } edit) _editSaveTask = SaveAfterPreviousAsync(_editSaveTask, edit.Id, edit.Edits, _editSaveRevision);
        return _editSaveTask;
    }

    private async Task SaveAfterPreviousAsync(Task previous, Guid id, WallpaperEdits edits, int revision)
    {
        await previous;
        await SaveWallpaperEditsAsync(id, edits, revision);
    }

    private async Task SaveWallpaperEditsAsync(Guid id, WallpaperEdits edits, int revision)
    {
        if (_viewerId == id && revision == _editSaveRevision && _editSaveStatus != null) _editSaveStatus.Text = "Saving…";
        try
        {
            var services = App.Current.ServiceProvider;
            var current = await services.GetRequiredService<ILibraryService>().GetWallpaperByIdAsync(id);
            if (current is null || current.IsMissing) throw new InvalidOperationException("This wallpaper is no longer available in your library.");
            var settings = services.GetRequiredService<ISettingsService>();
            await settings.UpdateSettingsAsync(s => s.WallpaperEdits[id] = edits);
            if (services.GetRequiredService<SavedWallpaperService>().ActiveWallpaper?.Id == id)
                await services.GetRequiredService<IWallpaperEngineOrchestrator>().ApplyPlaybackSettingsAllAsync(settings.CurrentSettings.CreatePlaybackSettings());
            if (_viewerId == id && revision == _editSaveRevision && _editSaveStatus != null) _editSaveStatus.Text = "Saved";
        }
        catch (Exception ex)
        {
            if (_viewerId == id && revision == _editSaveRevision && _editSaveStatus != null)
            {
                _editSaveStatus.Text = "Could not save · change a setting to retry";
                ToolTipService.SetToolTip(_editSaveStatus, ex.Message);
            }
        }
    }
}
