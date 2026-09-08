// W2 - the module the managed [JSImport]s bind against. Docs/Standalone/M3-Spec.md section 5.1.
//
// One function crosses the boundary per frame. `record` is called from inside WebApp.Frame(), it runs the author's
// draw function, and it copies the recorded op stream into the managed buffers with set(). Nothing here retains a
// MemoryView past the call that handed it over - that is the rule nowui-gl.js already states
// ("A .NET MemoryView is valid only for the duration of the call", nowui-gl.js:3668-3673) and the rule W1 measured.
//
// Everything else in this file is the plumbing around that one call: start(), the fault report, and the
// deliberately-corrupt frame modes that exist so W2's validator can be shown refusing a malformed buffer rather
// than asserted to.

import { NEED_MORE, HDR_OP_END, SURFACE_HASH, OPS } from './abi.js';
import { HDR_SLOTS, HDR_TEXT_BYTES } from './results.js';
import { Recorder } from './recorder.js';
import { makeSurface as makeStubSurface } from './surface.stub.js';

let W = null;
let ui = null;
let drawFn = null;
let corruptMode = null;
let stopped = false;
let lastFault = null;
let frames = 0;

/// Brings the recorder up and installs the author's draw function. Called from the page, before the first frame.
///
/// `options.surface` is W6's one addition: a factory that binds a surface module to the new recorder and returns
/// the object handed to the draw function. It defaults to surface.stub.js, which is the sliver W2-W5's own draw
/// functions are written against; wwwroot/nowui/nowui.js passes its own and is what an author imports.
export function start(draw, options = {}) {
    if (typeof draw !== 'function') throw new TypeError('nowui.start(draw) requires a function.');
    W = new Recorder(options);
    ui = (options.surface || makeStubSurface)(W);
    drawFn = draw;
    stopped = false;
    frames = 0;
    return ui;
}

/// Section 2.1's `start(...).stop()`. The frame loop belongs to the host, so stopping is a matter of drawing
/// nothing: the crossing still happens, the buffer is empty, and NowUI clears to the theme's ground.
export function stop() {
    stopped = true;
}

/// The surface, for a draw function that would rather close over it than take it as an argument.
export function surface() {
    return ui;
}

/// Section 5.1, as written there. The two `if`s are the whole growth protocol.
export function record(ops, opsText, results, resultsText, resultSlots, frame) {
    if (!W.pending) {
        // W5. Last frame's result table, read out of the managed view before a single ui.* call runs - which is
        // what makes every read inside the draw function synchronous and one frame old (section 6.2).
        //
        // Inside `if (!W.pending)` on purpose. A NEED_MORE retry re-uses the bytes already recorded and must not
        // load the table a second time: the one-shot events of section 6.3 are consumed by reading, so a second
        // load would hand the same click to a draw function that has already had it.
        //
        // Two slices, and the second one is why the table carries a header. A MemoryView is valid only for the
        // duration of the call and slice() is what copies it out (nowui-gl.js:3668-3673); `resultSlots` bounds
        // the first slice, and the byte count for the second is in the table's own header - without it this would
        // have to copy the whole text buffer every frame, which grows the moment one text field holds a long
        // string.
        loadResults(results, resultsText, resultSlots, frame);

        W.reset(frame);
        try {
            if (!stopped) drawFn(ui);
        } catch (e) {
            // Section 4.4: NowUI has not been touched. The frame is marked faulted and whatever was recorded
            // before the throw is kept - closeAll() below makes it a balanced prefix.
            W.fault(e);
            reportFault(e);
        } finally {
            W.closeAll();
        }
        W.flush();
        if (corruptMode) applyCorruption();
        W.pending = true;
    }

    if (W.used > ops.length || W.textUsed > opsText.length) {
        // The two sizes the managed side must grow to, in the first two slots. `ops` is always at least 2 slots,
        // so this always fits, and the recorded bytes are NOT discarded: `pending` stays true and the retry is
        // two set()s with the author's draw function not re-run.
        ops.set(Int32Array.of(W.used, W.textUsed));
        return NEED_MORE;
    }

    ops.set(W.out.subarray(0, W.used));
    if (W.textUsed > 0) opsText.set(W.u8.subarray(0, W.textUsed));
    W.pending = false;
    frames++;
    return W.used;
}

const NO_TEXT = new Uint8Array(0);

/// Copies the managed result table out of its two views and hands it to the loader. Split out of `record` so the
/// node checks can drive it with plain typed arrays, which is all a MemoryView looks like from here.
function loadResults(results, resultsText, resultSlots, frame) {
    if (!results || resultSlots < HDR_SLOTS) {
        W.results.load(null, NO_TEXT, frame);
        return;
    }

    const table = results.slice(0, resultSlots);
    const textBytes = table[HDR_TEXT_BYTES];
    const text = textBytes > 0 && resultsText ? resultsText.slice(0, textBytes) : NO_TEXT;

    W.results.load(table, text, frame);
}

// ---------------------------------------------------------------------------------------------- diagnostics

function reportFault(error) {
    lastFault = error;
    const message = error && error.nowui
        ? String(error.message)
        : 'NowUI: the draw function threw.\n' + (error && error.stack ? error.stack : String(error));
    if (typeof console !== 'undefined' && console.error) console.error(message);
}

/// What a headless run reads without parsing a canvas. W7 turns this into an on-canvas banner; until then it is
/// the honest report of what the last frame did.
export function status() {
    return JSON.stringify({
        frames,
        surfaceHash: SURFACE_HASH,
        used: W ? W.used : 0,
        textBytes: W ? W.textUsed : 0,
        faulted: W ? W.faulted : false,
        fault: lastFault ? String(lastFault.message) : null,
        reorderReports: W ? W.trie.reports.map(r => r.message) : [],

        // The LOADER's own count, not the writer's. The managed report prints how many records BridgeResults
        // wrote; this is how many results.js decoded out of them, and the two being different is the one failure
        // mode a record-layout disagreement produces that nothing else would show.
        resultsRead: W ? W.results.records : 0,
        resultReports: W ? W.results.reports.slice(0, 4) : [],
    });
}

// ---------------------------------------------------------------------------------------------- corruption

/// W2's acceptance asks for a deliberately truncated buffer to be REFUSED by the validator with a slot offset,
/// with NowUI never touched. Corruption is injected here, after a correct frame has been recorded, so that the
/// thing being tested is the validator and not a hand-written byte array that might not resemble a real frame.
///
/// The modes damage exactly one property each, so the validator's message can be checked against the property it
/// was supposed to catch:
///
///   'truncate'  drop the last slot of the op stream, leaving the final op's declared argSlots running one slot
///               past opEnd. Section 5.5 rule 4.
///   'magic'     wrong magic. Rule 1.
///   'surface'   wrong surface hash - what a nowui.js built from a different commit looks like. Rule 1 / G3.
///   'bounds'    opEnd past the end of the buffer. Rule 2.
///   'intern'    an intern handle that is neither known nor declared this frame. Rule 3.
///   'unbalanced' an extra OP_SCOPE_CLOSE. Rule 5.
export function corrupt(mode) {
    corruptMode = mode || null;
}

function applyCorruption() {
    const out = W.out;

    switch (corruptMode) {
        case 'truncate':
            out[HDR_OP_END] = out[HDR_OP_END] - 1;
            W.used -= 1;
            break;
        case 'magic':
            out[0] = 0x4e554c4c;
            break;
        case 'surface':
            out[1] = (out[1] ^ 0x5a5a5a5a) | 0;
            break;
        case 'bounds':
            out[HDR_OP_END] = W.used + 64;
            break;
        case 'intern': {
            // Point the first `str` argument of the first TEXT op at a handle nobody declared.
            const opStart = out[6];
            for (let i = opStart; i < W.used;) {
                const opcode = out[i] & 0xffff;
                const slots = (out[i] >> 16) & 0xffff;
                if (opcode === OPS.TEXT.opcode) { out[i + 1] = 999999; break; }
                i += 1 + slots;
            }
            break;
        }
        case 'unbalanced':
            W.used += 1;
            out[HDR_OP_END] = out[HDR_OP_END] + 1;
            out[W.used - 1] = 1;   // OP_SCOPE_CLOSE with no scope open
            break;
        default:
            break;
    }
}

// ---------------------------------------------------------------------------------------------- page report

/// The same convention spike.js uses: a <pre> a headless --dump-dom can read, plus a poll marker.
export function report(text) {
    if (typeof console !== 'undefined') console.log(text);
    if (typeof document === 'undefined') return;

    // `?report=0` keeps the console line and drops the <pre>. W6 wanted it: the report is instrumentation for the
    // bridge, and it sits over the bottom of the canvas - which is fine when the picture IS the report, and wrong
    // when the picture is an author's application and the bottom of it is the part being photographed.
    if (typeof location !== 'undefined' && /(^|[?&])report=0([&]|$)/.test(location.search)) return;

    let node = document.getElementById('nowui-bridge-report');
    if (!node) {
        node = document.createElement('pre');
        node.id = 'nowui-bridge-report';
        node.style.cssText = 'position:fixed;left:0;right:0;bottom:0;margin:0;padding:10px;max-height:45%;' +
            'overflow:auto;z-index:10;background:#0d1117e6;color:#c9d1d9;' +
            'font:12px/1.45 ui-monospace,Menlo,Consolas,monospace;white-space:pre;';
        document.body.appendChild(node);
    }
    node.textContent = text;
    document.title = 'NowUI W2/W3 bridge: done';
    window.__nowuiBridgeReport = text;
}
