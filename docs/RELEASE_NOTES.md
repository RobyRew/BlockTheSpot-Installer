A native Windows app with a clearer install flow and a fixed, tested Spotify default.

- New Windows 11 Fluent interface with system light/dark appearance, scalable text, keyboard navigation, and a collapsible activity log.
- Spotify **1.2.93.667.g7b5cc0ce** is the default and latest tested compatible version. Newer builds require an explicit Advanced option.
- Current Spotify versions come from the live LoadSpot catalog. Version-specific links retain their exact architecture and file size.
- Download validation, Spotify publisher verification, staged patch files, rollback on replacement failure, and refreshed backups after reinstalling Spotify.
- Explicit Microsoft Store replacement, cancellation during downloads, visible progress, and log export.
- Self-contained Windows x64 EXE; no separate .NET installation needed.
- GitHub Pages download page with links directly to Spotify's official download servers.

**Downloads:** `BlockTheSpotInstaller.exe` and its SHA-256 checksum in `SHA256SUMS.txt`.

Open the app normally, without “Run as administrator.” The Spotify setup and patch target the current Windows account. The executable is not code-signed; Spotify's downloaded installer is signature-checked before it runs.

Automated verification includes core regression tests on Linux and Windows, a native Windows startup/render test in light and dark themes, and a release checksum. A real Spotify installation is not performed on GitHub's hosted runners.
