# 4K playback lag: diagnosis, implementation and handoff

Date: 2026-10-07. Diagnosis and initial implementation were done during the Astra turn; final validation and packaging continued after the user switched to Sol. Scope is only 4K playback lag.

## Cause

The previous repair accelerated resizing but still requested CPU-readable RGB32. A 3840x2160 frame is 33,177,600 bytes. Locking a DXGI-backed media buffer downloads that frame to system memory; `UpdateSubresource` then uploads it again. At 30 FPS that is about 1 GB/s of full-frame data in each direction, plus synchronization and conversion. The earlier native-resolution output measurements exceeded the 33.33 ms frame budget.

A second cause was presentation throttling against wall-clock wake-up time. With the default 30 FPS cap, a frame presented 12 ms late followed by one 1 ms late has only ~22 ms between wake-ups. The old limiter dropped the second frame even though their media timestamps are exactly one frame apart. Timer jitter could therefore produce visible skips despite fast decoding.

## Implemented pipeline

`MF Source Reader + shared IMFDXGIDeviceManager -> RGB DXGI texture -> GPU copy of visible subresource -> filtered D3D11 draw -> DXGI Present`

- The presenter creates a D3D11 device with BGRA and video support. Device creation retries without video support when necessary.
- `MediaFoundationD3DManager` registers this same device with the Source Reader through `MF_SOURCE_READER_D3D_MANAGER`. Hardware transforms and the advanced video processor remain enabled.
- If the sample buffer implements `IMFDXGIBuffer`, `ReadNextFrame` obtains its `ID3D11Texture2D` and subresource index without `Lock`. A single-buffer sample uses `GetBufferByIndex` to avoid unnecessary buffer consolidation.
- `DecodedVideoFrame` carries either CPU pixels or a GPU texture. Its releaser keeps sample, buffer and texture alive through presentation and releases them without unlocking an un-locked GPU buffer.
- `GpuFramePipeline.RenderTexture` copies the visible rectangle on the GPU into a reusable texture of matching format. It respects codec padding and texture-array subresource indices. Existing mip filtering and scaling modes remain available; source resolution is preserved.
- The device's multithread protection is enabled. Each complete render batch is bracketed by Enter/Leave so Media Foundation workers cannot interleave context changes within a draw.
- GPU reader setup failures reopen with the existing CPU RGB32 path. A reader that yields ordinary CPU buffers also continues through the old upload path. GPU-resident output is measured separately; it does not prove that every codec uses a hardware decoder.
- `VideoPresentationSchedule` schedules the FPS cap against media timestamps, supports fractional source/cap cadence, skips obsolete frames after stalls, and resets on looping. Pause/resume retains the existing playback clock and zero-decode pause behavior.

## Code saved in the repository

- `src/XenMotion.UI/Rendering/Media/MediaFoundationD3DManager.cs`
- `src/XenMotion.UI/Rendering/Media/MediaFoundationNativeMethods.cs`
- `src/XenMotion.UI/Rendering/Media/MediaFoundationPlaybackEngine.cs`
- `src/XenMotion.UI/Rendering/Direct3D/DesktopSwapChain.cs`
- `src/XenMotion.UI/Rendering/Direct3D/GpuFramePipeline.cs`
- `src/XenMotion.UI/Rendering/Direct3D/Direct3D11VideoPresenter.cs`
- `src/XenMotion.UI/Rendering/Video/VideoPresentationSchedule.cs`
- `src/XenMotion.UI/Rendering/VideoWallpaperRenderer.cs`
- `tests/XenMotion.Core.Tests/Rendering/NativeGpuResolutionTests.cs`
- `tests/XenMotion.Core.Tests/Rendering/VideoPresentationScheduleTests.cs`

An exact snapshot of these files and this report is saved at `artifacts/astra-4k-fix/source.zip`. No Git commit was created: the checkout already contains the broad Lumen-to-XenMotion rename and other prior edits, so committing the whole working tree would mix unrelated work.

## Validation

The actual imported `hachiware-elisa.3840x2160.mp4` is 3840x2160 at 30 FPS. Every benchmark run obtained **75/75 GPU-resident frames**. Final shared-system timing, after five warm-up frames:

| Output | Decode submission + copy/filter/Present, ms/frame |
| --- | ---: |
| 1920x1080 | 7.03 |
| 2560x1440 | 6.62 |
| 3840x2160 | 6.54 |
| 1080x1920 | 6.81 |
| 3440x1440 | 7.03 |

These measurements include normal GPU queue throttling during the benchmark. The ~0.3 ms Source Reader duration alone is not the total GPU decoding time; GPU work overlaps and is accounted for in the sustained combined pipeline measurements.

- Nine GPU readback pixels at native 4K resolution matched CPU reference output within 8 units per color channel, verifying orientation and color. GPU seek-to-start passed.
- Production `VideoWallpaperRenderer` decoded 22 GPU frames during the initial ~700 ms playback interval. Pause froze decoding; resizing the native window and renderer from 4K to 1440p, resuming and stopping passed. Test output was muted.
- Scheduling tests verified 30 FPS under alternating timer jitter, exact ten-second 60->24/30->24/60->30 presentation counts, stale-frame skipping and loop reset.
- Final full regression suite: **230 reported passing tests**. The imported-file tests and interactive-desktop test return early unless opted in. The seven imported-file tests were explicitly enabled and separately passed; the interactive Explorer desktop smoke test was not rerun in this scope.
- Release win-x64 self-contained publish: `artifacts/review-4k-gpu-fix/XenMotion.UI.exe`. Two pre-existing CA1416 MainWindow shutdown warnings remain.
- Raw results: `artifacts/astra-4k-fix/gpu-resident-final.trx`, `regression-final.trx`. Earlier initial GPU measurements are in `gpu-resident.trx`.

Output surfaces were real native D3D11 resources at each resolution, positioned off-screen. This establishes decoder/presenter throughput and pixel correctness on this machine, not performance on every GPU/codec or every physical display. Desktop icon layering was not changed by this fix. Exit the older tray application before launching the new build to avoid testing the previous binary.

## Reproduce

Set `XENMOTION_VIDEO_BENCHMARK` to an existing 4K video path and `XENMOTION_REQUIRE_GPU=1`, then run:

```powershell
dotnet test tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Imported' --logger 'console;verbosity=detailed'
```

`XENMOTION_CPU_BASELINE=1` selects the CPU-buffer benchmark for comparison. Keep it unset during GPU validation. Performance timing is diagnostic; GPU residency, pixels and lifecycle behavior are asserted.

## References

- [Source Reader D3D manager](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-source-reader-d3d-manager)
- [IMFDXGIBuffer](https://learn.microsoft.com/en-us/windows/win32/api/mfobjects/nn-mfobjects-imfdxgibuffer)
- [D3D11 multithreading](https://learn.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-render-multi-thread-intro)
