using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Models;
using XenMotion.UI.ViewModels;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class QualityPresetTests
{
    [Theory]
    [InlineData(1280, 720, QualityPreset.Economy)]
    [InlineData(1366, 768, QualityPreset.Economy)]
    [InlineData(1920, 1080, QualityPreset.Balanced)]
    [InlineData(2560, 1440, QualityPreset.High)]
    [InlineData(3840, 2160, QualityPreset.Ultra)]
    public void FirstRunUsesNearestDisplayMode(int width, int height, QualityPreset expected)
    {
        var preset = QualityPresets.ForDisplayPixels((long)width * height);
        Assert.Equal(expected, preset);
        var settings = new ApplicationSettings(); QualityPresets.Apply(settings, preset);
        Assert.False(settings.AutoTunePlayback); Assert.Equal(1, settings.RenderScale);
        Assert.Contains(settings.VideoHeightLimit, new[] { 720, 1080, 1440, 2160 });
    }

    [Fact]
    public void EconomyMigratesOldCoverageButPreservesCustomPolicy()
    {
        var settings = new ApplicationSettings { QualityPreset = QualityPreset.Economy };
        settings.Performance.CoveredThresholdPercentage = 1;
        settings.Normalize();
        Assert.Equal(85, settings.Performance.CoveredThresholdPercentage);
        settings.QualityPreset = QualityPreset.Custom;
        settings.Performance.CoveredThresholdPercentage = 1;
        settings.Normalize();
        Assert.Equal(1, settings.Performance.CoveredThresholdPercentage);
        Assert.Equal(XenMotion.Core.Enums.PerformanceState.PartiallyVisible,
            DesktopVisibilityEvaluator.Evaluate(new(0,0,100,100), [new(new(0,0,20,20), false)],
                new PerformanceSettings { CoveredThresholdPercentage = 85 }, false));
    }

    [Theory]
    [InlineData("game.exe, obs64.exe", "GAME", true)]
    [InlineData("game.exe, obs64.exe", "obs64", true)]
    [InlineData("game.exe, obs64.exe", "game-helper", false)]
    [InlineData("", "game", false)]
    public void FocusRulesMatchExactProcessNames(string rules, string process, bool expected)
        => Assert.Equal(expected, PlaybackApplicationRules.Matches(rules, process));

    [Theory]
    [InlineData(4096, 2, 0, QualityPreset.Economy)]
    [InlineData(8192, 4, 0, QualityPreset.Balanced)]
    [InlineData(12288, 8, 4096, QualityPreset.Balanced)]
    [InlineData(16384, 8, 8192, QualityPreset.Balanced)]
    public void RecommendsConservativeHardwareBudget(int ram, int cpu, int gpu, QualityPreset expected)
        => Assert.Equal(expected, QualityPresets.Recommend(new(ram, cpu, gpu, 1920 * 1080, "Test")));

    [Fact]
    public void PresetsPreserveCreativeSettingsAndManualChangesBecomeCustom()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.CurrentSettings).Returns(new ApplicationSettings { DefaultBrightness = .7f });
        var vm = new SettingsViewModel(settings.Object, Mock.Of<IWallpaperEngineOrchestrator>(), NullLogger<SettingsViewModel>.Instance);
        vm.ApplyQualityPreset(QualityPreset.Economy);
        Assert.Equal(.7f, vm.DefaultBrightness);
        Assert.Equal(15, vm.GlobalTargetFps);
        Assert.Equal(720, vm.VideoHeightLimit);
        Assert.Equal(1f, vm.RenderScale);
        Assert.Equal(85, vm.CoveredThresholdPercentage);
        Assert.False(vm.PauseOnlyWhenFullyCovered);
        Assert.Equal(QualityPreset.Economy, vm.QualityPreset);
        vm.RenderScale = .9f;
        Assert.Equal(QualityPreset.Custom, vm.QualityPreset);
        vm.ApplyQualityPreset(QualityPreset.Ultra);
        Assert.Equal(2160, vm.VideoHeightLimit);
        Assert.Equal(1, vm.RenderScale);
        Assert.True(vm.HighQualityFiltering);
    }
}
