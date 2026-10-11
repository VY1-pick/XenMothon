using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using XenMotion.Core.Enums;
using XenMotion.Infrastructure.Persistence.Services;
using XenMotion.Infrastructure.Persistence.Storage;
using XenMotion.UI.Services;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("artifacts", "thumbnail-check", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var storage = new LocalStorageManager(root, NullLogger<LocalStorageManager>.Instance);
var codec = new WindowsThumbnailCodec();
var service = new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance, codec);
string source = Path.Combine(root, "red-source.png");
using (File.Open(source, FileMode.CreateNew)) { }
var file = await StorageFile.GetFileFromPathAsync(source);
using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
{
    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
    byte[] pixels = new byte[1600 * 900 * 4];
    var random = new Random(42);
    for (int i = 0; i < pixels.Length; i += 4)
    {
        pixels[i] = (byte)random.Next(0, 25);
        pixels[i + 1] = (byte)random.Next(0, 25);
        pixels[i + 2] = (byte)random.Next(220, 255);
        pixels[i + 3] = 255;
    }
    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 1600, 900, 96, 96, pixels);
    await encoder.FlushAsync();
}
long sourceBytes = new FileInfo(source).Length;
Guid imageId = Guid.NewGuid();
string thumbnail = await service.CreateThumbnailAsync(source, imageId, WallpaperType.Image);
await CheckImageAsync(thumbnail);
if (new FileInfo(thumbnail).Length >= sourceBytes || new FileInfo(source).Length != sourceBytes)
    throw new InvalidOperationException("Image thumbnail was not reduced or original changed.");
var timestamp = File.GetLastWriteTimeUtc(thumbnail);
await service.CreateThumbnailAsync(source, imageId, WallpaperType.Image);
if (File.GetLastWriteTimeUtc(thumbnail) != timestamp) throw new InvalidOperationException("Valid cache was regenerated.");
await File.WriteAllTextAsync(thumbnail, "nonempty corrupt cache");
if (service.ThumbnailExists(imageId)) throw new InvalidOperationException("Corrupt thumbnail accepted.");
await service.CreateThumbnailAsync(source, imageId, WallpaperType.Image);
await CheckImageAsync(thumbnail);

string video = Path.Combine(root, "red-video.mp4");
using (File.Open(video, FileMode.CreateNew)) { }
var composition = new MediaComposition();
composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(2)));
var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
profile.Audio = null;
var result = await composition.RenderToFileAsync(await StorageFile.GetFileFromPathAsync(video), MediaTrimmingPreference.Precise, profile);
if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException(result.ToString());
string videoThumbnail = await service.CreateThumbnailAsync(video, Guid.NewGuid(), WallpaperType.Video);
await CheckImageAsync(videoThumbnail);
Console.WriteLine($"PASS: image resized to <=384 px ({sourceBytes} -> {new FileInfo(thumbnail).Length} bytes), original preserved, cache reused, corrupt cache repaired, video thumbnail contains actual red frame.");
Console.WriteLine(root);

static async Task CheckImageAsync(string path)
{
    if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Thumbnail generation failed.");
    using var stream = new InMemoryRandomAccessStream();
    await stream.WriteAsync((await File.ReadAllBytesAsync(path)).AsBuffer());
    stream.Seek(0);
    var decoder = await BitmapDecoder.CreateAsync(stream);
    if (decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId || decoder.PixelWidth != 384 || decoder.PixelHeight != 216)
        throw new InvalidOperationException($"Unexpected thumbnail: {decoder.PixelWidth}x{decoder.PixelHeight}.");
    byte[] pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb)).DetachPixelData();
    int center = ((int)decoder.PixelHeight / 2 * (int)decoder.PixelWidth + (int)decoder.PixelWidth / 2) * 4;
    if (pixels[center + 2] < 180 || pixels[center] > 70 || pixels[center + 1] > 70)
        throw new InvalidOperationException("Thumbnail does not match source content (red frame expected).");
}
