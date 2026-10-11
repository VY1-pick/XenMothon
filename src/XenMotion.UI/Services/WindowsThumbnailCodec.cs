using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;

namespace XenMotion.UI.Services;

public sealed class WindowsThumbnailCodec : IThumbnailCodec
{
    private const uint MaximumSize = 384;

    public bool IsValid(string path) => Task.Run(async () =>
    {
        try
        {
            if (new FileInfo(path).Length is 0 or > 1_000_000) return false;
            // Decode a memory copy so Windows does not keep the cached file mapped during replacement.
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync((await File.ReadAllBytesAsync(path)).AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            // Old gradient BMPs and full-size image copies must be rebuilt too.
            if (decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId ||
                decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
                Math.Max(decoder.PixelWidth, decoder.PixelHeight) > MaximumSize) return false;
            await decoder.GetPixelDataAsync();
            return true;
        }
        catch { return false; }
    }).GetAwaiter().GetResult();

    public async Task GenerateAsync(string source, string destination, WallpaperType type, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(source).AsTask(ct);
        using var input = type == WallpaperType.Video
            ? await ExtractFrameAsync(file, ct)
            : await file.OpenReadAsync().AsTask(ct);
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(ct);
        double scale = Math.Min(1, (double)MaximumSize / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        uint width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        uint height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct);
        bool rotated = decoder.OrientedPixelWidth != decoder.PixelWidth;
        using var output = await (await StorageFile.GetFileFromPathAsync(destination).AsTask(ct)).OpenAsync(FileAccessMode.ReadWrite).AsTask(ct);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output).AsTask(ct);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            rotated ? height : width, rotated ? width : height, 96, 96, pixels.DetachPixelData());
        await encoder.FlushAsync().AsTask(ct);
    }

    private static async Task<IRandomAccessStream> ExtractFrameAsync(StorageFile file, CancellationToken ct)
    {
        var clip = await MediaClip.CreateFromFileAsync(file).AsTask(ct);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var position = TimeSpan.FromSeconds(Math.Min(1, clip.OriginalDuration.TotalSeconds / 2));
        return await composition.GetThumbnailAsync(position, (int)MaximumSize, 0, VideoFramePrecision.NearestFrame).AsTask(ct);
    }
}
