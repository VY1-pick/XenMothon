# XenMotion Beta 05 — release review

Version: `0.9.0-beta.5` · Public beta · Windows x64

## Audit fixes

1. Resume downloads preserve the total media length and retry until the complete file is present.
2. Foreground-app overrides and daily schedules share one serialized application path. App rules take priority; ending an override restores the current schedule, then the saved wallpaper.
3. Adding or removing schedule/app rows marks the settings draft as changed, so unrelated saves cannot discard it.
4. The requested monitor ID reaches the renderer factory and desktop surface; a missing display no longer falls back to the primary screen.
5. Foreground changes wait for the previous evaluation instead of being discarded during loading.
6. Removed monitor rules are reconciled against previously applied monitors. With no saved base, the temporary wallpaper stops.
7. Startup evaluates the already-focused application.
8. Runtime wallpaper changes update the active-wallpaper state and UI without persisting temporary startup assignments; live edit application uses that runtime state.
9. Local/link imports reject files that the Windows thumbnail/media decoder cannot decode and clean up failed imports.
10. Catalog network timeouts use the configured offline fallback; explicit caller cancellation remains cancellation.
11. Media and HTTP error-response body reads have an idle timeout; link-import cancellation is connected to the dialog without disabling its close action.
12. Cancellation after catalog media is committed removes final media and thumbnails before returning.
13. Link imports and downloads no longer log full URLs or network exception details that can contain private URL query values.
14. The signing script uses the actual SDK executable path when signtool is not on PATH.

## Verification

- Full solution Release build and .NET regression suite are required by `tools/Build-Release.ps1` before packaging.
- 400 .NET tests pass, including 18 new regression cases for automation, draft persistence, runtime UI state, monitor routing, partial-range completion, body timeouts, fallback/cancellation, decode rejection, cleanup and URL-log privacy.
- The adaptive-quality lab passes 41 behavioral checks.
- PowerShell sources parse successfully.
- NuGet vulnerability checks and redacted secret scans of Git history and release source are run before publication.
- The application package includes the native quality probe and bundled Python runtime, and its file hashes are checked by `tools/Verify-Release.ps1`.

## Limits

The installer is unsigned. No publisher certificate was created or purchased, and signing with a real certificate was not exercised. Physical display/audio timing, independent multi-monitor quality allocation, the full visual interaction flow and installer upgrade/uninstall were not re-tested for Beta 05. Automated monitor-routing tests do not establish physical multi-monitor behavior. The 14 audit fixes do not imply a complete security certification.

## فارسی

بتای عمومی ۰۵ با نسخهٔ `0.9.0-beta.5` شامل رفع ۱۴ ایراد گزارش‌شده است: کامل‌بودن دانلود قابل‌ادامه، هماهنگی زمان‌بندی با قوانین برنامه‌ها، حفظ پیش‌نویس تنظیمات، انتقال شناسهٔ نمایشگر تا رندرر، پردازش تغییر فوکوس و ارزیابی فوکوس اولیه، بازیابی نمایشگرهای حذف‌شده از قوانین، همگامی وضعیت فعال رابط، رد رسانهٔ ناخوانا، جایگزین آفلاین هنگام Timeout، مهلت خواندن و لغو دانلود، پاک‌سازی فایل پس از لغو، حذف لینک خصوصی از لاگ و اصلاح مسیر ابزار امضا.

۴۰۰ تست ‎.NET، شامل ۱۸ مورد جدید، و ۴۱ بررسی ابزار کیفیت پاس شدند. نصاب امضای دیجیتال ناشر ندارد؛ آزمون فیزیکی تصویر/صدا، رفتار واقعی چندنمایشگر، همهٔ تعاملات بصری و نصب/ارتقا/حذف واقعی در این نسخه تکرار نشده‌اند.
