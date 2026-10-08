using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering.Direct3D;
using XenMotion.UI.Rendering.Video;
using Xunit;
using Xunit.Abstractions;

namespace XenMotion.Core.Tests.Services;

public sealed class WallpaperEditingTests(ITestOutputHelper output)
{
    [Fact]
    public void GlobalEditsApplyToNewWallpapersAndPreserveCustomEdits()
    {
        var id = Guid.NewGuid();
        var settings = new ApplicationSettings { GlobalEditsEnabled = true, GlobalEdits = new WallpaperEdits { Red = .4f } };
        settings.WallpaperEdits[id] = new WallpaperEdits { Red = .8f };
        var playback = settings.CreatePlaybackSettings();
        settings.ApplyEdits(id, playback); Assert.Equal(.4f, playback.Red);
        settings.ApplyEdits(Guid.NewGuid(), playback); Assert.Equal(.4f, playback.Red);
        settings.CustomEditWallpapers.Add(id);
        settings.ApplyEdits(id, playback); Assert.Equal(.8f, playback.Red);
        var persisted = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings))!;
        persisted.CustomEditWallpapers.Remove(id); persisted.ApplyEdits(id, playback); Assert.Equal(.4f, playback.Red);
        persisted.GlobalEditsEnabled = false; persisted.ApplyEdits(id, playback); Assert.Equal(.8f, playback.Red);
        Assert.Equal(.8f, persisted.WallpaperEdits[id].Red);
    }

    [Fact]
    public void PresetsRoundTripAndCloneWithoutChangingOriginal()
    {
        var settings = new ApplicationSettings();
        settings.WallpaperEditPresets["Night"] = new WallpaperEdits { Brightness = .7f, FlipHorizontal = true };
        var restored = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings))!;
        var copy = restored.WallpaperEditPresets["Night"].Clone();
        copy.Brightness = 1.2f;
        Assert.Equal(.7f, restored.WallpaperEditPresets["Night"].Brightness);
        var playback = new WallpaperSettings();
        copy.ApplyTo(playback);
        Assert.True(playback.FlipHorizontal);
        Assert.True(playback.Clone().FlipHorizontal);
    }

    [Fact]
    public void NativeFlipMirrorsActualGpuPixels()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 8, 8));
        Assert.NotNull(surface);
        using var chain = new DesktopSwapChain(surface.Handle, 8, 8);
        chain.SetFiltering(false);
        var pixels = GCHandle.Alloc(Enumerable.Range(0, 64).Select(i => i % 8 < 4 ? 0xff0000 : 0x0000ff).ToArray(), GCHandleType.Pinned);
        try
        {
            chain.Render(pixels.AddrOfPinnedObject(), 8, 8, new ViewportRectangle(0, 0, 8, 8));
            Assert.Equal(0x0000ffu, chain.ReadPixel(1, 4, 8, 8));
            chain.SetVisualAdjustments(1, 1, 1, flipHorizontal: true);
            chain.Render(pixels.AddrOfPinnedObject(), 8, 8, new ViewportRectangle(0, 0, 8, 8));
            Assert.Equal(0xff0000u, chain.ReadPixel(1, 4, 8, 8));
        }
        finally { pixels.Free(); }
    }
    [Fact]
    public async Task EditsSurvivePersistenceAndGlobalSettingsChangesWithoutAffectingOtherWallpapers()
    {
        var wallpaper = new Wallpaper { Title = "Edited", FilePath = "video.mp4", ContentHash = "test", IsSubscribed = true };
        var config = new ApplicationSettings();
        config.WallpaperEdits[wallpaper.Id] = new WallpaperEdits { Contrast = 1.3f, Red = .6f, Opacity = .4f,
            PlaybackSpeed = 1.5f, Loop = false, Volume = .7f, IsMuted = false, ScalingMode = PlaybackScalingMode.Fit, Sharpness = .1f };
        config = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(config))!;
        var settings = new Mock<ISettingsService>(); settings.SetupGet(x => x.CurrentSettings).Returns(config);
        var renderer = new Mock<IWallpaperRenderer>();
        await using var engine = new WallpaperEngineOrchestrator(NullLogger<WallpaperEngineOrchestrator>.Instance, _ => renderer.Object, settings.Object);
        var requested = new WallpaperSettings { TargetFps = 60 };
        await engine.ApplyWallpaperAsync("primary", wallpaper, requested);
        await engine.ApplyPlaybackSettingsAllAsync(new WallpaperSettings { TargetFps = 24 });
        renderer.Verify(x => x.LoadAsync("video.mp4", It.Is<WallpaperSettings>(s => s.Red == .6f && s.Contrast == 1.3f && s.Opacity == .4f && s.TargetFps == 24 && s.PlaybackSpeed == 1.5f && !s.Loop && s.Volume == .7f && !s.IsMuted && s.ScalingMode == PlaybackScalingMode.Fit && s.Sharpness == .1f), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, requested.Red);
        var cloned = new WallpaperSettings(); config.WallpaperEdits[wallpaper.Id].ApplyTo(cloned);
        Assert.Equal(.4f, cloned.Clone().Opacity);
        await engine.ApplyWallpaperAsync("primary", new Wallpaper { Title = "Other", FilePath = "other.mp4", ContentHash = "other" }, requested);
        renderer.Verify(x => x.LoadAsync("other.mp4", It.Is<WallpaperSettings>(s => s.Red == 1 && s.Opacity == 1), It.IsAny<CancellationToken>()), Times.Once);
        new WallpaperEdits { Red = float.NaN, Contrast = 10, Opacity = -2 }.ApplyTo(cloned);
        Assert.Equal(1, cloned.Red); Assert.Equal(2, cloned.Contrast); Assert.Equal(0, cloned.Opacity);
        var legacy = JsonSerializer.Deserialize<WallpaperEdits>("{\"Contrast\":1.2}")!;
        var global = new WallpaperSettings { PlaybackSpeed = .75f, Loop = false, Volume = .3f };
        legacy.ApplyTo(global);
        Assert.Equal(.75f, global.PlaybackSpeed); Assert.False(global.Loop); Assert.Equal(.3f, global.Volume);
        new WallpaperEdits { PlaybackSpeed = -1, Volume = float.NaN, Sharpness = 2, ScalingMode = (PlaybackScalingMode)100 }.ApplyTo(global);
        Assert.Equal(.25f, global.PlaybackSpeed); Assert.Equal(1, global.Volume); Assert.Equal(.3f, global.Sharpness);
        Assert.Equal(PlaybackScalingMode.Fill, global.ScalingMode);
    }

    [Fact]
    public void AccentIgnoresBlackBarsAndChoosesDominantColor()
    {
        var pixels = new List<byte>();
        for (int i = 0; i < 100; i++) pixels.AddRange(new byte[] { 0, 0, 0, 255 });
        for (int i = 0; i < 40; i++) pixels.AddRange(new byte[] { 50, 180, 80, 255 });
        for (int i = 0; i < 5; i++) pixels.AddRange(new byte[] { 20, 20, 230, 255 });
        var accent = WallpaperAccent.Select(pixels.ToArray());
        Assert.InRange(accent.Red, 79, 80); Assert.InRange(accent.Green, 179, 180);
        Assert.Equal((96, 112, 120), ((int)WallpaperAccent.Select(new byte[4]).Red, (int)WallpaperAccent.Select(new byte[4]).Green, (int)WallpaperAccent.Select(new byte[4]).Blue));
    }

    [Fact]
    public async Task NativeTintOpacityAndClockWork()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        using var surface = DesktopSurface.TryCreate(GetDesktopWindow(), new MonitorBounds(-16000, -16000, 8, 8));
        Assert.NotNull(surface);
        surface.SetOpacity(.4f);
        Assert.True(GetLayeredWindowAttributes(surface.Handle, out _, out byte alpha, out _)); Assert.Equal(102, alpha);
        using var chain = new DesktopSwapChain(surface.Handle, 8, 8);
        var pixels = GCHandle.Alloc(Enumerable.Repeat(0x808080, 64).ToArray(), GCHandleType.Pinned);
        try
        {
            chain.SetVisualAdjustments(1, 1, 1, 0, .5f, 1, 0);
            chain.Render(pixels.AddrOfPinnedObject(), 8, 8, new ViewportRectangle(0, 0, 8, 8));
            Assert.Equal(0x008040u, chain.ReadPixel(4, 4, 8, 8));
        }
        finally { pixels.Free(); }
        var clock = new VideoPlaybackClock();
        var lateness = new List<double>();
        clock.Reset();
        for (int i = 1; i <= 60; i++) lateness.Add(await clock.WaitUntilAsync(i * 1000d / 60, default) - i * 1000d / 60);
        output.WriteLine($"High-resolution clock: p90 lateness {lateness.Order().ElementAt(53):F2}ms, max {lateness.Max():F2}ms");
        Assert.All(lateness, x => Assert.True(x >= 0));
        using var cancel = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clock.WaitUntilAsync(60000, cancel.Token));
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint color, out byte alpha, out uint flags);
}
