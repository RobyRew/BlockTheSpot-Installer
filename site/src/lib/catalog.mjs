export const TESTED_VERSION = '1.2.93.667.g7b5cc0ce';
export const LIVE_CATALOG = 'https://raw.githubusercontent.com/LoaderSpot/table/main/table/versions.json';
export const LEGACY_CATALOG = 'https://raw.githubusercontent.com/LoaderSpot/LoaderSpot/main/versions.json';
export const LINUX_PACKAGES = 'https://repository.spotify.com/dists/stable/non-free/binary-amd64/Packages';
const versionPattern = /^1\.\d{1,5}\.\d{1,5}\.\d{1,8}\.g[0-9a-f]{8,40}$/i;
const targets = [
  ['win', 'x64', 'windows', 'x64', 'win32-x86_64', 'exe'],
  ['win', 'x86', 'windows', 'x86', 'win32-x86', 'exe'],
  ['win', 'arm64', 'windows', 'arm64', 'win32-arm64', 'exe'],
  ['mac', 'intel', 'macos', 'x64', 'osx-x86_64', 'tbz'],
  ['mac', 'arm64', 'macos', 'arm64', 'osx-arm64', 'tbz'],
  ['linux', 'amd64', 'linux', 'x64', '', 'deb'],
];

export function compareVersions(a, b) {
  const left = a.split('.').slice(0, 4).map(Number);
  const right = b.split('.').slice(0, 4).map(Number);
  for (let i = 0; i < 4; i++) if (left[i] !== right[i]) return left[i] - right[i];
  return 0;
}

function isoDate(value) {
  if (typeof value !== 'string' || !/^\d{2}\.\d{2}\.\d{4}$/.test(value)) return null;
  const result = value.split('.').reverse().join('-');
  const date = new Date(result);
  return Number.isFinite(+date) && date.toISOString().startsWith(result) ? result : null;
}

export function sourceFor(raw, fullVersion, platform, architecture) {
  if (!versionPattern.test(fullVersion)) return null;
  let url;
  try { url = new URL(raw); } catch { return null; }
  if (url.protocol !== 'https:' || url.username || url.password || url.port || url.search || url.hash) return null;
  const target = targets.find(([, , p, a]) => p === platform && a === architecture);
  if (!target) return null;
  const [, , , , directory, format] = target;
  const stem = platform === 'windows' ? `spotify_installer-${fullVersion}` : `spotify-autoupdate-${fullVersion}`;
  if (url.hostname === 'upgrade.scdn.co' && platform !== 'linux') {
    const prefix = `/upgrade/client/${directory}/${stem}-`;
    if (url.pathname.startsWith(prefix) && new RegExp(`^\\d+\\.${format}$`).test(url.pathname.slice(prefix.length)))
      return { url: url.href, kind: 'official', label: 'Spotify CDN' };
  }
  if (url.hostname === 'repository.spotify.com' && platform === 'linux' &&
      url.pathname === `/pool/non-free/s/spotify-client/spotify-client_${fullVersion}_amd64.deb`)
    return { url: url.href, kind: 'official', label: 'Spotify repository' };
  if (url.hostname === 'loadspot.amd64fox1.workers.dev') {
    const suffix = platform === 'macos' && architecture === 'x64' ? 'x86_64' : architecture;
    const file = platform === 'linux' ? `spotify-client_${fullVersion}_amd64.deb` : `${stem}-${suffix}.${format}`;
    if (url.pathname === `/download/${file}`) return { url: url.href, kind: 'mirror', label: 'LoadSpot mirror' };
  }
  return null;
}

export function normalizeCatalog(catalog) {
  if (!catalog || typeof catalog !== 'object' || Array.isArray(catalog)) throw new Error('Expected a version catalog object');
  const entries = [];
  for (const [version, entry] of Object.entries(catalog)) {
    if (!entry || typeof entry !== 'object' || !versionPattern.test(entry.fullversion ?? '') ||
        entry.fullversion.split('.').slice(0, 4).join('.') !== version ||
        (entry.buildType && entry.buildType.toLowerCase() !== 'release')) continue;
    for (const [group, key, platform, architecture, , format] of targets) {
      const asset = entry[group]?.[key] ?? entry.links?.[group]?.[key];
      const url = typeof asset === 'string' ? asset : asset?.url;
      const source = sourceFor(url, entry.fullversion, platform, architecture);
      if (!source) continue;
      entries.push({
        id: `${entry.fullversion}-${platform}-${architecture}`,
        version, fullVersion: entry.fullversion, platform, architecture, format,
        date: isoDate(asset?.date), size: Number.isSafeInteger(asset?.size) && asset.size > 0 ? asset.size : null,
        tested: platform === 'windows' && architecture === 'x64' && entry.fullversion.toLowerCase() === TESTED_VERSION,
        sources: [source],
      });
    }
  }
  return entries;
}

export function parseLinuxPackages(text) {
  const catalog = {};
  for (const paragraph of text.trim().split(/\r?\n\s*\r?\n/)) {
    const fields = Object.fromEntries(paragraph.split(/\r?\n/).filter(line => /^[A-Za-z0-9-]+: /.test(line))
      .map(line => [line.slice(0, line.indexOf(':')), line.slice(line.indexOf(':') + 2)]));
    const full = fields.Version?.replace(/^\d+:/, '');
    if (fields.Package !== 'spotify-client' || fields.Architecture !== 'amd64' || !versionPattern.test(full ?? '')) continue;
    const url = new URL(fields.Filename, 'https://repository.spotify.com/').href;
    if (!sourceFor(url, full, 'linux', 'x64')) continue;
    const version = full.split('.').slice(0, 4).join('.');
    catalog[version] = { fullversion: full, linux: { amd64: { url, size: Number(fields.Size) } } };
  }
  return normalizeCatalog(catalog);
}

export function mergeCatalogs(...catalogs) {
  const merged = new Map();
  for (const entry of catalogs.flat()) {
    const previous = merged.get(entry.id);
    const sources = new Map([...(previous?.sources ?? []), ...entry.sources].map(source => [source.url, source]));
    merged.set(entry.id, { ...entry, date: entry.date ?? previous?.date ?? null, size: entry.size ?? previous?.size ?? null,
      sources: [...sources.values()].sort((a, b) => a.kind.localeCompare(b.kind) || a.url.localeCompare(b.url)) });
  }
  const platforms = ['windows', 'macos', 'linux'];
  const architectures = ['x64', 'arm64', 'x86'];
  return [...merged.values()].sort((a, b) => compareVersions(b.version, a.version) ||
    platforms.indexOf(a.platform) - platforms.indexOf(b.platform) ||
    architectures.indexOf(a.architecture) - architectures.indexOf(b.architecture) || a.id.localeCompare(b.id));
}

export function selectSource(entry, kind = 'all') {
  const sources = entry.sources.filter(source => kind === 'all' || source.kind === kind);
  // Recent LoadSpot links are maintained. Old Spotify CDN URLs may have expired.
  return sources.find(source => source.kind === 'official' && source.label === 'Spotify repository')
    ?? sources.find(source => source.kind === 'mirror') ?? sources[0];
}

export function filterCatalog(entries, { query = '', platform = 'all', architecture = 'all', source = 'all', sort = 'newest' } = {}) {
  const search = query.trim().toLowerCase();
  const result = entries.filter(entry => (!search || entry.fullVersion.toLowerCase().includes(search)) &&
    (platform === 'all' || entry.platform === platform) && (architecture === 'all' || entry.architecture === architecture) &&
    !!selectSource(entry, source));
  return sort === 'oldest' ? result.toReversed() : result;
}

export function windowsFeed(entries) {
  return Object.fromEntries(entries.filter(entry => entry.platform === 'windows' && entry.architecture === 'x64' &&
    compareVersions(entry.version, TESTED_VERSION) >= 0).map(entry => {
    const source = selectSource(entry);
    return [entry.version, { fullversion: entry.fullVersion, win: { x64: {
      url: source.url, ...(entry.date ? { date: entry.date.split('-').reverse().join('.') } : {}), size: entry.size ?? 0,
    } } }];
  }));
}

export function formatSize(bytes) { return bytes ? `${(bytes / 1048576).toFixed(1)} MB` : '—'; }
export const platformNames = { windows: 'Windows', macos: 'macOS', linux: 'Linux' };
