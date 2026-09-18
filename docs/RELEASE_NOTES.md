Any Spotify version, Spotify's servers first, and a single-window layout.

- **Any version.** *All versions* lists every Windows x64 build in the catalog, older than the tested one included, with a filter box. A typed full version (`1.2.80.699.gd5f6ebe3`) or a link on Spotify's `upgrade.scdn.co` or the LoadSpot mirror becomes an installable *Custom* choice. The tested default **1.2.93.667.g7b5cc0ce** is unchanged and still the only build presented as compatible.
- **Spotify-only installs.** Turning off *Apply BlockTheSpot patch* installs the selected Spotify version without touching its files, removes previous patch files, and never contacts the patch server. The patch keeps refusing versions below BlockTheSpot's published minimum.
- **Spotify's servers first.** Where the catalog has Spotify's own `upgrade.scdn.co` link for a build, it is tried before the LoadSpot mirror; a failed request switches to the mirror and is logged as *Switching source*. Spotify only hands out those links to logged-in clients as signed, expiring URLs, so the fallback is expected for older builds; the findings are in `docs/SPOTIFY_DOWNLOADS.md`. Every setup is still checked for Spotify's Authenticode signature before it runs.
- **Compact window.** Installed Spotify in the header, version picker, four one-line options with hover details, status and actions in one fixed-width window that sizes to its content; the activity log toggles below the buttons. The install button reads *Install Spotify only* when the patch is off.
- **Release watcher.** A new workflow checks Spotify's permanent installer URLs every 30 minutes without any account; a changed ETag means a new build, which is downloaded from Spotify, identified from its PE version resource and hashed. With a stored Spotify session it also asks the desktop update service what Spotify offers and cross-checks Spotify's `binary_hash` against the file. Observations land in `site/data/official.json`; captured files can be attached to a `spotify-installers` release as CI-attested copies.
- **Checksums.** Builds the watcher observed carry a SHA-256 taken from Spotify's own file; the app hashes the whole download and refuses a mismatch before the Authenticode check. The tested build is pinned to its hash as well. Spotify's permanent URL is requested with `If-Match`, so a build that has rotated answers 412 and the app moves to the next source instead of running the wrong installer.
- The Pages feed `api/v1/windows-x64.json` now lists every build and adds `official` (with `etag` while current), `archive` and `sha256` next to the mirror `url`. The upstream LoadSpot table is still accepted as a fallback.
- The Windows UI smoke test now also lists all versions, filters, types a version, and switches modes before rendering both themes.

**Downloads:** `BlockTheSpotInstaller.exe` and its SHA-256 checksum in `SHA256SUMS.txt`.

Open the app normally, without “Run as administrator.” The Spotify setup and patch target the current Windows account. The executable is not code-signed; Spotify's downloaded installer is signature-checked before it runs.

Verification: 66 core regression tests on both Linux and Windows, 19 catalog/site/watcher checks, and the native Windows startup/render test in both themes. The full install/reinstall process is not exercised on GitHub's hosted runners.

---

A native Windows app with a clearer install flow and a fixed, tested Spotify default.

- New Windows 11 Fluent interface with system light/dark appearance, scalable text, keyboard navigation, and a collapsible activity log.
- Spotify **1.2.93.667.g7b5cc0ce** is the default and latest tested compatible version. Newer builds require an explicit Advanced option.
- Spotify versions load from a compact GitHub Pages feed with a bounded fallback to the live LoadSpot catalog. Version-specific links retain their exact architecture and file size.
- Download validation, Spotify publisher verification, staged patch files, rollback on replacement failure, and refreshed backups after reinstalling Spotify.
- Explicit Microsoft Store replacement, cancellation during downloads, visible progress, and log export.
- Self-contained Windows x64 EXE; no separate .NET installation needed.
- [Astro download library](https://robyrew.github.io/BlockTheSpot-Installer/) with Windows, macOS, and Linux versions; architecture and source filters; search; light/dark themes; and static JSON APIs. Checks for new versions every six hours and rebuilds only when catalog data changes.
- Official Spotify-hosted links and community mirrors are clearly distinguished. Older official CDN links may no longer be available; recent version-specific downloads use the maintained LoadSpot mirror.

**Downloads:** `BlockTheSpotInstaller.exe` and its SHA-256 checksum in `SHA256SUMS.txt`.

Open the app normally, without “Run as administrator.” The Spotify setup and patch target the current Windows account. The executable is not code-signed; Spotify's downloaded installer is signature-checked before it runs.

Verification: 34 core regression tests on both Linux and Windows, 12 catalog/site checks, native Windows startup/render tests in both themes, and a verified release checksum. A Windows runner also downloaded Spotify 1.2.93.667.g7b5cc0ce and verified its expected size and valid Spotify Authenticode publisher. The full install/reinstall process is not exercised on GitHub's hosted runners.
