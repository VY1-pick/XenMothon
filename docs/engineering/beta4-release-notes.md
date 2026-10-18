# XenMotion 0.9.0-beta.4 — Private Beta

This beta is feature-focused. It turns Explore into a real catalog, imports wallpapers straight from a
link, and lets XenMotion change wallpapers on its own — by time of day, by rotation, and by the
application you are using. On top of that it adds an in-app update check that only ever notifies.

- **Online catalog is real.** Explore can now load a live catalog API or a single static
  `catalog.json`, and both answer identical queries because they share one filtering/sorting engine.
  Media downloads keep progress, cancellation and HTTP-range resume in every mode.
- **Static catalog publishing.** `XenMotion.Server --static` serves a catalog directory exactly as a
  CDN, object store or static web host would (`catalog.json` plus relative media), so the same folder
  works locally and in production.
- **Import from link.** Library → *From link* downloads an HTTP(S) image or video, resumes an
  interrupted transfer, resolves the real container from the URL or the server's media type, then
  runs the normal import pipeline: duplicate detection, SHA-256 hashing, thumbnail, storage and
  metadata. Failures are reported in plain language.
- **Wallpaper schedule.** Settings → *Schedule* pins wallpapers to daily time windows (windows may
  cross midnight) and rotates through a pool at a chosen interval, per monitor. A window always wins
  over rotation, and when nothing applies the saved wallpaper comes back.
- **Wallpaper per application.** Settings → *Apps* shows a different wallpaper while a chosen
  application has focus. The saved startup wallpaper is restored when the application loses focus.
- **In-app update check.** Settings → *Updates* checks an HTTPS release manifest, reports the result
  and links the release notes. XenMotion never downloads or installs a release by itself. Version
  comparison now understands pre-release labels, so a beta can supersede an earlier beta.
- **Signature-ready tooling.** `tools/Sign-Release.ps1` and [code-signing.md](code-signing.md)
  describe and automate the signing step; the current installer is still unsigned.

## Validation and limits

The Release build of the full solution completes with **zero warnings and zero errors**, the five
standalone tools build cleanly, and **382 .NET tests pass**, including new coverage for the schedule
planner, per-application matching, pre-release version ordering, the static catalog transport over
loopback HTTP (manifest, search parity with the API, range/ETag resume, fallback) and link imports
(extension resolution, validation failures, staging cleanup). Static hosting was also verified
against the real server process: `catalog.json` was served with the expected media type, a strong
ETag and `Accept-Ranges`, and a byte range returned `206` with a correct `Content-Range`.

Not covered by this beta: physical display/audio timing, codec and driver coverage, independent
multi-monitor quality allocation and a real signed installer. The schedule and per-application
switchers were exercised through their planners and services, not by watching a physical desktop for
a full day of windows. The installer remains unsigned, and the update check can only reach a manifest
the network actually exposes (a private repository needs a reachable manifest URL).

## فارسی

این بتا قابلیت‌محور است. Explore به کاتالوگ واقعی تبدیل شده و می‌تواند هم از API زندهٔ سرور و هم از یک
فایل ثابت `catalog.json` بخواند؛ چون هر دو یک موتور فیلتر/مرتب‌سازی مشترک دارند، نتایج یکسان‌اند و
دانلود در هر دو حالت پیشرفت، لغو و ادامهٔ دوباره (Range) دارد. `XenMotion.Server --static` یک پوشهٔ
کاتالوگ را دقیقاً مثل یک CDN یا فضای ذخیره‌سازی ثابت منتشر می‌کند.

«افزودن از لینک» در Library یک عکس یا ویدیو را از یک آدرس HTTP(S) دانلود می‌کند، انتقال نیمه‌کاره را
ادامه می‌دهد، نوع واقعی فایل را از آدرس یا نوع رسانهٔ سرور تشخیص می‌دهد و سپس همان مسیر معمول
افزودن (تشخیص تکرار، هش SHA-256، تصویر بندانگشتی، ذخیره‌سازی و متادیتا) را اجرا می‌کند.

زمان‌بندی والپیپر (بازه‌های ساعتی روزانه که می‌توانند از نیمه‌شب بگذرند، به‌همراه چرخش دوره‌ای) و
والپیپر مخصوص هر برنامه به تنظیمات اضافه شده‌اند؛ در هر دو حالت والپیپر ذخیره‌شده در پایان بازمی‌گردد.
بررسی به‌روزرسانی درون‌برنامه‌ای فقط اطلاع می‌دهد و هیچ‌وقت نسخه‌ای را دانلود یا نصب نمی‌کند؛ مقایسهٔ
نسخه‌ها حالا برچسب پیش‌انتشار (beta) را هم می‌فهمد.

بیلد Release کل سولوشن بدون هشدار و خطا انجام می‌شود، پنج ابزار مستقل سالم بیلد می‌شوند و **۳۸۲ تست
.NET** پاس می‌شود. میزبانی ثابت روی خود سرور واقعی هم بررسی شد: `catalog.json` با نوع رسانهٔ درست،
ETag قوی و `Accept-Ranges` سرو شد و درخواست محدوده با `206` و `Content-Range` درست پاسخ گرفت.

آنچه در این بتا پوشش داده نشده: زمان‌بندی واقعی تصویر/صدا، پوشش کدک و درایور، تخصیص مستقل کیفیت
چندمانیتور و یک نصاب امضاشده. زمان‌بند و سوییچ هر‌برنامه از طریق سرویس‌ها و تست‌ها بررسی شده‌اند، نه با
تماشای یک دسکتاپ واقعی در طول یک روز کامل. نصاب همچنان امضای ناشر ندارد.
