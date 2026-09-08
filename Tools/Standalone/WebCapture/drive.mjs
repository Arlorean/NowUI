// drive.mjs -- drive the NowUI web gallery with real input, in a FRESH headless Chrome profile, over CDP.
//
// Why this exists. `chrome --headless --screenshot` can photograph an area but cannot touch it, and half of what
// M2 has to report is behaviour under input: does the button click, does the field take a keystroke, does the
// slider drag, does the wheel arrive in the right unit, does the paste reach the control. This opens a page in a
// throwaway profile (Docs/Standalone/M2-Scouting.md, "The browser cache will lie to you"), dispatches CDP input,
// screenshots between steps, and evaluates JavaScript to read the page's own instrumentation back.
//
// Two things it took a measurement to get right, both worth keeping:
//
//   1. TYPING MUST BE KEY EVENTS, not Input.insertText. `wwwroot/nowui-input.js` reads characters from `e.key` on
//      keydown, so insertText - which fires beforeinput/input on a focused editable and never synthesises a key -
//      silently types nothing. The `chars` step exists for that.
//   2. THE BUTTON MASK MUST MATCH THE BUTTON. Input.dispatchMouseEvent with button:'right' and buttons:1 does not
//      produce a secondary press. The masks are left 1, right 2, middle 4.
//
// Coordinates are CSS pixels in the page's own frame, which is the frame Page.captureScreenshot returns - so a
// coordinate read off one of this script's screenshots is directly usable in the next step. Note that they are NOT
// the same as coordinates read off a `--screenshot` capture, whose image is the window rather than the viewport.
// Where the page publishes its layout (`?debug=1` exposes window.nowui.debugRects), ask it instead of guessing.
//
// usage: node drive.mjs <script.json>
//
//   { "url": "http://localhost:5103/?capture=1&debug=1&area=controls",
//     "width": 1180, "height": 760, "out": "artifacts/local/features",
//     "steps": [
//       {"wait": 1200},
//       {"shot": "before.png"},
//       {"mouse": {"type": "mouseMoved",    "x": 620, "y": 171}},
//       {"mouse": {"type": "mousePressed",  "x": 620, "y": 171}},
//       {"mouse": {"type": "mouseReleased", "x": 620, "y": 171}},
//       {"chars": "typed with real key events"},
//       {"key":   {"key": "Tab", "code": "Tab", "vk": 9}},
//       {"mouse": {"type": "mouseWheel", "x": 908, "y": 237, "dy": 100}},
//       {"eval": "window.nowui.debugState()", "label": "state"}
//     ] }
//
// It prints {results, logs} as JSON on stdout: `results` is one entry per `eval`, `logs` is every console message
// and uncaught exception the page produced, in order. A drag needs a settled hover frame before the press - the
// input bridge folds DOM events into one snapshot per frame - so put a small `wait` after the first mouseMoved.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const cfg = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const port = 9300 + Math.floor(Math.random() * 400);
const prof = mkdtempSync(join(tmpdir(), 'nowui-drive-'));

const chrome = spawn(CHROME, [
  '--headless=new', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
  '--disable-gpu-sandbox', `--user-data-dir=${prof}`,
  `--remote-debugging-port=${port}`,
  `--window-size=${cfg.width || 1180},${cfg.height || 760}`,
  'about:blank',
], { stdio: ['ignore', 'pipe', 'pipe'] });
let chromeErr = '';
chrome.stderr.on('data', d => { chromeErr += d; });

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function target() {
  for (let i = 0; i < 100; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/json/list`);
      const list = await r.json();
      const p = list.find(t => t.type === 'page');
      if (p) return p.webSocketDebuggerUrl;
    } catch {}
    await sleep(300);
  }
  throw new Error('no devtools target\n' + chromeErr.slice(-2000));
}

const wsUrl = await target();
const ws = new WebSocket(wsUrl);
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });

let id = 0;
const pending = new Map();
const logs = [];
ws.onmessage = ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  if (m.method === 'Runtime.consoleAPICalled') {
    logs.push('[' + m.params.type + '] ' + m.params.args.map(a =>
      a.value !== undefined ? a.value : (a.description || a.type)).join(' '));
  }
  if (m.method === 'Runtime.exceptionThrown') {
    logs.push('[exception] ' + (m.params.exceptionDetails.exception?.description
      || m.params.exceptionDetails.text));
  }
  if (m.method === 'Log.entryAdded') logs.push('[log:' + m.params.entry.level + '] ' + m.params.entry.text);
};
function send(method, params = {}) {
  const mid = ++id;
  ws.send(JSON.stringify({ id: mid, method, params }));
  return new Promise(res => pending.set(mid, res));
}

await send('Runtime.enable');
await send('Log.enable');
await send('Page.enable');
await send('Page.navigate', { url: cfg.url });

// wait for the runtime to come up
let ready = false;
for (let i = 0; i < 120; i++) {
  await sleep(500);
  const r = await send('Runtime.evaluate', {
    expression: '!!(window.nowui && window.nowui.debugState)', returnByValue: true });
  if (r.result?.result?.value) { ready = true; break; }
}
if (!ready) logs.push('[driver] window.nowui.debugState never appeared');

const results = [];
async function shot(name) {
  const r = await send('Page.captureScreenshot', { format: 'png' });
  if (r.result?.data) writeFileSync(join(cfg.out, name), Buffer.from(r.result.data, 'base64'));
  else logs.push('[driver] screenshot failed for ' + name);
}
async function evalJs(expr) {
  const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r.result?.exceptionDetails) return { error: r.result.exceptionDetails.text + ' ' +
    (r.result.exceptionDetails.exception?.description || '') };
  return { value: r.result?.result?.value };
}

for (const s of cfg.steps) {
  if (s.wait) { await sleep(s.wait); continue; }
  if (s.shot) { await shot(s.shot); continue; }
  if (s.eval) { const v = await evalJs(s.eval); results.push({ eval: s.label || s.eval, ...v }); continue; }
  if (s.mouse) {
    const { type, x, y, button = 'left', clicks = 1, dx = 0, dy = 0, modifiers = 0 } = s.mouse;
    const mask = { left: 1, right: 2, middle: 4 };
    const isWheel = type === 'mouseWheel';
    const btn = (type === 'mouseMoved' || isWheel) ? 'none' : button;
    let buttons;
    if (type === 'mousePressed') buttons = mask[button] || 1;
    else if (type === 'mouseReleased') buttons = 0;
    else buttons = s.mouse.buttons ?? 0;
    await send('Input.dispatchMouseEvent', {
      type, x, y, button: btn, buttons,
      clickCount: isWheel ? 0 : clicks, deltaX: dx, deltaY: dy, modifiers });
    continue;
  }
  if (s.key) {
    const k = s.key;
    for (const type of (k.typeOnly ? ['keyDown'] : ['keyDown', 'keyUp'])) {
      await send('Input.dispatchKeyEvent', {
        type, key: k.key, code: k.code, windowsVirtualKeyCode: k.vk, nativeVirtualKeyCode: k.vk,
        text: type === 'keyDown' ? k.text : undefined, modifiers: k.modifiers || 0 });
    }
    continue;
  }
  if (s.text) { await send('Input.insertText', { text: s.text }); continue; }
  if (s.chars) {
    // NowUI's bridge reads characters from `e.key` on keydown (nowui-input.js onKeyDown),
    // so typing must be real key events, not Input.insertText.
    for (const ch of s.chars) {
      const upper = ch.toUpperCase();
      let code = 'Key' + upper;
      if (ch >= '0' && ch <= '9') code = 'Digit' + ch;
      else if (ch === ' ') code = 'Space';
      else if (ch === ',') code = 'Comma';
      else if (ch === '.') code = 'Period';
      else if (!(upper >= 'A' && upper <= 'Z')) code = '';
      const vk = (upper >= 'A' && upper <= 'Z') || (ch >= '0' && ch <= '9')
        ? upper.charCodeAt(0) : (ch === ' ' ? 32 : 0);
      await send('Input.dispatchKeyEvent', { type: 'keyDown', key: ch, code,
        windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, text: ch });
      await send('Input.dispatchKeyEvent', { type: 'keyUp', key: ch, code,
        windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
      await sleep(12);
    }
    continue;
  }
}

await sleep(400);
console.log(JSON.stringify({ results, logs }, null, 1));
ws.close();
chrome.kill();
await sleep(300);
try { rmSync(prof, { recursive: true, force: true }); } catch {}
process.exit(0);
