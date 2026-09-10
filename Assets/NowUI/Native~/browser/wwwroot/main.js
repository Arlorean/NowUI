import { dotnet } from './_framework/dotnet.js';

const canvas = document.getElementById('nowui-canvas');
const status = document.getElementById('nowui-status');
const search = new URLSearchParams(location.search);
const pinnedScale = Number(search.get('dpr'));
const scale = () => pinnedScale > 0 && pinnedScale <= 4 ? pinnedScale : Math.min(4, window.devicePixelRatio || 1);
let stopped = false, frameHandle = 0;
const diagnostics = window.__nowuiDiagnostics = { ready: false, frames: 0, error: null, startupMs: 0, frameCpuMs: [] };
function fail(error) {
  stopped = true;
  cancelAnimationFrame(frameHandle);
  diagnostics.error = String(error?.stack || error);
  document.documentElement.dataset.nowuiStatus = 'failed';
  status.hidden = false;
  status.textContent = 'Unable to run this application.\n\n' + String(error?.message || error);
  console.error(error);
}
function resize() {
  const width = Math.max(1, Math.round(canvas.clientWidth * scale()));
  const height = Math.max(1, Math.round(canvas.clientHeight * scale()));
  if (width > 8192 || height > 8192 || width * height > 16777216) throw new Error('The browser viewport exceeds the supported rendering size.');
  if (canvas.width !== width) canvas.width = width;
  if (canvas.height !== height) canvas.height = height;
}
window.addEventListener('error', event => fail(event.error || event.message));
window.addEventListener('unhandledrejection', event => fail(event.reason));
canvas.addEventListener('webglcontextlost', event => { event.preventDefault(); fail(new Error('Graphics context lost. Reload this page to restart.')); });

try {
  resize();
  const { setModuleImports, getAssemblyExports, runMain } = await dotnet.create();
  setModuleImports('main.js', { host: {
    baseUri: () => document.baseURI,
    width: () => canvas.width,
    height: () => canvas.height,
    scale,
    log: (level, text) => (level === 2 ? console.error : level === 1 ? console.warn : console.log)(text)
  }});
  await runMain();
  const app = (await getAssemblyExports('NowUI.Browser')).NowUI.Browser.BrowserApp;
  const started = performance.now();
  function draw(time) {
    resize();
    const before = performance.now();
    app.Frame(time);
    const elapsed = performance.now() - before;
    if (diagnostics.frames >= 120 && diagnostics.frameCpuMs.length < 600) diagnostics.frameCpuMs.push(elapsed);
    diagnostics.frames++;
    if (!diagnostics.ready) {
      diagnostics.ready = true;
      diagnostics.startupMs = performance.now();
      status.hidden = true;
      document.documentElement.dataset.nowuiStatus = 'ready';
    }
  }
  function pump(now) {
    if (stopped) return;
    try { draw((now - started) / 1000); frameHandle = requestAnimationFrame(pump); }
    catch (error) { fail(error); }
  }
  // A fixed clock for verification; scene authoring remains entirely in C#.
  if (search.has('time')) {
    const time = Number(search.get('time'));
    if (!Number.isFinite(time) || time < 0 || time > 60) throw new Error('Capture time must be between 0 and 60 seconds.');
    draw(0); draw(0); draw(0);
    for (let i = 1; i <= Math.ceil(time * 60); i++) draw(Math.min(time, i / 60));
    window.__nowuiCapture = canvas.toDataURL('image/png');
  } else {
    // Resolve text focus and clipboard actions inside their trusted browser gesture,
    // using the same C# frame entry point as the ordinary RAF loop.
    const { setInputFrameCallback } = await import('./nowui-input.js');
    setInputFrameCallback(() => {
      if (stopped) return;
      try { draw((performance.now() - started) / 1000); }
      catch (error) { fail(error); }
    });
    frameHandle = requestAnimationFrame(pump);
  }
  window.addEventListener('pagehide', () => { stopped = true; cancelAnimationFrame(frameHandle); app.Shutdown(); }, { once: true });
} catch (error) { fail(error); }
