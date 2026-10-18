# Beta 4 engineering handoff

## Scope

Three features plus supporting plumbing: a real online catalog (live API and static manifest), link
imports, automatic wallpaper changes (schedule/rotation and per-application), and an in-app update
check. The private beta installer stays unsigned; [code-signing.md](code-signing.md) covers that.

## New and changed paths

- Core: `Models/WallpaperSchedule.cs`, `Models/ApplicationWallpaperRule.cs`,
  `Models/MediaFileTypes.cs`, `Models/Catalog/CatalogQueryEngine.cs`,
  `Models/Updates/ReleaseVersion.cs`, `Enums/CatalogSource.cs`,
  `Interfaces/IForegroundApplicationMonitor.cs`, `Interfaces/IRemoteWallpaperImportService.cs`,
  `Models/ApplicationSettings.cs` (catalog source, manifest URL, schedule, per-app rules, update
  defaults).
- Infrastructure: `Catalog/Static/StaticCatalogProvider.cs`,
  `Services/RemoteMediaFetcher.cs`, `Services/UrlImportService.cs`,
  `Updates/UpdateCheckService.cs`, `Updates/RemoteUpdateService.cs` (pre-release comparison),
  `Services/WallpaperImportService.cs` (extension sets now come from `MediaFileTypes`),
  `Catalog/Remote/RemoteCatalogProvider.cs` and `Core/Models/Catalog/CatalogDownloadStream.cs`
  (report the response media type).
- Application: `Services/WallpaperScheduleService.cs`, `Services/ApplicationWallpaperService.cs`.
- Server: `CatalogServer.cs` gains `--static`, explicit `--Catalog:DataRoot` parsing and the shared
  query engine.
- UI: `Services/ForegroundApplicationMonitor.cs`, `MainWindow.Automation.cs`,
  `MainWindow.Settings.cs`, `MainWindow.Library.cs`, `MainWindow.xaml`,
  `ViewModels/SettingsViewModel.Automation.cs`, `ViewModels/MainViewModel.cs`,
  `Bootstrapper/AppCompositionRoot.cs`, `App.xaml.cs`.
- Tools and docs: `tools/Start-CatalogServer.ps1` (`-Static`), `tools/Sign-Release.ps1`,
  `docs/engineering/code-signing.md`, `docs/engineering/beta4-release-notes.md`,
  `docs/updates/update.json`.

## Design decisions worth keeping

- **One query engine.** `CatalogQueryEngine` is shared by the server API and the static client, so a
  catalog cannot behave differently depending on how it is hosted. `StaticCatalogProviderTests`
  asserts parity against the live API over loopback HTTP.
- **One download path.** Link imports and catalog downloads both go through the same HTTP range
  transport (`RemoteCatalogProvider`), so range validation, `If-Range` and strong-ETag rules cannot
  drift apart. `RemoteMediaFetcher` only adds the file, retry and progress layer.
- **No new settings pollution.** Per-application and schedule switching call the engine directly
  rather than `SavedWallpaperService`, which keeps the saved startup assignment intact so the base
  wallpaper can be restored. Nothing about a temporary switch is persisted.
- **Pure planners, thin services.** Window/rotation selection and rule matching are pure functions in
  Core; the services own only the clock, the cursor and the engine calls, which is what makes them
  testable with `TimeProvider` and plain lists.
- **Static hosting is a server mode, not a separate tool.** Serving the catalog directory with the
  static file middleware gives correct ETag, `Last-Modified` and range handling for free, and it is
  byte-identical to the layout any CDN expects.

## Verification performed

- `dotnet build XenMotion.sln -c Debug`: 0 warnings, 0 errors. All five tool projects build too.
- `dotnet test`: 382 passed, 0 failed.
- Static hosting verified against the real process (`--static` on loopback): `catalog.json` served
  (2 demo items), media answered `200` with `Content-Type`, `Accept-Ranges: bytes` and a strong ETag,
  and `Range: bytes=100-199` answered `206` with `Content-Range: bytes 100-199/12609` and exactly
  100 bytes.
- `--Catalog:DataRoot` now works: the argument is parsed explicitly, which fixed a latent bug where
  the documented flag was ignored and the server fell back to `<content root>/data`.

## Known limitations

- The schedule and per-application switchers were tested through their planners/services and the
  build, not by watching a physical desktop across a full day or a full application switch cycle.
  Multi-monitor schedule isolation is covered by unit tests only.
- The update check is exercised against a manifest fixture (`UpdateServiceTests`); no public manifest
  is reachable from this environment, so the end-to-end path over the internet is unverified.
- Link imports were verified against a loopback host. Large files on a slow public CDN (resume across
  a real network interruption) are unverified.
- The installer is unsigned and no installer/upgrade flow was executed.
