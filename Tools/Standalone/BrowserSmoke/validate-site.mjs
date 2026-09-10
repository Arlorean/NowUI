import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';

const site = path.resolve(process.argv[2] || '');
assert(process.argv[2], 'Usage: node validate-site.mjs <published-site> [--scaffold]');
for (const name of ['index.html', 'main.js', 'nowui-gl.js', 'nowui-input.js', '_framework/dotnet.js',
  'nowui-build.json', 'nowui-assets.json', 'THIRD_PARTY_NOTICES.md']) {
  assert((await fs.stat(path.join(site, name))).size > 0, `Missing or empty ${name}`);
}
const html = await fs.readFile(path.join(site, 'index.html'), 'utf8');
assert(html.includes('id="nowui-canvas"') && html.includes('src="./main.js"'), 'Missing C# browser host entry');
const build = JSON.parse(await fs.readFile(path.join(site, 'nowui-build.json'), 'utf8'));
const assets = JSON.parse(await fs.readFile(path.join(site, 'nowui-assets.json'), 'utf8'));
assert.equal(build.target, 'web');
assert.equal(typeof build.scene, 'string');
if (process.argv.includes('--aot')) assert.equal(build.aot, true, 'Expected an AOT publish');
assert(assets.hostFiles > 0 && assets.bytes > 0, 'Bundled text/material resources must be included');
assert.equal(assets.files.length, assets.hostFiles + assets.projectFiles);
assert.equal(new Set(assets.files.map(file => file.path)).size, assets.files.length, 'Duplicate asset paths');
if (process.argv.includes('--scaffold')) {
  assert.equal(build.scene, 'PreviewScene');
  assert.equal(assets.projectFiles, 0, 'Fresh init must not require Unity project assets');
}
const framework = await fs.readdir(path.join(site, '_framework'));
const wasm = framework.filter(name => name.endsWith('.wasm'));
assert(wasm.length > 0, 'No WebAssembly output');
for (const name of wasm) {
  const file = await fs.open(path.join(site, '_framework', name));
  try {
    const header = Buffer.alloc(4);
    assert.equal((await file.read(header, 0, 4, 0)).bytesRead, 4);
    assert.deepEqual(header, Buffer.from([0, 97, 115, 109]), `Invalid WASM header: ${name}`);
  } finally { await file.close(); }
}
let compressedFiles = 0;
for (const entry of await fs.readdir(site, { recursive: true, withFileTypes: true })) {
  if (!entry.isFile() || !/\.(br|gz)$/.test(entry.name)) continue;
  const fullName = path.join(entry.parentPath, entry.name);
  const original = fullName.replace(/\.(br|gz)$/, '');
  const compressed = await fs.readFile(fullName);
  const decoded = entry.name.endsWith('.br') ? brotliDecompressSync(compressed) : gunzipSync(compressed);
  assert(decoded.equals(await fs.readFile(original)), `Compressed output differs from original: ${fullName}`);
  compressedFiles++;
}
assert(compressedFiles > 0, 'Published browser output should include compressed transfer variants');
console.log(JSON.stringify({ site, scene: build.scene, wasmFiles: wasm.length,
  projectFiles: assets.projectFiles, hostFiles: assets.hostFiles, assetBytes: assets.bytes, compressedFiles }, null, 2));
