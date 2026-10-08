using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.UI.ViewModels;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private Wallpaper? _viewerWallpaper;
    private Guid? _viewerId;
    private WallpaperEdits _viewerEdits = new();
    private byte[]? _previewPixels;
    private WriteableBitmap? _editPreview;
    private readonly List<(Slider Slider, Action<float> Set, float Reset)> _editSliders = [];
    private int _editorRequest;
    private Guid? _loadingEditorId;
    private bool _viewerFromLibrary;
    private bool _loadingEditorFromLibrary;
    private Window? _floatingViewer;
    private bool _viewerDetached;
    private bool _aligningViewer;
    private PointInt32 _attachedViewerPosition;
    private const double ViewerWidth = 356;
    private bool _viewerLightsCreated;

    private void OpenWallpaperViewerWindow()
    {
        if (_floatingViewer != null) return;
        BodyGrid.Children.Remove(DetailsDock);
        DetailsDock.Margin = new Thickness(0);
        // Fill the client area beneath rounded content; its default canvas is white.
        DetailsDock.CornerRadius = new CornerRadius(0);
        var canvas = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 38)) };
        canvas.Children.Add(DetailsDock);
        var viewer = new Window { Title = "XenMotion · Wallpaper", Content = canvas };
        if (_settingsViewModel.EnableGlassEffects) viewer.SystemBackdrop = new DesktopAcrylicBackdrop();
        _floatingViewer = viewer;
        _viewerDetached = false;
        DetailsDetachButton.IsChecked = false;
        UpdateViewerWindowToggle();
        if (viewer.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        viewer.Activated += (_, _) => DispatcherQueue.TryEnqueue(() => ApplyViewerFrame(viewer));
        DetailsDragRegion.SizeChanged += OnViewerTitleSizeChanged;
        viewer.AppWindow.Changed += (_, args) =>
        {
            if (!ReferenceEquals(_floatingViewer, viewer) || _aligningViewer) return;
            if (args.DidPositionChange && !_viewerDetached)
            {
                var position = viewer.AppWindow.Position;
                int dx = position.X - _attachedViewerPosition.X, dy = position.Y - _attachedViewerPosition.Y;
                if (dx != 0 || dy != 0)
                {
                    _attachedViewerPosition = position;
                    AppWindow.Move(new PointInt32(AppWindow.Position.X + dx, AppWindow.Position.Y + dy));
                    AlignWallpaperViewer();
                }
            }
            if (args.DidSizeChange) UpdateViewerDragRegion();
        };
        viewer.AppWindow.Closing += (_, args) =>
        {
            if (!ReferenceEquals(_floatingViewer, viewer)) return;
            args.Cancel = true;
            DispatcherQueue.TryEnqueue(CloseWallpaperViewer);
        };
        AlignWallpaperViewer();
        viewer.Activate();
        ApplyViewerFrame(viewer);
        if (!_viewerLightsCreated)
        {
            AttachGlow(ViewerHeaderGlowHost, Windows.UI.Color.FromArgb(255, 94, 242, 207), 0x8C, drift: false);
            AttachGlow(ViewerMintGlowHost, Windows.UI.Color.FromArgb(255, 60, 220, 190), 0x40, drift: false);
            AttachGlow(ViewerBlueGlowHost, Windows.UI.Color.FromArgb(255, 79, 150, 255), 0x38, drift: false);
            AttachGlow(ViewerVioletGlowHost, Windows.UI.Color.FromArgb(255, 123, 108, 255), 0x34, drift: false);
            _viewerLightsCreated = true;
        }
    }

    private void AlignWallpaperViewer()
    {
        if (_floatingViewer is not { } viewer || _viewerDetached || _aligningViewer) return;
        if (!AppWindow.IsVisible || AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            viewer.AppWindow.Hide();
            return;
        }
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = (int)Math.Ceiling(ViewerWidth * (RootGrid.XamlRoot?.RasterizationScale ?? 1));
        // DWM excludes the invisible resize frame that otherwise leaves a visible gap.
        int mainX = AppWindow.Position.X, mainY = AppWindow.Position.Y;
        int mainWidth = AppWindow.Size.Width, mainHeight = AppWindow.Size.Height;
        if (DwmGetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 9, out var frame, Marshal.SizeOf<ViewerFrame>()) == 0)
        {
            mainX = frame.Left; mainY = frame.Top;
            mainWidth = frame.Right - frame.Left; mainHeight = frame.Bottom - frame.Top;
        }
        var bounds = Services.WallpaperViewerPlacement.Beside(mainX, mainY,
            mainWidth, mainHeight, width, work.X, work.Y, work.Width, work.Height);
        _aligningViewer = true;
        try
        {
            if (bounds.MainX != mainX) AppWindow.Move(new PointInt32(AppWindow.Position.X + bounds.MainX - mainX, AppWindow.Position.Y));
            DetailsDock.CornerRadius = new CornerRadius(0);
            _attachedViewerPosition = new PointInt32(bounds.X, bounds.Y);
            viewer.AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, width, bounds.Height));
            viewer.AppWindow.Show(false);
            ApplyViewerFrame(viewer);
        }
        finally { _aligningViewer = false; }
    }

    private static void ApplyViewerFrame(Window viewer)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(viewer);
        uint darkMode = 1, noBorder = 0xFFFFFFFE;
        DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(uint));
        // Activation can restore the native frame, so reapply after showing and activating.
        DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(uint));
    }

    private void OnViewerTitleSizeChanged(object sender, SizeChangedEventArgs e) => UpdateViewerDragRegion();

    private void UpdateViewerDragRegion()
    {
        if (_floatingViewer is not { } viewer || DetailsDock.XamlRoot is null) return;
        var regions = new List<RectInt32>();
        AddRegion(regions, DetailsDragRegion, DetailsDock.XamlRoot.RasterizationScale);
        InputNonClientPointerSource.GetForWindowId(viewer.AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, regions.ToArray());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ViewerFrame { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out ViewerFrame value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);

    private void OnDetachViewerClicked(object sender, RoutedEventArgs e)
    {
        _viewerDetached = DetailsDetachButton.IsChecked == true;
        UpdateViewerWindowToggle();
        if (_viewerDetached)
        {
            DetailsDock.CornerRadius = new CornerRadius(0);
            _windowManager.Hide();
            _floatingViewer?.Activate();
        }
        else _windowManager.ShowAndActivate();
        AlignWallpaperViewer();
    }

    private void UpdateViewerWindowToggle()
    {
        var label = _viewerDetached ? "Show main window" : "Hide main window";
        DetailsDetachIcon.Glyph = _viewerDetached ? "\uE8A0" : "\uE89F";
        ToolTipService.SetToolTip(DetailsDetachButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DetailsDetachButton, label);
    }

    private void CloseWallpaperViewerWindow()
    {
        if (_floatingViewer is not { } viewer) return;
        bool restoreMain = _viewerDetached;
        _floatingViewer = null;
        _viewerDetached = false;
        DetailsDragRegion.SizeChanged -= OnViewerTitleSizeChanged;
        if (viewer.Content is Grid canvas) canvas.Children.Remove(DetailsDock);
        viewer.Content = null;
        DetailsDock.Margin = new Thickness(0, 14, 20, 20);
        BodyGrid.Children.Add(DetailsDock);
        viewer.Close();
        if (restoreMain) _windowManager.ShowAndActivate();
    }

    private void CloseAttachedWallpaperViewer()
    {
        if (!_viewerDetached) CloseWallpaperViewer();
    }

    private void ShowWallpaperDock()
    {
        SetFiltersOpen(false);
        OpenWallpaperViewerWindow();
        DetailsDock.Visibility = Visibility.Visible;
        if (_floatingViewer != null) _floatingViewer.Title = "XenMotion · " + DetailsTitleTextBlock.Text;
        DetailsScrollViewer.ChangeView(null, 0, null, true);
        RefreshViewerActions();
        foreach (var item in _viewModel.Wallpapers) item.IsSelected = item.Id == _viewerId;
        foreach (var item in _viewModel.ExploreViewModel.SearchResults) item.IsSelected = item.Id == _viewerId;
    }

    private void OnLibraryItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WallpaperItemViewModel item) OpenLibraryViewer(item);
    }

    private void RefreshViewerActions()
    {
        WallpaperEditorPanel.Visibility = _viewerFromLibrary &&
            _viewerWallpaper is not null && WallpaperEditorPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_viewerId is null) return;
        var local = _viewModel.Wallpapers.FirstOrDefault(item => item.Id == _viewerId);
        DetailsRemoveButton.Tag = local;
        DetailsRemoveButton.Visibility = local != null ? Visibility.Visible : Visibility.Collapsed;
        bool active = local?.IsActive == true;
        DetailsPlaybackActions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        DetailsPauseButton.Content = _viewModel.IsPaused ? "Resume" : "Pause";
        DetailsMuteButton.Content = _viewModel.IsMuted ? "Unmute" : "Mute";
        if (_detailsItem is null) DetailsPrimaryButton.IsEnabled = local is { IsMissing: false, IsApplying: false };
    }

    private async void OpenLibraryViewer(WallpaperItemViewModel item)
    {
        _viewModel.ExploreViewModel.CloseDetails();
        _shownDetailsId = null;
        TrackDetailsItem(null);
        _viewerId = item.Id;
        DetailsCategoryTextBlock.Text = item.Wallpaper.Category.ToUpperInvariant();
        DetailsTitleTextBlock.Text = item.Title;
        DetailsAuthorTextBlock.Text = $"by {item.Wallpaper.Creator}";
        DetailsSubscriptionText.Text = item.Wallpaper.IsSubscribed ? "✓ Subscribed" : "In your library";
        DetailsResolutionText.Text = item.ResolutionText;
        UpdateDetailsPreviewAspect(item.Wallpaper.Width, item.Wallpaper.Height);
        DetailsSizeText.Text = item.FormattedSize;
        DetailsAspectText.Text = item.TypeBadge;
        DetailsStatsTextBlock.Text = "Lightweight still preview · Original file is preserved";
        DetailsDescriptionTextBlock.Text = item.Wallpaper.Description;
        DetailsTagsRepeater.ItemsSource = item.Wallpaper.Tags.Select(t => "#" + t).ToList();
        DetailsPreviewBrush.ImageSource = item.ThumbnailImage;
        DetailsPrimaryButton.Content = "Set as wallpaper";
        DetailsPrimaryButton.IsEnabled = !item.IsMissing;
        DetailsProgressBar.Visibility = Visibility.Collapsed;
        ShowWallpaperDock();
        await LoadWallpaperEditorAsync(item.Id, allowEditing: true);
    }

    private async Task LoadWallpaperEditorAsync(Guid id, bool allowEditing = false, WallpaperEdits? initialEdits = null)
    {
        if (initialEdits == null && _loadingEditorId == id && _loadingEditorFromLibrary == allowEditing) return;
        int request = ++_editorRequest;
        await FlushWallpaperEdits();
        if (request != _editorRequest) return;
        _loadingEditorId = id;
        _loadingEditorFromLibrary = allowEditing;
        _viewerFromLibrary = allowEditing;
        StopMotionPreview();
        _viewerId = id;
        _viewerWallpaper = null;
        _previewPixels = null;
        _editPreview = null;
        WallpaperEditorPanel.Visibility = Visibility.Collapsed;
        WallpaperEditorPanel.Children.Clear();
        _editSliders.Clear();
        try
        {
            var services = App.Current.ServiceProvider;
            var wallpaper = await services.GetRequiredService<ILibraryService>().GetWallpaperByIdAsync(id);
            if (_viewerId != id || _viewerFromLibrary != allowEditing) return;
            if (wallpaper is null)
            {
                if (!allowEditing && _detailsItem?.IsVideo == true)
                {
                    if (services.GetRequiredService<ISettingsService>().CurrentSettings.AutoPlayMotionPreviews) LoadCatalogMotionPreview(id);
                    else { DetailsMotionButton.Visibility = Visibility.Visible; DetailsMotionButton.Content = "Play motion preview"; DetailsMotionButton.IsEnabled = true; }
                }
                return;
            }
            _viewerWallpaper = wallpaper;
            DetailsSubscriptionText.Text = wallpaper.IsSubscribed ? "✓ Subscribed" : "In your library";
            if (wallpaper.IsVideo && !wallpaper.IsMissing)
            {
                if (services.GetRequiredService<ISettingsService>().CurrentSettings.AutoPlayMotionPreviews) _ = LoadMotionPreviewAsync(wallpaper);
                else { DetailsMotionButton.Visibility = Visibility.Visible; DetailsMotionButton.Content = "Play motion preview"; DetailsMotionButton.IsEnabled = true; }
            }
            if (!allowEditing || wallpaper.IsMissing) return;
            var settings = services.GetRequiredService<ISettingsService>().CurrentSettings;
            var playback = settings.CreatePlaybackSettings();
            if (initialEdits != null) initialEdits.ApplyTo(playback);
            else settings.ApplyEdits(id, playback);
            _viewerEdits = new WallpaperEdits { Opacity = playback.Opacity, Brightness = playback.Brightness,
                Contrast = playback.Contrast, Saturation = playback.Saturation,
                Red = playback.Red, Green = playback.Green, Blue = playback.Blue,
                PlaybackSpeed = playback.PlaybackSpeed, Volume = playback.Volume, Sharpness = playback.Sharpness,
                Loop = playback.Loop, IsMuted = playback.IsMuted, ScalingMode = playback.ScalingMode, FlipHorizontal = playback.FlipHorizontal };
            AddEditSourceControl(id, settings);
            WallpaperEditorPanel.Children.Add(new TextBlock { Text = "PLAYBACK", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionEyebrowTextStyle"] });
            AddEditSlider("Playback rate", playback.PlaybackSpeed, 2, v => _viewerEdits.PlaybackSpeed = v, minimum: .25f);
            AddEditSlider("Volume", playback.Volume, 1, v => _viewerEdits.Volume = v);
            AddEditSlider("Sharpness", playback.Sharpness, .3, v => _viewerEdits.Sharpness = v, resetValue: 0);
            var loop = new ToggleButton { Content = "Loop video", IsChecked = playback.Loop, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionChipStyle"] };
            var muted = new ToggleButton { Content = "Mute audio", IsChecked = playback.IsMuted, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionChipStyle"] };
            loop.Click += (_, _) => { _viewerEdits.Loop = loop.IsChecked == true; QueueWallpaperEdits(); };
            muted.Click += (_, _) => { _viewerEdits.IsMuted = muted.IsChecked == true; QueueWallpaperEdits(); };
            var flip = new ToggleButton { Content = "Flip", IsChecked = playback.FlipHorizontal, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionChipStyle"] };
            flip.Click += (_, _) => { _viewerEdits.FlipHorizontal = flip.IsChecked == true; _motionPlayer?.Pause(); DetailsMotionPreview.Visibility = Visibility.Collapsed; if (_motionPlayer != null) DetailsMotionButton.Content = "Play motion preview"; RenderEditedPreview(); QueueWallpaperEdits(); };
            var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            options.Children.Add(loop); options.Children.Add(muted); options.Children.Add(flip); WallpaperEditorPanel.Children.Add(options);
            var alignment = new ComboBox { Header = "Alignment", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var mode in Enum.GetValues<PlaybackScalingMode>())
                alignment.Items.Add(new ComboBoxItem { Content = mode == PlaybackScalingMode.Fill ? "Cover" : mode.ToString(), Tag = mode });
            alignment.SelectedIndex = (int)playback.ScalingMode;
            alignment.SelectionChanged += (_, _) => { if (alignment.SelectedItem is ComboBoxItem { Tag: PlaybackScalingMode mode }) { _viewerEdits.ScalingMode = mode; QueueWallpaperEdits(); } };
            WallpaperEditorPanel.Children.Add(alignment);
            WallpaperEditorPanel.Children.Add(new TextBlock { Text = "IMAGE & COLOR", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionEyebrowTextStyle"] });
            AddEditSlider("Opacity", _viewerEdits.Opacity, 1, v => _viewerEdits.Opacity = v);
            AddEditSlider("Brightness", _viewerEdits.Brightness, 2, v => _viewerEdits.Brightness = v);
            AddEditSlider("Contrast", _viewerEdits.Contrast, 2, v => _viewerEdits.Contrast = v);
            AddEditSlider("Saturation", _viewerEdits.Saturation, 2, v => _viewerEdits.Saturation = v);
            AddEditSlider("Red", _viewerEdits.Red, 2, v => _viewerEdits.Red = v);
            AddEditSlider("Green", _viewerEdits.Green, 2, v => _viewerEdits.Green = v);
            AddEditSlider("Blue", _viewerEdits.Blue, 2, v => _viewerEdits.Blue = v);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var reset = new Button { Content = "Reset", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionButtonStyle"] };
            reset.Click += (_, _) => { foreach (var (slider, set, value) in _editSliders) { set(value); slider.Value = value * 100; }
                loop.IsChecked = true; muted.IsChecked = true; flip.IsChecked = false; _viewerEdits.FlipHorizontal = false; alignment.SelectedIndex = 0;
                _viewerEdits.Loop = true; _viewerEdits.IsMuted = true; _viewerEdits.ScalingMode = PlaybackScalingMode.Fill;
                RenderEditedPreview(); QueueWallpaperEdits(); };
            actions.Children.Add(reset);
            _editSaveStatus = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionMintTextBrush"] };
            actions.Children.Add(_editSaveStatus);
            WallpaperEditorPanel.Children.Add(actions);
            WallpaperEditorPanel.Children.Add(new TextBlock { Text = "Changes save automatically after 5 seconds. Opacity reveals the Windows background.", TextWrapping = TextWrapping.Wrap, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionCaptionTextStyle"] });
            WallpaperEditorPanel.Children.Add(new TextBlock { Text = "PRESETS", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionEyebrowTextStyle"] });
            AddWallpaperPresetControls(id);
            StyleWallpaperEditorSections();
            RefreshViewerActions();
            var path = services.GetRequiredService<IStorageManager>().ResolveAbsolutePath(wallpaper.FilePath);
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 480, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image) return;
            var decoder = await BitmapDecoder.CreateAsync(thumbnail);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = Math.Min(480u, decoder.PixelWidth),
                    ScaledHeight = Math.Max(1u, (uint)(decoder.PixelHeight * Math.Min(480d / decoder.PixelWidth, 1))) },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
            if (_viewerId != id || _viewerFromLibrary != allowEditing) return;
            int width = (int)Math.Min(480u, decoder.PixelWidth);
            int height = Math.Max(1, (int)(decoder.PixelHeight * Math.Min(480d / decoder.PixelWidth, 1)));
            _previewPixels = pixels.DetachPixelData();
            _editPreview = new WriteableBitmap(width, height);
            UpdateDetailsPreviewAspect(width, height);
            RenderEditedPreview();
        }
        catch (Exception ex) { if (_viewerId == id) ShowToast("Preview: " + ex.Message); }
        finally { if (_loadingEditorId == id) _loadingEditorId = null; }
    }

    private void StyleWallpaperEditorSections()
    {
        var children = WallpaperEditorPanel.Children.ToArray();
        WallpaperEditorPanel.Children.Clear();
        StackPanel? section = null;
        foreach (var child in children)
        {
            if (child is TextBlock { Text: "PLAYBACK" or "IMAGE & COLOR" or "PRESETS" })
            {
                section = new StackPanel { Spacing = 12 };
                WallpaperEditorPanel.Children.Add(new Border
                {
                    Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionGlassPanelStyle"],
                    Padding = new Thickness(14), CornerRadius = new CornerRadius(18), Child = section
                });
            }
            if (section is null) { WallpaperEditorPanel.Children.Add(child); continue; }
            var settings = App.Current.ServiceProvider.GetRequiredService<ISettingsService>().CurrentSettings;
            section.Children.Add(new ContentControl { Content = child, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = !settings.GlobalEditsEnabled || _viewerId is not { } id || settings.CustomEditWallpapers.Contains(id) });
        }
    }

    private void AddEditSlider(string name, float value, double max, Action<float> set, float minimum = 0, float resetValue = 1)
    {
        var row = new Grid { ColumnSpacing = 10, RowSpacing = 2 };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionCaptionTextStyle"] };
        var amount = new TextBlock { Text = $"{value * 100:0}%", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["XenMotionMintTextBrush"] };
        var slider = new Slider { Minimum = minimum * 100, Maximum = max * 100, StepFrequency = 1, Value = value * 100, MinWidth = 70 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, e) =>
        {
            set((float)e.NewValue / 100); amount.Text = $"{e.NewValue:0}%";
            _motionPlayer?.Pause(); DetailsMotionPreview.Visibility = Visibility.Collapsed;
            if (_motionPlayer != null) DetailsMotionButton.Content = "Play motion preview";
            RenderEditedPreview();
            QueueWallpaperEdits();
        };
        _editSliders.Add((slider, set, resetValue));
        Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2); Grid.SetColumn(amount, 1);
        row.Children.Add(label); row.Children.Add(slider); row.Children.Add(amount);
        WallpaperEditorPanel.Children.Add(row);
    }

    private double _detailsPreviewAspect = 16d / 9;
    private void UpdateDetailsPreviewAspect(int width, int height)
    {
        _detailsPreviewAspect = width > 0 && height > 0 ? (double)width / height : 16d / 9;
        ResizeDetailsPreview();
    }
    private void OnDetailsPreviewSizeChanged(object sender, SizeChangedEventArgs e) => ResizeDetailsPreview();
    private void ResizeDetailsPreview()
    {
        double width = DetailsPreviewFrame.ActualWidth;
        if (width <= 0) return;
        double height = Math.Max(1, (width - 2) / _detailsPreviewAspect + 2);
        if (Math.Abs(DetailsPreviewFrame.Height - height) > .5) DetailsPreviewFrame.Height = height;
    }

    private void RenderEditedPreview()
    {
        if (_previewPixels is null || _editPreview is null) return;
        var pixels = (byte[])_previewPixels.Clone();
        var e = _viewerEdits;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            float grey = (pixels[i + 2] * .2126f + pixels[i + 1] * .7152f + pixels[i] * .0722f) / 255;
            for (int c = 0; c < 3; c++)
            {
                float rgb = grey + (pixels[i + c] / 255f - grey) * e.Saturation;
                float tint = c == 0 ? e.Blue : c == 1 ? e.Green : e.Red;
                pixels[i + c] = (byte)(Math.Clamp(((rgb - .5f) * e.Contrast + .5f) * e.Brightness * tint, 0, 1) * e.Opacity * 255);
            }
            pixels[i + 3] = (byte)(e.Opacity * 255);
        }
        if (e.FlipHorizontal)
            for (int y = 0; y < _editPreview.PixelHeight; y++)
                for (int x = 0; x < _editPreview.PixelWidth / 2; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int left = (y * _editPreview.PixelWidth + x) * 4 + c;
                        int right = (y * _editPreview.PixelWidth + _editPreview.PixelWidth - x - 1) * 4 + c;
                        (pixels[left], pixels[right]) = (pixels[right], pixels[left]);
                    }
        using var buffer = _editPreview.PixelBuffer.AsStream();
        buffer.Write(pixels);
        _editPreview.Invalidate();
        DetailsPreviewBrush.ImageSource = _editPreview;
    }

    private void CloseWallpaperViewer()
    {
        _editorRequest++;
        var saving = FlushWallpaperEdits();
        StopMotionPreview();
        _viewerId = null; _viewerWallpaper = null; _previewPixels = null; _editPreview = null;
        _viewModel.ExploreViewModel.CloseDetails();
        DetailsDock.Visibility = Visibility.Collapsed;
        CloseWallpaperViewerWindow();
        foreach (var item in _viewModel.Wallpapers) item.IsSelected = LibrarySelectionToggle.IsChecked == true && WallpaperGridView.SelectedItems.Contains(item);
        foreach (var item in _viewModel.ExploreViewModel.SearchResults) item.IsSelected = false;
        if (!_windowManager.IsVisible && !_trayManager.IsInitialized) _ = ExitAfterWallpaperSaveAsync(saving);
    }

    private async Task ExitAfterWallpaperSaveAsync(Task saving)
    {
        await saving;
        if (!_windowManager.IsVisible && _floatingViewer == null && !_trayManager.IsInitialized) App.Current.Shutdown();
    }
}
