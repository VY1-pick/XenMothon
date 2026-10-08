using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace XenMotion.UI.Services;

internal static class MotionPreviewEncoder
{
    public static async Task EncodeAsync(string source, string temporary, CancellationToken ct)
    {
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(source)).AsTask(ct);
        ct.ThrowIfCancellationRequested();
        if (clip.OriginalDuration > TimeSpan.FromSeconds(6)) clip.TrimTimeFromEnd = clip.OriginalDuration - TimeSpan.FromSeconds(6);
        var properties = await (await StorageFile.GetFileFromPathAsync(source)).Properties.GetVideoPropertiesAsync().AsTask(ct);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga);
        double ratio = properties.Height == 0 ? 16d / 9 : (double)properties.Width / properties.Height;
        profile.Video.Width = ratio >= 1 ? 480u : (uint)Math.Max(2, Math.Round(480 * ratio / 2) * 2);
        profile.Video.Height = ratio >= 1 ? (uint)Math.Max(2, Math.Round(480 / ratio / 2) * 2) : 480u;
        profile.Video.Bitrate = 400000;
        profile.Video.FrameRate.Numerator = 15; profile.Video.FrameRate.Denominator = 1;
        profile.Audio = null;
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        var target = await StorageFile.GetFileFromPathAsync(temporary);
        var result = await composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile).AsTask(ct);
        if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException($"Preview encoding failed: {result}");
        ct.ThrowIfCancellationRequested();
    }
}
