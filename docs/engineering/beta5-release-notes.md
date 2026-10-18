# XenMotion Beta 05

**Public beta · 0.9.0-beta.5 · Windows x64**

Beta 05 includes online catalogs (API or static catalog.json), direct-link imports, daily schedules and rotation, per-application wallpaper rules and notification-only update checks.

This release fixes all 14 findings from the source audit. Downloads now check the complete file length, stalled transfers time out, link downloads can be cancelled, settings drafts survive unrelated saves, and automatic wallpaper changes share one priority/order path. Display IDs reach the desktop renderer, runtime wallpaper changes update the UI, malformed imports are rejected, cancelled catalog imports clean their final files, and private link query values are omitted from logs. The SDK signing-tool lookup is fixed as well.

Validation: **400 .NET tests**, **41 quality-lab checks**, Release build/package checks, dependency audit and secret scans. See [the detailed review](beta5-review.md) for the 14-item mapping and verification limits.

Download `XenMotion-0.9.0-beta.5-Setup-x64.exe` and compare its SHA-256 with `SHA256SUMS.txt`. **The installer is unsigned.** A checksum verifies integrity; it does not establish publisher identity.

Physical display/audio timing, the full visual interaction flow, real multi-monitor behavior and installer upgrade/uninstall were not re-tested for this release.

## فارسی

**XenMotion بتای عمومی ۰۵ — نسخهٔ 0.9.0-beta.5**

این نسخه شامل کاتالوگ آنلاین، افزودن از لینک، زمان‌بندی و چرخش، والپیپر مخصوص هر برنامه و اطلاع‌رسانی به‌روزرسانی است. هر ۱۴ ایراد گزارش بررسی اصلاح شده‌اند؛ از جمله دانلود ناقص، تداخل تغییر خودکار والپیپر، حذف پیش‌نویس تنظیمات، هدف‌گیری نمایشگر، گم‌شدن فوکوس، وضعیت قدیمی رابط، پاک‌سازی لغو و ثبت لینک خصوصی در لاگ.

۴۰۰ تست ‎.NET و ۴۱ بررسی ابزار کیفیت پاس شدند. جزئیات در [گزارش نسخه](beta5-review.md) آمده است.

فایل `XenMotion-0.9.0-beta.5-Setup-x64.exe` را بگیر و هش آن را با `SHA256SUMS.txt` مقایسه کن. **نصاب امضای دیجیتال ناشر ندارد.** آزمون واقعی تصویر/صدا، چندنمایشگر، همهٔ تعاملات بصری و ارتقا/حذف نصاب در این نسخه تکرار نشده‌اند.
