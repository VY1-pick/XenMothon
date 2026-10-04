using System.Numerics;
using Lumen.Core.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Lumen.UI;

/// <summary>Settings sheet. Every control writes straight into the SettingsViewModel; "Save" persists it.</summary>
public sealed partial class MainWindow
{
    private bool _loadingSettings;

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        UpdateSettingsUiState();
        ShowOverlay(SettingsOverlay, SettingsPanel, new Vector3(56, 0, 0));
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e) => CloseSettingsSheet();

    private void OnSettingsBackdropTapped(object sender, TappedRoutedEventArgs e) => CloseSettingsSheet();

    private void CloseSettingsSheet() => HideOverlay(SettingsOverlay, SettingsPanel, new Vector3(56, 0, 0));

    private void OnSettingsTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tab }) ShowSettingsTab(tab);
    }

    private void ShowSettingsTab(string tab)
    {
        SettingsGeneralSection.Visibility = tab == "General" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPlaybackSection.Visibility = tab == "Playback" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPerformanceSection.Visibility = tab == "Performance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsTabGeneral.IsChecked = tab == "General";
        SettingsTabPlayback.IsChecked = tab == "Playback";
        SettingsTabPerformance.IsChecked = tab == "Performance";
    }

    // ------------------------------------------------------------------ view model -> controls

    private void UpdateSettingsUiState()
    {
        _loadingSettings = true;
        try
        {
            var s = _settingsViewModel;

            if (SettingsDisplayNameTextBox.Text != s.DisplayName) SettingsDisplayNameTextBox.Text = s.DisplayName ?? string.Empty;

            SettingsStartWithWindowsToggle.IsChecked = s.StartWithWindows;
            SettingsStartMinimizedToggle.IsChecked = s.StartMinimizedToTray;
            SettingsLoopToggle.IsChecked = s.DefaultLoop;
            SettingsDefaultMutedToggle.IsChecked = s.DefaultMuted;
            SettingsPauseFullscreenToggle.IsChecked = s.PauseOnFullscreen;
            SettingsPauseMaximizedToggle.IsChecked = s.PauseOnMaximized;
            SettingsPauseBatteryToggle.IsChecked = s.PauseOnBattery;
            SettingsPauseCoveredToggle.IsChecked = s.PauseWhenCovered;
            SettingsMuteCoveredToggle.IsChecked = s.MuteAudioWhenCovered;

            SettingsDefaultVolumeSlider.Value = s.DefaultVolume * 100.0;
            SettingsPlaybackSpeedSlider.Value = s.DefaultPlaybackSpeed;
            SettingsFpsSlider.Value = s.GlobalTargetFps;
            SettingsBrightnessSlider.Value = s.DefaultBrightness * 100.0;
            SettingsContrastSlider.Value = s.DefaultContrast * 100.0;
            SettingsSaturationSlider.Value = s.DefaultSaturation * 100.0;
            SettingsCoverageSlider.Value = s.CoveredThresholdPercentage;
            SettingsHeartbeatSlider.Value = s.HeartbeatIntervalMs;

            ScaleFillChip.IsChecked = s.DefaultScalingMode == PlaybackScalingMode.Fill;
            ScaleFitChip.IsChecked = s.DefaultScalingMode == PlaybackScalingMode.Fit;
            ScaleStretchChip.IsChecked = s.DefaultScalingMode == PlaybackScalingMode.Stretch;
            ScaleCenterChip.IsChecked = s.DefaultScalingMode == PlaybackScalingMode.Center;
            ScaleSpanChip.IsChecked = s.DefaultScalingMode == PlaybackScalingMode.Span;

            SettingsStatusTextBlock.Text = s.StatusMessage ?? string.Empty;
            RefreshSettingValueLabels();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void RefreshSettingValueLabels()
    {
        SettingsDefaultVolumeSliderText.Text = $"{SettingsDefaultVolumeSlider.Value:0}%";
        SettingsPlaybackSpeedSliderText.Text = $"{SettingsPlaybackSpeedSlider.Value:0.00}x";
        SettingsFpsSliderText.Text = $"{SettingsFpsSlider.Value:0} fps";
        SettingsBrightnessSliderText.Text = $"{SettingsBrightnessSlider.Value:0}%";
        SettingsContrastSliderText.Text = $"{SettingsContrastSlider.Value:0}%";
        SettingsSaturationSliderText.Text = $"{SettingsSaturationSlider.Value:0}%";
        SettingsCoverageSliderText.Text = $"{SettingsCoverageSlider.Value:0}%";
        SettingsHeartbeatSliderText.Text = $"{SettingsHeartbeatSlider.Value:0} ms";
    }

    // ------------------------------------------------------------------ controls -> view model

    private void OnSettingsDisplayNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null) return;
        _settingsViewModel.DisplayName = SettingsDisplayNameTextBox.Text;
    }

    private void OnSettingsToggleClicked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not ToggleButton toggle) return;
        bool on = toggle.IsChecked == true;
        var s = _settingsViewModel;

        if (ReferenceEquals(toggle, SettingsStartWithWindowsToggle)) s.StartWithWindows = on;
        else if (ReferenceEquals(toggle, SettingsStartMinimizedToggle)) s.StartMinimizedToTray = on;
        else if (ReferenceEquals(toggle, SettingsLoopToggle)) s.DefaultLoop = on;
        else if (ReferenceEquals(toggle, SettingsDefaultMutedToggle)) s.DefaultMuted = on;
        else if (ReferenceEquals(toggle, SettingsPauseFullscreenToggle)) s.PauseOnFullscreen = on;
        else if (ReferenceEquals(toggle, SettingsPauseMaximizedToggle)) s.PauseOnMaximized = on;
        else if (ReferenceEquals(toggle, SettingsPauseBatteryToggle)) s.PauseOnBattery = on;
        else if (ReferenceEquals(toggle, SettingsPauseCoveredToggle)) s.PauseWhenCovered = on;
        else if (ReferenceEquals(toggle, SettingsMuteCoveredToggle)) s.MuteAudioWhenCovered = on;
    }

    private void OnSettingsSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingsViewModel is null || double.IsNaN(e.NewValue)) return;

        // Labels follow the sliders even while the sheet is being filled from the view model.
        if (ReferenceEquals(sender, SettingsDefaultVolumeSlider)) SettingsDefaultVolumeSliderText.Text = $"{e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsPlaybackSpeedSlider)) SettingsPlaybackSpeedSliderText.Text = $"{e.NewValue:0.00}x";
        else if (ReferenceEquals(sender, SettingsFpsSlider)) SettingsFpsSliderText.Text = $"{e.NewValue:0} fps";
        else if (ReferenceEquals(sender, SettingsBrightnessSlider)) SettingsBrightnessSliderText.Text = $"{e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsContrastSlider)) SettingsContrastSliderText.Text = $"{e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsSaturationSlider)) SettingsSaturationSliderText.Text = $"{e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsCoverageSlider)) SettingsCoverageSliderText.Text = $"{e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsHeartbeatSlider)) SettingsHeartbeatSliderText.Text = $"{e.NewValue:0} ms";

        if (_loadingSettings) return;
        var s = _settingsViewModel;

        if (ReferenceEquals(sender, SettingsDefaultVolumeSlider)) s.DefaultVolume = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        else if (ReferenceEquals(sender, SettingsPlaybackSpeedSlider)) s.DefaultPlaybackSpeed = (float)Math.Clamp(e.NewValue, 0.25, 2.0);
        else if (ReferenceEquals(sender, SettingsFpsSlider)) s.GlobalTargetFps = (int)Math.Round(e.NewValue);
        else if (ReferenceEquals(sender, SettingsBrightnessSlider)) s.DefaultBrightness = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsContrastSlider)) s.DefaultContrast = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsSaturationSlider)) s.DefaultSaturation = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsCoverageSlider)) s.CoveredThresholdPercentage = (int)Math.Round(e.NewValue);
        else if (ReferenceEquals(sender, SettingsHeartbeatSlider)) s.HeartbeatIntervalMs = (int)Math.Round(e.NewValue);
    }

    private void OnScalingChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse<PlaybackScalingMode>(name, out var mode))
        {
            _settingsViewModel.DefaultScalingMode = mode;
        }

        // Radio behaviour: always exactly one chip stays lit.
        var current = _settingsViewModel.DefaultScalingMode;
        ScaleFillChip.IsChecked = current == PlaybackScalingMode.Fill;
        ScaleFitChip.IsChecked = current == PlaybackScalingMode.Fit;
        ScaleStretchChip.IsChecked = current == PlaybackScalingMode.Stretch;
        ScaleCenterChip.IsChecked = current == PlaybackScalingMode.Center;
        ScaleSpanChip.IsChecked = current == PlaybackScalingMode.Span;
    }

    private async void OnSaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsViewModel.Theme = "Dark";
            await _settingsViewModel.SaveAsync();
            UpdateSettingsUiState();
            CloseSettingsSheet();
            ShowToast("Settings saved");
        }
        catch
        {
            // The view model reports validation problems through StatusMessage.
            UpdateSettingsUiState();
        }
    }
}
