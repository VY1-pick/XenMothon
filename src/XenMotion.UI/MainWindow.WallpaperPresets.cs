using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XenMotion.Application.Services;
using XenMotion.Core.Models;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private void AddWallpaperPresetControls(Guid id)
    {
        var settings = App.Current.ServiceProvider.GetRequiredService<ISettingsService>();
        var presets = new ComboBox { Header = "Presets", PlaceholderText = "Load a look", HorizontalAlignment = HorizontalAlignment.Stretch };
        presets.Items.Add(new ComboBoxItem { Content = "Original", Tag = new WallpaperEdits() });
        presets.Items.Add(new ComboBoxItem { Content = "Vivid", Tag = new WallpaperEdits { Saturation = 1.3f, Contrast = 1.1f } });
        presets.Items.Add(new ComboBoxItem { Content = "Soft", Tag = new WallpaperEdits { Saturation = .85f, Contrast = .9f } });
        presets.Items.Add(new ComboBoxItem { Content = "Black and white", Tag = new WallpaperEdits { Saturation = 0 } });
        foreach (var preset in settings.CurrentSettings.WallpaperEditPresets.OrderBy(p => p.Key))
            presets.Items.Add(new ComboBoxItem { Content = preset.Key, Tag = preset.Value });
        presets.SelectionChanged += async (_, _) =>
        {
            if (_viewerId != id || presets.SelectedItem is not ComboBoxItem { Tag: WallpaperEdits edits }) return;
            await LoadWallpaperEditorAsync(id, allowEditing: true, initialEdits: edits.Clone());
            if (_viewerId == id) QueueWallpaperEdits();
        };
        WallpaperEditorPanel.Children.Add(presets);
        var name = new TextBox { PlaceholderText = "Name this preset", MaxLength = 80,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionTextBoxStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, "Preset name");
        var save = new Button { Content = "Save preset", HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionButtonStyle"] };
        save.Click += async (_, _) =>
        {
            if (_viewerId != id || string.IsNullOrWhiteSpace(name.Text)) return;
            string key = name.Text.Trim();
            var snapshot = _viewerEdits.Clone();
            save.IsEnabled = false;
            try
            {
                await settings.UpdateSettingsAsync(s => s.WallpaperEditPresets[key] = snapshot);
                var existing = presets.Items.OfType<ComboBoxItem>().FirstOrDefault(p => p.Content?.ToString() == key);
                if (existing != null) existing.Tag = snapshot;
                else presets.Items.Add(new ComboBoxItem { Content = key, Tag = snapshot });
                save.Content = "Preset saved";
            }
            catch (Exception ex) { save.Content = "Could not save preset"; ToolTipService.SetToolTip(save, ex.Message); }
            finally { save.IsEnabled = true; }
        };
        WallpaperEditorPanel.Children.Add(name);
        WallpaperEditorPanel.Children.Add(save);
    }
}
