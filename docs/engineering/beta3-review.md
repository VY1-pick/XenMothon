# Beta 3 engineering handoff

## Scope

This beta is a platform upgrade, not a feature release. XenMotion moves from **.NET 8** to **.NET 10** and from **Windows App SDK 1.8** (which reached the end of its servicing line on 2026-09-24) to the current stable **Windows App SDK 2.5.1**.

## Reviewed and changed paths

All twelve project files (Core, Contracts, Application, Infrastructure.Persistence, Server, UI, Core.Tests, and the five tools), the two CS9191 call sites, the release scripts, the installer script and the bilingual documentation.

## Concrete changes

- Target frameworks moved to `net10.0` and `net10.0-windows10.0.19041.0`.
- `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite` and `Microsoft.Extensions.Logging.Abstractions` moved to `10.0.12`; the test stack moved to `Microsoft.NET.Test.Sdk 18.10.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 4.0.1` and `Moq 4.21.0`.
- `Microsoft.WindowsAppSDK` moved from the end-of-life `1.8.260921001` to `2.5.1`.
- `Marshal.QueryInterface` call sites changed from `ref` to `in`; in .NET 10 the second parameter is `in`, so this is a behavior-identical token change that clears the CS9191 warning.
- The redundant `System.Text.Json` package reference was removed from the UI project (NU1510): the assembly is part of the .NET 10 shared framework.

## Platform notes

`DISABLE_XAML_GENERATED_MAIN` is still supported in Windows App SDK 2.x. Starting with 2.3.1 it renames the generated entry point to `XamlGeneratedProgram.XamlGeneratedMain` instead of omitting it; the project keeps its own `Program.Main` entry point via `<StartupObject>`, so the custom single-instance bootstrapping is unchanged. No optional XAML behavior changes are opted into. The unpackaged, self-contained Windows App SDK deployment model (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`) is kept as-is.

## Deliberate compatibility

The application directory/database names and legacy startup-value removal remain only in compatibility code. The Windows App SDK 2.x metapackage is still referenced as a single `Microsoft.WindowsAppSDK` package; its component packages are resolved transitively.

## Verification

- Full solution Release build: 0 warnings, 0 errors.
- `XenMotion.Core.Tests`: **316 passed, 0 failed, 0 skipped**.
- All five standalone tools build with 0 warnings, 0 errors.

## Verification limitations

The upgrade was validated by a clean restore, build and test run. Runtime behavior on physical displays, codec/driver coverage and multi-monitor allocation are not re-certified by this beta; testers should still exercise the app on their own hardware. The installer remains unsigned.
