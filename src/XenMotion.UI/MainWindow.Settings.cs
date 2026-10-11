using System.Numerics;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using XenMotion.Application.Services;
using XenMotion.UI.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace XenMotion.UI;

/// <summary>Settings sheet. Every control writes straight into the SettingsViewModel; "Save" persists it.</summary>
public sealed partial class MainWindow
{
    private bool _loadingSettings;

    private Window? _settingsWindow;
    public void OpenSettingsWindow()
    {
        UpdateSettingsUiState();
        if (_settingsWindow is null)
        {
            SettingsOverlay.Children.Remove(SettingsPanel);
            SettingsPanel.RequestedTheme = ElementTheme.Dark;
            SettingsPanel.Width = double.NaN;
            SettingsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            SettingsPanel.Margin = new Thickness(0);
            SettingsPanel.CornerRadius = new CornerRadius(0);
            SettingsPanel.BorderThickness = new Thickness(0);
            var canvas = new Grid { RequestedTheme = ElementTheme.Dark, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 38)) };
            canvas.Children.Add(SettingsPanel);
            _settingsWindow = new Window { Title = "XenMotion Settings", Content = canvas };
            AttachGlow(SettingsMintGlowHost, Windows.UI.Color.FromArgb(255, 94, 242, 207), 0x50, drift: false);
            AttachGlow(SettingsVioletGlowHost, Windows.UI.Color.FromArgb(255, 123, 108, 255), 0x34, drift: false);
            if (_settingsWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(true, false);
            }
            _settingsWindow.Activated += (_, _) => DispatcherQueue.TryEnqueue(() => ApplyViewerFrame(_settingsWindow));
            SettingsDragRegion.SizeChanged += (_, _) => UpdateSettingsDragRegion();
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
            _settingsWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)(680 * scale), work.Width), Math.Min((int)(820 * scale), work.Height)));
            _settingsWindow.AppWindow.Closing += (_, e) => { e.Cancel = true; if (!_measuringQuality) CloseSettingsSheet(); };
        }
        _settingsWindow.AppWindow.Show();
        _settingsWindow.Activate();
        ApplyViewerFrame(_settingsWindow);
        DispatcherQueue.TryEnqueue(UpdateSettingsDragRegion);
    }
    private void UpdateSettingsDragRegion()
    {
        if (_settingsWindow is null || SettingsPanel.XamlRoot is null) return;
        var regions = new List<Windows.Graphics.RectInt32>();
        AddRegion(regions, SettingsDragRegion, SettingsPanel.XamlRoot.RasterizationScale);
        Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(_settingsWindow.AppWindow.Id)
            .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption, regions.ToArray());
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e) => CloseSettingsSheet();
    private void OnSettingsBackdropTapped(object sender, TappedRoutedEventArgs e) => CloseSettingsSheet();
    private void CloseSettingsSheet() => _settingsWindow?.AppWindow.Hide();

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
            SettingsSyncAccentToggle.IsChecked = s.SyncWindowsAccent;
            SettingsSyncBackgroundToggle.IsChecked = s.SyncDesktopBackground;
            SettingsLockScreenCombo.SelectedIndex = (int)s.LockScreenMode;
            SettingsLockScreenImageText.Text = s.LockScreenImagePath;
            SettingsRemoteCatalogToggle.IsChecked = s.UseRemoteCatalog;
            SettingsCatalogUrlText.Text = s.CatalogBackendUrl;
            SettingsCatalogTimeoutNumber.Value = s.CatalogRequestTimeoutSeconds;
            SettingsLocalFallbackToggle.IsChecked = s.UseLocalCatalogFallback;
            SettingsPauseFocusToggle.IsChecked = s.PauseOnApplicationFocus;
            SettingsPauseAppsText.Text = s.PauseApplications;
            SettingsLoopToggle.IsChecked = s.DefaultLoop;
            SettingsDefaultMutedToggle.IsChecked = s.DefaultMuted;
            SettingsPauseFullscreenToggle.IsChecked = s.PauseOnFullscreen;
            SettingsPauseMaximizedToggle.IsChecked = s.PauseOnMaximized;
            SettingsPauseBatteryToggle.IsChecked = s.PauseOnBattery;
            SettingsScreenSaverToggle.IsChecked = s.PauseOnScreenSaver;
            SettingsPowerSaverToggle.IsChecked = s.PauseOnPowerSaver;
            SettingsRemoteSessionToggle.IsChecked = s.PauseOnRemoteSession;
            SettingsMotionPreviewsToggle.IsChecked = s.AutoPlayMotionPreviews;
            SettingsPauseCoveredToggle.IsChecked = s.PauseWhenCovered;
            SettingsMuteCoveredToggle.IsChecked = s.MuteAudioWhenCovered;

            SettingsDefaultVolumeSlider.Value = s.DefaultVolume * 100.0;
            SettingsPlaybackSpeedSlider.Value = s.DefaultPlaybackSpeed;
            SettingsFpsSlider.Value = s.GlobalTargetFps;
            SettingsBrightnessSlider.Value = s.DefaultBrightness * 100.0;
            SettingsContrastSlider.Value = s.DefaultContrast * 100.0;
            SettingsSaturationSlider.Value = s.DefaultSaturation * 100.0;
            SettingsCoverageNumberBox.Value = s.CoveredThresholdPercentage;
            SettingsCoverageModeCombo.SelectedIndex = s.PauseOnlyWhenFullyCovered ? 0 : 1;
            RefreshCoverageControls();
            SettingsHeartbeatSlider.Value = s.HeartbeatIntervalMs;
            SettingsRenderScaleSlider.Value = s.RenderScale * 100;
            SettingsBatteryFpsSlider.Value = s.BatteryFps;
            SettingsFilteringToggle.IsChecked = s.HighQualityFiltering;
            SettingsHardwareToggle.IsChecked = s.UseHardwareDecoding;
            SettingsGlassToggle.IsChecked = s.EnableGlassEffects;
            SettingsAutoTuneToggle.IsChecked = s.AutoTunePlayback;
            SettingsSharpnessSlider.Value = s.DefaultSharpness * 100;
            SettingsVideoHeightCombo.SelectedItem = SettingsVideoHeightCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == s.VideoHeightLimit.ToString());
            SettingsHardwareText.Text = $"{s.HardwareSummary}\n\n{s.PlaybackMeasurement}";
            RefreshQualitySummary();

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
        SettingsHeartbeatSliderText.Text = $"{SettingsHeartbeatSlider.Value:0} ms";
        SettingsRenderScaleText.Text = $"Render resolution: {SettingsRenderScaleSlider.Value:0}%";
        SettingsBatteryFpsText.Text = $"Battery limit: {SettingsBatteryFpsSlider.Value:0} fps";
        SettingsSharpnessText.Text = $"Sharpness: {SettingsSharpnessSlider.Value:0}%";
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
        else if (ReferenceEquals(toggle, SettingsScreenSaverToggle)) s.PauseOnScreenSaver = on;
        else if (ReferenceEquals(toggle, SettingsPowerSaverToggle)) s.PauseOnPowerSaver = on;
        else if (ReferenceEquals(toggle, SettingsRemoteSessionToggle)) s.PauseOnRemoteSession = on;
        else if (ReferenceEquals(toggle, SettingsMotionPreviewsToggle)) s.AutoPlayMotionPreviews = on;
        else if (ReferenceEquals(toggle, SettingsPauseCoveredToggle)) s.PauseWhenCovered = on;
        else if (ReferenceEquals(toggle, SettingsMuteCoveredToggle)) s.MuteAudioWhenCovered = on;
        else if (ReferenceEquals(toggle, SettingsFilteringToggle)) s.HighQualityFiltering = on;
        else if (ReferenceEquals(toggle, SettingsHardwareToggle)) s.UseHardwareDecoding = on;
        else if (ReferenceEquals(toggle, SettingsGlassToggle)) s.EnableGlassEffects = on;
        else if (ReferenceEquals(toggle, SettingsAutoTuneToggle)) s.AutoTunePlayback = on;
        else if (ReferenceEquals(toggle, SettingsSyncAccentToggle)) s.SyncWindowsAccent = on;
        else if (ReferenceEquals(toggle, SettingsSyncBackgroundToggle)) s.SyncDesktopBackground = on;
        else if (ReferenceEquals(toggle, SettingsRemoteCatalogToggle)) s.UseRemoteCatalog = on;
        else if (ReferenceEquals(toggle, SettingsLocalFallbackToggle)) s.UseLocalCatalogFallback = on;
        else if (ReferenceEquals(toggle, SettingsPauseFocusToggle)) s.PauseOnApplicationFocus = on;
        RefreshCoverageControls();
        RefreshQualitySummary();
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
        else if (ReferenceEquals(sender, SettingsHeartbeatSlider)) SettingsHeartbeatSliderText.Text = $"{e.NewValue:0} ms";
        else if (ReferenceEquals(sender, SettingsRenderScaleSlider)) SettingsRenderScaleText.Text = $"Render resolution: {e.NewValue:0}%";
        else if (ReferenceEquals(sender, SettingsBatteryFpsSlider)) SettingsBatteryFpsText.Text = $"Battery limit: {e.NewValue:0} fps";
        else if (ReferenceEquals(sender, SettingsSharpnessSlider)) SettingsSharpnessText.Text = $"Sharpness: {e.NewValue:0}%";

        if (_loadingSettings) return;
        var s = _settingsViewModel;

        if (ReferenceEquals(sender, SettingsDefaultVolumeSlider)) s.DefaultVolume = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        else if (ReferenceEquals(sender, SettingsPlaybackSpeedSlider)) s.DefaultPlaybackSpeed = (float)Math.Clamp(e.NewValue, 0.25, 2.0);
        else if (ReferenceEquals(sender, SettingsFpsSlider)) s.GlobalTargetFps = (int)Math.Round(e.NewValue);
        else if (ReferenceEquals(sender, SettingsBrightnessSlider)) s.DefaultBrightness = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsContrastSlider)) s.DefaultContrast = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsSaturationSlider)) s.DefaultSaturation = (float)Math.Clamp(e.NewValue / 100.0, 0.0, 2.0);
        else if (ReferenceEquals(sender, SettingsHeartbeatSlider)) s.HeartbeatIntervalMs = (int)Math.Round(e.NewValue);
        else if (ReferenceEquals(sender, SettingsRenderScaleSlider)) s.RenderScale = (float)e.NewValue / 100;
        else if (ReferenceEquals(sender, SettingsBatteryFpsSlider)) s.BatteryFps = (int)Math.Round(e.NewValue);
        else if (ReferenceEquals(sender, SettingsSharpnessSlider)) s.DefaultSharpness = (float)e.NewValue / 100;
        RefreshQualitySummary();
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
            if (!_settingsViewModel.AutoPlayMotionPreviews) StopMotionPreview();
            await _viewModel.SyncAudioSettingsAsync();
            var personalization = App.Current.ServiceProvider.GetRequiredService<WindowsPersonalizationService>();
            await personalization.ApplyAsync();
            UpdateSettingsUiState();
            TrySetBackdrop();
            CloseSettingsSheet();
            ShowToast(_settingsViewModel.StatusMessage + " " + personalization.Status);
        }
        catch (Exception ex)
        {
            // The view model reports validation problems through StatusMessage.
            UpdateSettingsUiState();
            ShowToast($"Could not apply settings: {ex.Message}");
        }
    }
    private void OnQualityPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (sender is FrameworkElement { Tag: string name })
        {
            var preset = name == "Recommended" ? _settingsViewModel.RecommendedPreset : Enum.Parse<QualityPreset>(name);
            _settingsViewModel.ApplyQualityPreset(preset);
            _settingsViewModel.AutoTunePlayback = name == "Recommended";
            UpdateSettingsUiState();
        }
    }
    private void OnVideoHeightChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null) return;
        if (SettingsVideoHeightCombo.SelectedItem is ComboBoxItem { Tag: string value } && int.TryParse(value, out int height))
            _settingsViewModel.VideoHeightLimit = height;
        RefreshQualitySummary();
    }
    private void RefreshQualitySummary()
    {
        var s = _settingsViewModel;
        bool wasLoading = _loadingSettings;
        _loadingSettings = true;
        try
        {
            SettingsEconomyRadio.IsChecked = s.QualityPreset == QualityPreset.Economy;
            SettingsBalancedRadio.IsChecked = s.QualityPreset == QualityPreset.Balanced;
            SettingsHighRadio.IsChecked = s.QualityPreset == QualityPreset.High;
            SettingsUltraRadio.IsChecked = s.QualityPreset == QualityPreset.Ultra;
            string resolution = s.VideoHeightLimit == 0 ? "original resolution" : $"up to {s.VideoHeightLimit}p";
            SettingsQualityText.Text = $"{s.QualityPreset} · {s.GlobalTargetFps} fps ceiling · {resolution} · {s.RenderScale * 100:0}% render";
            SettingsRecommendedButton.Content = "Automatic — measure my system";
        }
        finally { _loadingSettings = wasLoading; }
    }
    private void RefreshCoverageControls()
    {
        SettingsCoverageModeCombo.IsEnabled = _settingsViewModel.PauseWhenCovered;
        SettingsCoverageNumberBox.IsEnabled = _settingsViewModel.PauseWhenCovered;
        SettingsCustomCoveragePanel.Visibility = _settingsViewModel.PauseOnlyWhenFullyCovered ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnCoverageModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null || SettingsCoverageModeCombo.SelectedIndex < 0) return;
        _settingsViewModel.PauseOnlyWhenFullyCovered = SettingsCoverageModeCombo.SelectedIndex == 0;
        RefreshCoverageControls();
        RefreshQualitySummary();
    }
    private void OnCoveragePercentageChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null || !double.IsFinite(e.NewValue)) return;
        _settingsViewModel.CoveredThresholdPercentage = (int)Math.Round(Math.Clamp(e.NewValue, 1, 100));
        RefreshQualitySummary();
    }

    private void OnAdvancedTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null) return;
        if (ReferenceEquals(sender, SettingsCatalogUrlText)) _settingsViewModel.CatalogBackendUrl = SettingsCatalogUrlText.Text;
        else if (ReferenceEquals(sender, SettingsPauseAppsText)) _settingsViewModel.PauseApplications = SettingsPauseAppsText.Text;
    }

    private void OnCatalogTimeoutChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (!_loadingSettings && _settingsViewModel is not null && double.IsFinite(e.NewValue))
            _settingsViewModel.CatalogRequestTimeoutSeconds = (int)e.NewValue;
    }

    private void OnLockScreenModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || _settingsViewModel is null || SettingsLockScreenCombo.SelectedIndex < 0) return;
        _settingsViewModel.LockScreenMode = (LockScreenWallpaperMode)SettingsLockScreenCombo.SelectedIndex;
    }

    private async void OnPickLockScreenImageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_settingsWindow ?? this));
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp" }) picker.FileTypeFilter.Add(extension);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            _settingsViewModel.LockScreenImagePath = file.Path;
            _settingsViewModel.LockScreenMode = LockScreenWallpaperMode.SeparateImage;
            UpdateSettingsUiState();
        }
        catch (Exception ex) { ShowToast("Could not select image: " + ex.Message); }
    }

    private bool _measuringQuality;
    public bool IsMeasuringQuality => _measuringQuality;
    private async void OnMeasureQualityClicked(object sender, RoutedEventArgs e)
    {
        if (_measuringQuality) return;
        var services = App.Current.ServiceProvider;
        var engine = services.GetRequiredService<IWallpaperEngineOrchestrator>();
        var saved = services.GetRequiredService<SavedWallpaperService>();
        var adaptive = services.GetRequiredService<AdaptiveQualityService>();
        var wallpaper = saved.ActiveWallpaper;
        var monitor = services.GetRequiredService<MonitorTopologyService>().CurrentMonitors.FirstOrDefault(m => m.IsPrimary);
        if (wallpaper?.Type != WallpaperType.Video || monitor is null) { SettingsStatusTextBlock.Text = "Apply a video wallpaper first."; return; }
        var confirmation = new ContentDialog { XamlRoot = SettingsPanel.XamlRoot, RequestedTheme = ElementTheme.Dark,
            Title = "Measure automatic quality?", Content = "Allow up to 3.5 minutes. Playback pauses and XenMotion controls are unavailable during the test. Close games or heavy applications first. We prioritize your display resolution and only lower it if playback cannot keep up.",
            PrimaryButtonText = "Start measurement", CloseButtonText = "Cancel" };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { Text = "Preparing measurement…", TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { IsIndeterminate = true };
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(status); content.Children.Add(progress);
        var advanced = new Expander { Header = "Advanced: measurement details", Content = new TextBlock { Text = _settingsViewModel.HardwareSummary, TextWrapping = TextWrapping.Wrap, MaxWidth = 430 } };
        content.Children.Add(advanced);
        var dialog = new ContentDialog { XamlRoot = SettingsPanel.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Automatic quality", Content = content, CloseButtonText = "Cancel" };
        dialog.Closing += (_, args) => { if (_measuringQuality) { args.Cancel = true; cancellation.Cancel(); status.Text = "Cancelling and restoring playback…"; } };
        var started = System.Diagnostics.Stopwatch.StartNew();
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => { status.Text = $"Elapsed: {started.Elapsed.TotalSeconds:0}s · Up to 3:30\n{_settingsViewModel.PlaybackMeasurement}"; };
        bool wasPaused = engine.IsGloballyPaused;
        bool viewerVisible = _floatingViewer?.AppWindow.IsVisible == true;
        _floatingViewer?.AppWindow.Hide();
        _measuringQuality = true; _mainInteraction.IsEnabled = false;
        var showing = dialog.ShowAsync(); timer.Start();
        try
        {
            await engine.PauseAllAsync();
            adaptive.AllowMeasurements = true;
            var requested = services.GetRequiredService<ISettingsService>().CurrentSettings.CreatePlaybackSettings();
            var baseline = new ApplicationSettings();
            QualityPresets.Apply(baseline, QualityPresets.ForDisplayPixels((long)monitor.Bounds.Width * monitor.Bounds.Height));
            requested.VideoHeightLimit = baseline.VideoHeightLimit;
            requested.RenderScale = 1; requested.TargetFps = 60;
            requested.PlaybackSpeed = 1; requested.ScalingMode = PlaybackScalingMode.Fill;
            requested.AutoTunePlayback = true;
            var tuned = await adaptive.TuneAsync(wallpaper.FilePath, requested, monitor.Bounds, cancellation.Token);
            if (adaptive.LastMeasurementSucceeded)
            {
                _settingsViewModel.VideoHeightLimit = tuned.VideoHeightLimit;
                _settingsViewModel.RenderScale = tuned.RenderScale;
                _settingsViewModel.GlobalTargetFps = tuned.TargetFps;
                _settingsViewModel.DefaultSharpness = tuned.Sharpness;
                _settingsViewModel.HighQualityFiltering = tuned.HighQualityFiltering;
                _settingsViewModel.AutoTunePlayback = true;
                await _settingsViewModel.SaveAsync();
                status.Text = $"Recommended: {tuned.VideoHeightLimit}p · {tuned.TargetFps} fps · {tuned.RenderScale:P0} render. Settings saved.";
            }
            else status.Text = _settingsViewModel.PlaybackMeasurement;
            advanced.Content = new TextBlock { Text = $"Target display: {monitor.Bounds.Width} × {monitor.Bounds.Height}.\n{_settingsViewModel.HardwareSummary}\n{_settingsViewModel.PlaybackMeasurement}", TextWrapping = TextWrapping.Wrap, MaxWidth = 430 };
        }
        catch (OperationCanceledException) { status.Text = "Measurement cancelled. Your previous settings were kept."; }
        catch (Exception ex) { status.Text = "Measurement failed: " + ex.Message; }
        finally
        {
            timer.Stop(); adaptive.AllowMeasurements = false;
            try { if (!wasPaused) await engine.ResumeAllAsync(); }
            catch (Exception ex) { status.Text += " Playback could not resume: " + ex.Message; }
            _measuringQuality = false; _mainInteraction.IsEnabled = true;
            if (viewerVisible) _floatingViewer?.AppWindow.Show(false);
            progress.IsIndeterminate = false; progress.Value = 100;
            dialog.CloseButtonText = "Done"; UpdateSettingsUiState();
        }
        await showing;
    }
}
