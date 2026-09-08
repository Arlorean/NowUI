// W6 - the tier-1 surface. Docs/Standalone/M3-Spec.md section 2, and the application of section 1.
//
// This is the file an author imports. Everything else under wwwroot/nowui/ is plumbing they never see: the
// recorder, the path trie, the result table, the transport. Forty-one functions, hand-written for now, and
// regenerated from Standalone/Surface/surface.json in W8 - which is why every function here is written the way a
// generator would emit it (one shape, no clever special cases) rather than the way a person would.
//
// THE SIX RULES OF SECTION 1.1 ARE THE WHOLE OF THE DESIGN, and each one is a property of this file:
//
//   R1  every interactive control takes a key first; nothing else does.  `trie.control(key)` vs no call at all.
//   R2  argument order is key, value, data, options, and a body comes last. Every signature below, no overloads.
//   R3  values go in and come back out.  `return R.value(rid, incoming)` - see section 6.4 and `value` below.
//   R4  scopes are callbacks.  `open; try { body(); } finally { close(); }` - four lines, and the same four for
//       all nine of them, which is what makes balance a property of the JavaScript call stack (section 4.1).
//   R5  boolean-returning controls are events.  `R.event(rid, F_CLICKED)` latches and consumes (section 6.3).
//   R6  everything a control returns is one frame old; everything under `ui.frame` is current (section 6.2).
//
// WHAT IS NOT HERE, and why, stated once rather than discovered at a call site. Four of section 2's forty-one
// functions throw with a message naming the reason, rather than silently drawing nothing:
//
//   ui.split         two bodies in one scope; the recorder needs a second scope bracket the ABI does not have.
//   ui.overlay       W11 - it is the sole consumer of the recorded subtrees of section 5.8.
//   ui.contextMenu   needs a NowResolvedId rather than a NowId, through the begin/end pair of section 2.6.
//   ui.theme         needs the host resource manifest to resolve a theme asset by name; the host has none yet.
//
// Everything else in section 2 is wired to a real NowUI call. See the report at the end of W6 for what each one
// was verified against.

import {
    OPS,
    OPT_WIDTH, OPT_HEIGHT, OPT_MIN_WIDTH, OPT_MAX_WIDTH, OPT_MIN_HEIGHT, OPT_MAX_HEIGHT,
    OPT_GROW, OPT_GAP, OPT_PADDING, OPT_ALIGN, OPT_JUSTIFY, OPT_STYLE, OPT_TEXT_STYLE, OPT_STEP,
    OPT_RECT, OPT_DISABLED,
    ALIGN, JUSTIFY, RECT_STYLE, TEXT_STYLE,
} from './abi.js';

import { NowUIAuthorError } from './trie.js';
import { F_CLICKED, F_SUBMITTED, F_CHANGED, F_CANCELLED } from './results.js';
import { start as bridgeStart, stop as bridgeStop } from './bridge.js';

// ------------------------------------------------------------------------------------------------ state
//
// One recorder, installed by `start`. The `ui` object below is built once at module load and closes over these
// two names, so an author can `import { ui }` at the top of their file and call it inside a draw function that
// runs much later - which is exactly what section 1's application does.

let W = null;
let trie = null;
let R = null;

/// Section 2.1's `ui.frame`: the host poll at the top of the CURRENT frame, not one frame old (R6). Mutated in
/// place rather than replaced so that an author who destructures it once keeps a live object.
const frameInfo = { width: 0, height: 0, dpr: 1, dt: 0, time: 0, count: 0 };

let lastTimeMs = 0;

function updateFrame(count) {
    const nowMs = typeof performance !== 'undefined' ? performance.now() : Date.now();

    frameInfo.count = count;
    frameInfo.dt = lastTimeMs === 0 ? 0 : (nowMs - lastTimeMs) / 1000;
    frameInfo.time = nowMs / 1000;
    lastTimeMs = nowMs;

    // The canvas is the host's own source for the screen rect (Program.cs sizes it and NowUI lays out inside it),
    // so reading it here is reading the same number the wasm side will use this frame rather than an echo of the
    // last one. In CSS pixels, which is the unit every layout option in section 2.7 is in.
    if (typeof document !== 'undefined') {
        const canvas = document.getElementById('nowui-canvas');
        if (canvas) {
            frameInfo.width = canvas.clientWidth;
            frameInfo.height = canvas.clientHeight;
        }
    }

    if (typeof window !== 'undefined') frameInfo.dpr = window.devicePixelRatio || 1;
}

// ------------------------------------------------------------------------------------------------ handlers
//
// `onSubmit` and `onRemove` (section 2.7) fire in the FRAME PROLOGUE - after the result table has been loaded and
// before the author's draw function runs - not at the point of the call. That is a correction to section 2.7, and
// it is section 1's own application that forces it.
//
// Section 2.7 says a handler "runs synchronously at the point of the call, so a handler's state change is visible
// to every control drawn after it". Now read section 1:
//
//     state.name = ui.textField('name', state.name, { onSubmit: addPerson });
//     function addPerson() { ...; state.name = state.email = ''; ... }
//
// The handler writes the very variable the call's return value is assigned to, and the assignment happens after
// the call returns. So with point-of-call dispatch the sequence is: resolve "Alan Turing", emit it, fire
// addPerson, which sets state.name = '', return "Alan Turing" - and the assignment puts "Alan Turing" straight
// back. The next frame's incoming value now EQUALS what this side last returned, so section 6.4 reads it as an
// echo and hands back the field's own text forever. Measured in the browser: press Enter, the row is added, the
// status line says "Added Alan Turing." and the field never clears. `Clear` works, because a button's return is
// consumed by `if` rather than assigned, and the same write lands.
//
// Dispatching in the prologue fixes it exactly, and costs nothing the spec was relying on:
//
//   * the handler's write happens BEFORE ui.textField is called, so its `value` argument is already '', which
//     differs from what this side last returned and therefore wins under section 6.4's rule;
//   * "visible to every control drawn after it" still holds - it is now visible to every control, full stop;
//   * it is still one frame after the event, which is what section 6.2 promises either way;
//   * the one-shot latch is still consumed exactly once (section 6.3), by the handler instead of by the control.
//
// The registry is what makes it possible: a handler is only known once its control has been called, so it is
// remembered on the frame it is drawn and fired on the next one.

const handlers = new Map();

/// Remembers (or forgets) the handlers of the control at `rid`. Called by every function that takes one.
function rememberHandlers(rid, opts) {
    const onSubmit = opts && typeof opts.onSubmit === 'function' ? opts.onSubmit : null;
    const onRemove = opts && typeof opts.onRemove === 'function' ? opts.onRemove : null;

    if (onSubmit || onRemove) handlers.set(rid, { onSubmit, onRemove });
    else if (handlers.size !== 0) handlers.delete(rid);
}

/// The prologue. A rid whose control was not drawn last frame has no record, so `present` is false and nothing
/// fires - which is the same rule that stops a click being delivered for a control that stopped existing.
function fireHandlers() {
    if (handlers.size === 0) return;

    // Over a snapshot of the keys: a handler is the author's code and may draw, remove or add anything, which
    // includes changing what the next frame registers.
    for (const rid of Array.from(handlers.keys())) {
        const h = handlers.get(rid);
        if (h === undefined || !R.present(rid)) continue;

        if (h.onSubmit && R.event(rid, F_SUBMITTED)) h.onSubmit(R.current(rid));
        if (h.onRemove && R.event(rid, F_CANCELLED)) h.onRemove();
    }
}

// ------------------------------------------------------------------------------------------------ options
//
// Section 2.7's options object, encoded into the OPTS op. The scratch buffer is module-level and reused: an
// options object is read, encoded and emitted inside one call, and allocating a payload array per control per
// frame is the kind of cost that only shows up at 200 controls.

const SCRATCH = new ArrayBuffer(64 * 4);
const SCRATCH_I32 = new Int32Array(SCRATCH);
const SCRATCH_F32 = new Float32Array(SCRATCH);

let optMask = 0;
let optCount = 0;

/// Every option name section 2.7 defines, plus the four that are positional-by-another-name. An option this set
/// does not contain is an author error rather than a silent no-op - a typo in `plcaeholder` would otherwise be
/// invisible, and invisible is the failure mode this whole design is trying to avoid.
const KNOWN_OPTIONS = new Set([
    'key', 'label', 'width', 'height', 'minWidth', 'maxWidth', 'minHeight', 'maxHeight', 'grow', 'gap',
    'padding', 'align', 'justify', 'style', 'textStyle', 'step', 'placeholder', 'rect', 'disabled',
    'onSubmit', 'onRemove', 'min', 'max', 'format', 'removable', 'selected',
]);

function pushF(value) {
    SCRATCH_F32[optCount++] = value;
}

function pushI(value) {
    SCRATCH_I32[optCount++] = value | 0;
}

function num(value, what) {
    if (typeof value !== 'number' || !isFinite(value))
        throw new NowUIAuthorError('NowUI: the ' + what + ' option must be a finite number, and this one is ' +
            JSON.stringify(value) + '.');
    return value;
}

function enumValue(table, name, what) {
    const value = table[name];
    if (value === undefined)
        throw new NowUIAuthorError(
            'NowUI: "' + name + '" is not a ' + what + '. The values are: ' + Object.keys(table).join(', ') + '.');
    return value;
}

/// Encodes `opts` into the scratch buffer and returns the number of PAYLOAD slots. `optMask` holds the bitmask.
/// Fields are written in ascending bit order, which is the only ordering rule the decoder needs.
function encodeOptions(opts, forcedTextStyle) {
    optMask = 0;
    optCount = 0;

    if (forcedTextStyle !== undefined && (!opts || opts.textStyle === undefined)) {
        // An alias (section 2.3's heading, subheading, caption) is this op with textStyle pre-set. The author's
        // own textStyle wins if they passed one, which is what makes `ui.heading(x, { textStyle: 'display' })`
        // mean what it reads as.
        optMask |= OPT_TEXT_STYLE;
    }

    if (opts !== null && opts !== undefined) {
        if (typeof opts !== 'object')
            throw new NowUIAuthorError('NowUI: the options argument must be an object, and this one is a ' +
                typeof opts + '. Argument order is key, value, data, options (section 1.1 R2).');

        for (const name in opts) {
            if (!KNOWN_OPTIONS.has(name))
                throw new NowUIAuthorError(
                    'NowUI: "' + name + '" is not an option.\n' +
                    'The options are: ' + Array.from(KNOWN_OPTIONS).sort().join(', ') + '.');
        }

        if (opts.width !== undefined) optMask |= OPT_WIDTH;
        if (opts.height !== undefined) optMask |= OPT_HEIGHT;
        if (opts.minWidth !== undefined) optMask |= OPT_MIN_WIDTH;
        if (opts.maxWidth !== undefined) optMask |= OPT_MAX_WIDTH;
        if (opts.minHeight !== undefined) optMask |= OPT_MIN_HEIGHT;
        if (opts.maxHeight !== undefined) optMask |= OPT_MAX_HEIGHT;
        if (opts.grow !== undefined) optMask |= OPT_GROW;
        if (opts.gap !== undefined) optMask |= OPT_GAP;
        if (opts.padding !== undefined) optMask |= OPT_PADDING;
        if (opts.align !== undefined) optMask |= OPT_ALIGN;
        if (opts.justify !== undefined) optMask |= OPT_JUSTIFY;
        if (opts.style !== undefined) optMask |= OPT_STYLE;
        if (opts.textStyle !== undefined) optMask |= OPT_TEXT_STYLE;
        if (opts.step !== undefined) optMask |= OPT_STEP;
        if (opts.rect === true) optMask |= OPT_RECT;
        if (opts.disabled === true) optMask |= OPT_DISABLED;
    }

    if (optMask === 0) return 0;

    if ((optMask & OPT_WIDTH) !== 0) pushF(num(opts.width, 'width'));
    if ((optMask & OPT_HEIGHT) !== 0) pushF(num(opts.height, 'height'));
    if ((optMask & OPT_MIN_WIDTH) !== 0) pushF(num(opts.minWidth, 'minWidth'));
    if ((optMask & OPT_MAX_WIDTH) !== 0) pushF(num(opts.maxWidth, 'maxWidth'));
    if ((optMask & OPT_MIN_HEIGHT) !== 0) pushF(num(opts.minHeight, 'minHeight'));
    if ((optMask & OPT_MAX_HEIGHT) !== 0) pushF(num(opts.maxHeight, 'maxHeight'));
    if ((optMask & OPT_GROW) !== 0) pushF(num(opts.grow, 'grow'));
    if ((optMask & OPT_GAP) !== 0) pushF(num(opts.gap, 'gap'));

    if ((optMask & OPT_PADDING) !== 0) {
        const p = opts.padding;

        if (typeof p === 'number') {
            pushF(p); pushF(p); pushF(p); pushF(p);
        } else if (Array.isArray(p) && p.length === 2) {
            pushF(num(p[0], 'padding[0]')); pushF(num(p[1], 'padding[1]'));
            pushF(num(p[0], 'padding[0]')); pushF(num(p[1], 'padding[1]'));
        } else if (Array.isArray(p) && p.length === 4) {
            for (let i = 0; i < 4; i++) pushF(num(p[i], 'padding[' + i + ']'));
        } else {
            throw new NowUIAuthorError(
                'NowUI: padding is a number, [horizontal, vertical] or [left, top, right, bottom]; this is ' +
                JSON.stringify(p) + '.');
        }
    }

    // Section 2.7 lists 'stretch' as a fourth value for `align`. NowLayoutAlign has three members - Start,
    // Center, End (NowLayout.cs:8-13) - and there is no fourth to map it to. The source wins, and the option is
    // rejected by name rather than coerced into one of the three.
    if ((optMask & OPT_ALIGN) !== 0) pushI(enumValue(ALIGN, opts.align, 'child alignment'));
    if ((optMask & OPT_JUSTIFY) !== 0) pushI(enumValue(JUSTIFY, opts.justify, 'justification'));
    if ((optMask & OPT_STYLE) !== 0) pushI(enumValue(RECT_STYLE, opts.style, 'rectangle style'));

    if ((optMask & OPT_TEXT_STYLE) !== 0) {
        const name = opts && opts.textStyle !== undefined ? opts.textStyle : forcedTextStyle;
        pushI(enumValue(TEXT_STYLE, name, 'text style'));
    }

    if ((optMask & OPT_STEP) !== 0) pushF(num(opts.step, 'step'));

    return optCount;
}

/// Emits the OPTS op, if the last `encodeOptions` produced one. Nothing is emitted for a call with no options,
/// which is what keeps section 5.3's at-rest size estimate honest.
function emitOptions(payload) {
    if (optMask === 0) return;

    W.op(OPS.OPTS, 1 + payload);
    W.i32(optMask);
    for (let i = 0; i < payload; i++) W.i32(SCRATCH_I32[i]);
}

/// The two-line pattern every function below starts with: encode, then emit, then the op itself.
function options(opts, forcedTextStyle) {
    emitOptions(encodeOptions(opts, forcedTextStyle));
}

/// A DRAWING's options, which are not quite a control's. Section 2.7 maps `style` to
/// `SetStyle(NowRectangleStyle)`, and section 1's own application writes
///
///     ui.text('Nobody on the team yet.', { style: 'muted' })
///
/// on a call that draws a NowLabel. A NowLabel has no rectangle and no SetStyle: its entire style surface is
/// SetFont / SetFontSize / SetColor / SetGradient / the layout setters (NowLayout.cs:466-780). So one of the two
/// is wrong, and the SOURCE wins - but the example is the contract, and it plainly means muted TEXT, which
/// NowTextStyle.Muted is (NowThemeStyles.cs:22).
///
/// So on the four drawings that have no rectangle, `style` names a text style. It is a rename rather than a
/// second meaning: 'muted' is the only name the two enums share, and every other rectangle-style name is
/// rejected here by enumValue with the list of text styles, rather than silently drawing nothing.
function drawingOptions(opts, forcedTextStyle) {
    if (opts && opts.style !== undefined) {
        const copy = Object.assign({}, opts);
        if (copy.textStyle === undefined) copy.textStyle = copy.style;
        delete copy.style;
        opts = copy;
    }

    emitOptions(encodeOptions(opts, forcedTextStyle));
}

// ------------------------------------------------------------------------------------------------ helpers

function text(value) {
    return value === undefined || value === null ? '' : String(value);
}

function labelOf(opts, key) {
    return opts && opts.label !== undefined ? String(opts.label) : String(key);
}

function segArgs(node) {
    W.i32(node.rid);
    W.i32(node.seg);
}

/// R4, once. Every scope in this file is these four lines, and the `finally` is what makes the emitted stream
/// balanced on every path out - including the one where the author's body throws (section 4.4).
function scope(record, node, writeArgs, body) {
    W.beginScope(record, node, writeArgs);
    try {
        if (typeof body === 'function') body();
    } finally {
        W.endScope();
    }
}

/// A container scope: keyed through `opts.key` (section 3.5's escape 2), anonymous otherwise.
function container(record, opts, body) {
    const key = opts && typeof opts === 'object' ? opts.key : undefined;
    const payload = encodeOptions(opts);
    const node = key === undefined || key === null ? trie.pushAnon() : trie.pushScope(key);

    emitOptions(payload);
    scope(record, node, () => segArgs(node), body);
}

/// The option-list argument of ui.dropdown, ui.combo, ui.tabs and ui.radio.
function requireList(value, fn) {
    if (!Array.isArray(value))
        throw new NowUIAuthorError('NowUI: ' + fn + ' takes an array of strings, and this is ' +
            (value === undefined ? 'undefined' : typeof value) + '. Argument order is key, value, data, options.');
    return value;
}

function emitStrings(list) {
    W.i32(list.length);
    for (let i = 0; i < list.length; i++) W.i32(W.str(text(list[i])));
}

function notInThisRelease(name, why) {
    return () => {
        throw new NowUIAuthorError('NowUI: ui.' + name + ' is not in this build. ' + why);
    };
}

// ------------------------------------------------------------------------------------------------ the surface

export const ui = {

    // ---------------------------------------------------------------------------------------- 2.1 lifecycle

    /// Section 2.1. The host poll at the top of the CURRENT frame - the one thing in this API that is not one
    /// frame old (R6, section 6.2).
    get frame() {
        return frameInfo;
    },

    /// Section 2.1. The canonical path (section 3.2) of where the recorder is, for a diagnostic an author writes
    /// themselves. Costs one string build, on demand.
    debugPath() {
        return trie.path(trie.current);
    },

    theme: notInThisRelease('theme',
        'It resolves a NowUI theme asset by name from the host resource manifest (section 2.1), and this host ' +
        'serves exactly one theme with no manifest to look a second one up in.'),

    reset: notInThisRelease('reset',
        'Hot reload is W7: it has to clear NowRuntime, the intern table on BOTH sides of the boundary, the path ' +
        'trie and the result table together, and clearing one without the others is how a string handle comes to ' +
        'mean two different strings.'),

    // ------------------------------------------------------------------------------------------- 2.2 scopes

    column(opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }
        container(OPS.COLUMN, opts, body);
    },

    row(opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }
        container(OPS.ROW, opts, body);
    },

    /// Section 2.2's composite: a column, with a themed rectangle drawn over the group's reserved rect before the
    /// children, so the background is behind them.
    card(opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }
        container(OPS.CARD, opts, body);
    },

    /// Section 3.5's escape 1, and the reason it is a function in a 41-function API instead of "just write an
    /// if": an `if` around a control is free, an `if` around a SCOPE renumbers every anonymous sibling after it.
    /// This always consumes its ordinal and runs the body only when `cond` holds, so toggling it shifts nothing.
    when(cond, body) {
        const node = trie.pushAnon();
        W.beginScope(OPS.IDSCOPE, node, () => segArgs(node));
        try {
            if (cond && typeof body === 'function') body();
        } finally {
            W.endScope();
        }
    },

    /// Section 2.2. `keyOf` is required and there is no index default: returning the index makes state follow
    /// position, which is the exact failure this whole identity model exists to prevent. Section 3.6's check 3
    /// lives here, because NowControls.KeyedItemIn would throw on an empty key (NowControls.cs:262-266) with the
    /// JavaScript context already lost.
    list(key, items, keyOf, render) {
        if (typeof keyOf !== 'function')
            throw new NowUIAuthorError('NowUI: ui.list(key, items, keyOf, render) requires keyOf to be a function.');

        const seen = new Set();
        const array = items || [];

        for (let i = 0; i < array.length; i++) {
            const itemKey = keyOf(array[i], i);

            if (typeof itemKey !== 'string' || itemKey.length === 0) {
                throw new NowUIAuthorError(
                    'NowUI: ui.list(\'' + key + '\', ...) - keyOf returned ' +
                    (itemKey === undefined ? 'undefined' : JSON.stringify(itemKey)) + ' for index ' + i + '.\n' +
                    'keyOf must return a non-empty string that identifies the item across frames. Returning the\n' +
                    'index makes state follow position, so a reorder moves carets and scroll offsets.');
            }

            if (seen.has(itemKey)) {
                throw new NowUIAuthorError(
                    'NowUI: ui.list(\'' + key + '\', ...) - keyOf returned the duplicate key ' +
                    JSON.stringify(itemKey) + ' at index ' + i + '.\n' +
                    'Two items with the same key are one control path: they would share focus, caret and drag\n' +
                    'state.');
            }

            seen.add(itemKey);

            const node = trie.pushItem(key, itemKey);
            scope(OPS.LIST_ITEM, node, () => {
                W.i32(node.rid);
                W.i32(node.seg);
                W.i32(node.seg2);
            }, () => render(array[i], i));
        }
    },

    scroll(key, opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }
        const payload = encodeOptions(opts);
        const node = trie.pushScope(key);
        emitOptions(payload);
        scope(OPS.SCROLL, node, () => segArgs(node), body);
    },

    /// Section 2.2. Both a control and a scope: the header draws and returns the open state, and the body is
    /// recorded only when it is open, so a closed foldout costs one op.
    foldout(key, open, opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }

        const payload = encodeOptions(opts);
        const node = trie.pushScope(key);
        const resolved = R.value(node.rid, open === true);

        emitOptions(payload);
        W.beginScope(OPS.FOLDOUT, node, () => {
            W.i32(node.rid);
            W.i32(node.seg);
            W.i32(resolved ? 1 : 0);
            W.i32(W.str(labelOf(opts, key)));
        });

        try {
            if (resolved && typeof body === 'function') body();
        } finally {
            W.endScope();
        }

        return resolved;
    },

    split: notInThisRelease('split',
        'It is the only function with two bodies, and the command stream has one scope bracket: recording pane ' +
        'two needs a second one that section 5.2 does not define. The op is designed and unbuilt.'),

    overlay: notInThisRelease('overlay',
        'It is W11, and the sole consumer of the recorded subtrees of section 5.8 - OP_CALLBACK_BEGIN and ' +
        'OP_CALLBACK_END are reserved opcodes 2 and 3 and nothing emits them yet.'),

    // ----------------------------------------------------------------------------------------- 2.3 drawings

    text(content, opts) {
        drawingOptions(opts);
        W.op(OPS.TEXT);
        W.i32(W.str(text(content)));
    },

    heading(content, opts) {
        drawingOptions(opts, 'heading');
        W.op(OPS.TEXT);
        W.i32(W.str(text(content)));
    },

    subheading(content, opts) {
        drawingOptions(opts, 'subheading');
        W.op(OPS.TEXT);
        W.i32(W.str(text(content)));
    },

    caption(content, opts) {
        drawingOptions(opts, 'caption');
        W.op(OPS.TEXT);
        W.i32(W.str(text(content)));
    },

    space(pixels) {
        W.op(OPS.SPACE);
        W.f32(typeof pixels === 'number' ? pixels : 0);
    },

    flexSpace(weight) {
        W.op(OPS.FLEX_SPACE);
        W.f32(typeof weight === 'number' ? weight : 1);
    },

    rule(opts) {
        options(opts);
        W.op(OPS.RULE);
    },

    badge(content, opts) {
        // A badge DOES have a rectangle (NowBadge.SetStyle, Controls/NowBadge.cs:50), so `style` keeps section
        // 2.7's meaning here and `textStyle` is separate. It is the one drawing that is not a bare label.
        options(opts);
        W.op(OPS.BADGE);
        W.i32(W.str(text(content)));
    },

    // ------------------------------------------------------------------------------------------ 2.4 actions

    /// R5: true on exactly the frame the click is delivered, once. The read consumes the latch (section 6.3), so
    /// reading it twice in one frame cannot fire twice, and a button that stopped being drawn cannot fire at all.
    button(key, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);

        emitOptions(payload);
        W.op(OPS.BUTTON);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.str(labelOf(opts, key)));

        return R.event(node.rid, F_CLICKED);
    },

    selectable(key, selected, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);

        emitOptions(payload);
        W.op(OPS.SELECTABLE);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(selected ? 1 : 0);
        W.i32(W.str(labelOf(opts, key)));

        return R.event(node.rid, F_CLICKED);
    },

    /// `removed` is delivered through opts.onRemove and never as a returned object (section 2.4): a bag of flags
    /// would reintroduce the `if (obj)` trap that R5 exists to remove.
    chip(key, label, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const removable = !!(opts && typeof opts.onRemove === 'function');

        emitOptions(payload);
        W.op(OPS.CHIP);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.str(text(label)));
        W.i32(opts && opts.selected ? 1 : 0);
        W.i32(removable ? 1 : 0);

        rememberHandlers(node.rid, opts);
        return R.event(node.rid, F_CLICKED);
    },

    // ------------------------------------------------------------------------------------------- 2.5 values
    //
    // Every one of the fourteen is the same five lines, and the ORDER of the middle two is section 6.4: resolve
    // the value FIRST, emit the RESOLVED value, return the resolved value. Emitting the caller's value and then
    // consulting the result is the version that reverts a keystroke one frame after it was typed.

    textField(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, text(value));

        emitOptions(payload);
        W.op(OPS.TEXT_FIELD);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.volatileStr(resolved));
        W.i32(W.str(opts && opts.placeholder !== undefined ? String(opts.placeholder) : ''));

        rememberHandlers(node.rid, opts);
        return resolved;
    },

    textArea(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, text(value));

        emitOptions(payload);
        W.op(OPS.TEXT_AREA);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.volatileStr(resolved));
        W.i32(W.str(opts && opts.placeholder !== undefined ? String(opts.placeholder) : ''));

        return resolved;
    },

    numberField(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        let resolved = R.value(node.rid, typeof value === 'number' ? value : 0);

        emitOptions(payload);
        W.op(OPS.NUMBER_FIELD);
        W.i32(node.rid);
        W.i32(node.seg);
        W.f32(resolved);
        W.i32(W.str(opts && opts.format !== undefined ? String(opts.format) : ''));

        // min/max are the author's range and are applied on the way OUT, not on the way in: clamping a
        // half-typed number as it is typed deletes digits.
        if (opts && typeof opts.min === 'number' && resolved < opts.min) resolved = opts.min;
        if (opts && typeof opts.max === 'number' && resolved > opts.max) resolved = opts.max;

        rememberHandlers(node.rid, opts);
        return resolved;
    },

    checkbox(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, value === true);

        emitOptions(payload);
        W.op(OPS.CHECKBOX);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved ? 1 : 0);
        W.i32(W.str(labelOf(opts, key)));

        return resolved;
    },

    switch(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, value === true);

        emitOptions(payload);
        W.op(OPS.SWITCH);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved ? 1 : 0);

        // A switch's label defaults to EMPTY rather than to the key, unlike a button's. A button's key is the
        // word on it; a switch's key is what it controls, and `ui.switch('active', p.active)` in section 1's
        // roster would otherwise print "active" beside every row.
        W.i32(W.str(opts && opts.label !== undefined ? String(opts.label) : ''));

        return resolved;
    },

    /// Section 2.5's composite: one NowLayout.Radio per option under one identity scope, each keyed by its own
    /// option string, so adding an option cannot renumber the others.
    radio(key, value, list, opts) {
        requireList(list, 'ui.radio(key, value, options, opts?)');

        const node = trie.pushScope(key);
        let selected = value;

        W.beginScope(OPS.IDSCOPE, node, () => segArgs(node));
        try {
            for (let i = 0; i < list.length; i++) {
                const option = String(list[i]);
                const payload = encodeOptions(opts);
                const item = trie.control(option);

                emitOptions(payload);
                W.op(OPS.RADIO_ITEM);
                W.i32(item.rid);
                W.i32(item.seg);
                W.i32(option === value ? 1 : 0);
                W.i32(W.str(option));

                if (R.event(item.rid, F_CLICKED)) selected = option;
            }
        } finally {
            W.endScope();
        }

        return selected;
    },

    slider(key, value, min, max, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, typeof value === 'number' ? value : 0);

        emitOptions(payload);
        W.op(OPS.SLIDER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.f32(resolved);
        W.f32(num(min, 'slider min'));
        W.f32(num(max, 'slider max'));

        return resolved;
    },

    intSlider(key, value, min, max, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(typeof value === 'number' ? value : 0));

        emitOptions(payload);
        W.op(OPS.INT_SLIDER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved);
        W.f32(num(min, 'slider min'));
        W.f32(num(max, 'slider max'));

        return resolved;
    },

    /// Section 2.5. The INDEX never surfaces: the value going in and the value coming out are both the option
    /// itself. Two frames late (section 6.2) - the popup's selection is delivered on the frame after the click.
    dropdown(key, value, list, opts) {
        return emitChoice(OPS.DROPDOWN, key, value, list, opts, 'ui.dropdown(key, value, options, opts?)');
    },

    combo(key, value, list, opts) {
        return emitChoice(OPS.COMBO, key, value, list, opts, 'ui.combo(key, value, options, opts?)');
    },

    /// Section 2.5. The one tier-1 function that returns a non-primitive, and it is a VALUE rather than an event:
    /// `if (ui.colorField(...))` is as meaningless as `if (someArray)` anywhere else in JavaScript.
    colorField(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);

        // Reconciled in the packed integer, not in the array: a fresh [r,g,b,a] every frame is never `Object.is`
        // to the last one, so an array would make section 6.4's "the author is echoing" test always false and
        // the control would never see its own value back.
        const resolved = R.value(node.rid, pack(value));

        emitOptions(payload);
        W.op(OPS.COLOR_FIELD);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved | 0);

        return unpack(resolved);
    },

    datePicker(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, typeof value === 'number' ? value : 0);

        emitOptions(payload);
        W.op(OPS.DATE_PICKER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved | 0);                              // low word
        W.i32(Math.floor(resolved / 4294967296) | 0);     // high word

        return resolved;
    },

    timePicker(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(typeof value === 'number' ? value : 0));

        emitOptions(payload);
        W.op(OPS.TIME_PICKER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved);

        return resolved;
    },

    tabs(key, selected, labels, opts) {
        requireList(labels, 'ui.tabs(key, selected, labels, opts?)');

        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(typeof selected === 'number' ? selected : 0));

        emitOptions(payload);
        W.op(OPS.TABS, OPS.TABS.slots + labels.length);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved);
        emitStrings(labels);

        return resolved;
    },

    // ----------------------------------------------------------------------------------------- 2.6 feedback

    progress(value01, opts) {
        options(opts);
        W.op(OPS.PROGRESS);
        W.f32(typeof value01 === 'number' ? value01 : 0);
    },

    contextMenu: notInThisRelease('contextMenu',
        'It is the one control that needs a NowResolvedId rather than a NowId - NowContextMenu.Begin takes one ' +
        'and Begin(int) is [Obsolete(error)] - through a begin/item/end triple the command stream does not carry ' +
        'yet (section 2.6).'),
};

/// The shared body of ui.dropdown and ui.combo. Named in a way no author ever types, because it is not part of
/// the surface: the two functions above are, and they differ only in which op they emit.
function emitChoice(record, key, value, list, opts, signature) {
    requireList(list, signature);

    const payload = encodeOptions(opts);
    const node = trie.control(key);

    // Reconciled in INDEX space, because that is what the control produces and what the result table carries.
    // A value that is not in the list sends index 0; the return below hands the author back their own value in
    // that case rather than silently rewriting it to the first option.
    const found = list.indexOf(value);
    const resolved = R.value(node.rid, found < 0 ? 0 : found);

    emitOptions(payload);
    W.op(record, record.slots + list.length);
    W.i32(node.rid);
    W.i32(node.seg);
    W.i32(resolved);
    emitStrings(list);

    if (found < 0 && resolved === 0) return value;
    return resolved >= 0 && resolved < list.length ? list[resolved] : value;
}

function pack(rgba) {
    if (!Array.isArray(rgba)) return 0xFF000000 | 0;
    const b = (i) => Math.max(0, Math.min(255, Math.round((rgba[i] === undefined ? (i === 3 ? 1 : 0) : rgba[i]) * 255)));
    return ((b(0) | (b(1) << 8) | (b(2) << 16) | (b(3) << 24)) | 0);
}

function unpack(packed) {
    const v = packed >>> 0;
    return [(v & 0xFF) / 255, ((v >> 8) & 0xFF) / 255, ((v >> 16) & 0xFF) / 255, ((v >>> 24) & 0xFF) / 255];
}

// ------------------------------------------------------------------------------------------------ start

/// The surface factory bridge.js calls once per `start`. It binds this module to that recorder; the `ui` object
/// itself is the same one for the life of the page, which is what lets an author import it at module scope.
function install(recorder) {
    W = recorder;
    trie = recorder.trie;
    R = recorder.results;
    return ui;
}

/// Section 2.1. `draw` is called once per animation frame, from inside the wasm frame, before NowUI is opened.
/// Returns a handle with `stop()` and `reset()`; W7 gives `reset()` something to do.
export function start(draw, opts = {}) {
    if (typeof draw !== 'function') throw new TypeError('nowui.start(draw) requires a function.');

    bridgeStart(() => {
        updateFrame(frameInfo.count + 1);
        fireHandlers();
        draw();
    }, Object.assign({}, opts, { surface: install }));

    return {
        stop() { bridgeStop(); },
        reset() { ui.reset(); },
    };
}

export default ui;
