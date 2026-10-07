# XenMotion

Windows live wallpapers with a native .NET wallpaper engine and the restored native WinUI 3 interface.

Open `XenMotion.sln`. Build the UI with:

```powershell
dotnet restore XenMotion.sln
dotnet build src/XenMotion.UI/XenMotion.UI.csproj -c Release -p:Platform=x64
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj
```

## Native interface

The interface uses the pre-Kokonut XAML screens for Explore, Library, filters, settings and dialogs. Build and release packaging use the .NET toolchain.

The application executable is under `src/XenMotion.UI/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/`.
Close hides the interface to the notification area; **Exit XenMotion** in the tray terminates it.
Use the trash button on a Library card to remove its library copy. Imported originals are preserved.

## Compatibility

Existing Lumen libraries/settings are reused in their original data directory so stored paths stay valid.
The legacy single-instance IPC identity is retained to prevent two wallpaper engines competing for the desktop.
The source projects, solution, namespaces, application title and assets use XenMotion.

## Rendering and performance

- Images retain their oriented source resolution, are decoded once with sRGB color management, and are repainted on demand. Resizing uses HALFTONE filtering. Neither images nor videos change the Windows wallpaper registry setting.
- Video uses Media Foundation with timestamp-based pacing. The FPS cap controls presentation without slowing media playback.
- Decoded RGB32 frames are drawn into a GDI-compatible D3D11/DXGI back buffer and presented to DWM. Decode/upload still uses CPU-readable buffers; this is not an end-to-end GPU texture decoder, and representative hardware benchmarks remain necessary.
- Covering the desktop pauses playback, including the primary-monitor alias used by Library. Moving the covering window away resumes the actual renderer.
- Static ambient gradients, coalesced UI updates, bounded thumbnail decode, and message-driven desktop windows reduce idle work.
- New installations default to 30 FPS. Existing settings are preserved.
- Background startup creates the tray and restores the saved primary wallpaper before creating the main XAML window. The window, catalog and previews load when opened.
- Successful Library/Explore selections persist for the next launch. Stopping all wallpapers clears the startup assignment.
- Startup uses a current-user interactive logon task with no configured delay and ordinary privileges; a Run entry remains the fallback when task registration is unavailable. Actual cold-boot timing still depends on Windows and Explorer readiness.
- Foreground changes wake the activity monitor immediately. It uses physical DWM bounds and also checks visible background windows, so a small foreground overlay does not resume wallpaper decoding behind a fullscreen game.
- On raised Windows 11 desktops, the DXGI surface is a child of Progman, directly below DefView and above the stock background. It remains visible while the icon list is shown.

## Validation and remaining limits

The regression suite includes real Windows Media Foundation video/audio decoding, rewind, pause/resume, audio device output, and FPS-cap timing tests. The Now Playing dock has a live 0–100% volume slider; mute and volume persist across launches. Audio selects only the audio stream and follows video pause, stop and loop boundaries.
An optional interactive Explorer smoke test creates a surface and runs the real video renderer:

```powershell
$env:XENMOTION_DESKTOP_SMOKE = '1'
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj
```

Desktop pixels can only be sampled when unobscured; a foreground window clips the wallpaper DC. Automated lifecycle checks do not replace visual verification on each Windows version.

The local Explore catalog contains preview-only video entries; it no longer saves fake BMP data as MP4 files. Import real videos or configure a real remote catalog. Previously downloaded synthetic entries remain in the user's library until removed.

GPU-resident video frames use the shared D3D11 presentation path; CPU upload remains the fallback. Current limitations include primary-display targeting in the desktop adapter. Imported image metadata in the persistence layer still uses legacy defaults; video card dimensions/duration are now read from the real file. These should be addressed before a production release.

Native interop repairs were checked against the installed Windows SDK headers. HRESULT declarations use `PreserveSig` as described in [Microsoft's interop documentation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/preserve-sig). Desktop parenting follows [SetParent's window-style and DPI requirements](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent).


## Private beta 0.9.0-beta.1

Automatic quality uses the same isolated optimizer as the standalone lab. First launch records capabilities, the first video is measured, and validated per-source/display profiles are cached. Existing wallpaper restoration does not wait for a full benchmark. User quality ceilings and Economy remain in force. See [beta review and limitations](docs/engineering/private-beta-review.md).

`tools/Build-Release.ps1` builds/tests the application, bundles the embedded Python runtime and native probe, verifies checksums and produces the per-user Inno Setup installer. Windows 10 build 19041+/Windows 11 x64 is required. The beta installer is unsigned.
