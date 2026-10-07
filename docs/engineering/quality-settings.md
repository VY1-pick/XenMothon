# Quality settings and live controls

The Performance sheet offers Economy, Balanced, High and Ultra presets, a hardware recommendation, and individual controls. Apply changes persists the draft and reloads active wallpapers; paused sessions return to paused and stopped sessions stay stopped. Playback restarts when applying settings.

| Profile | FPS ceiling | Video processing height | Display render scale | Mip filtering | Acrylic |
| --- | --- | --- | --- | --- | --- |
| Economy | 15 | 720 | 50% | Off | Off |
| Balanced | 24 | 1080 | 75% | On | On |
| High | 30 | 1440 | 100% | On | On |
| Ultra | 60 | Original | 100% | On | On |

All profiles prefer hardware decoding and pause behind fullscreen, maximized or covering applications. Economy also pauses on battery. Individual controls include battery FPS, coverage threshold, audio muting, window check interval, scaling, volume, playback speed, brightness, contrast and saturation. Changing a profile-related value labels the draft Custom; Brightness, contrast, saturation and audio controls are preserved by presets; Economy selects mild 15% sharpening, other presets select neutral sharpening.

The desktop coverage rule offers fully covered (100%) or Custom with a numeric 1–100% input. Custom retains its saved percentage when switching modes. Economy selects 1%; other presets select 85%. Visible window coverage is the union of clipped rectangles per monitor, so adjacent windows combine and overlapping pixels count once. Clicking the desktop does not exempt still-visible windows. Fullscreen/maximized and battery pause switches remain independent policies. Minimized, invisible and cloaked windows are excluded by the activity monitor.

## Initial recommendation

Only when no settings file exists, startup detects RAM, logical CPU count, the default DXGI adapter's dedicated memory and virtual desktop pixel count using native Windows APIs. Less than 6 GB RAM or at most two CPU threads selects Economy; more than one 4K desktop worth of pixels with less than 2 GB dedicated memory also selects Economy. At least 16 GB RAM, eight threads and 6 GB dedicated graphics selects Ultra; otherwise at least 12 GB RAM, eight threads and 4 GB dedicated graphics selects High; otherwise Balanced. Existing preferences are not overwritten. This is a conservative capability heuristic, not a performance benchmark or a guarantee for every codec or GPU generation.

## Real rendering behavior

- Media Foundation negotiates a smaller RGB output when the source exceeds the processing-height ceiling. Aspect ratio is preserved with even dimensions. This reduces conversion/texture work, not necessarily the cost of decoding the original compressed video. Source files remain unchanged.
- Render scale changes DXGI back-buffer dimensions independently of physical display dimensions. Each display's buffer follows its resolution, including after resize; Windows stretches it to the window.
- Economy filtering uses a base-level linear sampler and skips mip generation, avoiding uninitialized mip sampling. High quality allocates and generates mip levels only when downscaling needs them; native-size rendering uses one texture level too.
- Hardware decoding shares the presenter's D3D device; disabled/unsupported decoding uses the existing CPU fallback. D3D initialization also falls back to Windows WARP if a hardware device cannot be created; software rendering can be slower.
- Brightness, contrast and saturation affect both image and video GPU pixels. Neutral values preserve the source. FPS controls presentation cadence, not synthesized frames or original codec decode workload.
- Battery FPS is the lesser of the global cap and battery cap, reevaluated by the activity monitor. Fullscreen/coverage pause stops decode and audio.
- Playback speed changes video scheduling and PCM playback rate together; audio pitch changes with speed.

Minimum supported OS remains Windows 10 build 19041. Weak hardware may require lighter source media even with Economy settings. Exact performance and visual UI appearance need physical-device verification; native tests are not proof of smoothness on every system.

## Validation

Release build and 240 tests passed, covering persisted and reapplied settings, actual GPU brightness pixels, decoder output-size changes without upscaling, base-level filtering pixels, back-buffer scaling across resolution changes, presentation FPS, pause and speed. Review executable: `artifacts/review-quality-settings/XenMotion.UI.exe`.

Coverage-rule update: 243 tests passed, including a native renderer/orchestrator integration check proving 1% coverage freezes the decoded-frame counter and full-only mode resumes it. Build/publish succeeded; updated executable: `artifacts/review-coverage-pause/XenMotion.UI.exe`. No application window was opened for these tests.

## Measured automatic tuning and playback refinement

Automatic tuning is enabled for new installations and by Apply recommended, and can be toggled independently. Selecting a manual profile disables it. Existing saved preferences are preserved. The capability recommendation is only a starting ceiling: loading an automatically tuned video measures its actual decoder and GPU path.

Calibration tries at most four descending preset budgets, discarding two warm-up frames and collecting up to eight samples per trial. It compares p90 decode time multiplied by source FPS (including playback speed), plus p90 render/GPU-completion time multiplied by presentation FPS, with a background headroom policy of 250 ms of estimated work per second shared between monitors (25 ms minimum per monitor). This is not a reported CPU/GPU utilization percentage. The sample loop stops after 1.2 seconds; driver open/decode calls cannot be preempted, so this is not a strict total loading-time guarantee. GPU completion waits time out after 200 ms. Failed measurement restores the user's original settings; exceeding the budget at the minimum leaves a clear lighter-source warning. The last measurement is visible under Hardware and playback measurements.

The adjustment never raises the chosen FPS, processing height or render scale, and never upscales source frames. It does not presume that reducing presentation FPS reduces compressed video decoding cost. Economy remains **720p / 15 FPS / 50% render**. Mild optional sharpening improves local edge contrast at that same size, costs additional shader samples and can emphasize noise or halos; the slider can disable it. There is no claim of identical resource use or universally improved image quality.

Calibration runs on video load, not on resume or continuously during playback. Short beginning-of-video samples cannot predict every later scene or workload; no universal smoothness guarantee is possible for unsupported/heavy codecs or weak GPUs. Measurement is not persisted as a permanent machine-wide optimum.

Video and audio now await an asynchronous pause signal instead of parking ThreadPool workers. Foreground, window visibility/location and minimize events wake the coverage monitor immediately, retaining polling as a fallback. GPU textures omit unused mip levels. Muted PCM avoids input copying, and unity-volume PCM avoids per-sample gain math; audio decoding still maintains synchronization.

The existing native glass styling is retained. Profiles are persistent radio choices with concrete budgets, the recommendation has an explicit action, and hardware measurements and detailed controls use native expanders. This follows [Microsoft settings guidance](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) and [NN/g progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/). Native tests verify shader pixels, texture allocation, tuning decisions and resume submission, but UI appearance and physical-display smoothness still require a user-visible check.

Implementation: `PlaybackCalibration.cs`, `QualityPresets.cs`, `VideoWallpaperRenderer.cs`, `PlaybackSignal.cs`, `PerformanceMonitorService.cs`, `GpuFramePipeline.cs`, and the native settings sheet. Final review build: `artifacts/review-playback-refinement/XenMotion.UI.exe`. Regression results: `artifacts/playback-refinement-tests/playback-refinement.trx`.

Final refinement validation (2026-10-07): 250 Release tests passed and self-contained win-x64 publish succeeded. Five off-screen native GPU resume cycles submitted the next frame in 22.2, 32.9, 34.1, 33.8 and 32.7 ms. The small 64x36 calibration fixture reported 0.2 ms p90 decode and 10.0 ms render/GPU completion at Economy settings. These numbers validate the code path, not 4K throughput or physical-display smoothness. Build retains two pre-existing CA1416 shutdown warnings. No application window was opened.
