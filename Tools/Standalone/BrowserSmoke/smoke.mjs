import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';

const url = process.argv[2];
assert(url && /^https?:\/\//.test(url), 'Usage: node smoke.mjs <http-url> <artifact-directory> [--scaffold|--playground]');
const playground = process.argv.includes('--playground');
const scaffold = process.argv.includes('--scaffold');
assert(!(playground && scaffold), 'Choose either --scaffold or --playground');
const viewport = playground ? { width: 1120, height: 780 } : { width: 960, height: 640 };
const target = new URL(url);
assert(!target.searchParams.has('time'), 'This smoke requires the live frame loop, not a fixed capture time');
target.searchParams.set('capture', '1');
target.searchParams.set('dpr', '1');
assert(process.argv[3], 'An artifact directory is required');
const output = path.resolve(process.argv[3]);
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport, deviceScaleFactor: 1, locale: 'en-US' });
await context.addInitScript(() => {
  performance.setResourceTimingBufferSize(16384);
  window.__nowuiResourceTimingOverflow = false;
  performance.addEventListener('resourcetimingbufferfull', () => { window.__nowuiResourceTimingOverflow = true; });
});
const page = await context.newPage();
const failures = [], messages = [];
function observe(current) {
  current.on('pageerror', error => failures.push(String(error.stack || error)));
  current.on('console', message => {
    messages.push({ type: message.type(), text: message.text() });
    if (message.type() === 'error' && !message.location().url.endsWith('/favicon.ico')) failures.push(message.text());
  });
  current.on('response', response => {
    if (response.status() >= 400 && !new URL(response.url()).pathname.endsWith('/favicon.ico'))
      failures.push(`HTTP ${response.status()}: ${response.url()}`);
  });
  current.on('requestfailed', request => failures.push(`${request.failure()?.errorText}: ${request.url()}`));
}
observe(page);
let timing = null, network = null;

async function frames(count) {
  const start = await page.evaluate(() => window.__nowuiDiagnostics.frames);
  await page.waitForFunction(target => window.__nowuiDiagnostics.error || window.__nowuiDiagnostics.frames >= target,
    start + count, { timeout: 30000 });
  assert.equal(await page.evaluate(() => window.__nowuiDiagnostics.error), null);
}
try {
  await page.goto(target.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await page.waitForFunction(() => ['ready', 'failed'].includes(document.documentElement.dataset.nowuiStatus), null, { timeout: 120000 });
  assert.equal(await page.evaluate(() => document.documentElement.dataset.nowuiStatus), 'ready',
    await page.locator('#nowui-status').textContent());
  await frames(30);
  // A fresh context makes this a cold load; record before screenshots, controls, or the second scene.
  network = await page.evaluate(() => {
    const resources = [...performance.getEntriesByType('navigation'), ...performance.getEntriesByType('resource')]
      .map(entry => ({ path: new URL(entry.name).pathname, encodedBodySize: entry.encodedBodySize,
        decodedBodySize: entry.decodedBodySize, transferSize: entry.transferSize }));
    return { locale: navigator.language, overflow: window.__nowuiResourceTimingOverflow,
      encodedBodyBytes: resources.reduce((sum, entry) => sum + entry.encodedBodySize, 0),
      decodedBodyBytes: resources.reduce((sum, entry) => sum + entry.decodedBodySize, 0),
      transferBytes: resources.reduce((sum, entry) => sum + entry.transferSize, 0), resources,
      note: 'Fresh Chromium context, initial page through 30 rendered frames; same-origin Resource Timing includes navigation and response bodies. Transfer bytes include browser-estimated headers. No network throttling.' };
  });
  assert.equal(network.overflow, false, 'Resource Timing buffer overflowed; cold-load byte totals are incomplete');
  const canvas = page.locator('#nowui-canvas');
  const pixels = await canvas.evaluate(element => {
    const copy = document.createElement('canvas');
    copy.width = element.width; copy.height = element.height;
    const drawing = copy.getContext('2d'); drawing.drawImage(element, 0, 0);
    const rgba = drawing.getImageData(0, 0, copy.width, copy.height).data;
    const colors = new Set();
    for (let i = 0; i < rgba.length; i += 4) colors.add(`${rgba[i]},${rgba[i + 1]},${rgba[i + 2]},${rgba[i + 3]}`);
    return { width: copy.width, height: copy.height, colors: colors.size };
  });
  assert.deepEqual([pixels.width, pixels.height], [viewport.width, viewport.height]);
  assert(pixels.colors > 16, `Canvas has only ${pixels.colors} colors; scene did not render`);
  await canvas.screenshot({ path: path.join(output, 'ready.png') });
  if (scaffold) {
    // Public init scene: persistent count text changes, after hover/ripple has settled.
    await page.mouse.move(900, 600);
    await frames(60);
    const before = await canvas.screenshot();
    await page.mouse.click(140, 134);
    await page.mouse.move(900, 600);
    await frames(60);
    const firstClick = await canvas.screenshot({ path: path.join(output, 'clicked.png') });
    assert(!before.equals(firstClick), 'The shared C# button did not change after a pointer click');
    await page.mouse.click(140, 134);
    await page.mouse.move(900, 600);
    await frames(60);
    assert(!firstClick.equals(await canvas.screenshot()), 'The button did not retain state for its second click');
  }
  if (playground) {
    await page.waitForFunction(() => window.__nowuiDiagnostics.error || window.__nowuiDiagnostics.frameCpuMs.length >= 600,
      null, { timeout: 120000 });
    const diagnostics = await page.evaluate(() => window.__nowuiDiagnostics);
    assert.equal(diagnostics.error, null);
    const sorted = diagnostics.frameCpuMs.slice().sort((a, b) => a - b);
    timing = { samples: sorted.length, medianMs: sorted[Math.floor((sorted.length - 1) * .5)],
      p95Ms: sorted[Math.ceil((sorted.length - 1) * .95)], startupMs: diagnostics.startupMs,
      note: 'C# frame CPU, 120 warmup frames, preserved canvas, headless Chromium software graphics; excludes GPU completion.' };

    // Compare quiet text regions rather than animated artwork or the editable caret.
    const captionClip = { x: 336, y: 152, width: 700, height: 40 };
    const originalCaption = await page.screenshot({ clip: captionClip });
    await page.mouse.click(100, 218);
    await page.waitForFunction(() => document.activeElement?.tagName === 'TEXTAREA');
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.insertText('Browser C# works');
    await frames(5);
    await page.mouse.click(304, 100);
    await frames(5);
    assert(!originalCaption.equals(await page.screenshot({ clip: captionClip })), 'C# caption did not reflect browser text input');
    await canvas.screenshot({ path: path.join(output, 'playground-caption.png') });

    await page.mouse.click(802, 44);
    await frames(10);
    const background = await canvas.evaluate(element => {
      const copy = document.createElement('canvas'); copy.width = copy.height = 1;
      const drawing = copy.getContext('2d'); drawing.drawImage(element, 0, 0);
      return [...drawing.getImageData(0, 0, 1, 1).data];
    });
    assert(background[0] > 200 && background[1] > 200, 'Light theme did not update the C# view');
    const selectionClip = { x: 336, y: 121, width: 360, height: 22 };
    const originalSelection = await page.screenshot({ clip: selectionClip });
    await page.mouse.click(801, 695);
    await page.mouse.move(304, 100);
    await frames(30);
    assert(!originalSelection.equals(await page.screenshot({ clip: selectionClip })), 'Heart gallery selection did not update the stage label');

    const keyClip = { x: 48, y: 541, width: 224, height: 38 };
    const originalKey = await page.screenshot({ clip: keyClip });
    await page.mouse.click(140, 560);
    await frames(3);
    await page.keyboard.press('F8');
    await frames(3);
    await page.mouse.click(304, 100);
    await frames(30);
    assert(!originalKey.equals(await page.screenshot({ clip: keyClip })), 'Shared key binding did not capture F8');
    await canvas.screenshot({ path: path.join(output, 'playground-controls.png') });

    // A fresh scene at the same deterministic clock as the native comparison capture.
    const capturePage = await context.newPage();
    observe(capturePage);
    const referenceUrl = new URL(target); referenceUrl.searchParams.set('time', '0.5');
    try {
      await capturePage.goto(referenceUrl.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
      await capturePage.waitForFunction(() => window.__nowuiCapture || window.__nowuiDiagnostics?.error, null, { timeout: 120000 });
      assert.equal(await capturePage.evaluate(() => window.__nowuiDiagnostics.error), null);
      const png = await capturePage.evaluate(() => window.__nowuiCapture);
      assert(png?.startsWith('data:image/png;base64,'), 'Fixed frame capture is missing');
      await fs.writeFile(path.join(output, 'browser-reference.png'), Buffer.from(png.split(',')[1], 'base64'));
    } finally { await capturePage.close(); }
  }
  const beforeResize = await page.evaluate(() => window.__nowuiDiagnostics.frames);
  await page.setViewportSize({ width: 720, height: 480 });
  await frames(5);
  assert.deepEqual(await canvas.evaluate(element => [element.width, element.height]), [720, 480]);
  assert((await page.evaluate(() => window.__nowuiDiagnostics.frames)) > beforeResize);
  assert.deepEqual(failures, [], 'Browser errors');
  await canvas.screenshot({ path: path.join(output, 'resized.png') });
  console.log('Browser startup, rendering, resize' + (scaffold ? ', and C# button input' : playground ? ', playground controls, and fixed capture' : '') + ' passed.');
} catch (error) {
  await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
  throw error;
} finally {
  const diagnostics = await page.evaluate(() => window.__nowuiDiagnostics).catch(() => null);
  await fs.writeFile(path.join(output, 'browser.json'), JSON.stringify({ diagnostics, timing, network, failures, messages }, null, 2));
  await browser.close();
}
