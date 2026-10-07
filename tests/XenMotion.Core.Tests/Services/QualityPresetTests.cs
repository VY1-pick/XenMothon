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
    [InlineData(4096, 2, 0, QualityPreset.Economy)]
    [InlineData(8192, 4, 0, QualityPreset.Balanced)]
    [InlineData(12288, 8, 4096, QualityPreset.High)]
    [InlineData(16384, 8, 8192, QualityPreset.Ultra)]
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
        Assert.Equal(.5f, vm.RenderScale);
        Assert.Equal(1, vm.CoveredThresholdPercentage);
        Assert.False(vm.PauseOnlyWhenFullyCovered);
        Assert.Equal(QualityPreset.Economy, vm.QualityPreset);
        vm.RenderScale = .9f;
        Assert.Equal(QualityPreset.Custom, vm.QualityPreset);
        vm.ApplyQualityPreset(QualityPreset.Ultra);
        Assert.Equal(0, vm.VideoHeightLimit);
        Assert.Equal(1, vm.RenderScale);
        Assert.True(vm.HighQualityFiltering);
    }
}
