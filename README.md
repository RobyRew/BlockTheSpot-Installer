# BlockTheSpot Installer

A native Windows app for installing and restoring [BlockTheSpot](https://github.com/Nuzair46/BlockTheSpot). Built with .NET 10 WPF and Microsoft's Windows 11 Fluent theme.

[Download the installer](https://github.com/RobyRew/BlockTheSpot-Installer/releases/latest/download/BlockTheSpotInstaller.exe) · [Spotify version library](https://robyrew.github.io/BlockTheSpot-Installer/) · [Release notes](https://github.com/RobyRew/BlockTheSpot-Installer/releases/latest)

## Install

1. Download `BlockTheSpotInstaller.exe` and open it normally, **without running as administrator**.
2. The default is **Spotify 1.2.93.667.g7b5cc0ce**, the latest tested compatible version for this release. Tick **All versions** to pick any other build from the catalog, or type a full version or installer link.
3. Leave **Install this Spotify version** on to install that version. Turn it off only to keep an already compatible installation.
4. Select **Install BlockTheSpot**. The app downloads and validates all required files before changing Spotify.
5. Use **Restore original Spotify** to undo the patch.

Everything fits one window: the installed Spotify in the header, the version picker, four options (hover for details), status, and the actions. **Activity log** opens the log below the buttons.

The EXE bundles the .NET runtime. It targets Windows x64, uses the system light/dark theme, and supports keyboard navigation and Windows scaling. Windows 11 is recommended; supported Windows 10 editions also work. Spotify and the patch must both be x64.

Support the artists you listen to. Please consider [Spotify Premium](https://www.spotify.com/premium/).

## Versions and options

- The tested default is pinned in `src/BlockTheSpot.Core/Compatibility.cs`. It does **not** follow the newest catalog entry or change automatically when an upstream script changes. Update this constant and its tests only after verifying a new version with BlockTheSpot.
- **All versions** lists every Windows x64 build in the catalog (newest first, older than the tested one included) plus the latest official Spotify installer, and shows a filter box. A full version such as `1.2.80.699.gd5f6ebe3`, or a link on Spotify's `upgrade.scdn.co` or the LoadSpot mirror, typed into that box becomes an installable **Custom** choice. Only the tested build is presented as compatible.
- **Apply BlockTheSpot patch** is on by default. Turned off, the app installs only the selected Spotify version, any version, removes previous patch files, and never contacts the patch server. The patch itself still refuses versions below BlockTheSpot's published minimum.
- **Where installers come from.** Spotify publishes one permanent link per platform (`download.scdn.co/SpotifyFullSetupX64.exe`, always the current build) and hands versioned `upgrade.scdn.co` links only to logged-in clients as signed, expiring URLs; expired ones answer HTTP 403. The app tries Spotify's permanent link with `If-Match` on the ETag the release watcher recorded, then Spotify's versioned link when one was published, then the CI archive copy, then the LoadSpot mirror (each switch is logged as *Switching source*). Builds the watcher observed carry a SHA-256 taken from Spotify's own file, which the app checks over the whole download before the Authenticode check. The full findings are in [docs/SPOTIFY_DOWNLOADS.md](docs/SPOTIFY_DOWNLOADS.md).
- The live [LoadSpot catalog](https://loadspot.pages.dev/versions) comes from [`LoaderSpot/table`](https://github.com/LoaderSpot/table/blob/main/table/versions.json). Its predecessor [`LoaderSpot/LoaderSpot`](https://github.com/LoaderSpot/LoaderSpot) is archived. The maintained catalog includes architecture-specific download URLs, dates, and sizes.
- **Refresh** (Ctrl+R) loads the compact GitHub Pages feed first (four-second deadline), then the maintained upstream catalog (eight-second deadline), while preserving a valid selection. The Pages feed carries both Spotify's link and the mirror per build; the upstream table carries one. The known tested link remains available if both fail. Startup does not wait for the patch server; its current configuration is validated when installation starts.
- **Replace Microsoft Store edition** is opt-in and affects only the current Windows account.
- Downloads can be cancelled. Once Spotify setup or file replacement begins, the operation finishes before the app can be closed.
- The activity log can be saved locally. No analytics or telemetry are added by the application.

## Release watcher

`.github/workflows/watch-spotify.yml` runs `site/scripts/watch-official.mjs` every 30 minutes with two independent sensors, described in [docs/SPOTIFY_DOWNLOADS.md](docs/SPOTIFY_DOWNLOADS.md):

- **Permanent URLs (no account).** A changed ETag on `SpotifyFullSetupX64.exe` / `…ARM64.exe` means a new build; the file is downloaded from Spotify with `If-Match`, its PE `ProductVersion`, SHA-256 and SHA-1 are recorded in `site/data/official.json`, and the Pages build overlays that on the catalog and the installer feed.
- **Update service (optional).** With a `SPOTIFY_CREDENTIALS` secret (a librespot `credentials.json` produced once by `python3 site/scripts/probe-update-service.py --login`), `probe-update-service.py` asks `desktop-update/v2/update` what Spotify offers each platform and returns the version, Spotify's `http_prefix` and its `binary_hash`. Both sensors merge into one record per build; a build is `verified` when Spotify's hash matches the downloaded file and flagged as a `conflict` when it does not.
- **Archive (opt-in).** Set the `SPOTIFY_ARCHIVE_TAG` repository variable (for example `spotify-installers`) or tick the workflow input, and each captured file is attached to that release as `spotify_installer-<version>-<arch>.exe`. Older builds then keep a copy this repository's CI took from Spotify, with the hash logged; the app and the library list it as *GitHub archive*.

The watcher commits only when observations change and then dispatches the Pages workflow. Nothing in it requires the patch server or a Spotify account unless the secret is present.

## Reliability

The app verifies downloaded PE files, expected sizes when supplied, and Spotify's Authenticode publisher before launching setup. It waits for setup to exit and verifies the installed version and architecture. Patch files are staged before use; failed replacements restore a snapshot of the previous files. Reinstalling Spotify refreshes the original DLL backup so it does not keep an older version's backup.

These checks do not turn untested Spotify versions into supported versions. The latest upstream BlockTheSpot configuration is still checked, and an incompatible upstream minimum stops installation rather than silently changing the pinned default.

## Development

Install the SDK specified by `global.json` (.NET 10 LTS). There are no third-party application framework packages. Tests use xUnit; dependency versions are locked.

```sh
dotnet test tests/BlockTheSpot.Tests/BlockTheSpot.Tests.csproj -c Release
dotnet build src/BlockTheSpot.App/BlockTheSpot.App.csproj -c Release
node --test scripts/test-site.mjs
npm ci --prefix site
npm run build --prefix site
```

The core and its tests run on Windows, macOS, and Linux. WPF can be cross-compiled, but running the GUI requires Windows. Publish the standalone EXE on Windows:

```powershell
dotnet publish src/BlockTheSpot.App/BlockTheSpot.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

Architecture:

- `BlockTheSpot.Core`: catalog parsing with Spotify-first/mirror-fallback sources, typed version and link parsing, compatibility policy, streaming downloads, install orchestration (with or without the patch), and transactional patch/restore/discard. Windows operations use an injected interface, making failure paths testable.
- `BlockTheSpot.App`: native WPF Fluent UI, view model, and Windows process/signature/installation adapter.
- `tests`: regressions for catalog formats, pinned defaults, source fallback, custom input, Spotify-only installs, HTTP failures, partial downloads, cancellation, architecture checks, install ordering, backup refresh, and rollback.
- `site`: Astro 7 static version library, source validation and normalization, scheduled catalog updater, responsive search/filter UI, and versioned JSON endpoints. Node 24 is used in CI; dependencies are locked.

The previous Go/Walk implementation is preserved in git history.

## CI, releases, and GitHub Pages

These are independent workflows:

| Workflow | Trigger | Result |
|---|---|---|
| Installer CI | Commits/PRs touching app, tests, or build configuration | Linux/Windows tests, native UI smoke test (renders both themes and drives the version picker), EXE artifact; **no release** |
| Release installer | **Manual workflow dispatch** with an explicit version | Repeat all tests, build the self-contained EXE, publish `vX.Y.Z` with a SHA-256 checksum |
| Spotify download library | Commits/PRs touching `site/`, its test, or its workflow | Build and test Astro; deploy to Pages only on `main` |
| Catalog refresh | Every six hours, or manual Pages workflow dispatch | Fetch current metadata; commit, build and deploy **only if it changed** |
| Verify live Spotify download | Manual workflow dispatch | Download the pinned Spotify setup on Windows and verify its size and Authenticode publisher; never install it |
| Watch Spotify releases | Every 30 minutes, or manual dispatch | HEAD Spotify's permanent installer URLs (and, with credentials, query the update service); on a new build download it from Spotify, record version and hashes in `site/data/official.json`, optionally attach it to the `spotify-installers` release, then rebuild Pages |

To release, update `docs/RELEASE_NOTES.md`, then use **Actions → Release installer → Run workflow**, select `main`, and enter a version such as `0.4.1`. Alternatively:

```sh
gh workflow run release.yml --ref main -f version=0.4.1
```

Normal pushes and tags do not publish EXEs. Releases do not rebuild Pages. The page's stable “latest release” link and optional GitHub metadata fetch pick up new releases without deployment. The Pages settings must use **GitHub Actions** as the source. Scheduled refreshes deploy their own generated commit because a commit made with `GITHUB_TOKEN` does not trigger another push workflow. Failed or incomplete upstream responses retain the last published catalog. GitHub may delay scheduled runs or disable schedules on inactive repositories.

Windows CI renders actual WPF screenshots in light and dark themes and uploads them as `windows-ui-smoke`. It does not install Spotify on hosted runners. Full install/reinstall testing belongs on a Windows test account before changing the tested compatibility pin.

## Spotify version library and API

The library includes Windows x86/x64/ARM64, macOS Intel/Apple silicon, and Linux x64 packages where upstream lists them. Search by version or hash, filter platform/architecture/source, sort numerically, and share a filtered URL. The site works on mobile and has light/dark themes, keyboard navigation, pagination, and a useful first page without JavaScript.

Download origins are explicit:

- Current version-specific downloads are usually **LoadSpot-hosted mirrors**, not Spotify servers. The maintained catalog no longer provides official CDN URLs for these builds.
- Historical **Spotify CDN** URLs are retained exactly as published by LoaderSpot, including installer build suffixes. Spotify may stop serving them; they are not claimed to be availability-verified. Select a mirror when an official historical URL has expired.
- Current Windows and Mac shortcuts use Spotify's official `download.scdn.co` servers. The official Linux package is discovered from `repository.spotify.com`. The “Official Spotify only” filter never includes mirrors.

No Spotify binaries are stored in this repository or hosted on Pages. The catalog does not claim BlockTheSpot support for other platforms or newer Spotify builds.

| Endpoint | Contents |
|---|---|
| [`api/v1/catalog.json`](https://robyrew.github.io/BlockTheSpot-Installer/api/v1/catalog.json) | Full normalized catalog: schema version, last data change, tested pin, upstream sources, the watcher's `official.watched` ETags, and installers with explicit download origins and `sha256` where observed |
| [`api/v1/windows-x64.json`](https://robyrew.github.io/BlockTheSpot-Installer/api/v1/windows-x64.json) | Compact LoadSpot-compatible Windows x64 feed of every build, consumed by the native app: `url` is the stable mirror link; `official` is Spotify's own link (the permanent URL with its `etag` while the build is current, otherwise the historical `upgrade.scdn.co` link); `archive` the CI copy; `sha256` the hash the watcher took from Spotify's file |

The full feed's `entries` have `id`, `version`, `fullVersion`, `platform`, `architecture`, `format`, nullable `date`/`size`, `tested`, and `sources`. Each source has `url`, `kind` (`official` or `mirror`), and `label`. Dates are ISO dates and sizes are bytes. `updatedAt` changes only when the data changes, not on every scheduled check. Consumers should tolerate added fields; breaking changes require a new API path.

To refresh metadata locally and preview the site:

```sh
npm run catalog:update --prefix site
npm run build --prefix site
npm test --prefix site
npm run preview --prefix site
```

Astro serves the preview under `/BlockTheSpot-Installer/`. The site build uses the committed metadata snapshot, so ordinary builds do not depend on upstream servers being available. Refreshing data never changes the tested pin.

## License

MIT. Original installer and BlockTheSpot work by Nuzair46 and contributors; Spotify version discovery and catalog by LoaderSpot contributors. Spotify is a trademark of Spotify AB. This project is not affiliated with Spotify.
