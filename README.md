# BlockTheSpot Installer

A native Windows app for installing and restoring [BlockTheSpot](https://github.com/Nuzair46/BlockTheSpot). Built with .NET 10 WPF and Microsoft's Windows 11 Fluent theme.

[Download the installer](https://github.com/RobyRew/BlockTheSpot-Installer/releases/latest/download/BlockTheSpotInstaller.exe) · [Download page](https://robyrew.github.io/BlockTheSpot-Installer/) · [Release notes](https://github.com/RobyRew/BlockTheSpot-Installer/releases/latest)

## Install

1. Download `BlockTheSpotInstaller.exe` and open it normally, **without running as administrator**.
2. The default is **Spotify 1.2.93.667.g7b5cc0ce**, the latest tested compatible version for this release.
3. Leave **Install selected Spotify version before patching** on to install that version. Turn it off only to keep an already compatible installation.
4. Select **Install BlockTheSpot**. The app downloads and validates all required files before changing Spotify.
5. Use **Restore original Spotify** to undo the patch.

The EXE bundles the .NET runtime. It targets Windows x64, uses the system light/dark theme, and supports keyboard navigation and Windows scaling. Windows 11 is recommended; supported Windows 10 editions also work. Spotify and the patch must both be x64.

Support the artists you listen to. Please consider [Spotify Premium](https://www.spotify.com/premium/).

## Versions and options

- The tested default is pinned in `src/BlockTheSpot.Core/Compatibility.cs`. It does **not** follow the newest catalog entry or change automatically when an upstream script changes. Update this constant and its tests only after verifying a new version with BlockTheSpot.
- **Advanced → Show newer, untested Spotify versions** exposes newer LoadSpot builds and the latest official Spotify installer. These versions are not presented as compatible by default.
- The live [LoadSpot catalog](https://loadspot.pages.dev/versions) comes from [`LoaderSpot/table`](https://github.com/LoaderSpot/table/blob/main/table/versions.json). Its predecessor [`LoaderSpot/LoaderSpot`](https://github.com/LoaderSpot/LoaderSpot) is archived. The maintained catalog includes architecture-specific download URLs, dates, and sizes.
- **Refresh** reloads versions while preserving a valid selection. The known tested link remains available if catalog retrieval fails.
- **Replace Microsoft Store edition** is opt-in and affects only the current Windows account.
- Downloads can be cancelled. Once Spotify setup or file replacement begins, the operation finishes before the app can be closed.
- The activity log can be saved locally. No analytics or telemetry are added by the application.

## Reliability

The app verifies downloaded PE files, expected sizes when supplied, and Spotify's Authenticode publisher before launching setup. It waits for setup to exit and verifies the installed version and architecture. Patch files are staged before use; failed replacements restore a snapshot of the previous files. Reinstalling Spotify refreshes the original DLL backup so it does not keep an older version's backup.

These checks do not turn untested Spotify versions into supported versions. The latest upstream BlockTheSpot configuration is still checked, and an incompatible upstream minimum stops installation rather than silently changing the pinned default.

## Development

Install the SDK specified by `global.json` (.NET 10 LTS). There are no third-party application framework packages. Tests use xUnit; dependency versions are locked.

```sh
dotnet test tests/BlockTheSpot.Tests/BlockTheSpot.Tests.csproj -c Release
dotnet build src/BlockTheSpot.App/BlockTheSpot.App.csproj -c Release
node --test scripts/test-site.mjs
```

The core and its tests run on Windows, macOS, and Linux. WPF can be cross-compiled, but running the GUI requires Windows. Publish the standalone EXE on Windows:

```powershell
dotnet publish src/BlockTheSpot.App/BlockTheSpot.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

Architecture:

- `BlockTheSpot.Core`: catalog parsing, compatibility policy, streaming downloads, install orchestration, and transactional patch/restore. Windows operations use an injected interface, making failure paths testable.
- `BlockTheSpot.App`: native WPF Fluent UI, view model, and Windows process/signature/installation adapter.
- `tests`: regressions for catalog formats, pinned defaults, HTTP failures, partial downloads, cancellation, architecture checks, install ordering, backup refresh, and rollback.
- `site`: dependency-free static download page. Spotify buttons point directly to `download.scdn.co`; version-specific LoadSpot links are not labeled as official Spotify-hosted downloads.

The previous Go/Walk implementation is preserved in git history.

## CI, releases, and GitHub Pages

These are independent workflows:

| Workflow | Trigger | Result |
|---|---|---|
| Installer CI | Commits/PRs touching app, tests, or build configuration | Linux/Windows tests, native UI smoke test, EXE artifact; **no release** |
| Release installer | **Manual workflow dispatch** with an explicit version | Repeat all tests, build the self-contained EXE, publish `vX.Y.Z` with a SHA-256 checksum |
| Download page | Commits/PRs touching `site/`, its test, or its workflow | Validate the page; deploy to Pages only on `main` |

To release, update `docs/RELEASE_NOTES.md`, then use **Actions → Release installer → Run workflow**, select `main`, and enter a version such as `0.4.1`. Alternatively:

```sh
gh workflow run release.yml --ref main -f version=0.4.1
```

Normal pushes and tags do not publish EXEs. Releases do not rebuild Pages. The page's stable “latest release” link and optional GitHub metadata fetch pick up new releases without deployment. The Pages settings must use **GitHub Actions** as the source.

Windows CI renders actual WPF screenshots in light and dark themes and uploads them as `windows-ui-smoke`. It does not install Spotify on hosted runners. Full install/reinstall testing belongs on a Windows test account before changing the tested compatibility pin.

## License

MIT. Original installer and BlockTheSpot work by Nuzair46 and contributors; Spotify version discovery and catalog by LoaderSpot contributors. Spotify is a trademark of Spotify AB. This project is not affiliated with Spotify.
