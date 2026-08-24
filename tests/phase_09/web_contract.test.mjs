import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..', '..');
const webRoot = join(root, 'src', 'TkgPda.Pda', 'web');
const read = (name) => readFileSync(join(webRoot, name), 'utf8');

test('manifest defines a local installable TKG Operator PWA', () => {
  const manifest = JSON.parse(read('manifest.json'));
  assert.equal(manifest.name, 'TKG Operator');
  assert.equal(manifest.short_name, 'TKG Operator');
  assert.equal(manifest.display, 'standalone');
  assert.equal(manifest.start_url, '.');
  assert.equal(manifest.scope, '.');
  assert.equal(manifest.prefer_related_applications, false);
  assert.ok(manifest.icons.length >= 4);
  for (const icon of manifest.icons) {
    assert.equal(/^https?:/i.test(icon.src), false);
    assert.ok(existsSync(join(webRoot, icon.src)));
  }
});

test('bootstrap self-hosts renderer and fallback font resources', () => {
  const bootstrap = read('flutter_bootstrap.js');
  assert.match(bootstrap, /fontFallbackBaseUrl:\s*'font-fallbacks\/'/);
  assert.match(bootstrap, /operator_service_worker\.js/);
  assert.equal(/https?:\/\//i.test(bootstrap.replaceAll('fonts.gstatic.com', '')), false);

  const requiredFonts = [
    'font-fallbacks/roboto/v32/KFOmCnqEu92Fr1Me4GZLCzYlKw.woff2',
    'font-fallbacks/notosanssymbols/v43/rP2up3q65FkAtHfwd-eIS2brbDN6gxP34F9jRRCe4W3gfQ8gb_VFRkzrbQ.woff2',
  ];
  for (const font of requiredFonts) {
    assert.ok(existsSync(join(webRoot, font)));
  }
});

test('service worker caches static assets but never queues API commands', () => {
  const worker = read('operator_service_worker.js');
  assert.match(worker, /request\.method !== 'GET'/);
  assert.match(worker, /url\.pathname\.startsWith\('\/api\/'\)/);
  assert.match(worker, /request\.headers\.has\('Authorization'\)/);
  assert.match(worker, /request\.headers\.has\('X-Device-Code'\)/);
  assert.match(worker, /request\.headers\.has\('Idempotency-Key'\)/);
  assert.equal(/backgroundsync|\bsync\b|indexeddb|postmessage/i.test(worker), false);
  assert.equal(/catch\s*\(\s*\(\)\s*=>\s*fetch\(/i.test(worker), false);
});

test('Web source contains no direct RDS endpoint or production address', () => {
  const sources = [
    read('index.html'),
    read('flutter_bootstrap.js'),
    read('operator_service_worker.js'),
    read('operator-config.json'),
    read('manifest.json'),
  ].join('\n');
  assert.equal(/127\.0\.0\.1:8080|script-api|queryWindTask|stop-task/i.test(sources), false);
  assert.deepEqual(JSON.parse(read('operator-config.json')), { apiBaseUrl: '' });
});
