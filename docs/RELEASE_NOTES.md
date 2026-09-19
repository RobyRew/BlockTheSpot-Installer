Two bundled BlockTheSpot kits, chosen by Spotify version.

- **Legacy and current kits ship inside the installer** as embedded resources; the patch files are no longer downloaded at install time. The release also attaches the current kit's `chrome_elf.dll`, `blockthespot.dll` and `config.ini` for manual installs, and `install.ps1` is served from GitHub Pages. A kit table maps each Spotify version to a kit: **legacy** (Spotify 1.2.70–1.2.95, the upstream Nuzair46 files verified with 1.2.93.667) and **current** (Spotify ≥ 1.2.96, adapted for 1.3.1.234). The newest kit in the table is always the current one and keeps taking every newer build; adding a future kit is a folder plus one table row.
- **The kit follows the Spotify that will run.** Reinstalling a build stages that build's kit; keeping the installed Spotify stages the kit for the installed version; if setup lands on a version in the other range, the kit is re-staged before patching. The version picker shows a Method chip (Current/Legacy) per build and a line naming the kit that will be applied.
- **Two pins by default:** 1.3.1.234.g59d6bf59 (current, default) and 1.2.93.667.g7b5cc0ce (legacy). Tick **All versions** for any other build; the patch floor is 1.2.70.
- The current kit's `blockthespot.dll` is the upstream 1.2.93.667 build with two IAT jumps NOP'd, and its `config.ini` carries xpui signatures rebuilt for 1.3.1.234. The legacy kit is upstream v1.2.93.667-build.8 byte for byte. Only the legacy build has been verified end to end.

**Downloads:** `BlockTheSpotInstaller.exe` and its SHA-256 checksum in `SHA256SUMS.txt`.

Open the app normally, without “Run as administrator.” The Spotify setup and patch target the current Windows account. The executable is not code-signed; Spotify's downloaded installer is signature-checked before it runs.

Verification: 83 core regression tests on both Linux and Windows, 23 catalog/site/watcher checks, and the native Windows startup/render test in both themes, which now also exercises the two pinned builds. The full install/reinstall process is not exercised on GitHub's hosted runners.
