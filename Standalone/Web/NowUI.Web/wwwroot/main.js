// The page's boot script and the browser half of INowHostServices.
//
// It does five things and deliberately no more:
//   1. sizes the canvas' drawing buffer,
//   2. exposes the six host functions WebHostServices imports (clock, canvas size, device pixel ratio, base URL,
//      console),
//   3. registers the fetch bridge (nowui-fetch.js) in both directions - its imports for the managed side, the
//      assembly exports for its callbacks - because it is the one bridge that calls back INTO managed code,
//   4. boots the .NET runtime and runs Main, which brings NowUI up,
//   5. pumps requestAnimationFrame into the exported Frame().
//
// It is NOT the WebGL backend and it is NOT the input bridge. The GL context, the two shader programs and every draw
// call live in nowui-gl.js and WebGL2Backend.cs; every DOM event listener lives in nowui-input.js and WebInput.cs.
// Both of those are imported by the managed side through JSHost.ImportAsync, not from here. What this file owes the
// input bridge is the canvas itself: an element that can take keyboard focus and will not have its gestures stolen
// by the page (see makeCanvasInteractive), and the one device pixel ratio both halves agree on.

import { dotnet } from './_framework/dotnet.js'
import * as nowuiFetch from './nowui-fetch.js'

const canvas = document.getElementById('nowui-canvas');

// The drawing buffer is the CSS box times this, and Program.cs passes the same number to Now.StartUI as the UI
// scale. Both must move together: doubling the buffer alone renders the same layout at half the on-screen size,
// because NowUI measures in drawing-buffer pixels. Together they mean one NowUI unit stays one CSS pixel while the
// panel edge and glyph stems are rasterised at native density.
//
// `?dpr=N` pins it for capture. Unity's reference images are rendered at 2x, so comparing against them means asking
// for 2 rather than inheriting whatever display the capture happens to run on - a comparison is only meaningful if
// both sides rasterise at the same density.
const DEVICE_PIXEL_RATIO = (() => {
    const requested = new URLSearchParams(location.search).get('dpr');
    const parsed = requested === null ? NaN : Number(requested);
    if (Number.isFinite(parsed) && parsed > 0) return parsed;
    return window.devicePixelRatio || 1;
})();

// Set by the managed side when the page is asked to render ONE parity scene (?scene=NAME) instead of the demo.
// A parity capture is only meaningful if the drawing buffer is exactly the size Unity's reference render used, so
// in that mode the canvas stops following the window and takes the scene's declared size in CSS pixels; the
// drawing buffer is then that times DEVICE_PIXEL_RATIO, which at ?dpr=2 is Unity's own 2x capture size.
// See NowParityScenes.cs and Program.cs.
let fixedCssSize = null;

function resizeCanvas() {
    const cssWidth = fixedCssSize ? fixedCssSize.width : canvas.clientWidth;
    const cssHeight = fixedCssSize ? fixedCssSize.height : canvas.clientHeight;

    const width = Math.max(1, Math.round(cssWidth * DEVICE_PIXEL_RATIO));
    const height = Math.max(1, Math.round(cssHeight * DEVICE_PIXEL_RATIO));

    // Guarded: assigning canvas.width reallocates and clears the drawing buffer even when the value is unchanged.
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
}

// Page-level hygiene the input bridge needs and that slice 1 had no reason to do. Set here rather than in the CSS
// because these are behavioural, not cosmetic, and because the element they apply to is owned by this file:
//
//   tabindex     - without it the canvas cannot take keyboard focus at all, so no keydown ever reaches it and every
//                  key handler in nowui-input.js is dead code. -1 rather than 0: the canvas is focused explicitly on
//                  pointerdown, and it should not become a stop in the browser's own Tab order, because Tab inside
//                  the canvas belongs to NowUI's focus navigation.
//   touch-action - without `none` a touch drag scrolls the page instead of reaching the UI, and the browser steals
//                  the pointer with a pointercancel partway through the gesture.
//   user-select  - stops a drag over the canvas from starting a text selection on the page behind it.
//   outline      - the canvas is focused on every pointerdown; NowUI draws its own focus ring, and a second one
//                  around the whole window would be the browser answering a question the UI already answered.
function makeCanvasInteractive() {
    canvas.setAttribute('tabindex', '-1');
    canvas.style.touchAction = 'none';
    canvas.style.userSelect = 'none';
    canvas.style.webkitUserSelect = 'none';
    canvas.style.outline = 'none';
}

makeCanvasInteractive();
resizeCanvas();
window.addEventListener('resize', resizeCanvas);

const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();

let frameCallback = null;

function frame() {
    // Before the frame, so WebHostServices.Poll and the backend's viewport see the same size in the same frame.
    resizeCanvas();
    frameCallback();
}

function pump() {
    frame();
    requestAnimationFrame(pump);
}

setModuleImports('main.js', {
    host: {
        now: () => performance.now(),
        canvasWidth: () => canvas.width,
        canvasHeight: () => canvas.height,
        // The effective ratio, not window.devicePixelRatio: the C# side scales the UI by this and must agree
        // with the number the drawing buffer was actually sized with, including a ?dpr= override.
        devicePixelRatio: () => DEVICE_PIXEL_RATIO,
        baseUri: () => document.baseURI,
        // One query parameter, read by name. The managed side owns which flags exist (?scene=), so this stays a
        // lookup rather than a second, competing parser of the URL.
        queryParam: (name) => new URLSearchParams(location.search).get(name) ?? '',
        // Pins the canvas to an exact CSS size, for a parity capture. The element is sized as well as the buffer:
        // a headless screenshot crops the window, so a canvas that still stretched to 100% would be captured at
        // the window's size and scaled, which measures the browser's compositor rather than the renderer.
        setCanvasSize: (width, height) => {
            fixedCssSize = { width, height };
            canvas.style.width = width + 'px';
            canvas.style.height = height + 'px';
            document.body.style.background = '#000';
            resizeCanvas();
        },
        log: (level, message) => {
            if (level >= 2) console.error(message);
            else if (level === 1) console.warn(message);
            else console.log(message);
        },
        startFrameLoop: () => requestAnimationFrame(pump),
    }
});

// The fetch bridge (INowFetchProvider, and the image pre-decode behind INowImageDecoder). Registered as its own
// module name rather than folded into `host` above, for the same reason the GL and input bridges are separate
// files: this one is ~400 lines of fetch and codec plumbing and main.js is meant to stay four things long.
//
// Registered HERE, through setModuleImports, rather than pulled in from C# with JSHost.ImportAsync — which is what
// nowui-gl.js and nowui-input.js do — because this module is the only one that needs to call INTO managed code
// (a chunk arriving, a decode finishing). Loading it from both sides would make its single-instance-ness depend on
// two URL resolutions agreeing; loading it only here makes it a fact. The managed [JSImport]s name the module
// 'nowui-fetch.js' and resolve against this registration.
setModuleImports('nowui-fetch.js', nowuiFetch.imports);

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
frameCallback = exports.WebApp.Frame;

// The other direction, and it has to happen before runMain(): WebFetchProvider probes the bridge during start-up
// and refuses to install itself as the host's fetch provider if these are not in place, so that a stale cached
// main.js produces one named error rather than requests that never complete.
nowuiFetch.setExports(exports);

// The input tests' oracle, behind `?debug=1` for the same reason `?capture=1` gates preserveDrawingBuffer: it is a
// diagnostic, and a page that always publishes its internals to the global scope is a page that has decided its
// internals are an API. With the flag on, a driver can dispatch a PointerEvent and then read the scene's actual
// state rather than inferring it from pixels - which is the difference between "the click counter incremented" and
// "some pixels changed".
//
// `step` runs one frame synchronously, which is what makes the input tests deterministic AND what makes them run
// at all: a browser stops delivering requestAnimationFrame to a hidden or minimised page, so a test that awaits a
// frame there waits forever. Driving the frame directly removes the wait and the flakiness in one move - dispatch,
// step, assert, with nothing in between that can reorder.
if (new URLSearchParams(location.search).get('debug') === '1') {
    // debugRects answers "where is the Add button?" in CSS pixels relative to the canvas, at any dpr, so a driver
    // dispatches events at coordinates the scene reported rather than at ones it guessed. See DemoScene.DebugRects.
    const parseRects = (line) => Object.fromEntries(line.split(';').filter(Boolean).map(entry => {
        const [name, values] = entry.split('=');
        const [x, y, width, height] = values.split(',').map(Number);
        return [name, { x, y, width, height, cx: x + width / 2, cy: y + height / 2 }];
    }));

    window.nowui = {
        debugState: () => exports.WebApp.DebugState(),
        debugRects: () => parseRects(exports.WebApp.DebugRects()),
        step: frame,
    };
}

// Canvas readback, behind ?capture=1 (which is also what makes nowui-gl.js keep the drawing buffer).
//
// Two traps this exists to route around, both recorded in M2-Scouting.md and both silent:
//   1. canvas.toDataURL on a WebGL canvas returns a blank image unless preserveDrawingBuffer is on.
//   2. Even with it on, drawImage(webglCanvas, ...) into a 2D canvas still came back black, while gl.readPixels
//      on the same buffer returned the correct pixels. So: read the framebuffer, then putImageData - never
//      drawImage.
// gl.readPixels is bottom-up, so the rows are reversed on the way into the ImageData. getContext('webgl2') a
// second time returns the SAME context the backend created; it does not make a new one.
if (new URLSearchParams(location.search).get('capture') === '1') {
    const readPixelsDataUrl = () => {
        const gl = canvas.getContext('webgl2');
        const width = canvas.width;
        const height = canvas.height;
        const pixels = new Uint8ClampedArray(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);

        const flipped = new Uint8ClampedArray(pixels.length);
        const stride = width * 4;
        for (let y = 0; y < height; y++) {
            flipped.set(pixels.subarray(y * stride, y * stride + stride), (height - 1 - y) * stride);
        }

        const out = document.createElement('canvas');
        out.width = width;
        out.height = height;
        out.getContext('2d').putImageData(new ImageData(flipped, width, height), 0, 0);
        return out.toDataURL('image/png');
    };

    window.nowuiCapture = {
        // The drawing buffer as a base64 PNG data URL. A driver can pull this over CDP and write the bytes; the
        // alternative - transcribing base64 out of a tool result - corrupted silently at 4.6 KB once already.
        png: () => readPixelsDataUrl(),
        size: () => ({ width: canvas.width, height: canvas.height }),
        // Lets the page hand the file to the browser's own download machinery instead.
        save: (filename) => {
            const a = document.createElement('a');
            a.href = readPixelsDataUrl();
            a.download = filename || 'nowui-capture.png';
            document.body.appendChild(a);
            a.click();
            a.remove();
        },
    };
}

// Runs Main(): fetches the fixtures, installs the host and the WebGL2 backend, and asks for the first frame.
await runMain();
