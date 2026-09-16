import { filterCatalog, selectSource, formatSize, platformNames } from './catalog.mjs';

const find = id => document.getElementById(id);
const controls = { query: find('search'), architecture: find('architecture'), source: find('source'), sort: find('sort') };
const tabs = [...document.querySelectorAll('[data-platform]')];
const allowed = { platform: ['all', 'windows', 'macos', 'linux'], architecture: ['all', 'x64', 'x86', 'arm64'], source: ['all', 'official', 'mirror'], sort: ['newest', 'oldest'] };
const params = new URLSearchParams(location.search);
const state = { query: params.get('q')?.slice(0, 100) ?? '', platform: 'windows', architecture: 'all', source: 'all', sort: 'newest' };
for (const key of Object.keys(allowed)) if (allowed[key].includes(params.get(key))) state[key] = params.get(key);
let entries = [], page = 0;
const pageSize = 30;

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text) node.textContent = text;
  if (className) node.className = className;
  return node;
}
function row(entry) {
  const tr = element('tr');
  const version = element('td');
  version.append(element('strong', entry.version));
  if (entry.tested) version.append(element('span', 'Tested for BTS', 'badge tested'));
  version.append(element('small', entry.fullVersion.split('.').at(-1), 'hash'));
  const platform = element('td');
  platform.append(element('span', platformNames[entry.platform], 'platform-label'), element('small', `${entry.architecture} · ${entry.format.toUpperCase()}`));
  const date = element('td');
  date.append(element('span', entry.date ?? 'Not listed'), element('small', formatSize(entry.size)));
  const source = selectSource(entry, state.source);
  const sourceCell = element('td');
  sourceCell.append(element('span', source.label, `source-label ${source.kind}`), element('small', source.kind === 'official' && source.label === 'Spotify CDN' ? 'Archived link · may expire' : source.kind === 'official' ? 'Direct download' : 'Community hosted'));
  const action = element('td', null, 'row-action');
  const download = element('a', 'Download ↗', 'download-link');
  download.href = source.url;
  download.setAttribute('aria-label', `Download Spotify ${entry.fullVersion} for ${platformNames[entry.platform]} ${entry.architecture} from ${source.label}`);
  action.append(download);
  tr.append(version, platform, date, sourceCell, action);
  return tr;
}
function syncControls() {
  for (const [key, control] of Object.entries(controls)) control.value = state[key];
  tabs.forEach(tab => tab.setAttribute('aria-pressed', String(tab.dataset.platform === state.platform)));
}
function render(updateUrl = true) {
  syncControls();
  const filtered = filterCatalog(entries, state);
  page = Math.max(0, Math.min(page, Math.ceil(filtered.length / pageSize) - 1));
  find('results').replaceChildren(...filtered.slice(page * pageSize, (page + 1) * pageSize).map(row));
  find('empty').hidden = filtered.length > 0;
  find('result-count').textContent = `${filtered.length.toLocaleString()} installer${filtered.length === 1 ? '' : 's'}${state.platform === 'all' ? ' across all platforms' : ` for ${platformNames[state.platform]}`}`;
  find('page-label').textContent = filtered.length ? `Showing ${page * pageSize + 1}–${Math.min((page + 1) * pageSize, filtered.length)} of ${filtered.length.toLocaleString()}` : 'No results';
  find('previous').disabled = page === 0;
  find('next').disabled = (page + 1) * pageSize >= filtered.length;
  if (updateUrl) {
    const query = new URLSearchParams();
    if (state.query) query.set('q', state.query);
    for (const [key, value] of Object.entries(state)) if (key !== 'query' && value !== (key === 'platform' ? 'windows' : key === 'sort' ? 'newest' : 'all')) query.set(key, value);
    history.replaceState(null, '', location.pathname + (query.size ? '?' + query : '') + location.hash);
  }
}
function reset() {
  Object.assign(state, { query: '', platform: 'windows', architecture: 'all', source: 'all', sort: 'newest' });
  page = 0; render();
}
async function load() {
  find('load-error').hidden = true;
  try {
    const response = await fetch(find('catalog').dataset.api, { signal: AbortSignal.timeout(15_000) });
    if (!response.ok) throw new Error('Catalog unavailable');
    const catalog = await response.json();
    if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.entries)) throw new Error('Unknown catalog');
    entries = catalog.entries;
    render(false);
  } catch { find('load-error').hidden = false; }
}
for (const [key, control] of Object.entries(controls)) control.addEventListener(key === 'query' ? 'input' : 'change', () => { state[key] = control.value; page = 0; if (entries.length) render(); });
tabs.forEach(tab => tab.addEventListener('click', () => { state.platform = tab.dataset.platform; page = 0; if (entries.length) render(); }));
find('reset').addEventListener('click', reset);
find('empty-reset').addEventListener('click', reset);
find('retry').addEventListener('click', load);
for (const [id, direction] of [['previous', -1], ['next', 1]]) find(id).addEventListener('click', () => { page += direction; render(); find('catalog').scrollIntoView({ block: 'start' }); });
find('theme-toggle').addEventListener('click', () => {
  const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = theme;
  try { localStorage.setItem('catalog-theme', theme); } catch {}
});
document.addEventListener('keydown', event => {
  if (event.key === '/' && !['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName)) { event.preventDefault(); find('search').focus(); }
});
syncControls();
load();
// The stable download URL and this optional label follow manual app releases
// without rebuilding the catalog site.
fetch('https://api.github.com/repos/RobyRew/BlockTheSpot-Installer/releases/latest', { signal: AbortSignal.timeout(4000) })
  .then(response => { if (!response.ok) throw new Error('Release metadata unavailable'); return response.json(); })
  .then(release => {
    if (/^v\d+\.\d+\.\d+$/.test(release.tag_name) && release.assets?.some(asset => asset.name === 'BlockTheSpotInstaller.exe'))
      find('app-version').textContent = release.tag_name;
  }).catch(() => {});
