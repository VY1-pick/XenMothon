# XenMotion 0.9.0-beta.3 — Private Beta

This beta upgrades the application platform. It does not change wallpaper features; it moves the whole solution onto a supported toolchain.

- **.NET 10**: every project now targets `net10.0` (the UI and tools target `net10.0-windows10.0.19041.0`).
- **Windows App SDK 2.5.1**: replaces Windows App SDK 1.8, whose servicing line ended on 2026-09-24. The unpackaged, self-contained deployment model, custom entry point and single-instance bootstrapping are unchanged.
- Updated Microsoft.Extensions / Microsoft.Data.Sqlite packages to 10.0.12 and the test stack to Microsoft.NET.Test.Sdk 18.10.1, xunit 2.9.3, xunit.runner.visualstudio 4.0.1 and Moq 4.21.0.
- Cleaned up two .NET 10 authoring warnings (a `ref`→`in` COM interop call and a redundant framework package reference) without changing behavior.

## Validation and limits

The Release build of the full solution completes with **zero warnings and zero errors**, and **316 .NET tests pass**. All five standalone tools build cleanly. The release is a private prerelease; package dependencies and checksums are verified by the release script.

Physical display/audio timing, codec and driver coverage and independent multi-monitor quality allocation are not re-certified by this beta. The installer remains unsigned; Windows may show a publisher or reputation warning. The remote catalog/creator backend is not hosted by this release.

## فارسی

این بتا ارتقای پلتفرم است، نه افزودن قابلیت تازه. کل سولوشن به **‎.NET 10** منتقل شده و **Windows App SDK 1.8** (که پشتیبانی‌اش در ۲۰۲۶/۰۹/۲۴ پایان یافت) با **Windows App SDK 2.5.1** جایگزین شده است. مدل استقرار unpackaged و self-contained، نقطهٔ ورود سفارشی و تک‌نسخه‌بودن برنامه بدون تغییر مانده‌اند.

بیلد Release کل سولوشن بدون هشدار و خطا انجام می‌شود و **۳۱۶ تست ‎.NET** پاس می‌شود. بسته‌های Microsoft.Extensions، Microsoft.Data.Sqlite و مجموعهٔ تست هم به‌روزرسانی شده‌اند. دو هشدار نگارشی ‎.NET 10 بدون تغییر رفتار پاک شده‌اند.

این نسخه همچنان بتای خصوصی است؛ زمان‌بندی واقعی تصویر و صدا، پوشش کدک و درایور و تخصیص مستقل چندمانیتوره در این بتا دوباره تأیید نشده‌اند. نصاب همچنان امضای ناشر ندارد.
