using Windows.Storage;
using XenMotion.UI.Services;

if (args.Length != 2) throw new ArgumentException("Pass source video and an unused output .mp4 path.");
string output = Path.GetFullPath(args[1]);
using (File.Open(output, FileMode.CreateNew)) { }
await MotionPreviewEncoder.EncodeAsync(Path.GetFullPath(args[0]), output, CancellationToken.None);
var file = await StorageFile.GetFileFromPathAsync(output);
var properties = await file.Properties.GetVideoPropertiesAsync();
var extra = await file.Properties.RetrievePropertiesAsync(new[] { "System.Video.FrameRate", "System.Audio.ChannelCount" });
double fps = Convert.ToDouble(extra["System.Video.FrameRate"]) / 1000;
long bytes = new FileInfo(output).Length;
if (properties.Width == 0 || properties.Height == 0 || Math.Max(properties.Width, properties.Height) > 480 ||
    properties.Duration.TotalSeconds > 6.2 || fps <= 0 || fps > 15.1 || bytes == 0 || bytes > 1_000_000 ||
    (extra["System.Audio.ChannelCount"] != null && Convert.ToInt32(extra["System.Audio.ChannelCount"]) > 0))
    throw new InvalidOperationException("Preview exceeds its resource limits.");
Console.WriteLine($"PASS: {properties.Width}x{properties.Height}, {properties.Duration.TotalSeconds:F2}s, {fps:F1} fps, {bytes / 1024d:F1} KiB, no audio.");
