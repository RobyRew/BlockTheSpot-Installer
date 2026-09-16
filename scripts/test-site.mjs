import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const html = await readFile(new URL('../site/index.html', import.meta.url), 'utf8');
test('Spotify download buttons point only to the official HTTPS CDN', () => {
  const buttons = [...html.matchAll(/<a\b[^>]*href="([^"]+)"[^>]*data-official-download[^>]*>/g)];
  assert.equal(buttons.length, 2);
  for (const [, link] of buttons) {
    const url = new URL(link);
    assert.equal(url.protocol, 'https:');
    assert.equal(url.hostname, 'download.scdn.co');
    assert.equal(url.search, '');
  }
});
test('The app download follows explicit GitHub Releases without rebuilding the site', () => {
  assert.ok(html.includes('https://github.com/RobyRew/BlockTheSpot-Installer/releases/latest/download/BlockTheSpotInstaller.exe'));
});
test('Tested compatibility and official latest downloads are distinguished', () => {
  assert.ok(html.includes('1.2.93.667.g7b5cc0ce'));
  assert.ok(html.includes('latest release may be newer'));
});
test('Local resources exist and the page has accessible navigation', async () => {
  for (const file of ['styles.css', 'app.js']) assert.ok((await readFile(new URL('../site/' + file, import.meta.url))).length > 0);
  assert.ok(html.includes('class="skip-link"'));
  assert.ok(html.includes('aria-label="Main navigation"'));
});
