# Quality settings and live controls

The Performance sheet offers Economy, Balanced, High and Ultra presets, a hardware recommendation, and individual controls. Save changes persists the draft and reloads active wallpapers; paused sessions return to paused and stopped sessions stay stopped. Playback restarts when applying settings.

| Profile | FPS ceiling | Video processing height | Display render scale | Mip filtering | Acrylic |
| --- | --- | --- | --- | --- | --- |
| Economy | 15 | 720 | 50% | Off | Off |
| Balanced | 30 | 1440 | 75% | On | On |
| High | 60 | Original | 100% | On | On |
| Ultra | 120 | Original | 100% | On | On |

All profiles prefer hardware decoding and pause behind fullscreen, maximized or covering applications. Economy also pauses on battery. Individual controls include battery FPS, coverage threshold, audio muting, window check interval, scaling, volume, playback speed, brightness, contrast and saturation. Changing a profile-related value labels the draft Custom; creative controls are preserved by presets.

## Initial recommendation

Only when no settings file exists, startup detects RAM, logical CPU count, the default DXGI adapter's dedicated memory and virtual desktop pixel count using native Windows APIs. Less than 6 GB RAM or at most two CPU threads selects Economy; more than one 4K desktop worth of pixels with less than 2 GB dedicated memory also selects Economy. At least 12 GB RAM, eight threads and 4 GB dedicated graphics selects High; otherwise Balanced. Ultra remains an explicit user choice. Existing preferences are not overwritten. This is a conservative capability heuristic, not a performance benchmark or a guarantee for every codec or GPU generation.

## Real rendering behavior

- Media Foundation negotiates a smaller RGB output when the source exceeds the processing-height ceiling. Aspect ratio is preserved with even dimensions. This reduces conversion/texture work, not necessarily the cost of decoding the original compressed video. Source files remain unchanged.
- Render scale changes DXGI back-buffer dimensions independently of physical display dimensions. Each display's buffer follows its resolution, including after resize; Windows stretches it to the window.
- Economy filtering uses a base-level linear sampler and skips mip generation, avoiding uninitialized mip sampling. High quality uses generated mip levels for downscaling.
- Hardware decoding shares the presenter's D3D device; disabled/unsupported decoding uses the existing CPU fallback. D3D initialization also falls back to Windows WARP if a hardware device cannot be created; software rendering can be slower.
- Brightness, contrast and saturation affect both image and video GPU pixels. Neutral values preserve the source. FPS controls presentation cadence, not synthesized frames or original codec decode workload.
- Battery FPS is the lesser of the global cap and battery cap, reevaluated by the activity monitor. Fullscreen/coverage pause stops decode and audio.
- Playback speed changes video scheduling and PCM playback rate together; audio pitch changes with speed.

Minimum supported OS remains Windows 10 build 19041. Weak hardware may require lighter source media even with Economy settings. Exact performance and visual UI appearance need physical-device verification; native tests are not proof of smoothness on every system.

## Validation

Release build and 240 tests passed, covering persisted and reapplied settings, actual GPU brightness pixels, decoder output-size changes without upscaling, base-level filtering pixels, back-buffer scaling across resolution changes, presentation FPS, pause and speed. Review executable: `artifacts/review-quality-settings/XenMotion.UI.exe`.
