using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XenMotion.Application.Services;
using XenMotion.Core.Models;

namespace XenMotion.UI.Services;

/// <summary>Runs the tested optimizer in an isolated, bounded worker, never on the UI thread.</summary>
public sealed class AdaptiveQualityService(string appDataRoot, HardwareTuningService hardware,
    ILogger<AdaptiveQualityService> logger) : IDisposable
{
    public bool AllowMeasurements { get; set; } = true;
    public const string AlgorithmVersion = "quality-lab-2.2";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _root = Path.Combine(appDataRoot, "QualityTuning");

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "first-run.json");
        if (File.Exists(path)) return;
        await WriteAtomicAsync(path, JsonSerializer.Serialize(new {
            algorithmVersion = AlgorithmVersion, status = "AwaitingFirstVideo",
            hardware = hardware.Hardware, createdUtc = DateTimeOffset.UtcNow,
            note = "Capability hint only. Actual source/display measurements run on the first automatically tuned video."
        }), ct).ConfigureAwait(false);
        hardware.RecordPlaybackReport("Automatic quality is ready. The first video will be measured; later loads reuse its saved profile.");
    }

    public async Task<WallpaperSettings> TuneAsync(string video, WallpaperSettings requested, MonitorBounds bounds, CancellationToken ct)
    {
        var original = requested.Clone();
        if (!requested.AutoTunePlayback) return original;
        // The benchmark currently measures Fill at normal speed; other modes must not inherit its evidence.
        if (requested.PlaybackSpeed != 1 || requested.ScalingMode != XenMotion.Core.Enums.PlaybackScalingMode.Fill)
        {
            hardware.RecordPlaybackReport("Automatic quality retained your settings: this playback speed/scaling mode is not measured yet.");
            return original;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            MediaFormatValidator.Validate(video);
            var file = new FileInfo(video);
            var identity = JsonSerializer.Serialize(new { AlgorithmVersion, path = file.FullName,
                file.Length, file.LastWriteTimeUtc, bounds.Width, bounds.Height, hardware.Hardware,
                driver = Environment.OSVersion.VersionString,
                battery = OnBattery(), requested.VideoHeightLimit,
                requested.RenderScale, requested.TargetFps, requested.UseHardwareDecoding });
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var output = Path.Combine(_root, key);
            var resultPath = Path.Combine(output, "results.json");
            if (TryReadResult(resultPath, original, out var cached))
            {
                hardware.RecordPlaybackReport($"Saved automatic quality: {cached.VideoHeightLimit}p / {cached.TargetFps} FPS / {cached.RenderScale:P0}. Display/audio timing remain unverified.");
                return cached;
            }
            if (!AllowMeasurements)
            {
                hardware.RecordPlaybackReport("Saved wallpaper restored immediately. Its first quality measurement will run after startup.");
                return original;
            }
            var install = AppContext.BaseDirectory;
            var python = Path.Combine(install, "Runtime", "Python", "python.exe");
            var optimizer = Path.Combine(install, "tools", "AdaptiveQualityLab", "lab.py");
            var probe = Path.Combine(install, "QualityProbe", "NativeProbe.exe");
            if (!File.Exists(python) || !File.Exists(optimizer) || !File.Exists(probe))
                throw new FileNotFoundException("Bundled automatic-quality tools are missing. Use the complete installer build.");
            Directory.CreateDirectory(output);
            var reference = Path.Combine(output, "reference-settings.json");
            await WriteAtomicAsync(reference, JsonSerializer.Serialize(new {
                VideoHeightLimit = requested.VideoHeightLimit, RenderScale = requested.RenderScale,
                GlobalTargetFps = requested.TargetFps, HighQualityFiltering = requested.HighQualityFiltering,
                DefaultSharpness = requested.Sharpness, UseHardwareDecoding = requested.UseHardwareDecoding
            }), linked.Token).ConfigureAwait(false);
            hardware.RecordPlaybackReport("Measuring automatic quality (up to 3 minutes). No settings are changed until validation passes.");
            var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = install, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (var argument in new[] { "-I", "-B", "-X", "utf8", optimizer, "--video", file.FullName,
                "--probe", probe, "--output", output, "--settings-file", reference, "--production",
                "--width", bounds.Width.ToString(), "--height", bounds.Height.ToString(),
                "--height-limit", requested.VideoHeightLimit.ToString(), "--fps-limit", requested.TargetFps.ToString(),
                "--scale-limit", requested.RenderScale.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument);
            if (!requested.UseHardwareDecoding) start.ArgumentList.Add("--software-only");
            if (requested.VideoHeightLimit is > 0 and <= 720 && requested.RenderScale <= .5 && requested.TargetFps <= 15)
                start.ArgumentList.Add("--economy");
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 and < 1024 }) hardware.RecordPlaybackReport("Automatic quality: " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 and < 1024 }) logger.LogDebug("Quality worker: {Message}", e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(210));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (process.ExitCode != 0 || !TryReadResult(resultPath, original, out var tuned))
            {
                hardware.RecordPlaybackReport("Measurement did not pass validation or the system was busy. Your current quality is retained; retry automatic quality when idle.");
                return original;
            }
            hardware.RecordPlaybackReport($"Measured automatic quality: {tuned.VideoHeightLimit}p / {tuned.TargetFps} FPS / {tuned.RenderScale:P0}. Saved for this source and display; display/audio timing remain unverified.");
            return tuned;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            hardware.RecordPlaybackReport("Automatic quality timed out. Your settings are retained.");
            return original;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Automatic quality unavailable; retaining requested settings.");
            hardware.RecordPlaybackReport("Automatic quality unavailable. Your settings are retained.");
            return original;
        }
        finally { _gate.Release(); }
    }

    internal static bool TryReadResult(string path, WallpaperSettings requested, out WallpaperSettings result)
    {
        result = requested.Clone();
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024 || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(7)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement.GetProperty("results")[0];
            if (root.GetProperty("algorithmVersion").GetString() != AlgorithmVersion ||
                root.GetProperty("reasonCodes").EnumerateArray().Any(x => x.GetString() == "CONTAMINATED_BASELINE")) return false;
            var chosen = root.GetProperty("selected");
            if (chosen.ValueKind != JsonValueKind.Object || !chosen.GetProperty("accepted").GetBoolean() ||
                chosen.GetProperty("phase").GetString() != "validate" ||
                chosen.GetProperty("missing").EnumerateArray().Any(x => x.GetString() == "measuredQuality")) return false;
            var s = chosen.GetProperty("settings");
            int height = s.GetProperty("videoHeightLimit").GetInt32(), fps = s.GetProperty("targetFps").GetInt32();
            float scale = s.GetProperty("renderScale").GetSingle(), sharp = s.GetProperty("sharpness").GetSingle();
            bool hardware = s.GetProperty("useHardwareDecoding").GetBoolean();
            if (height is < 1 or > 16384 || fps < 1 || fps > Math.Min(240, requested.TargetFps) ||
                !float.IsFinite(scale) || scale < .25f || scale > requested.RenderScale ||
                !float.IsFinite(sharp) || sharp is < 0 or > .3f ||
                (requested.VideoHeightLimit > 0 && height > requested.VideoHeightLimit) || (!requested.UseHardwareDecoding && hardware)) return false;
            result.VideoHeightLimit = height; result.TargetFps = fps; result.RenderScale = scale;
            result.Sharpness = sharp; result.HighQualityFiltering = s.GetProperty("highQualityFiltering").GetBoolean();
            result.UseHardwareDecoding = hardware;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IndexOutOfRangeException or ArgumentException) { return false; }
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path + ".tmp", content, ct).ConfigureAwait(false);
        File.Move(path + ".tmp", path, overwrite: true);
    }
    private static bool OnBattery() => GetSystemPowerStatus(out var power) && power.Ac == 0;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PowerStatus { public byte Ac, Flag, Percent, Saver; public uint Life, FullLife; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
    public void Dispose() { _lifetime.Cancel(); }
}
