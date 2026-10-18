using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.ViewModels;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public sealed class AutomationRegressionTests : IDisposable
{
    private readonly string _file = Path.GetTempFileName();
    private readonly ApplicationSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<ILibraryService> _library = new();
    private readonly Mock<IStorageManager> _storage = new();
    private readonly Mock<IWallpaperEngineOrchestrator> _engine = new();
    private readonly Mock<IForegroundApplicationMonitor> _monitor = new();
    private readonly Dictionary<Guid, Wallpaper> _wallpapers = [];
    private readonly List<(string Monitor, Guid Wallpaper)> _applied = [];
    private readonly WallpaperScheduleService _schedule;
    private readonly ApplicationWallpaperService _apps;
    private const string Primary = WallpaperSchedule.PrimaryMonitorId;

    public AutomationRegressionTests()
    {
        _settingsService.SetupGet(x => x.CurrentSettings).Returns(_settings);
        _library.Setup(x => x.GetWallpaperByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => Task.FromResult(_wallpapers.GetValueOrDefault(id)));
        _storage.Setup(x => x.ResolveAbsolutePath(It.IsAny<string>())).Returns((string s) => s);
        _engine.Setup(x => x.ApplyWallpaperAsync(It.IsAny<string>(), It.IsAny<Wallpaper>(), It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
            .Returns((string monitor, Wallpaper wallpaper, WallpaperSettings? _, CancellationToken _) => { _applied.Add((monitor, wallpaper.Id)); return Task.CompletedTask; });
        _schedule = new(_settingsService.Object, _library.Object, _storage.Object, _engine.Object, NullLogger<WallpaperScheduleService>.Instance);
        _apps = new(_monitor.Object, _settingsService.Object, _schedule, NullLogger<ApplicationWallpaperService>.Instance);
    }

    private Wallpaper AddWallpaper()
    {
        var wallpaper = new Wallpaper { Title = "Test", FilePath = _file, ContentHash = Guid.NewGuid().ToString() };
        _wallpapers[wallpaper.Id] = wallpaper;
        return wallpaper;
    }

    [Fact]
    public async Task ApplicationOverrideWinsOverScheduleAndRestoresCurrentPlan()
    {
        var scheduled = AddWallpaper(); var app = AddWallpaper(); var original = AddWallpaper();
        _settings.MonitorWallpaperAssignments[Primary] = original.Id;
        _settings.Schedule = new() { Enabled = true, Entries = [new() { WallpaperId = scheduled.Id, UseTimeWindow = true, StartMinutes = 0, EndMinutes = 0 }] };
        _settings.ApplicationWallpaperRules = [new() { ProcessName = "code", WallpaperId = app.Id }];
        await _schedule.ApplyAsync();
        await _apps.HandleAsync("code");
        await _schedule.ApplyAsync();
        Assert.Equal(app.Id, _applied.Last().Wallpaper);
        await _apps.HandleAsync("other");
        Assert.Equal(scheduled.Id, _applied.Last().Wallpaper);
        Assert.Equal(original.Id, _settings.MonitorWallpaperAssignments[Primary]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovedSecondaryMonitorRulesRestoreItsBase(bool scheduled)
    {
        var temporary = AddWallpaper(); var original = AddWallpaper();
        _settings.MonitorWallpaperAssignments["DISPLAY2"] = original.Id;
        if (scheduled)
        {
            _settings.Schedule = new() { Enabled = true, Entries = [new() { WallpaperId = temporary.Id, MonitorId = "DISPLAY2", UseTimeWindow = true, StartMinutes = 0, EndMinutes = 0 }] };
            await _schedule.ApplyAsync();
            _settings.Schedule.Entries.Clear();
            await _schedule.ApplyAsync();
        }
        else
        {
            _settings.ApplicationWallpaperRules = [new() { ProcessName = "code", WallpaperId = temporary.Id, MonitorId = "DISPLAY2" }, new() { ProcessName = "chrome", WallpaperId = temporary.Id }];
            await _apps.HandleAsync("code");
            _settings.ApplicationWallpaperRules.RemoveAt(0);
            await _apps.HandleAsync("other");
        }
        Assert.Equal(("DISPLAY2", original.Id), _applied.Last());
    }

    [Fact]
    public async Task EndingOverrideWithoutSavedWallpaperStopsTheMonitor()
    {
        _settings.ApplicationWallpaperRules = [new() { ProcessName = "code", WallpaperId = AddWallpaper().Id }];
        await _apps.HandleAsync("code"); await _apps.HandleAsync("other");
        _engine.Verify(x => x.StopAsync(Primary, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FocusChangeDuringLoadIsProcessedAfterItFinishes()
    {
        var code = AddWallpaper(); var chrome = AddWallpaper();
        _settings.ApplicationWallpaperRules = [new() { ProcessName = "code", WallpaperId = code.Id }, new() { ProcessName = "chrome", WallpaperId = chrome.Id }];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Setup(x => x.ApplyWallpaperAsync(Primary, code, It.IsAny<WallpaperSettings>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { entered.SetResult(); await release.Task; });
        var first = _apps.HandleAsync("code"); await entered.Task;
        var second = _apps.HandleAsync("chrome");
        release.SetResult(); await Task.WhenAll(first, second);
        Assert.Equal("chrome", _apps.ActiveApplication);
        Assert.Equal(chrome.Id, _applied.Last().Wallpaper);
    }

    [Fact]
    public async Task StartupEvaluatesAlreadyFocusedApplication()
    {
        var wallpaper = AddWallpaper();
        _monitor.SetupGet(x => x.CurrentProcessName).Returns("code");
        _settings.ApplicationWallpaperRules = [new() { ProcessName = "code", WallpaperId = wallpaper.Id }];
        _apps.Start();
        await _apps.StopAsync();
        Assert.Contains((Primary, wallpaper.Id), _applied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnrelatedSettingsSavePreservesAutomationDraft(bool schedule)
    {
        string path = _file + ".json";
        try
        {
            var settings = new SettingsService(path, NullLogger<SettingsService>.Instance);
            var vm = new SettingsViewModel(settings, _engine.Object, NullLogger<SettingsViewModel>.Instance);
            if (schedule) Assert.True(vm.TryAddScheduleEntry(AddWallpaper().Id, "07:00-19:00", out _));
            else Assert.True(vm.TryAddApplicationRule("code", AddWallpaper().Id, out _));
            await settings.UpdateSettingsAsync(s => s.DefaultVolume = .5f);
            Assert.Equal(1, schedule ? vm.ScheduleEntries.Count : vm.ApplicationRules.Count);
            await vm.SaveAsync();
            Assert.Equal(1, schedule ? settings.CurrentSettings.Schedule.Entries.Count : settings.CurrentSettings.ApplicationWallpaperRules.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RuntimeSwitchUpdatesActiveWallpaperWithoutSavingTemporaryAssignment()
    {
        var original = AddWallpaper(); var temporary = AddWallpaper();
        _settings.MonitorWallpaperAssignments[Primary] = original.Id;
        await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => Mock.Of<IWallpaperRenderer>());
        var saved = new SavedWallpaperService(engine, _settingsService.Object, _library.Object, _storage.Object, NullLogger<SavedWallpaperService>.Instance);
        int events = 0; saved.ActiveWallpaperChanged += (_, _) => events++;
        await engine.ApplyWallpaperAsync(Primary, temporary);
        Assert.Equal(temporary.Id, saved.ActiveWallpaper?.Id);
        Assert.Equal(original.Id, _settings.MonitorWallpaperAssignments[Primary]);
        await engine.StopAsync(Primary);
        Assert.Null(saved.ActiveWallpaper);
        Assert.Equal(2, events);
    }

    [Fact]
    public async Task RendererFactoryReceivesTheRequestedMonitor()
    {
        var seen = new List<string>();
        await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance,
            monitorRendererFactory: (monitor, _) => { seen.Add(monitor); return Mock.Of<IWallpaperRenderer>(); });
        await engine.ApplyWallpaperAsync("DISPLAY2", AddWallpaper());
        Assert.Equal(["DISPLAY2"], seen);
    }

    public void Dispose() => File.Delete(_file);
}
