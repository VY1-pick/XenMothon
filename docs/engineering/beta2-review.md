# Beta 2 engineering handoff

## Reviewed and changed paths

Startup/composition, display preset selection, settings persistence and view model, main-window caption sizing, wallpaper viewer/autosave/presets, image/video renderer updates, tray callbacks, library import/refresh/migration, catalog endpoint selection, optional quality worker, installer and bilingual documentation.

The review traced controls through persistence and the renderer. It was not a completed line-by-line audit of every source file. This repository has 138 C# source files; no claim of eliminating all possible defects is made.

## Concrete defects addressed

- New installations previously changed a hardware recommendation to Custom with automatic quality enabled. The new default is display-based and manual.
- Custom caption failure could leave no drag fallback; native caption and resizing are used, with initial/minimum bounds constrained to the display work area.
- Image preview generation truncated encoded data at 256 KiB. Complete bytes are preserved; large decoded wallpaper images are bounded.
- Repeated library refresh/import entry was serialized to reduce overlapping collection resets and native thumbnails.
- A decoded video frame could submit after PauseAsync returned. Submission and pause now share the renderer lock; the regression caught this race.
- Settings updates previously mutated the shared snapshot before the file write, and released the lock between mutation and persistence. Updates now write a normalized copy atomically before publishing it.
- Background wallpaper saves could overwrite unsaved settings drafts. Dirty drafts now survive unrelated settings notifications.
- Storage relocation deleted source media before its new root was persisted, and startup ignored the saved root. The root is now reused, copies are hash-checked, overlapping/nonempty destinations are rejected, and originals remain for recovery.
- Imports previously fabricated 1920×1080/30 fps/15-second metadata. Unknown metadata is now stored as unknown; UI reads actual image/video properties.
- Invalid persisted catalog endpoints could prevent opening the UI. Startup falls back to the local provider for invalid endpoints.

## Deliberate compatibility

The old application directory/database names and startup-value removal remain only in compatibility code. Replacing those literals would abandon existing libraries or leave duplicate startup registration. User-facing branding is XenMotion; repository URLs use the verified current private repository, VY1-pick/XenMothon.

## Verification limitations

Computer Use failed at initialization with `failed to write kernel assets: The system cannot find the path specified. (os error 3)` even after a reset. Consequently, no fresh screenshots or manual interaction evidence is claimed, and the completion phone alert is unavailable. The separate low-end PC's crash report/hardware specification was not supplied.

Before promoting beyond private beta: install with a fresh profile on the reported device; drag/resize immediately; import video then image; apply both; exercise global/custom edits and reset; test single/double tray clicks; cancel/finish automatic measurement; restart after library relocation; observe the unobscured desktop. Test both low-resolution/high-DPI and normal displays.

The legal text uses the existing LICENSE's owner name, Amir. It preserves open-source rights and does not select a jurisdiction. It is not a legal review of ownership or enforceability.
