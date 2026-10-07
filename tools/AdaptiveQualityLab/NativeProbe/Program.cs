using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering.Direct3D;
using XenMotion.UI.Rendering.Media;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static int Main(string[] args)
    {
        string? output = null;
        try
        {
            if (args.Length != 2) throw new ArgumentException("Usage: NativeProbe <request.json> <output.json>");
            output = Path.GetFullPath(args[1]);
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(args[0]), Json) ?? throw new ArgumentException("Empty request.");
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new PlatformNotSupportedException("Windows 10 build 19041+ required.");
            MediaFormatValidatorPath(request.Video);
            object result = request.Inspect ? Inspect(request.Video) : Probe(request);
            File.WriteAllText(output, JsonSerializer.Serialize(result, Json));
            return 0;
        }
        catch (Exception ex)
        {
            if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message }, Json));
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void MediaFormatValidatorPath(string video) => XenMotion.Application.Services.MediaFormatValidator.Validate(video);

    private static object Inspect(string video)
    {
        using var engine = new MediaFoundationPlaybackEngine(NullLogger.Instance);
        engine.Open(video);
        return new {
            environment = NativeMetrics.Inspect(),
            source = new { name = Path.GetFileName(video), width = engine.VideoWidth, height = engine.VideoHeight,
                fps = engine.NativeFps, durationSeconds = engine.Duration.TotalSeconds,
                codec = "Unknown (container is not a codec)", color = "Unverified SDR conversion" }
        };
    }

    private static object Probe(Request r)
    {
        if (r.Width is < 32 or > 16384 || r.Height is < 32 or > 16384 || r.Width * (long)r.Height > 40000000 ||
            r.Scale is < .25 or > 1 || r.Fps is < 1 or > 240 || r.Seconds is < .1 or > 24 ||
            r.ProcessingHeight is < 0 or > 16384 || r.Sharpness is < 0 or > .3 || r.StartSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(r), "Invalid benchmark bounds.");
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long baselinePrivate = process.PrivateMemorySize64;
        var stages = new List<object>();
        void Snapshot(string stage) { process.Refresh(); stages.Add(new { stage, privateMiB = process.PrivateMemorySize64 / 1048576.0, workingSetMiB = process.WorkingSet64 / 1048576.0, managedMiB = GC.GetTotalMemory(false) / 1048576.0 }); }
        Snapshot("process-ready");
        // Off-screen child owned by this process: never changes wallpaper, icons, tray, or app settings.
        using var surface = DesktopSurface.TryCreate(NativeMetrics.GetDesktopWindow(), new MonitorBounds(-24000, -24000, r.Width, r.Height))
            ?? throw new InvalidOperationException("Native test surface unavailable.");
        int width = Math.Max(1, (int)Math.Round(r.Width * r.Scale));
        int height = Math.Max(1, (int)Math.Round(r.Height * r.Scale));
        using var chain = new DesktopSwapChain(surface.Handle, width, height);
        chain.SetFiltering(r.Filter);
        chain.SetVisualAdjustments(1, 1, 1, (float)r.Sharpness);
        Snapshot("renderer-ready");
        using var reader = new MediaFoundationPlaybackEngine(NullLogger.Instance);
        var opening = Stopwatch.StartNew();
        reader.Open(r.Video, r.Hardware ? chain.Device : IntPtr.Zero, r.ProcessingHeight);
        opening.Stop();
        Snapshot("decoder-open");
        using var gpu = new NativeMetrics.GpuClock(chain.Device);

        // Warm-up is explicitly outside the measured window.
        Play(reader, chain, null, r, Math.Min(2, r.Seconds), false);
        Snapshot("warmed-playback");
        reader.SeekToStart();
        double sourceOffset = r.StartSeconds;
        if (sourceOffset > 0)
        {
            // Existing decoder has start-only seek: skip forward outside timing; bounded by parent timeout.
            while (true)
            {
                using var skip = reader.ReadNextFrame(false);
                if (skip is null || skip.Timestamp.TotalSeconds >= sourceOffset) break;
            }
        }
        var measured = Play(reader, chain, gpu, r, r.Seconds, true);
        gpu.Finish();
        object? memory = NativeMetrics.MemoryBudget(chain.Device);
        Snapshot("after-measurement");
        // Dense matched samples at three PTS positions, outside measured playback.
        reader.SeekToStart();
        var qualityFrames = new List<object>();
        foreach (double targetTime in new[] { 0.0, 1.0, 2.0 })
        {
            while (true)
            {
                using var frame = reader.ReadNextFrame(false);
                if (frame is null) break;
                if (frame.Timestamp.TotalSeconds + .0001 < targetTime) continue;
                Render(chain, frame, width, height); chain.PresentGpu();
                qualityFrames.Add(new { timestamp = frame.Timestamp.TotalSeconds, colors = QualityCapture.Capture(chain, width, height) });
                break;
            }
        }
        return new {
            mode = "native-offscreen", hardwareRenderer = chain.IsHardwareDevice,
            sharedDeviceRequested = r.Hardware,
            gpuResidentOutputFrames = measured.GpuFrames, decodedFrames = measured.Decoded,
            decodedWidth = reader.VideoWidth, decodedHeight = reader.VideoHeight,
            sourceFps = reader.NativeFps, openMs = opening.Elapsed.TotalMilliseconds,
            measuredSeconds = measured.Elapsed,
            submittedFrames = measured.Presented,
            cpuCoreMsPerSec = measured.CpuMean,
            cpuWindows = measured.CpuWindows,
            memoryStages = stages, workingSetPeakMiB = measured.PeakWorkingSet / 1048576.0,
            privateAddedMiB = Math.Max(0, measured.PeakPrivate - baselinePrivate) / 1048576.0,
            totalPrivateMiB = measured.PeakPrivate / 1048576.0,
            gpuRenderMs = gpu.Samples,
            decodeWallMs = measured.DecodeTimes,
            submitLateMs = measured.LateTimes,
            submitMissRatio = measured.Presented == 0 ? 1 : (double)measured.Missed / measured.Presented,
            maxSubmitLateMs = measured.LateTimes.Count == 0 ? (double?)null : measured.LateTimes.Max(),
            memory,
            qualityFrames, qualityWidth = QualityCapture.Width, qualityHeight = QualityCapture.Height,
            eofLoops = measured.Loops, firstFrameReadMs = measured.FirstFrameReadMs,
            unavailable = new[] { "physical-display-timing", "gpu-engine-busy", "audio-underruns", "audio-drift", "power-watts", "hdr-validation", "hardware-codec-confirmation", "joint-multi-monitor-validation" }
        };
    }

    private static Pass Play(MediaFoundationPlaybackEngine reader, DesktopSwapChain chain, NativeMetrics.GpuClock? gpu, Request r, double seconds, bool measure)
    {
        int width = Math.Max(1, (int)Math.Round(r.Width * r.Scale)), height = Math.Max(1, (int)Math.Round(r.Height * r.Scale));
        using var process = Process.GetCurrentProcess();
        var pass = new Pass();
        var watch = Stopwatch.StartNew();
        double cpuStart = process.TotalProcessorTime.TotalMilliseconds, cpuPrevious = cpuStart, windowAt = 0;
        double? firstPts = null; double loopOffset = 0, previousPts = 0; long lastSlot = -1;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var decoding = Stopwatch.StartNew();
            using var frame = reader.ReadNextFrame(false);
            if (frame is null)
            {
                double duration = reader.Duration.TotalSeconds > 0 ? reader.Duration.TotalSeconds : previousPts - (firstPts ?? 0) + 1 / reader.NativeFps;
                if (duration <= 0) break;
                loopOffset += duration; pass.Loops++;
                reader.SeekToStart(); continue;
            }
            pass.Decoded++;
            if (frame.TexturePointer != IntPtr.Zero) pass.GpuFrames++;
            if (measure) pass.DecodeTimes.Add(decoding.Elapsed.TotalMilliseconds);
            if (firstPts is null)
            {
                firstPts = frame.Timestamp.TotalSeconds;
                pass.FirstFrameReadMs = decoding.Elapsed.TotalMilliseconds;
                watch.Restart(); cpuStart = cpuPrevious = process.TotalProcessorTime.TotalMilliseconds;
            }
            previousPts = frame.Timestamp.TotalSeconds;
            double due = loopOffset + frame.Timestamp.TotalSeconds - firstPts.Value;
            if (due > seconds) break;
            while (watch.Elapsed.TotalSeconds < due)
            {
                double remaining = (due - watch.Elapsed.TotalSeconds) * 1000;
                Thread.Sleep(Math.Clamp((int)remaining, 1, 10));
            }
            long slot = (long)Math.Floor(due * Math.Min(reader.NativeFps, r.Fps) + .0001);
            if (slot > lastSlot)
            {
                lastSlot = slot;
                double late = Math.Max(0, (watch.Elapsed.TotalSeconds - due) * 1000);
                if (measure)
                {
                    pass.LateTimes.Add(late);
                    if (late > 250.0 / Math.Min(reader.NativeFps, r.Fps)) pass.Missed++;
                }
                gpu?.Begin();
                Render(chain, frame, width, height);
                gpu?.End();
                chain.PresentGpu();
                pass.Presented++;
            }
            if (measure && watch.Elapsed.TotalSeconds - windowAt >= 1)
            {
                double now = process.TotalProcessorTime.TotalMilliseconds;
                pass.CpuWindows.Add((now - cpuPrevious) / (watch.Elapsed.TotalSeconds - windowAt));
                cpuPrevious = now; windowAt = watch.Elapsed.TotalSeconds;
                process.Refresh(); pass.PeakPrivate = Math.Max(pass.PeakPrivate, process.PrivateMemorySize64); pass.PeakWorkingSet = Math.Max(pass.PeakWorkingSet, process.WorkingSet64);
            }
        }
        pass.Elapsed = watch.Elapsed.TotalSeconds;
        pass.CpuMean = (process.TotalProcessorTime.TotalMilliseconds - cpuStart) / Math.Max(.001, pass.Elapsed);
        process.Refresh(); pass.PeakPrivate = Math.Max(pass.PeakPrivate, process.PrivateMemorySize64); pass.PeakWorkingSet = Math.Max(pass.PeakWorkingSet, process.WorkingSet64);
        return pass;
    }

    private static void Render(DesktopSwapChain chain, DecodedVideoFrame frame, int width, int height)
    {
        var viewport = VideoViewportCalculator.Calculate(frame.Width, frame.Height, new MonitorBounds(0, 0, width, height), PlaybackScalingMode.Fill);
        if (frame.TexturePointer != IntPtr.Zero) chain.RenderTexture(frame.TexturePointer, frame.TextureSubresource, frame.Width, frame.Height, viewport);
        else chain.Render(frame.DataPointer, frame.Width, frame.Height, viewport);
    }
    private sealed record Request(string Video, bool Inspect = false, int Width = 1920, int Height = 1080,
        int ProcessingHeight = 720, double Scale = .5, int Fps = 15, bool Filter = false,
        double Sharpness = 0, bool Hardware = true, double Seconds = 6, double StartSeconds = 0);
    private sealed class Pass
    {
        public double CpuMean, Elapsed, FirstFrameReadMs; public long PeakPrivate, PeakWorkingSet, Decoded, GpuFrames, Presented, Missed, Loops;
        public List<double> CpuWindows { get; } = [];
        public List<double> DecodeTimes { get; } = [];
        public List<double> LateTimes { get; } = [];
    }
}
