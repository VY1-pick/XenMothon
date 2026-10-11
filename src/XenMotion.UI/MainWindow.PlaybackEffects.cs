using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace XenMotion.UI;

public sealed partial class MainWindow
{
    private readonly UISettings _motionSettings = new();

    private void OnPlaybackDockSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePlaybackDockSpace();

    private void UpdatePlaybackDockSpace()
    {
        // Keep the gallery behind the floating dock; trailing scroll space lets the last cards clear it.
        BodyGrid.Margin = new Thickness(0);
        double bottomSpace = _viewModel?.HasActiveWallpaper == true ? Math.Max(62, NowPlayingPanel.ActualHeight) + 38 : 16;
        ExploreGridView.Padding = new Thickness(0, 4, 0, bottomSpace);
        WallpaperGridView.Padding = new Thickness(0, 4, 0, bottomSpace);
    }

    private void ShowVolumeFeedback(double percentage)
    {
        if (!_motionSettings.AnimationsEnabled) return;
        float peak = (float)(.2 + .8 * percentage / 100);
        int index = 0;
        foreach (var bar in NowPlayingVolumePulse.Children.OfType<Border>())
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3(1, (float)bar.Height, 0);
            var wave = visual.Compositor.CreateScalarKeyFrameAnimation();
            wave.InsertKeyFrame(0, .25f);
            wave.InsertKeyFrame(.25f, peak);
            wave.InsertKeyFrame(.6f, Math.Max(.18f, peak * .4f));
            wave.InsertKeyFrame(1, .25f);
            wave.Duration = TimeSpan.FromMilliseconds(480 + index++ * 55);
            visual.StartAnimation("Scale.Y", wave);
        }
        var number = ElementCompositionPreview.GetElementVisual(NowPlayingVolumeText);
        number.CenterPoint = new Vector3((float)NowPlayingVolumeText.ActualWidth / 2, (float)NowPlayingVolumeText.ActualHeight / 2, 0);
        var pop = number.Compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0, new Vector3(1.18f, 1.18f, 1));
        pop.InsertKeyFrame(1, Vector3.One);
        pop.Duration = TimeSpan.FromMilliseconds(260);
        number.StartAnimation("Scale", pop);
        var halo = ElementCompositionPreview.GetElementVisual(NowPlayingVolumeHalo);
        var flash = halo.Compositor.CreateScalarKeyFrameAnimation();
        flash.InsertKeyFrame(0, 1); flash.InsertKeyFrame(.5f, .45f); flash.InsertKeyFrame(1, 1);
        flash.Duration = TimeSpan.FromMilliseconds(450);
        halo.StartAnimation("Opacity", flash);
    }
}
