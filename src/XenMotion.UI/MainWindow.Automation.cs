using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace XenMotion.UI;

/// <summary>
/// Settings sheet hosting content: the wallpaper schedule, per-application rules and the manual
/// update check. Rows are built in code so the draft collections stay the single source of truth.
/// </summary>
public sealed partial class MainWindow
{
    private void OnAddScheduleEntryClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null) return;
        var choice = ScheduleWallpaperCombo.SelectedItem as WallpaperChoice;
        if (!_settingsViewModel.TryAddScheduleEntry(choice?.Id ?? Guid.Empty, ScheduleWindowText.Text, out string error))
        {
            ShowToast(error, true);
            return;
        }

        ScheduleWindowText.Text = string.Empty;
        UpdateSettingsUiState();
    }

    private void OnRemoveScheduleEntryClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || sender is not Button { Tag: ScheduleEntryDraft entry }) return;
        _settingsViewModel.RemoveScheduleEntry(entry);
        UpdateSettingsUiState();
    }

    private async void OnRotateNowClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var service = App.Current.ServiceProvider.GetRequiredService<WallpaperScheduleService>();
            await service.RotateNowAsync();
        }
        catch (Exception ex)
        {
            ShowToast("Could not rotate the wallpaper: " + ex.Message, true);
        }
    }

    private void OnAddApplicationRuleClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null) return;
        var choice = SettingsRuleWallpaperCombo.SelectedItem as WallpaperChoice;
        if (!_settingsViewModel.TryAddApplicationRule(SettingsProcessNameText.Text, choice?.Id ?? Guid.Empty, out string error))
        {
            ShowToast(error, true);
            return;
        }

        SettingsProcessNameText.Text = string.Empty;
        UpdateSettingsUiState();
    }

    private void OnRemoveApplicationRuleClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null || sender is not Button { Tag: ApplicationRuleDraft rule }) return;
        _settingsViewModel.RemoveApplicationRule(rule);
        UpdateSettingsUiState();
    }

    private async void OnCheckUpdatesNowClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel is null) return;
        SettingsCheckUpdatesButton.IsEnabled = false;
        try
        {
            // The check reads the saved settings, so persist the draft first.
            await _settingsViewModel.SaveAsync();
            await _settingsViewModel.CheckForUpdatesNowAsync();
            SettingsUpdateStatusText.Text = _settingsViewModel.UpdateStatus;
        }
        catch (Exception ex)
        {
            SettingsUpdateStatusText.Text = "Could not save the update settings: " + ex.Message;
        }
        finally
        {
            SettingsCheckUpdatesButton.IsEnabled = true;
        }
    }

    /// <summary>Loads the library once per settings session so the pickers show real titles.</summary>
    private async void RefreshAutomationChoices()
    {
        if (_settingsViewModel is null) return;
        try
        {
            var library = App.Current.ServiceProvider.GetRequiredService<ILibraryService>();
            await _settingsViewModel.RefreshWallpaperChoicesAsync(library);
            ScheduleWallpaperCombo.ItemsSource = _settingsViewModel.WallpaperChoices;
            SettingsRuleWallpaperCombo.ItemsSource = _settingsViewModel.WallpaperChoices;
            if (_settingsViewModel.WallpaperChoices.Count > 0)
            {
                ScheduleWallpaperCombo.SelectedIndex = 0;
                SettingsRuleWallpaperCombo.SelectedIndex = 0;
            }

            RefreshScheduleEntryRows();
            RefreshApplicationRuleRows();
        }
        catch (Exception ex)
        {
            ShowToast("Could not load wallpapers for the schedule: " + ex.Message, true);
        }
    }

    private void RefreshScheduleEntryRows()
    {
        ScheduleEntriesPanel.Children.Clear();
        var entries = _settingsViewModel.ScheduleEntries;
        ScheduleEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in entries)
        {
            ScheduleEntriesPanel.Children.Add(BuildAutomationRow(entry.Title, entry.DetailText, entry, OnRemoveScheduleEntryClicked, "Remove from schedule"));
        }

        SettingsScheduleSummaryText.Text = _settingsViewModel.ScheduleSummary;
    }

    private void RefreshApplicationRuleRows()
    {
        AppRulesPanel.Children.Clear();
        var rules = _settingsViewModel.ApplicationRules;
        AppRulesEmptyText.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var rule in rules)
        {
            AppRulesPanel.Children.Add(BuildAutomationRow(rule.Title, rule.DetailText, rule, OnRemoveApplicationRuleClicked, "Remove application rule"));
        }
    }

    private Border BuildAutomationRow(string title, string detail, object draft, RoutedEventHandler onRemove, string removeLabel)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)resources["XenMotionTextBrush"]
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)resources["XenMotionMutedTextBrush"]
        });

        var remove = new Button
        {
            Style = (Style)resources["XenMotionSmallIconButtonStyle"],
            Tag = draft,
            VerticalAlignment = VerticalAlignment.Center
        };
        remove.Click += onRemove;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, removeLabel);
        ToolTipService.SetToolTip(remove, removeLabel);
        remove.Content = new FontIcon
        {
            Style = (Style)resources["XenMotionIconStyle"],
            Glyph = "\uE74D",
            FontSize = 12
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);

        return new Border
        {
            Padding = new Thickness(12, 10, 8, 10),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)resources["XenMotionSectionBrush"],
            BorderBrush = (Brush)resources["XenMotionRimSoftBrush"],
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }
}
