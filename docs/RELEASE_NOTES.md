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
