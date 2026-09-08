// W9 acceptance: the new drawing and styling opcodes, emitted from JavaScript, drawn by NowUI, in a browser.
//
// THIS IS NOT THE AUTHOR-FACING API. There is no ui.rect, no ui.canvas and no colour parsing here, because that
// is the next unit's work; this file is the minimum hand-written JavaScript that puts the new wire shape on the
// buffer, so that "the decoder handles it" is backed by pixels rather than only by a passing unit test.
//
// It talks to the recorder directly. bridge.js's `start(draw, { surface })` hands the surface factory the new
// Recorder and passes whatever it returns to the draw function, so a factory of `W => W` gives the draw function
// the recorder itself - one line, and no part of nowui.js is involved or implicated.
//
// Served as ?app=w9-draw.

import { start as bridgeStart } from './nowui/bridge.js';
import {
    OPS,
    OPT_WIDTH, OPT_HEIGHT, OPT_COLOR, OPT_STROKE, OPT_STROKE_COLOR, OPT_RADIUS, OPT_BLUR,
    OPT_CAP, OPT_DASH, OPT_SEGMENTS, OPT_FILL, OPT_SPREAD, OPT_ANGLE, OPT_MORE,
    OPT_PADDING, OPT_GAP,
    PAINT_LITERAL, PAINT_TOKEN, COLOR_TOKEN, MASK_KIND, GRADIENT_KIND, LINE_CAP, SPLIT_AXIS, THEME_MODE,
} from './nowui/abi.js';

// ------------------------------------------------------------------------------------------------ the emitter
//
// Everything below writes slots. The one rule it has to keep is section 5.3's: an OPTS payload follows the mask
// in ASCENDING BIT ORDER, and the widths come from OPT_SLOTS so that this file and the decoder cannot disagree
// about where the next field starts.

let W = null;

/// One colour, as the two slots of a `paint`. A string names a theme token; an array is literal RGBA 0..1.
function paint(value) {
    if (typeof value === 'string') {
        const token = COLOR_TOKEN[value];
        if (token === undefined) throw new Error('w9-draw: no theme colour named "' + value + '".');
        return [PAINT_TOKEN, token];
    }

    const [r, g, b, a = 1] = value;
    const byte = (x) => Math.max(0, Math.min(255, Math.round(x * 255)));
    const packed = (byte(r) | (byte(g) << 8) | (byte(b) << 16) | (byte(a) << 24)) >>> 0;
    return [PAINT_LITERAL, packed | 0];
}

/// The option fields this file can emit, in ascending bit order - which is the order they must be written in.
/// Each entry says which bit it sets and returns its payload slots as {i} ints or {f} floats.
const FIELDS = [
    ['width', OPT_WIDTH, (v) => [{ f: v }]],
    ['height', OPT_HEIGHT, (v) => [{ f: v }]],
    ['gap', OPT_GAP, (v) => [{ f: v }]],
    ['padding', OPT_PADDING, (v) => (Array.isArray(v) ? v : [v, v, v, v]).map((x) => ({ f: x }))],
    ['color', OPT_COLOR, (v) => paint(v).map((x) => ({ i: x }))],
    ['stroke', OPT_STROKE, (v) => [{ f: v }]],
    ['strokeColor', OPT_STROKE_COLOR, (v) => paint(v).map((x) => ({ i: x }))],
    ['radius', OPT_RADIUS, (v) => (Array.isArray(v) ? v : [v, v, v, v]).map((x) => ({ f: x }))],
    ['blur', OPT_BLUR, (v) => [{ f: v }]],
    ['cap', OPT_CAP, (v) => [{ i: LINE_CAP[v] }]],
    ['dash', OPT_DASH, (v) => v.map((x) => ({ f: x }))],
    ['segments', OPT_SEGMENTS, (v) => [{ i: v }]],
    ['fill', OPT_FILL, (v) => [{ i: v ? 1 : 0 }]],
    ['spread', OPT_SPREAD, (v) => [{ i: v }]],
    ['angle', OPT_ANGLE, (v) => [{ f: v }]],
];

// Sorted by bit rather than trusted to be written in order above: getting this wrong is exactly the class of bug
// the ascending-order rule exists to make impossible, and one sort at module load costs nothing.
FIELDS.sort((a, b) => (a[1] >>> 0) - (b[1] >>> 0));

function opts(o) {
    if (!o) return;

    let mask = 0;
    const payload = [];

    for (const [name, bit, encode] of FIELDS) {
        if (o[name] === undefined) continue;
        mask |= bit;
        payload.push(...encode(o[name]));
    }

    if (mask === 0) return;

    W.op(OPS.OPTS, 1 + payload.length);
    W.i32(mask);
    for (const slot of payload) {
        if (slot.i !== undefined) W.i32(slot.i);
        else W.f32(slot.f);
    }
}

/// The bit-31 continuation, emitted by nothing in the real surface and exercised here so the browser sees the
/// path the unit test covers: an unknown second word, its bits' payload at the very end, and this build's own
/// fields still read correctly out of the middle.
function optsWithContinuation(o, unknownMask, unknownPayload) {
    let mask = OPT_MORE;
    const payload = [];

    for (const [name, bit, encode] of FIELDS) {
        if (o[name] === undefined) continue;
        mask |= bit;
        payload.push(...encode(o[name]));
    }

    W.op(OPS.OPTS, 2 + payload.length + unknownPayload.length);
    W.i32(mask);
    W.i32(unknownMask);
    for (const slot of payload) {
        if (slot.i !== undefined) W.i32(slot.i);
        else W.f32(slot.f);
    }
    for (const v of unknownPayload) W.i32(v);
}

function f32s(...values) {
    for (const v of values) W.f32(v);
}

// ---- the drawings ---------------------------------------------------------------------------------------------

function rect(box, o) { opts(o); W.op(OPS.RECT); f32s(...box); }

function circle(cx, cy, rx, ry, o) { opts(o); W.op(OPS.CIRCLE); f32s(cx, cy, rx, ry); }

function line(x0, y0, x1, y1, o) { opts(o); W.op(OPS.LINE); f32s(x0, y0, x1, y1); }

function bezier(pts, o) { opts(o); W.op(OPS.BEZIER); f32s(...pts); }

function triangle(pts, o) { opts(o); W.op(OPS.TRIANGLE); f32s(...pts); }

function polygon(points, o) {
    opts(o);
    W.op(OPS.POLYGON, 1 + points.length);
    W.i32(points.length / 2);
    f32s(...points);
}

function gradient(box, from, to, kind, o) {
    opts(o);
    W.op(OPS.GRADIENT);
    f32s(...box);
    for (const v of paint(from)) W.i32(v);
    for (const v of paint(to)) W.i32(v);
    W.i32(kind);
}

// ---- the scopes -----------------------------------------------------------------------------------------------
//
// A scope pushes a trie node, emits its op with the node's rid and segment, and closes in a `finally` so the
// buffer is balanced on every path out - which is R4, and the only rule this file borrows from nowui.js.

function scope(record, node, writeArgs, body) {
    W.beginScope(record, node, writeArgs);
    try {
        if (typeof body === 'function') body();
    } finally {
        W.endScope();
    }
}

function seg(node) { W.i32(node.rid); W.i32(node.seg); }

function canvas(key, o, body) {
    const node = W.trie.pushScope(key);
    opts(o);
    scope(OPS.CANVAS, node, () => seg(node), body);
}

function mask(key, kind, box, extra, feather, body) {
    const node = W.trie.pushScope(key);
    scope(OPS.MASK, node, () => {
        seg(node);
        W.i32(kind);
        f32s(...box);
        f32s(...extra);
        W.f32(feather);
    }, body);
}

function theme(key, mode, body) {
    const node = W.trie.pushScope(key);
    scope(OPS.THEME, node, () => { seg(node); W.i32(mode); }, body);
}

function split(key, ratio, axis, o, first, second) {
    const node = W.trie.pushScope(key);
    opts(o);

    W.beginScope(OPS.SPLIT, node, () => {
        seg(node);
        W.f32(ratio);
        W.i32(axis);
    });

    try {
        pane(0, first);
        pane(1, second);
    } finally {
        W.endScope();
    }
}

function pane(index, body) {
    // A pane consumes an anonymous trie ordinal so the two panes' contents cannot share a path, and carries only
    // its index on the wire: the identity that matters is the split's own, which NowLayout.Area derives per pane.
    const node = W.trie.pushAnon();
    scope(OPS.PANE, node, () => W.i32(index), body);
}

function text(value) {
    W.op(OPS.TEXT);
    W.i32(W.str(value));
}

// ------------------------------------------------------------------------------------------------ the picture
//
// One of every new op, laid out so that a person looking at the capture can name what is missing. The canvas is
// sized rather than grown, so its box is exact on frame one - a grown canvas is one frame stale in its SIZE,
// which is correct and would make the first captured frame a poor witness.

const SHAPE = [0.16, 0.55, 0.92, 1];
const WARM = [0.95, 0.45, 0.15, 1];

function draw() {
    // A split, so its two panes and the draggable divider are visible in the capture. The left pane holds the
    // canvas with every drawing op; the right holds a theme scope, so light and dark are both on screen at once.
    split('root', 0.62, SPLIT_AXIS.horizontal, { width: 980, height: 620 },
        () => {
            canvas('plate', { width: 600, height: 600 }, () => {
                // 1. RECT - explicit colour, radius, outline and blur.
                rect([16, 16, 260, 90], {
                    color: SHAPE, radius: [16, 16, 4, 4], stroke: 2, strokeColor: 'borderStrong', blur: 0,
                });

                // 2. RECT again, this time with only a semantic style - the no-colour path, which must NOT come
                //    out white on white.
                rect([292, 16, 260, 90], { radius: 10 });

                // 3. CIRCLE - filled, and an unfilled ring beside it, which is the `fill: false` path that a
                //    flag-only option bit could not have expressed.
                circle(72, 168, 48, 48, { color: 'accent' });
                circle(196, 168, 48, 32, { color: 'danger', fill: false, stroke: 6, segments: 64 });

                // 4. LINE - a solid one with round caps, and a dashed one.
                line(300, 130, 560, 130, { color: 'text', stroke: 6, cap: 'round' });
                line(300, 160, 560, 160, { color: 'warning', stroke: 4, dash: [14, 8, 0] });

                // 5. BEZIER - the cubic path through the same op family.
                bezier([300, 200, 380, 110, 480, 300, 560, 200], { color: 'success', stroke: 5, cap: 'round' });

                // 6. TRIANGLE and 7. POLYGON.
                triangle([40, 300, 150, 260, 130, 380], { color: WARM });
                polygon([200, 260, 300, 280, 320, 380, 230, 400, 180, 330],
                    { color: 'accentMuted', stroke: 3, strokeColor: 'accent' });

                // 8. GRADIENT.
                gradient([360, 250, 200, 130], [0.9, 0.2, 0.4, 1], 'accent', GRADIENT_KIND.linear, { angle: 45 });

                // 9. MASK - a rounded-rect clip over a rect that is deliberately larger than it, so the capture
                //    shows the clip rather than the rect.
                mask('clip', MASK_KIND.roundedRect, [40, 420, 220, 140], [40, 40, 40, 40], 1, () => {
                    rect([0, 400, 600, 200], { color: 'accentPressed' });
                });

                // 10. MASK again, circular, over a full-width band.
                mask('lens', MASK_KIND.circle, [400, 490, 70, 0], [0, 0, 0, 0], 2, () => {
                    rect([300, 400, 320, 200], { color: 'success' });
                });

                // 11. The OPT_MORE continuation, exercised on a real frame: a second mask word this build has
                //     never heard of, its payload at the end, and this build's own colour still read correctly
                //     out of the middle. If the skip were wrong, this bar would be the wrong colour or absent.
                optsWithContinuation({ color: 'focusRing' }, 0x0000000f, [11, 22, 33, 44]);
                W.op(OPS.RECT);
                f32s(16, 576, 536, 12);
            });
        },
        () => {
            // The right pane, under a dark theme scope - the other half of "ui.theme is implementable".
            theme('dark', THEME_MODE.dark, () => {
                canvas('dark-plate', { width: 340, height: 600 }, () => {
                    rect([0, 0, 340, 600], { color: 'background' });
                    rect([20, 20, 300, 80], { color: 'surfaceElevated', radius: 12 });
                    rect([20, 120, 300, 80], { color: 'accent', radius: 12 });
                    circle(170, 280, 60, 60, { color: 'warning' });
                    line(20, 380, 320, 380, { color: 'border', stroke: 2 });
                    triangle([60, 420, 280, 440, 170, 560], { color: 'success' });
                });
            });
        });

    text('W9: canvas, mask, rect, circle, line, bezier, triangle, polygon, gradient, split, pane, theme');
}

// One frame's worth of what was emitted, for a headless driver that would rather read a number than a picture.
const handle = bridgeStart(() => {
    draw();
    window.__nowuiW9 = { ops: W.opsUsed, frames: (window.__nowuiW9 ? window.__nowuiW9.frames : 0) + 1 };
}, { surface: (recorder) => { W = recorder; return recorder; } });

export default handle;
