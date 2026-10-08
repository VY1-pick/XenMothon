using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XenMotion.Application.Services;
using XenMotion.Core.Models;
using XenMotion.Core.Enums;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private async void OnGlobalEditsClicked(object sender, RoutedEventArgs e)
    {
        var service = App.Current.ServiceProvider.GetRequiredService<ISettingsService>();
        var edit = service.CurrentSettings.GlobalEdits.Clone();
        var panel = new StackPanel { Spacing = 12 };
        var enabled = new CheckBox { Content = "Enable edits for all wallpapers", IsChecked = service.CurrentSettings.GlobalEditsEnabled };
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "Applies to existing and future wallpapers. Individual wallpapers can use custom edits instead.", TextWrapping = TextWrapping.Wrap });
        var sliders = new List<(Slider Slider, float Default)>();
        void Add(string label, float value, float max, Action<float> set, float min = 0, float reset = 1)
        {
            var slider = new Slider { Header = label, Minimum = min, Maximum = max, Value = value, StepFrequency = .01 };
            slider.ValueChanged += (_, args) => set((float)args.NewValue);
            sliders.Add((slider, reset)); panel.Children.Add(slider);
        }
        Add("Opacity", edit.Opacity, 1, v => edit.Opacity = v);
        Add("Brightness", edit.Brightness, 2, v => edit.Brightness = v);
        Add("Contrast", edit.Contrast, 2, v => edit.Contrast = v);
        Add("Saturation", edit.Saturation, 2, v => edit.Saturation = v);
        Add("Red", edit.Red, 2, v => edit.Red = v);
        Add("Green", edit.Green, 2, v => edit.Green = v);
        Add("Blue", edit.Blue, 2, v => edit.Blue = v);
        Add("Playback speed", edit.PlaybackSpeed ?? 1, 2, v => edit.PlaybackSpeed = v, .25f);
        Add("Volume", edit.Volume ?? 1, 1, v => edit.Volume = v);
        Add("Sharpness", edit.Sharpness ?? 0, .3f, v => edit.Sharpness = v, reset: 0);
        var flip = new CheckBox { Content = "Flip horizontally", IsChecked = edit.FlipHorizontal };
        var mute = new CheckBox { Content = "Mute", IsChecked = edit.IsMuted ?? true };
        var loop = new CheckBox { Content = "Loop video", IsChecked = edit.Loop ?? true };
        panel.Children.Add(flip); panel.Children.Add(mute); panel.Children.Add(loop);
        var scaling = new ComboBox { Header = "Alignment", ItemsSource = Enum.GetValues<PlaybackScalingMode>(), SelectedItem = edit.ScalingMode ?? PlaybackScalingMode.Fill };
        panel.Children.Add(scaling);
        var reset = new Button { Content = "Reset edits" };
        reset.Click += (_, _) => { foreach (var item in sliders) item.Slider.Value = item.Default; flip.IsChecked = false; mute.IsChecked = loop.IsChecked = true; scaling.SelectedItem = PlaybackScalingMode.Fill; };
        panel.Children.Add(reset);
        var dialog = new ContentDialog { XamlRoot = SettingsPanel.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Global wallpaper edits",
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 }, PrimaryButtonText = "Apply to wallpapers", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await FlushWallpaperEdits();
            edit.FlipHorizontal = flip.IsChecked == true; edit.IsMuted = mute.IsChecked == true; edit.Loop = loop.IsChecked == true;
            edit.ScalingMode = (PlaybackScalingMode)scaling.SelectedItem;
            await service.UpdateSettingsAsync(s => { s.GlobalEdits = edit; s.GlobalEditsEnabled = enabled.IsChecked == true; });
            await App.Current.ServiceProvider.GetRequiredService<IWallpaperEngineOrchestrator>().ApplyPlaybackSettingsAllAsync(service.CurrentSettings.CreatePlaybackSettings());
            if (_viewerId is { } id) { _loadingEditorId = null; await LoadWallpaperEditorAsync(id, true); }
            SettingsStatusTextBlock.Text = "Global edits saved and applied.";
        }
        catch (Exception ex) { SettingsStatusTextBlock.Text = "Could not apply edits: " + ex.Message; }
    }

    private void AddEditSourceControl(Guid id, ApplicationSettings settings)
    {
        var useGlobal = new CheckBox { Content = "Use global edits (turn off for custom)",
            IsChecked = settings.GlobalEditsEnabled && !settings.CustomEditWallpapers.Contains(id), IsEnabled = settings.GlobalEditsEnabled };
        var host = new Grid(); host.Children.Add(useGlobal);
        ToolTipService.SetToolTip(host, settings.GlobalEditsEnabled ? "Custom edits are preserved when switching." : "First enable Global wallpaper edits in Settings.");
        WallpaperEditorPanel.Children.Add(host);
        useGlobal.Click += async (_, _) =>
        {
            try
            {
                await FlushWallpaperEdits();
                var service = App.Current.ServiceProvider.GetRequiredService<ISettingsService>();
                await service.UpdateSettingsAsync(s => { if (useGlobal.IsChecked == true) s.CustomEditWallpapers.Remove(id); else s.CustomEditWallpapers.Add(id); });
                await App.Current.ServiceProvider.GetRequiredService<IWallpaperEngineOrchestrator>().ApplyPlaybackSettingsAllAsync(service.CurrentSettings.CreatePlaybackSettings());
                _loadingEditorId = null;
                await LoadWallpaperEditorAsync(id, true);
            }
            catch (Exception ex) { ShowToast("Could not change edit source: " + ex.Message, true); }
        };
    }

    private async void OnResetSettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = SettingsPanel.XamlRoot, RequestedTheme = ElementTheme.Dark,
            Title = "Reset settings?", Content = "Resets app preferences and disables global edits. Your library, saved presets and custom wallpaper edits are kept.",
            PrimaryButtonText = "Reset", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            _settingsViewModel.ResetDraft();
            await _settingsViewModel.SaveAsync();
            var service = App.Current.ServiceProvider.GetRequiredService<ISettingsService>();
            await service.UpdateSettingsAsync(s => { s.GlobalEditsEnabled = false; s.GlobalEdits = new(); s.AutoPlayMotionPreviews = false;
                s.Performance.PauseOnScreenSaver = s.Performance.PauseOnPowerSaver = s.Performance.PauseOnRemoteSession = true; });
            await App.Current.ServiceProvider.GetRequiredService<IWallpaperEngineOrchestrator>().ApplyPlaybackSettingsAllAsync(service.CurrentSettings.CreatePlaybackSettings());
            UpdateSettingsUiState(); TrySetBackdrop();
        }
        catch (Exception ex) { SettingsStatusTextBlock.Text = "Reset failed: " + ex.Message; }
    }
}
