using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace XenMotion.UI.Controls;

public sealed partial class ActiveWallpaperOverlay : UserControl
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(nameof(IsPlaying), typeof(bool),
        typeof(ActiveWallpaperOverlay), new PropertyMetadata(false, (sender, _) => ((ActiveWallpaperOverlay)sender).UpdateMotion()));
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    private bool _loaded;
    private bool _inViewport = true;
    private readonly UISettings _uiSettings = new();
    private CompositionLinearGradientBrush? _orbit;
    private CompositionRoundedRectangleGeometry? _outlineGeometry;
    private ShapeVisual? _outlineVisual;

    public ActiveWallpaperOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => { _loaded = true; CreateOrbit(); UpdateMotion(); };
        GlowFrame.SizeChanged += (_, _) => { CreateOrbit(); UpdateOutlineSize(); UpdateMotion(); };
        Unloaded += (_, _) => { _loaded = false; UpdateMotion(); };
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateMotion());
        EffectiveViewportChanged += (_, e) =>
        {
            var viewport = e.EffectiveViewport;
            bool visible = viewport.Width > 0 && viewport.Height > 0 && viewport.X < ActualWidth && viewport.Y < ActualHeight &&
                viewport.X + viewport.Width > 0 && viewport.Y + viewport.Height > 0;
            if (_inViewport == visible) return;
            _inViewport = visible;
            UpdateMotion();
        };
    }

    private void CreateOrbit()
    {
        if (_orbit != null || GlowFrame.ActualWidth <= 4 || GlowFrame.ActualHeight <= 4) return;
        var compositor = ElementCompositionPreview.GetElementVisual(GlowFrame).Compositor;
        _orbit = compositor.CreateLinearGradientBrush();
        _orbit.StartPoint = Vector2.Zero; _orbit.EndPoint = Vector2.One;
        _orbit.CenterPoint = new Vector2(.5f);
        _orbit.ColorStops.Add(compositor.CreateColorGradientStop(0, Windows.UI.Color.FromArgb(255, 94, 242, 207)));
        _orbit.ColorStops.Add(compositor.CreateColorGradientStop(.35f, Windows.UI.Color.FromArgb(255, 79, 216, 255)));
        _orbit.ColorStops.Add(compositor.CreateColorGradientStop(.55f, Windows.UI.Color.FromArgb(0, 79, 216, 255)));
        _orbit.ColorStops.Add(compositor.CreateColorGradientStop(.8f, Windows.UI.Color.FromArgb(255, 139, 123, 255)));
        var geometry = _outlineGeometry = compositor.CreateRoundedRectangleGeometry();
        geometry.CornerRadius = new Vector2(17);
        var outline = compositor.CreateSpriteShape(geometry);
        outline.Offset = new Vector2(2); outline.StrokeThickness = 2.5f; outline.StrokeBrush = _orbit;
        var visual = _outlineVisual = compositor.CreateShapeVisual();
        UpdateOutlineSize();
        visual.Shapes.Add(outline);
        GlowFrame.BorderBrush = null;
        ElementCompositionPreview.SetElementChildVisual(GlowFrame, visual);
    }

    private void UpdateOutlineSize()
    {
        if (_outlineGeometry is null || _outlineVisual is null) return;
        var size = new Vector2((float)GlowFrame.ActualWidth, (float)GlowFrame.ActualHeight);
        _outlineVisual.Size = size;
        _outlineGeometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(4));
    }

    private void UpdateMotion()
    {
        if (GlowFrame is null) return;
        bool animate = _loaded && _inViewport && Visibility == Visibility.Visible && IsPlaying && _uiSettings.AnimationsEnabled;
        StateText.Text = IsPlaying ? "LIVE" : "PAUSED";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, IsPlaying ? "Active wallpaper playing" : "Active wallpaper paused");
        var frame = ElementCompositionPreview.GetElementVisual(GlowFrame);
        frame.StopAnimation("Opacity");
        _orbit?.StopAnimation("RotationAngleInDegrees");
        frame.Opacity = .7f;
        if (animate)
        {
            var pulse = frame.Compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0, .4f); pulse.InsertKeyFrame(.5f, 1); pulse.InsertKeyFrame(1, .4f);
            pulse.Duration = TimeSpan.FromSeconds(2.8);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            frame.StartAnimation("Opacity", pulse);
            var orbit = frame.Compositor.CreateScalarKeyFrameAnimation();
            orbit.InsertKeyFrame(0, 0); orbit.InsertKeyFrame(1, 360);
            orbit.Duration = TimeSpan.FromSeconds(5);
            orbit.IterationBehavior = AnimationIterationBehavior.Forever;
            _orbit?.StartAnimation("RotationAngleInDegrees", orbit);
        }
        int index = 0;
        foreach (var bar in new[] { BarOne, BarTwo, BarThree })
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3((float)bar.Width / 2, (float)bar.Height, 0);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
            if (!animate) continue;
            var wave = visual.Compositor.CreateScalarKeyFrameAnimation();
            wave.InsertKeyFrame(0, .3f); wave.InsertKeyFrame(.35f, 1); wave.InsertKeyFrame(.7f, .5f); wave.InsertKeyFrame(1, .3f);
            wave.Duration = TimeSpan.FromMilliseconds(950 + index++ * 210);
            wave.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale.Y", wave);
        }
    }
}
