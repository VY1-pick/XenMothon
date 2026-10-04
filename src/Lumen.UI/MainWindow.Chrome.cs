using System.Numerics;
using Lumen.UI.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Lumen.UI;

/// <summary>Window chrome: custom caption buttons, backdrop, ambient liquid-glass light, overlays and toast.</summary>
public sealed partial class MainWindow
{
    private bool _customCaption;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _toastTimer;

    private void TrySetBackdrop()
    {
        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            // Older shells have no acrylic backdrop; the solid gradient page still looks right.
        }
    }

    private void ConfigureWindowChrome()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                // Removes the system title bar *and* its caption buttons, keeps the resize border.
                presenter.SetBorderAndTitleBar(true, false);
                _customCaption = true;
            }
        }
        catch
        {
            _customCaption = false;
        }

        if (!_customCaption)
        {
            // Fallback: keep the system buttons, but make them blend into the glass header.
            CaptionPanel.Visibility = Visibility.Collapsed;
            try
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(DragRegion);
                var bar = AppWindow.TitleBar;
                var clear = Color.FromArgb(0, 0, 0, 0);
                var fg = Color.FromArgb(255, 243, 251, 249);
                bar.BackgroundColor = clear;
                bar.InactiveBackgroundColor = clear;
                bar.ButtonBackgroundColor = clear;
                bar.ButtonInactiveBackgroundColor = clear;
                bar.ButtonForegroundColor = fg;
                bar.ButtonInactiveForegroundColor = Color.FromArgb(140, 243, 251, 249);
                bar.ButtonHoverBackgroundColor = Color.FromArgb(60, 94, 242, 207);
                bar.ButtonHoverForegroundColor = fg;
                bar.ButtonPressedBackgroundColor = Color.FromArgb(110, 94, 242, 207);
                bar.ButtonPressedForegroundColor = fg;
            }
            catch
            {
                // Caption customization is best-effort.
            }
        }

        try
        {
            string icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "lumen-logo.ico");
            if (System.IO.File.Exists(icon)) AppWindow.SetIcon(icon);
        }
        catch
        {
        }

        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange || args.DidSizeChange)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateMaximizeGlyph();
                    UpdateInputRegions();
                });
            }
        };
        HeaderBar.SizeChanged += (_, _) => UpdateInputRegions();
        NavPill.SizeChanged += (_, _) => UpdateInputRegions();
    }

    /// <summary>Everything in the header that is not a control becomes a window drag handle.</summary>
    private void UpdateInputRegions()
    {
        if (!_customCaption || RootGrid.XamlRoot is null) return;

        try
        {
            double scale = RootGrid.XamlRoot.RasterizationScale;
            var rects = new List<RectInt32>();
            AddRegion(rects, DragRegion, scale);
            AddRegion(rects, BrandPanel, scale);

            var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            source.SetRegionRects(NonClientRegionKind.Caption, rects.ToArray());
        }
        catch
        {
            // Dragging falls back to nothing special; the buttons still work.
        }
    }

    private static void AddRegion(List<RectInt32> rects, FrameworkElement element, double scale)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var origin = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        rects.Add(new RectInt32(
            (int)Math.Round(origin.X * scale),
            (int)Math.Round(origin.Y * scale),
            (int)Math.Round(element.ActualWidth * scale),
            (int)Math.Round(element.ActualHeight * scale)));
    }

    private void UpdateMaximizeGlyph()
    {
        try
        {
            bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            MaximizeGlyph.Glyph = maximized ? "" : "";
            ToolTipService.SetToolTip(MaximizeButton, maximized ? "Restore" : "Maximize");
        }
        catch
        {
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
        }
        catch
        {
        }
    }

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter p)
            {
                if (p.State == OverlappedPresenterState.Maximized) p.Restore();
                else p.Maximize();
            }
        }
        catch
        {
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ ambient liquid-glass light

    private void BuildAmbientLight()
    {
        AttachGlow(HeaderGlowHost, Color.FromArgb(255, 94, 242, 207), 0x8C, drift: false);
        AttachGlow(AmbientMintHost, Color.FromArgb(255, 60, 220, 190), 0x40, drift: true);
        AttachGlow(AmbientBlueHost, Color.FromArgb(255, 79, 150, 255), 0x38, drift: true);
        AttachGlow(AmbientVioletHost, Color.FromArgb(255, 123, 108, 255), 0x34, drift: true);
    }

    /// <summary>
    /// Paints a soft radial light into <paramref name="host"/> using the compositor, so the glow fades smoothly
    /// with distance from its centre. Falls back to a plain horizontal fade if composition is unavailable.
    /// </summary>
    private static void AttachGlow(FrameworkElement host, Color color, byte peakAlpha, bool drift)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(host);
            var compositor = visual.Compositor;
            var brush = compositor.CreateRadialGradientBrush();
            brush.MappingMode = CompositionMappingMode.Relative;
            brush.EllipseCenter = new Vector2(0.5f, 0.5f);
            brush.EllipseRadius = new Vector2(0.5f, 0.5f);

            Color Stop(double factor) => Color.FromArgb((byte)Math.Round(peakAlpha * factor), color.R, color.G, color.B);
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.00f, Stop(1.00)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.25f, Stop(0.62)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.50f, Stop(0.28)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.75f, Stop(0.08)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(1.00f, Stop(0.00)));

            var sprite = compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.Size = new Vector2((float)Math.Max(host.Width, 1), (float)Math.Max(host.Height, 1));
            ElementCompositionPreview.SetElementChildVisual(host, sprite);
            host.SizeChanged += (_, e) => sprite.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);

            if (drift)
            {
                var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.InsertKeyFrame(0f, Vector3.Zero);
                animation.InsertKeyFrame(0.5f, new Vector3(46f, -30f, 0f));
                animation.InsertKeyFrame(1f, Vector3.Zero);
                animation.Duration = TimeSpan.FromSeconds(26);
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
                sprite.StartAnimation("Offset", animation);
            }
        }
        catch
        {
            host.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint = new Windows.Foundation.Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb((byte)(peakAlpha / 2), color.R, color.G, color.B), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 1 }
                }
            };
        }
    }

    // ------------------------------------------------------------------ overlays

    private void ShowOverlay(UIElement overlay, UIElement panel, Vector3 from)
    {
        panel.Translation = from;
        overlay.Opacity = 0;
        overlay.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            overlay.Opacity = 1;
            panel.Translation = Vector3.Zero;
        });
    }

    private void HideOverlay(UIElement overlay, UIElement panel, Vector3 to)
    {
        overlay.Opacity = 0;
        panel.Translation = to;
        DelayCollapse(overlay, 260);
    }

    private void DelayCollapse(UIElement element, int milliseconds)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            if (element.Opacity < 0.01) element.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void OnDialogPanelTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Taps inside a dialog must never fall through to the backdrop and close it.
        e.Handled = true;
    }

    // ------------------------------------------------------------------ toast

    private void ShowToast(string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        ToastText.Text = message;
        ToastIcon.Glyph = isError ? "" : "";
        ToastIcon.Foreground = Res(isError ? "LumenDangerBrush" : "LumenMintTextBrush");
        ToastIconHost.Background = Res(isError ? "LumenDangerSoftBrush" : "LumenAccentSoftBrush");

        ToastHost.Translation = new Vector3(0, 18, 0);
        ToastHost.Opacity = 0;
        ToastHost.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ToastHost.Opacity = 1;
            ToastHost.Translation = Vector3.Zero;
        });

        _toastTimer ??= DispatcherQueue.CreateTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(isError ? 6 : 3.6);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick -= OnToastTimerTick;
        _toastTimer.Tick += OnToastTimerTick;
        _toastTimer.Start();
    }

    private void OnToastTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        HideToast();
    }

    private void HideToast()
    {
        ToastHost.Opacity = 0;
        ToastHost.Translation = new Vector3(0, 18, 0);
        DelayCollapse(ToastHost, 300);
    }

    private void OnToastCloseClicked(object sender, RoutedEventArgs e) => HideToast();

    private static Brush Res(string key)
    {
        var resources = global::Microsoft.UI.Xaml.Application.Current.Resources;
        return resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.White);
    }
}
