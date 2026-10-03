using Xunit;
using Lumen.Application.Services;
using Lumen.Core.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Core.Tests.Services;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadSettingsAsync_MissingFile_UsesDefaults()
    {
        string root = CreateTempDirectory();
        try
        {
            var service = CreateService(root);
            await service.LoadSettingsAsync();

            Assert.Equal("Guest", service.CurrentSettings.DisplayName);
            Assert.True(service.CurrentSettings.StartWithWindows);
            Assert.True(service.CurrentSettings.StartMinimizedToTray);
            Assert.Equal(60, service.CurrentSettings.GlobalTargetFps);
            Assert.Equal(PlaybackScalingMode.Fill, service.CurrentSettings.DefaultScalingMode);
            Assert.Equal(1500, service.CurrentSettings.Performance.HeartbeatIntervalMs);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsNestedPerformanceSettings()
    {
        string root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var service = new SettingsService(path, NullLogger<SettingsService>.Instance);

            await service.UpdateSettingsAsync(settings =>
            {
                settings.DisplayName = "Amir";
                settings.Theme = "Dark";
                settings.StartWithWindows = false;
                settings.GlobalTargetFps = 120;
                settings.DefaultScalingMode = PlaybackScalingMode.Fit;
                settings.Performance.PauseOnFullscreen = false;
                settings.Performance.HeartbeatIntervalMs = 750;
            });

            var reloaded = CreateService(root);
            await reloaded.LoadSettingsAsync();

            Assert.Equal("Amir", reloaded.CurrentSettings.DisplayName);
            Assert.Equal("Dark", reloaded.CurrentSettings.Theme);
            Assert.False(reloaded.CurrentSettings.StartWithWindows);
            Assert.Equal(120, reloaded.CurrentSettings.GlobalTargetFps);
            Assert.Equal(PlaybackScalingMode.Fit, reloaded.CurrentSettings.DefaultScalingMode);
            Assert.False(reloaded.CurrentSettings.Performance.PauseOnFullscreen);
            Assert.Equal(750, reloaded.CurrentSettings.Performance.HeartbeatIntervalMs);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SettingsChanged_RaisedAfterSaveAndLoad()
    {
        string root = CreateTempDirectory();
        try
        {
            var service = CreateService(root);
            int changedCount = 0;
            service.SettingsChanged += (_, _) => changedCount++;

            await service.LoadSettingsAsync();
            await service.UpdateSettingsAsync(settings => settings.DisplayName = "Changed");

            Assert.Equal(2, changedCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CorruptSettingsFile_FallsBackToDefaults()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "settings.json");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, "{ not valid json");

            var service = new SettingsService(path, NullLogger<SettingsService>.Instance);
            await service.LoadSettingsAsync();

            Assert.Equal("Guest", service.CurrentSettings.DisplayName);
            Assert.Equal(60, service.CurrentSettings.GlobalTargetFps);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static SettingsService CreateService(string root)
        => new(Path.Combine(root, "settings.json"), NullLogger<SettingsService>.Instance);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "LumenSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test isolation.
        }
    }
}
