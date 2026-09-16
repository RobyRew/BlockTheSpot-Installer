<center>
	<h1 align="center">BlockTheSpot Installer</h1> 
   <h4 align="center">Official installer for a multi-purpose adblocker and skip-bypass for the <strong>Spotify for Windows (64 bit)</strong> </h4>
   <h5 align="center">Please support Spotify by purchasing premium</h5>
   <p align="center">
     <a href="https://github.com/Nuzair46/BlockTheSpot-Installer/releases"><img src="https://github.com/Nuzair46/BlockTheSpot-Installer/blob/main/assets/blockthespot.png" /></a>
   </p>
</center>

[![Build status](https://github.com/Nuzair46/BlockTheSpot-Installer/actions/workflows/ci-release.yml/badge.svg?branch=main)](https://github.com/Nuzair46/BlockTheSpot-Installer/actions/workflows/ci-release.yml)  [![Discord](https://discord.com/api/guilds/807273906872123412/widget.png)](https://discord.gg/eYudMwgYtY) <img src="https://img.shields.io/github/downloads/Nuzair46/blockthespot-installer/total.svg" />

Official installer for [BlockTheSpot](https://github.com/Nuzair46/BlockTheSpot).

## Install

1. Download latest [BlockTheSpotInstaller.exe](https://github.com/Nuzair46/BlockTheSpot-Installer/releases/latest/download/BlockTheSpotInstaller.exe).
2. Close Spotify if it is running.
3. Run `BlockTheSpotInstaller.exe`.
4. Choose one action:
   - `Install / Patch` to install or update BlockTheSpot.
   - `Uninstall / Restore` to remove BlockTheSpot and restore original `chrome_elf.dll` when backup exists.
5. Choose the Spotify Windows x64 version you want to install. Versions come from the maintained [LoadSpot catalog](https://loadspot.pages.dev/versions), using the [same JSON as its website](https://github.com/LoaderSpot/table/blob/main/table/versions.json). The list shows the minimum version from BlockTheSpot's `config.ini` and newer versions, including their dates and sizes. The exact recommended version is preselected when available; otherwise, the closest newer version is selected and labeled accordingly. Explicitly marked development builds and other architectures are excluded.
6. Enable `Update or reinstall Spotify before patching` when you want to install the selected Spotify version before patching.
7. If `Launch Spotify and close installer after completion` is enabled, Spotify starts and the installer closes automatically.

### Version and download options

- **Refresh versions** reloads the catalog and the supported minimum without restarting the installer. It preserves your selection when it is still available.
- **Latest official Spotify x64** downloads Spotify's current full installer directly. Enable the update/reinstall checkbox to replace an already installed version. This option remains available if the catalog cannot be loaded.
- Pinned versions use the exact download URLs supplied by LoadSpot, including its download service and legacy Spotify CDN links. The installer no longer scrapes Uptodown or constructs its token-based download URLs.
- If a pinned download returns 403, 404, or 410, refresh the list or explicitly choose the latest official installer. The installer does not silently substitute another version.
- Empty responses, HTML error pages, truncated transfers, and downloads that do not match the catalog's file size are rejected. Before patching, the installed Spotify version must meet the supported minimum and match any pinned selection.

LoadSpot's live website is maintained in [`LoaderSpot/table`](https://github.com/LoaderSpot/table). The older [`LoaderSpot/LoaderSpot`](https://github.com/LoaderSpot/LoaderSpot) repository contains the original installer discovery work, but its archived `versions.json` is no longer current.

## Development

### Tests

The catalog, version checks, and HTTP download tests run on Windows, macOS, and Linux. The normal suite uses local HTTP servers and a small fixture from the live LoadSpot catalog, including the `1.2.93.667.g7b5cc0ce` download regression.

```bash
go test -race -count=1 ./...
go vet ./...
```

To verify the current upstream catalog and BlockTheSpot configuration, opt into the network smoke test:

```bash
BLOCKTHESPOT_LIVE_TEST=1 go test -run TestLiveLoadSpotCatalog -v -count=1 .
```

On Windows, generate the app resources below before running tests; `go test -count=1 ./...` does not require the C compiler needed by the race detector. CI runs the portable suite with the race detector on Linux, then tests, vets, and builds the installer on Windows. A Windows install/reinstall smoke test is still needed to verify the Spotify setup process and native GUI.

### Prerequisite (before local build)

Generate the Windows app resources object (required for `walk`):

```bash
go run github.com/akavel/rsrc@v0.10.2 -manifest assets/app.manifest -ico assets/blockthespot.ico -arch amd64 -o windows_app_resources_amd64.syso
```

### Build locally (on Windows)

```powershell
go build -trimpath -ldflags="-H=windowsgui -X main.installerVersion=v1.0.0" -o BlockTheSpotInstaller.exe .
```

### Cross-build from Linux/macOS

```bash
GOOS=windows GOARCH=amd64 go build -trimpath -ldflags="-H=windowsgui -X main.installerVersion=v1.0.0" -o BlockTheSpotInstaller.exe .
```

### If you update the icon PNG

Rebuild the `.ico`, then regenerate app resources:

```bash
convert assets/blockthespot.png -define icon:auto-resize=256,128,64,48,32,16 assets/blockthespot.ico
go run github.com/akavel/rsrc@v0.10.2 -manifest assets/app.manifest -ico assets/blockthespot.ico -arch amd64 -o windows_app_resources_amd64.syso
```
