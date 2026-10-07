using System.Text;
using Microsoft.Extensions.Logging;
using XenMotion.UI.Rendering.Media;
using Xunit;

namespace XenMotion.Core.Tests.Rendering;

public class NativeAudioPlaybackTests
{
    [Fact]
    public async Task PcmAudio_QueuesToOutput_Pauses_Rewinds_AndReopens()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-audio-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, CreateWav());
            await using var player = new WallpaperAudioPlayer(new ConsoleLogger());
            await player.OpenAsync(path, 0); // Silent native output test.
            Assert.True(player.HasAudio);
            player.Play();
            await Task.Delay(180);
            player.Pause();
            long count = player.QueuedBytes;
            Assert.True(count > 0);
            await Task.Delay(180);
            Assert.Equal(count, player.QueuedBytes);
            player.SetVolume(0);
            player.Rewind();
            player.Play();
            await Task.Delay(180);
            Assert.True(player.QueuedBytes > count);
            player.Pause();
            await player.OpenAsync(path, 0);
            Assert.True(player.HasAudio);
            Assert.Equal(0, player.QueuedBytes);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task SilentVideo_DoesNotCreateAnAudioOutput()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-silent-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, NativeMediaFoundationTests.CreateAvi());
            await using var player = new WallpaperAudioPlayer(new ConsoleLogger());
            await player.OpenAsync(path, 0);
            Assert.False(player.HasAudio);
            player.Play(); player.Pause(); player.Rewind();
            Assert.Equal(0, player.QueuedBytes);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task VideoWithAudio_DecodesBothStreams_AndSuspendsBoth()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        string path = Path.Combine(Path.GetTempPath(), $"xenmotion-av-{Guid.NewGuid():N}.avi");
        try
        {
            File.WriteAllBytes(path, NativeMediaFoundationTests.CreateAvi(includeAudio: true));
            await using var renderer = new XenMotion.UI.Rendering.VideoWallpaperRenderer(Microsoft.Extensions.Logging.Abstractions.NullLogger<XenMotion.UI.Rendering.VideoWallpaperRenderer>.Instance);
            await renderer.InitializeAsync(IntPtr.Zero, new XenMotion.Core.Models.MonitorBounds(0, 0, 64, 36));
            await renderer.LoadAsync(path, new XenMotion.Core.Models.WallpaperSettings { IsMuted = true, Loop = true });
            Assert.True(renderer.HasAudio);
            await renderer.PlayAsync();
            await Task.Delay(250);
            await renderer.PauseAsync();
            await Task.Delay(70);
            long audio = renderer.QueuedAudioBytes, video = renderer.DecodedFrameCount;
            Assert.True(audio > 0); Assert.True(video > 0);
            await Task.Delay(180);
            Assert.Equal(audio, renderer.QueuedAudioBytes);
            Assert.Equal(video, renderer.DecodedFrameCount);
            await renderer.SetVolumeAsync(0);
            await renderer.PlayAsync();
            await Task.Delay(1150); // Cross the loop boundary.
            Assert.True(renderer.QueuedAudioBytes > audio);
            Assert.True(renderer.DecodedFrameCount > video);
            Assert.Equal(XenMotion.Core.Enums.WallpaperState.Playing, renderer.CurrentState);
            await renderer.StopAsync();
        }
        finally { File.Delete(path); }
    }
    private static byte[] CreateWav()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        const int bytes = 48000 * 2 * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(48000); w.Write(96000); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        for (int i = 0; i < bytes / 2; i++) w.Write((short)(1000 * Math.Sin(i * 2 * Math.PI * 440 / 48000)));
        return stream.ToArray();
    }
    private sealed class ConsoleLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Console.WriteLine(formatter(state, exception) + " " + exception);
    }
}
