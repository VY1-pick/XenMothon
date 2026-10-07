# XenMotion 0.9.0-beta.1 — integration and beta review

This release integrates the existing quality-lab-2.2 optimizer, rather than reimplementing its decisions in a second language. The installer includes a pinned Python 3.13.16 embedded runtime and the native D3D11/Media Foundation probe. No separate Python or .NET installation is needed. Target: Windows 10 build 19041+ / Windows 11, x64.

## Automatic-quality behavior

- First launch records hardware capabilities and `AwaitingFirstVideo` in the application's local `QualityTuning/first-run.json`. Non-Economy first-run hardware hints do not cap the search below source/monitor quality; explicit subsequent user ceilings remain in force. A video is required for a real codec/presentation measurement; hardware detection alone is not a completed benchmark.
- First automatically tuned video runs in bounded child processes, off the UI thread. Existing wallpaper restoration uses saved evidence immediately; missing evidence is measured after startup. Startup measurements cancel when playback pauses or the wallpaper changes.
- Results are stored by file identity, dimensions, GPU/driver snapshot, power context and requested ceilings. Cache expires after seven days. Invalid/noisy results are not applied. Changing settings/source/display changes the cache key.
- Only an accepted longer-validation result with measured quality can be applied. Unknown physical presentation, audio, GPU-engine busy and multi-monitor allocation remain explicit limitations. Failed/deferred measurements retain the user's current settings and can be retried with automatic quality when idle.
- FPS, height, scale and software-decoding preferences are ceilings. Economy stays at its requested limits. Near-quality, near-resource candidates prefer effective monitor dimensions, respecting source size and aspect ratio.
- Default resident RAM ceiling is min(1 GiB, 25% available RAM). Private commit has a separate min(2 GiB, 25% available commit) ceiling. GPU memory is measured separately and is not added blindly to RAM.
- Tuning changes rendering parameters for the current wallpaper, preserving brightness, volume, speed and other user controls. Global preset controls remain the requested ceilings; the measured profile is shown in the playback measurement report and persisted separately.
- The process invocation uses explicit argument lists, bundled absolute executables, isolated Python mode, timeout and tree cancellation. Production results remove known raw frame/request JSON files after successful report generation; aggregate results/report remain local. Interrupted measurements may leave local raw diagnostics.

## Review scope and fixes

Reviewed the changed renderer/settings/startup paths and security boundaries for local imports/storage, remote catalog/update URLs, SQL persistence, IPC and release packaging. This is a source review and automated validation, not a penetration test or a guarantee that no vulnerability exists.

| Area | Finding / treatment |
|---|---|
| Dependency audit | The previous SQLite native library was flagged for CVE-2025-6965. Pin `SQLitePCLRaw.lib.e_sqlite3` 3.53.3 and update Microsoft.Data.Sqlite to 8.0.31. Pin System.Text.Json 8.0.6 to remove remaining JSON advisories. |
| Settings | Limit persisted settings to 1 MiB; normalize finite render/visual values, dimensions, cadence, enums and nullable collections before use. |
| Media memory | Reject invalid/nonfinite rates, dimensions above 16384, and frames above 40 million pixels. CPU frame lengths are checked before upload; GPU sample lifetime is retained through presentation. |
| Library paths | Reject relative paths escaping the managed root. User-selected absolute paths remain supported. Lexical path containment is not a complete defense against a local same-user adversary replacing directories with junctions. |
| Updates | HTTPS required for manifests and download/notes URLs. The application does not install downloaded updates automatically. No embedded GitHub credentials are shipped. |
| SQL | Search/id/tag values use bound parameters; SQL structure is fixed. No user SQL interface exists. |
| IPC | Fail closed if single-instance initialization fails, rather than starting a competing wallpaper engine. Native window creation retains its reentrancy guard. |
| Packaging | Stop on restore/build/test/publish failures, verify bundled tools and recursive SHA-256 manifest, reject bundled settings/databases, and produce a per-user installer without administrator privileges. |

## Validation and boundaries

The reviewed Release build passed with zero warnings/errors and 255 .NET tests passed. Release builds and the full .NET suite are required before packaging; the optimizer has 35 runnable behavioral checks. Native rendering tests cover GPU buffers, scaling/filtering/sharpness, Media Foundation playback, pause/resume and FPS behavior. Added checks cover validated-profile application, contaminated/screen-only/over-limit cache rejection, invalid settings, relative traversal and HTTP update rejection. Installer and bundled-runtime smoke results are recorded in local release-validation artifacts.

The previously supplied 3840×2160/30 video passed the standalone 24-second 4K and 1080p validations on a 1920×1080 panel. This is evidence for that pipeline/sample, not proof for every codec, GPU, panel or sound device. No claims of flawless physical playback are made.

The installer and application are unsigned: no publisher signing certificate was supplied. A SHA-256 checksum provides integrity verification but does not establish publisher trust. SmartScreen reputation and code signing must be handled before a public release. User media/library/settings are preserved by uninstall. The current desktop adapter targets the primary display; independent joint multi-monitor quality budgeting is not complete.

Sources: [SQLite advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q), [official Python runtime release/checksum](https://www.python.org/downloads/release/python-31316/), [Microsoft memory counter definitions](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex).
