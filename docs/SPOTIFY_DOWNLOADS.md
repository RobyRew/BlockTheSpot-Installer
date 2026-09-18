# Where Spotify installers come from

Findings from probing Spotify's distribution on 2026-09-17, recorded so the download policy in
`src/BlockTheSpot.Core/Catalog.cs` and `InstallerService.cs` can be re-checked instead of trusted.

## Spotify publishes exactly one permanent installer URL per platform

| URL | Serves |
|---|---|
| `https://download.scdn.co/SpotifyFullSetupX64.exe` | current Windows x64 full installer (also `SpotifyFullSetup.exe` for x86) |
| `https://download.scdn.co/Spotify.dmg`, `SpotifyARM64.dmg` | current macOS builds |
| `https://repository.spotify.com/pool/non-free/s/spotify-client/…` | Linux `.deb` packages, versioned, listed in the apt `Packages` index |

These links are unsigned, stable and always point at the newest build. On 2026-09-17 the x64 EXE
answered `200`, `Content-Length: 148153000`, `Last-Modified: Thu, 17 Sep 2026 15:26:53 GMT`. The
app exposes this as **Latest official Spotify**.

## Versioned Windows installers are handed out by the client update service, signed and short-lived

The desktop client does not poll a public list. It calls

```
GET https://spclient.wg.spotify.com/desktop-update/v2/update?client_version=<version>&ct=S
Authorization: Bearer <access token of a logged-in session>
Spotify-App-Version: <version the client claims to run>
App-Platform: Win32_x86_64 | Win32_ARM64 | OSX | OSX_ARM64
```

and receives a protobuf `UpdateQueryResponse` (`protocol/proto/client_update.proto` in librespot,
extracted from Spotify 1.2.52): `poll_interval` plus, when an upgrade applies, an
`UpgradeRequiredMessage { upgrade_signed_part, signature, http_suffix }`. The download URL is
`UpgradeSignedPart.http_prefix + http_suffix`, i.e.

```
https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-<full version>-<n>.exe?fauth=<token>
```

Consequences observed:

- The service only ever returns the *current* build for the claimed platform, never an archive.
  A specific older version cannot be requested from Spotify.
- The `?fauth=` query is a Fastly-style signed token. Without it `upgrade.scdn.co` answers
  `403 Forbidden` (Varnish error 54113) for every path, including paths that never existed.
  Sixteen historical `upgrade.scdn.co` links sampled from the catalog, from 1.2.9.741 up to
  1.2.85.519 (published 2026-03), all answered `403`; the same paths on `download.scdn.co` and
  `download.spotify.com` answered `404`.
- The trailing `-<n>` is not derivable from the version; older tooling (SpotifyUpgradeFinder)
  brute-forced it while the bucket was still public, which it no longer is.
- Community catalogs obtain new links from inside a logged-in client: SpotX's `checkVersion.js`
  calls the endpoint above with the captured bearer token for each platform and reports the
  resulting `upgrade.scdn.co…?fauth=` link to LoadSpot's ingest worker, which stores a copy behind
  `https://loadspot.amd64fox1.workers.dev/download/spotify_installer-<full version>-x64.exe`
  (Cloudflare, `200`, no redirect, `content-disposition` with the stable filename).

## What the installer does with this

1. **Latest official Spotify** downloads straight from `download.scdn.co`.
2. A versioned build keeps Spotify's `upgrade.scdn.co` link, when one was ever published, as the
   first attempt and the LoadSpot filename as the fallback. A failed request on the first link
   (403 today) moves on to the mirror and is logged as *Switching source*; a size or executable
   mismatch does not fall back.
3. A typed full version (`1.2.80.699.gd5f6ebe3`) or a link on either host becomes a choice through
   `SpotifyVersions.TryCustom`; a link on any other host is rejected.
4. Every downloaded setup must carry a valid Authenticode signature from `O=Spotify AB` or
   `O=Spotify USA Inc.` (`WindowsSpotifyPlatform.VerifySpotifyPublisherAsync`) before it runs, and
   the installed version must equal the selected one afterwards. That check, not the hostname, is
   what ties a mirror download to Spotify's own bytes.

Re-check with:

```sh
curl -sI https://download.scdn.co/SpotifyFullSetupX64.exe | head -5
curl -sI https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-1.2.85.519.g549a528b-4062.exe | head -1
curl -sI https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe | head -1
```

Obtaining a fresh signed link would require a logged-in Spotify session token inside the
installer; that was left out deliberately.
