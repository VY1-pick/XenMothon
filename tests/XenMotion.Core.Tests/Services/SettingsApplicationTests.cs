using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.ViewModels;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class SettingsApplicationTests
{
    [Fact]
    public async Task SaveReappliesEveryPlaybackValuePreservesPauseAndDoesNotRestartStoppedWallpaper()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path, NullLogger<SettingsService>.Instance);
            var loaded = new List<WallpaperSettings>();
            var renderer = new Mock<IWallpaperRenderer>();
            renderer.Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
                .Callback<string, WallpaperSettings, CancellationToken>((_, value, _) => loaded.Add(value.Clone()))
                .Returns(Task.CompletedTask);
            await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => renderer.Object);
            var wallpaper = new Wallpaper { Title = "Settings", FilePath = "settings.mp4", ContentHash = "settings", Type = WallpaperType.Video };
            await engine.ApplyWallpaperAsync("M1", wallpaper);
            await engine.PauseAsync("M1");
            await engine.ApplyWallpaperAsync("M2", wallpaper);
            await engine.StopAsync("M2");
            var vm = new SettingsViewModel(settings, engine, NullLogger<SettingsViewModel>.Instance)
            {
                GlobalTargetFps = 15, DefaultBrightness = 0.5f, DefaultContrast = 1.2f, DefaultSaturation = 0.2f,
                DefaultScalingMode = PlaybackScalingMode.Fit, DefaultPlaybackSpeed = 2, DefaultLoop = false,
                DefaultVolume = 0.3f, DefaultMuted = false, VideoHeightLimit = 720, RenderScale = .5f,
                HighQualityFiltering = false, UseHardwareDecoding = false, EnableGlassEffects = false, BatteryFps = 20,
                CoveredThresholdPercentage = 1, PauseOnlyWhenFullyCovered = true, AutoTunePlayback = true, DefaultSharpness = .15f
            };
            await vm.SaveAsync();
            var current = loaded.Last();
            Assert.Equal(3, loaded.Count);
            Assert.Equal(15, current.TargetFps);
            Assert.Equal(720, current.VideoHeightLimit);
            Assert.Equal(.5f, current.RenderScale);
            Assert.False(current.HighQualityFiltering);
            Assert.False(current.UseHardwareDecoding);
            Assert.True(current.AutoTunePlayback);
            Assert.Equal(.15f, current.Sharpness);
            Assert.Equal(0.5f, current.Brightness);
            Assert.Equal(1.2f, current.Contrast);
            Assert.Equal(0.2f, current.Saturation);
            Assert.Equal(PlaybackScalingMode.Fit, current.ScalingMode);
            Assert.Equal(2, current.PlaybackSpeed);
            Assert.False(current.Loop);
            Assert.False(current.IsMuted);
            Assert.Equal(0.3f, current.Volume);
            Assert.Equal(WallpaperState.Paused, engine.GetMonitorState("M1"));
            Assert.Equal(WallpaperState.Stopped, engine.GetMonitorState("M2"));
            var restored = new SettingsService(path, NullLogger<SettingsService>.Instance);
            await restored.LoadSettingsAsync();
            Assert.Equal(0.5f, restored.CurrentSettings.DefaultBrightness);
            Assert.Equal(15, restored.CurrentSettings.GlobalTargetFps);
            Assert.Equal(720, restored.CurrentSettings.VideoHeightLimit);
            Assert.Equal(20, restored.CurrentSettings.Performance.BatteryFps);
            Assert.Equal(1, restored.CurrentSettings.Performance.CoveredThresholdPercentage);
            Assert.True(restored.CurrentSettings.Performance.PauseOnlyWhenFullyCovered);
            Assert.True(restored.CurrentSettings.AutoTunePlayback);
            Assert.Equal(.15f, restored.CurrentSettings.DefaultSharpness);
            Assert.False(restored.IsFirstRun);
        }
        finally { File.Delete(path); }
    }
}
