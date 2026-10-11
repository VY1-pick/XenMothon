# XenMotion

**Live wallpapers for Windows, with measured automatic quality and a native WinUI 3 interface.**

[فارسی](README.fa.md) · **English** · [Download private beta](https://github.com/VY1-pick/XenMothon/releases/tag/v0.9.0-beta.3) · [Report an issue](https://github.com/VY1-pick/XenMothon/issues)

![XenMotion Library with imported video wallpapers and playback controls](docs/images/xenmotion-beta.jpg)

*The Library interface in version 0.9.0-beta.3. Wallpapers shown are example user imports; they are not included in the installer.*

XenMotion turns local images and videos into desktop wallpapers. Import your own media, manage it in a library, control playback and sound, and choose how much work the wallpaper should do while other applications are running.

This is a **private beta**. Repository access is required to download its release assets. Hardware compatibility and physical display/audio behavior still need feedback from testers.

## Features

- Image and video wallpaper playback through a native Windows rendering engine.
- Library import, drag and drop, thumbnails, search/filtering in Explore, and library storage tools.
- D3D11 presentation with GPU-resident video frames where supported and a CPU upload fallback.
- Pause/resume, looping, playback speed, mute and a 0–100% volume control.
- Brightness, contrast, saturation, scaling, render resolution, filtering and sharpness controls.
- Automatic quality measurement and saved profiles for the actual video and display context.
- Pause policies for fullscreen/maximized applications, desktop coverage and battery use.
- Notification-area operation, optional Windows startup and restoration of the saved wallpaper.

## Requirements

| Item | Requirement |
| --- | --- |
| Operating system | Windows 10 build 19041 or newer, or Windows 11 |
| Architecture | x64; this release does not provide an ARM64 or x86 package |
| Graphics/media | Working Windows graphics drivers and Media Foundation support for the selected codec |
| Installation | Current-user installation; administrator privileges are not required |
| Runtimes | Included in the installer; no separate Python or .NET installation is needed |
| Internet | Needed for GitHub downloads and configured online services; local imports/playback work offline |

There is no certified minimum hardware specification yet. Media codec, bitrate, frame rate and monitor dimensions affect resource use as well as resolution. Automatic quality helps choose a practical configuration; it cannot guarantee smooth 4K playback on every computer.

## Install and start

1. Open the [0.9.0-beta.3 release](https://github.com/VY1-pick/XenMothon/releases/tag/v0.9.0-beta.3) using an account with repository access.
2. Download `XenMotion-0.9.0-beta.3-Setup-x64.exe`. The release also includes `SHA256SUMS.txt` and `BETA-REVIEW.md`.
3. Run the installer and choose the destination. The default is `%LOCALAPPDATA%\Programs\XenMotion`.
4. Open XenMotion from its shortcut. In **Library**, choose **Import**, or drop media into the window.
5. Select **Set as wallpaper** on a library card. Use the **Now Playing** dock to pause, adjust volume, mute or stop.
6. Open **Settings**, choose your playback/performance preferences and select **Apply changes** to save and apply them.

The beta installer is **unsigned**. Windows may show a publisher or reputation warning. A checksum verifies file integrity; it does not establish publisher identity. Do not disable Windows protection to install the beta.

To inspect the downloaded installer hash in PowerShell, run this from its download folder and compare the full result with `SHA256SUMS.txt`:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\XenMotion-0.9.0-beta.3-Setup-x64.exe
```

Closing the window keeps XenMotion in the notification area. Open it again from its tray icon. To end the engine, choose **Exit XenMotion** from the tray menu. Stopping all wallpapers clears the saved startup wallpaper assignment.

## Media support

The import picker accepts these extensions:

| Images | Videos |
| --- | --- |
| JPG, JPEG, PNG, BMP, WEBP | MP4, WEBM, WMV, AVI, MOV |

An extension is not a codec guarantee. Video/audio support depends on the streams inside the file and the codecs available to Windows. Import a known working local file when testing playback.

The default Explore catalog contains illustrative previews. Its video entries are **preview-only**, not downloadable video files. Import your own videos or configure a real remote catalog. Artwork in the screenshot remains the property of its respective creators; media is not bundled with this repository's license.

## Catalog server

Run `powershell -File .\tools\Start-CatalogServer.ps1` to start the local catalog and download server at `http://127.0.0.1:5188`. The demo contains two downloadable images. See the [server guide](docs/engineering/catalog-server.md) for publishing media, connecting XenMotion and deployment requirements.

## Beta controls

Settings open in their own window, with a reset that preserves the library and custom edits. Playback → Global wallpaper edits shares color, flip, opacity, alignment and playback controls across wallpapers. The editor can switch between global and preserved custom edits; global selection is disabled with a tooltip until enabled in Settings. Images and videos both support visual edits and reset.

Library → Select supports multiple selection, Select all and confirmed batch removal. A tray single click opens the menu; double click opens XenMotion. The menu also opens Settings and Windows screenshot capture. Activity options pause on screen saver, energy saver and Remote Desktop. Motion previews are manual by default to reduce background work.

The installer presents [private-beta terms](docs/legal/BETA-TERMS.txt); the original [MIT license](LICENSE) continues to apply to covered material. Silent installs require `/ACCEPTTERMS=yes`.

## Automatic quality

On a new installation, XenMotion chooses the nearest fixed quality mode to the primary display resolution. It does not run a benchmark or enable automatic tuning.

Choose **Settings → Performance → Automatic** after applying a video. Confirm the optional test (up to 3.5 minutes). Playback pauses, application controls are blocked during measurement, and Cancel restores the previous settings. The result and an Advanced explanation are shown at the end.

The optimizer prioritizes effective display resolution among configurations that pass its resource and frame-delivery checks. Production search goes down at most one resolution tier, with a small render-scale reduction; if no measured candidate passes, existing settings stay in place. The test measures Fill at normal speed. Results are source/display-specific and do not certify physical desktop smoothness or audio synchronization.

## Quality and performance settings

Preset defaults in this beta:

| Preset | FPS ceiling | Video-height ceiling | Render scale | Battery FPS |
| --- | ---: | ---: | ---: | ---: |
| Economy | 15 | 720p | 100% | 15 |
| Balanced | 24 | 1080p | 100% | 20 |
| High | 30 | 1440p | 100% | 24 |
| Ultra | 60 | 2160p / 4K | 100% | 30 |
| Custom | User-selected | User-selected | User-selected | User-selected |

These are fixed preset defaults, not guaranteed measured performance. The first-run choice uses display resolution without a benchmark. Economy also disables glass effects/high-quality filtering, uses modest sharpening and pauses on battery.

| Control | What it changes |
| --- | --- |
| FPS | Presentation cadence; a higher cap cannot add frames absent from the source |
| Video-height limit | Maximum height requested for video playback |
| Render scale | Rendering resolution relative to the desktop; lower values trade detail for reduced rendering work |
| Hardware decoding | Requests the hardware decode path; availability depends on Windows, codec and driver |
| Filtering / sharpness | Resampling and edge treatment; sharpening cannot recover missing source detail |
| Brightness / contrast / saturation | Visual appearance of the wallpaper |
| Scaling | Fill crops to cover, Fit preserves the full image with possible borders, Stretch changes proportions, Center centers the content; Span is exposed but independent multi-monitor behavior is not certified |
| Fullscreen / maximized pause | Pauses playback when matching windows are detected |
| Coverage pause | Choose full coverage or a custom covered-screen percentage |
| Battery policies | Battery FPS limit and optional pause on battery |
| Activity check interval | Periodic check interval alongside foreground-change notifications |
| Glass effects | Interface appearance; separate from wallpaper resolution |

For a weak computer, start with Economy and enable pause on coverage with a low custom threshold. For example, **1%** pauses even with a small covering window. Fullscreen and maximized policies are separate; keep those enabled if you also want them to trigger a pause.

For best measurement reliability, run the first tuning session with the desktop available and no game or heavy background task. For manual quality control, turn automatic tuning off, adjust the controls and save.

## Storage, privacy and removal

Settings, library metadata and quality reports are local. New installations use `%LOCALAPPDATA%\XenMotion`; an existing `%LOCALAPPDATA%\Lumen` library may be reused for compatibility. A custom library folder can be selected through storage tools; it is saved for the next launch. The old folder remains as a recovery copy after migration.

Quality reports under `QualityTuning` can contain file paths, media metadata, hardware/driver information and measurement statistics. Interrupted runs may leave local diagnostic files. Review reports before sharing them.

Remote catalog, creator upload and update checks require configured endpoints; they are not a built-in hosted service. Updates require HTTPS and are not automatically installed. Removing a Library card removes its managed library copy, preserving the original imported file. Uninstall preserves the user's library and settings; it is not a data reset.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No wallpaper is visible | Import a known working local file, confirm it is applied in Library, check pause state and desktop coverage, and ensure another wallpaper engine is not competing |
| 4K playback stutters | Enable automatic quality, inspect the report, try a lower FPS/height/scale ceiling, and compare with a lower-bitrate sample; resolution alone does not describe decode cost |
| Wallpaper stays paused | Check fullscreen/maximized, coverage percentage and battery policies; a background covering window can still qualify |
| No sound | Check mute, the Now Playing volume, Windows volume/output device and whether the file has a supported audio stream |
| App seems closed | Check the notification area; window close hides it, while tray Exit terminates it |
| Automatic result is deferred | Repeat under lighter system load; current settings remain active and no unvalidated profile is applied |
| Explore video cannot download | Default local video entries are previews; import a real local video |

Do not assume a visually changed video position alone proves pause failure: report the active pause policy, covering windows and reproducible steps so renderer behavior can be checked.

## Beta validation and known limits

Version **0.9.0-beta.3** moves the solution to **.NET 10** and **Windows App SDK 2.5.1** (replacing the end-of-life 1.8 servicing line). Its Release build had zero warnings/errors and passed **316 .NET tests**. Installer upgrade, installed-file hashes and uninstall were tested. The dependency audit reported no known vulnerable NuGet packages at release time.

These checks are evidence for tested paths, not a security guarantee or certification of every device. Current limits include primary-display targeting, incomplete independent multi-monitor quality allocation, codec/driver-dependent behavior and unverified physical timing on all display/audio devices. Image library metadata still has legacy defaults in parts of persistence. See the [beta review](docs/engineering/private-beta-review.md) for scope and remaining boundaries.

## Feedback for testers

Open an [issue](https://github.com/VY1-pick/XenMothon/issues) with:

- App version, Windows version, CPU/GPU/RAM and graphics driver.
- Monitor resolution/refresh rate, power mode and relevant quality/pause settings.
- Media codec, resolution, frame rate and bitrate when available.
- Reproduction steps, expected/actual behavior and the measurement report if relevant.

Remove private paths or unrelated information from reports. Share media only when you have permission to do so.

## Build from source

Use Windows x64, the .NET 10 SDK and the Windows/WinUI build tools required by the solution; **Windows App SDK 2.5.1** is restored from NuGet. Open `XenMotion.sln`, or run from the repository root:

```powershell
dotnet restore XenMotion.sln
dotnet build XenMotion.sln -c Release --no-restore
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj -c Release --no-build --no-restore
```

For an easy-to-open local build, run `.\tools\Build-Preview.ps1` and open `artifacts\XenMotion\XenMotion.lnk`. The executable is named `XenMotion.exe`; its supporting files stay together in the `App` subfolder.

The solution contains Core, Application, Infrastructure.Persistence and the native WinUI UI. It does not require a Node/npm frontend build. The independent optimizer lives in `tools/AdaptiveQualityLab`, with a native probe for real playback measurement.

For installer packaging, provide Inno Setup at the script's default compiler path or pass `-InnoCompiler`:

```powershell
.\tools\Build-Release.ps1 -InnoCompiler 'C:\Path\To\Inno Setup\ISCC.exe'
.\tools\Verify-Release.ps1 -PublishDirectory '.\artifacts\publish\0.9.0-beta.3'
```

The build script restores, audits dependencies, builds/tests, publishes self-contained runtimes, downloads/verifies the embedded Python archive, checks packaged files and compiles the installer. Output is under `artifacts/releases/0.9.0-beta.3`. GitHub release publication is a separate step.

## Documentation and license

- [Persian README](README.fa.md)
- [Algorithm design (Persian)](docs/engineering/adaptive-quality-algorithm.fa.md)
- [Algorithm flowchart (open the HTML locally)](docs/engineering/adaptive-quality-flowchart.html)
- [Standalone lab guide (Persian)](tools/AdaptiveQualityLab/README.fa.md)
- [Private beta review](docs/engineering/private-beta-review.md)
- [4K playback handoff](docs/engineering/4k-playback-fix.md)

Application source is licensed under [MIT](LICENSE). Bundled dependencies retain their own licenses; the embedded Python runtime includes its license file. User wallpaper artwork is not covered by the application's MIT license.
