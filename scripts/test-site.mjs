import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import { TESTED_VERSION, normalizeCatalog, sourceFor, mergeCatalogs, parseLinuxPackages, filterCatalog, selectSource, windowsFeed, compareVersions } from '../site/src/lib/catalog.mjs';
const catalog = JSON.parse(await readFile(new URL('../site/data/catalog.json', import.meta.url), 'utf8'));
const fixture = JSON.parse(await readFile(new URL('../testdata/loadspot_versions.json', import.meta.url), 'utf8'));
const entries = normalizeCatalog(fixture);
const html = await readFile(new URL('../site/src/pages/index.astro', import.meta.url), 'utf8');

test('Published catalog includes the full library and all supported architectures', () => {
  assert.equal(catalog.schemaVersion, 1);
  assert.ok(catalog.entries.length > 1000);
  assert.equal(new Set(catalog.entries.map(entry => entry.id)).size, catalog.entries.length);
  assert.deepEqual([...new Set(catalog.entries.map(entry => entry.platform))].sort(), ['linux', 'macos', 'windows']);
  assert.deepEqual([...new Set(catalog.entries.map(entry => entry.architecture))].sort(), ['arm64', 'x64', 'x86']);
  assert.equal(catalog.testedVersion, TESTED_VERSION);
  assert.equal(catalog.entries.filter(entry => entry.tested).length, 1);
});
test('Every download has an allowed HTTPS host, exact version, platform and architecture', () => {
  for (const entry of catalog.entries) for (const source of entry.sources)
    assert.deepEqual(sourceFor(source.url, entry.fullVersion, entry.platform, entry.architecture), source);
});
test('Rejects unsafe or mismatched source URLs instead of manufacturing a download', () => {
  const full = TESTED_VERSION;
  for (const url of [
    `http://loadspot.amd64fox1.workers.dev/download/spotify_installer-${full}-x64.exe`,
    `https://loadspot.amd64fox1.workers.dev.evil.test/download/spotify_installer-${full}-x64.exe`,
    `https://user@loadspot.amd64fox1.workers.dev/download/spotify_installer-${full}-x64.exe`,
    `https://loadspot.amd64fox1.workers.dev/download/spotify_installer-${full}-arm64.exe`,
    `https://loadspot.amd64fox1.workers.dev/download/spotify_installer-${full}-x64.exe?redirect=evil`,
    'javascript:alert(1)',
  ]) assert.equal(sourceFor(url, full, 'windows', 'x64'), null);
});
test('Normalizes live metadata, ignores malformed and non-release builds', () => {
  const tested = entries.find(entry => entry.tested);
  assert.ok(tested);
  assert.equal(tested.size, 146096232);
  assert.equal(tested.date, '2026-07-01');
  assert.equal(tested.sources[0].kind, 'mirror');
  assert.equal(normalizeCatalog({ broken: {}, '1.2.93.667': { ...fixture['1.2.93.667'], buildType: 'Master' } }).length, 0);
  assert.throws(() => normalizeCatalog([]));
});
test('Merge preserves exact historic Spotify URLs and maintained mirror alternatives', () => {
  const tested = entries.find(entry => entry.tested);
  const official = { ...tested, date: null, size: null, sources: [{ url: `https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-${TESTED_VERSION}-1234.exe`, kind: 'official', label: 'Spotify CDN' }] };
  const merged = mergeCatalogs([official], [tested]);
  assert.equal(merged.length, 1);
  assert.equal(merged[0].sources.length, 2);
  assert.equal(selectSource(merged[0]).kind, 'mirror');
  assert.equal(selectSource(merged[0], 'official').kind, 'official');
  assert.equal(merged[0].size, tested.size);
  assert.deepEqual(mergeCatalogs(merged, [official], [tested]), merged);
});
test('Official Linux repository metadata takes precedence over a mirror', () => {
  const linux = parseLinuxPackages(`Package: spotify-client\nArchitecture: amd64\nVersion: 1:${TESTED_VERSION}\nFilename: pool/non-free/s/spotify-client/spotify-client_${TESTED_VERSION}_amd64.deb\nSize: 1234\n\nPackage: fake-client\nVersion: 1.0.0.0.gabcdefab\n`);
  assert.equal(linux.length, 1);
  assert.equal(linux[0].sources[0].kind, 'official');
  assert.equal(linux[0].architecture, 'x64');
  assert.equal(linux[0].tested, false);
  assert.equal(parseLinuxPackages(`Package: spotify-client\nArchitecture: amd64\nVersion: ${TESTED_VERSION}\nFilename: https://evil.test/installer.deb`).length, 0);
});
test('Search combines version, platform, architecture and source filters', () => {
  const result = filterCatalog(catalog.entries, { query: 'G7B5CC0CE', platform: 'windows', architecture: 'x64', source: 'mirror' });
  assert.equal(result.length, 1);
  assert.equal(result[0].fullVersion, TESTED_VERSION);
  assert.equal(filterCatalog(catalog.entries, { query: 'no-such-version' }).length, 0);
  const official = filterCatalog(catalog.entries, { source: 'official', platform: 'linux' });
  assert.ok(official.length > 0);
  assert.ok(official.every(entry => selectSource(entry, 'official').url.startsWith('https://repository.spotify.com/')));
  assert.ok(filterCatalog(catalog.entries, { platform: 'linux', architecture: 'arm64' }).length === 0);
});
test('Sort uses numeric versions, and reversing never mutates the catalog', () => {
  assert.ok(compareVersions('1.2.100.1', '1.2.99.999') > 0);
  const newest = filterCatalog(catalog.entries);
  const oldest = filterCatalog(catalog.entries, { sort: 'oldest' });
  assert.deepEqual(oldest.toReversed(), newest);
  assert.equal(newest[0], catalog.entries[0]);
});
test('Installer feed lists every Windows x64 build with the mirror as url and Spotify as official', () => {
  const feed = windowsFeed(catalog.entries);
  assert.ok(JSON.stringify(feed).length < 150000);
  const windows = catalog.entries.filter(entry => entry.platform === 'windows' && entry.architecture === 'x64');
  assert.equal(Object.keys(feed).length, windows.length);
  assert.ok(windows.some(entry => compareVersions(entry.version, TESTED_VERSION) < 0), 'older builds are part of the feed');
  assert.equal(feed['1.2.93.667'].fullversion, TESTED_VERSION);
  let withOfficial = 0;
  for (const [version, entry] of Object.entries(feed)) {
    const { url, official } = entry.win.x64;
    assert.ok(sourceFor(url, entry.fullversion, 'windows', 'x64'));
    if (official) {
      withOfficial++;
      assert.equal(sourceFor(official, entry.fullversion, 'windows', 'x64').kind, 'official');
      assert.equal(sourceFor(url, entry.fullversion, 'windows', 'x64').kind, 'mirror');
    }
    assert.ok(version);
  }
  assert.ok(withOfficial > 100);
  const fixtureFeed = windowsFeed(entries);
  assert.deepEqual(Object.keys(fixtureFeed['1.2.85.519'].win.x64).sort(), ['date', 'size', 'url']);
});
test('Snapshot refresh is idempotent: unchanged metadata does not cause another deployment', () => {
  const merged = mergeCatalogs(catalog.entries, catalog.entries);
  assert.deepEqual(merged, catalog.entries);
});
test('Official latest links and release link are explicit, with accessible page controls', () => {
  const links = [...html.matchAll(/data-official-download href="([^"]+)"/g)];
  assert.equal(links.length, 3);
  for (const [, link] of links) assert.equal(new URL(link).hostname, 'download.scdn.co');
  assert.ok(html.includes('/releases/latest/download/BlockTheSpotInstaller.exe'));
  assert.ok(html.includes('skip-link'));
  assert.ok(html.includes('aria-label="Main navigation"'));
  assert.ok(html.includes('aria-live="polite"'));
  assert.ok(html.includes('Official Spotify only'));
  assert.ok(html.includes('Older official CDN links may have expired'));
});
let built = false;
try { await access(new URL('../site/dist/index.html', import.meta.url)); built = true; } catch {}
test('Built Pages APIs match source data and use the correct repository base path', { skip: !built }, async () => {
  const full = JSON.parse(await readFile(new URL('../site/dist/api/v1/catalog.json', import.meta.url), 'utf8'));
  const compact = JSON.parse(await readFile(new URL('../site/dist/api/v1/windows-x64.json', import.meta.url), 'utf8'));
  assert.deepEqual(full, catalog);
  assert.deepEqual(compact, windowsFeed(catalog.entries));
  const builtHtml = await readFile(new URL('../site/dist/index.html', import.meta.url), 'utf8');
  assert.ok(builtHtml.includes('data-api="/BlockTheSpot-Installer/api/v1/catalog.json"'));
  assert.ok(builtHtml.includes('href="/BlockTheSpot-Installer/favicon.svg"'));
});
