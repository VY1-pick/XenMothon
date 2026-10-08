using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Media.Editing;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System.UserProfile;
using XenMotion.Application.Services;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;

namespace XenMotion.UI.Services;

/// <summary>Syncs a cached still on wallpaper changes, never continuously during video playback.</summary>
public sealed class WindowsPersonalizationService
{
    private readonly ISettingsService _settings;
    private readonly SavedWallpaperService _saved;
    private readonly IStorageManager _storage;
    private readonly ILogger<WindowsPersonalizationService> _logger;
    private readonly SemaphoreSlim _gate = new(1);
    private string? _appliedIdentity;
    public string Status { get; private set; } = "Windows personalization is disabled.";

    public WindowsPersonalizationService(ISettingsService settings, SavedWallpaperService saved,
        IStorageManager storage, ILogger<WindowsPersonalizationService> logger)
    {
        _settings = settings; _saved = saved; _storage = storage; _logger = logger;
        saved.ActiveWallpaperChanged += async (_, _) => await ApplyAsync();
    }

    public async Task ApplyAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var options = _settings.CurrentSettings;
            var wallpaper = _saved.ActiveWallpaper;
            if (!options.SyncWindowsAccent && !options.SyncDesktopBackground && options.LockScreenMode == LockScreenWallpaperMode.Disabled)
            { _appliedIdentity = null; Status = "Windows personalization is disabled. Existing Windows images/colors are kept."; return; }
            string identity = JsonSerializer.Serialize(new { wallpaper?.Id, wallpaper?.FilePath,
                options.SyncWindowsAccent, options.SyncDesktopBackground, options.LockScreenMode, options.LockScreenImagePath });
            if (_appliedIdentity == identity) return;
            string? still = null;
            if (options.SyncWindowsAccent || options.SyncDesktopBackground || options.LockScreenMode == LockScreenWallpaperMode.DesktopWallpaper)
            {
                if (wallpaper is null) throw new InvalidOperationException("Apply a desktop wallpaper first.");
                string thumbnail = string.IsNullOrWhiteSpace(wallpaper.ThumbnailPath) ? string.Empty : _storage.ResolveAbsolutePath(wallpaper.ThumbnailPath);
                still = !options.SyncDesktopBackground && options.LockScreenMode != LockScreenWallpaperMode.DesktopWallpaper && File.Exists(thumbnail)
                    ? thumbnail : await GetStillAsync(wallpaper);
            }
            if (options.SyncDesktopBackground)
            {
                if (!SystemParametersInfo(0x14, 0, still!, 3)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (options.SyncWindowsAccent) await ApplyAccentAsync(still!);
            if (options.LockScreenMode != LockScreenWallpaperMode.Disabled)
            {
                if (!UserProfilePersonalizationSettings.IsSupported())
                    throw new NotSupportedException("Windows does not support changing the lock screen image for this account.");
                string image = options.LockScreenMode == LockScreenWallpaperMode.DesktopWallpaper ? still! : options.LockScreenImagePath;
                if (!File.Exists(image)) throw new FileNotFoundException("Choose an existing lock screen image.", image);
                // Use app-local storage for the desktop WinRT personalization API.
                var local = ApplicationData.Current.LocalFolder;
                var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(image));
                var target = await source.CopyAsync(local, "xenmotion-lock" + source.FileType, NameCollisionOption.ReplaceExisting);
                if (!await UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(target))
                    throw new InvalidOperationException("Windows rejected the lock screen image. Check account policy and Windows activation.");
            }
            _appliedIdentity = identity;
            Status = "Windows personalization applied. Lock screen uses a still image; live video is not supported by Windows.";
        }
        catch (Exception ex)
        {
            Status = "Windows personalization: " + ex.Message;
            _logger.LogWarning(ex, "Windows personalization could not be applied.");
        }
        finally { _gate.Release(); }
    }

    private static async Task ApplyAccentAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = 96, ScaledHeight = 96 },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var (r, g, b) = WallpaperAccent.Select(pixels.DetachPixelData());
        int abgr = unchecked((int)(0xff000000u | (uint)b << 16 | (uint)g << 8 | r));
        int argb = unchecked((int)(0xff000000u | (uint)r << 16 | (uint)g << 8 | b));
        using var desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
        desktop.SetValue("AutoColorization", 0, RegistryValueKind.DWord);
        using var dwm = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM");
        dwm.SetValue("AccentColor", abgr, RegistryValueKind.DWord);
        dwm.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
        dwm.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord);
        using var accent = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        accent.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
        accent.SetValue("StartColorMenu", abgr, RegistryValueKind.DWord);
        var palette = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            double shade = (3 - i) * .16;
            static byte Shade(byte c, double s) => (byte)Math.Clamp(s >= 0 ? c + (255 - c) * s : c * (1 + s), 0, 255);
            palette[i * 4] = Shade(r, shade); palette[i * 4 + 1] = Shade(g, shade);
            palette[i * 4 + 2] = Shade(b, shade); palette[i * 4 + 3] = 255;
        }
        accent.SetValue("AccentPalette", palette, RegistryValueKind.Binary);
        SendMessageTimeout(new IntPtr(0xffff), 0x1a, IntPtr.Zero, "ImmersiveColorSet", 2, 1000, out _);
        SendMessageTimeout(new IntPtr(0xffff), 0x1a, IntPtr.Zero, "WindowsThemeElement", 2, 1000, out _);
    }

    private async Task<string> GetStillAsync(Wallpaper wallpaper)
    {
        string source = _storage.ResolveAbsolutePath(wallpaper.FilePath);
        if (wallpaper.Type == WallpaperType.Image) return source;
        var file = new FileInfo(source);
        string folder = Path.Combine(_storage.RootStoragePath, "WindowsPersonalization");
        Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, $"{wallpaper.Id:N}-{file.LastWriteTimeUtc.Ticks}.jpg");
        if (File.Exists(output)) return output;
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(source));
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.Zero, 1920, 1080, VideoFramePrecision.NearestFrame);
        using var input = thumbnail.AsStreamForRead();
        await using (var destination = File.Create(output + ".tmp")) await input.CopyToAsync(destination);
        File.Move(output + ".tmp", output, true);
        return output;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr param, string value, uint flags, uint timeout, out IntPtr result);
}
