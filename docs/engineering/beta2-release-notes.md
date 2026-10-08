# XenMotion 0.9.0-beta.2 — Private Beta

This beta updates first-run quality selection, wallpaper editing, settings and library controls.

- First launch selects the nearest display-based mode: 720p, 1080p, 1440p or 2160p. No first-launch benchmark.
- Optional Automatic measurement has confirmation, cancellation, a progress screen and advanced results. Display resolution is the primary quality preference; fallback is limited to one resolution tier.
- Settings open in a separate window. Reset preserves the library and custom wallpaper edits.
- Global visual/playback edits apply across wallpapers, with a per-wallpaper switch to preserved custom edits. Images now have visual editing as well as videos.
- Library selection supports multiple items, select-all and confirmed removal.
- Single-click tray menu, double-click main window, Settings and Windows screenshot capture.
- Screen saver, energy saver, Remote Desktop and motion-preview behavior controls. Motion previews are manual by default.
- Native resize/caption handling, bounded image decoding, complete image previews, serialized library refresh, atomic settings updates and a paused-video frame-submission race fix.
- Library relocation persists its destination, verifies copied files and retains the old folder for recovery.
- Installer acceptance of English beta terms, a Persian companion, and preserved MIT notices. Silent installation requires `/ACCEPTTERMS=yes`.

## Validation and limits

The release is a private prerelease. Regression tests and the quality optimizer's deterministic checks are run before packaging. Package dependencies and checksums are verified by the release script.

The reported first-install failure on a separate low-end PC has **not been reproduced on that device**. Native UI automation was unavailable in the release session; manual verification of the new windows, tray gestures, installer flow and unobscured physical desktop playback is still required. Automated/off-screen rendering tests do not certify visible smoothness or audio synchronization. The changes address identified defects; they are not a guarantee that every reported crash has been eliminated.

The remote catalog/creator backend is not hosted by this release. Some codecs require Windows support. Independent multi-monitor playback remains uncertified. MIT-covered material retains its existing rights; beta terms do not revoke them.

## فارسی

بتای دوم شامل انتخاب کیفیت براساس نمایشگر بدون بنچمارک اولیه، تست خودکار اختیاری، پنجرهٔ مستقل تنظیمات، ریست، ادیت سراسری و شخصی، ادیت عکس، انتخاب چندتایی و کنترل‌های ترای است. اشکالات مشخص ذخیرهٔ تنظیمات، پیش‌نمایش عکس، توقف فریم و انتقال کتابخانه نیز اصلاح شده‌اند.

این نسخه همچنان بتای خصوصی است. بازتست روی همان سیستم ضعیف و بررسی دیداری پنجره‌ها، نصب و پخش واقعی دسکتاپ هنوز لازم است؛ نمی‌توان رفع قطعی تمام کرش‌های گزارش‌شده را ادعا کرد.
