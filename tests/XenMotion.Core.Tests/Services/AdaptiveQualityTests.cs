using System.Text.Json;
using XenMotion.Core.Models;
using XenMotion.UI.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class AdaptiveQualityTests
{
    [Fact]
    public void CacheAcceptsValidatedProfileAndRejectsContaminationAndLimits()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        var requested = new WallpaperSettings { TargetFps=30, VideoHeightLimit=1080, RenderScale=1, Volume=.4f, Brightness=.7f };
        object Payload(string phase="validate", bool busy=false, int height=1080) => new {
            results = new[] { new { algorithmVersion=AdaptiveQualityService.AlgorithmVersion,
                reasonCodes=busy ? new[] {"CONTAMINATED_BASELINE"} : Array.Empty<string>(),
                selected=new { accepted=true, phase, missing=Array.Empty<string>(), settings=new {
                    videoHeightLimit=height, targetFps=30, renderScale=1, sharpness=0,
                    highQualityFiltering=true, useHardwareDecoding=true } } } } };
        try
        {
            File.WriteAllText(path,JsonSerializer.Serialize(Payload()));
            Assert.True(AdaptiveQualityService.TryReadResult(path,requested,out var chosen));
            Assert.Equal(requested.Volume,chosen.Volume); Assert.Equal(requested.Brightness,chosen.Brightness);
            File.WriteAllText(path,JsonSerializer.Serialize(Payload(phase:"screen")));
            Assert.False(AdaptiveQualityService.TryReadResult(path,requested,out _));
            File.WriteAllText(path,JsonSerializer.Serialize(Payload(busy:true)));
            Assert.False(AdaptiveQualityService.TryReadResult(path,requested,out _));
            File.WriteAllText(path,JsonSerializer.Serialize(Payload(height:2160)));
            Assert.False(AdaptiveQualityService.TryReadResult(path,requested,out _));
            File.WriteAllText(path,"{\"results\":[]}");
            Assert.False(AdaptiveQualityService.TryReadResult(path,requested,out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidPersistedSettingsAreNormalizedBeforeRendererUse()
    {
        var settings = new ApplicationSettings { RenderScale=float.PositiveInfinity,
            GlobalTargetFps=int.MaxValue, VideoHeightLimit=int.MaxValue,
            DefaultPlaybackSpeed=float.NaN, DisplayName=null!, Performance=null!, MonitorWallpaperAssignments=null! };
        settings.Normalize();
        Assert.Equal(1,settings.RenderScale); Assert.Equal(240,settings.GlobalTargetFps);
        Assert.Equal(16384,settings.VideoHeightLimit); Assert.Equal(1,settings.DefaultPlaybackSpeed);
        Assert.Equal("Guest",settings.DisplayName); Assert.NotNull(settings.Performance); Assert.NotNull(settings.MonitorWallpaperAssignments);
    }
}
