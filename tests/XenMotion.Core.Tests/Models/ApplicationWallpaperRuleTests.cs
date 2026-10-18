using XenMotion.Core.Models;
using Xunit;

namespace XenMotion.Core.Tests.Models;

public class ApplicationWallpaperRuleTests
{
    [Theory]
    [InlineData("code", "code")]
    [InlineData("Code.exe", "Code")]
    [InlineData(@"C:\Program Files\Microsoft VS Code\Code.exe", "Code")]
    [InlineData("  chrome.EXE  ", "chrome")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ProcessNamesAreNormalizedToTheBareExecutableName(string? input, string expected)
    {
        string normalized = ApplicationWallpaperRules.NormalizeProcessName(input);
        Assert.Equal(expected, normalized, ignoreCase: true);
    }

    [Fact]
    public void SelectMatchesTheForegroundProcessIgnoringSuffixCaseAndPath()
    {
        var id = Guid.NewGuid();
        var rules = new List<ApplicationWallpaperRule> { new() { ProcessName = @"C:\Apps\Code.exe", WallpaperId = id } };

        Assert.Equal(id, ApplicationWallpaperRules.Select(rules, "code", WallpaperSchedule.PrimaryMonitorId));
        Assert.Equal(id, ApplicationWallpaperRules.Select(rules, "CODE.EXE", WallpaperSchedule.PrimaryMonitorId));
        Assert.Null(ApplicationWallpaperRules.Select(rules, "chrome", WallpaperSchedule.PrimaryMonitorId));
        Assert.Null(ApplicationWallpaperRules.Select(rules, null, WallpaperSchedule.PrimaryMonitorId));
        Assert.Null(ApplicationWallpaperRules.Select(null, "code", WallpaperSchedule.PrimaryMonitorId));
    }

    [Fact]
    public void RulesAreScopedToTheirMonitor()
    {
        var id = Guid.NewGuid();
        var rules = new List<ApplicationWallpaperRule> { new() { ProcessName = "game", WallpaperId = id, MonitorId = @"\\.\DISPLAY2" } };

        Assert.Equal(id, ApplicationWallpaperRules.Select(rules, "game", @"\\.\DISPLAY2"));
        Assert.Null(ApplicationWallpaperRules.Select(rules, "game", WallpaperSchedule.PrimaryMonitorId));
    }

    [Fact]
    public void NormalizeRulesDropsEmptyEntriesAndDuplicates()
    {
        var id = Guid.NewGuid();
        var rules = new List<ApplicationWallpaperRule>
        {
            new() { ProcessName = "code.exe", WallpaperId = id },
            new() { ProcessName = "Code", WallpaperId = Guid.NewGuid() },
            new() { ProcessName = "  ", WallpaperId = id },
            new() { ProcessName = "chrome", WallpaperId = Guid.Empty }
        };

        ApplicationWallpaperRules.NormalizeRules(rules);

        var rule = Assert.Single(rules);
        Assert.Equal("code", rule.ProcessName);
        Assert.Equal(id, rule.WallpaperId);
    }
}
