// W6/W10 - the tier-1 surface. Docs/Standalone/M3-Spec.md section 2, and the application of section 1.
//
// This is the file an author imports. Everything else under wwwroot/nowui/ is plumbing they never see: the
// recorder, the path trie, the result table, the transport. Fifty functions, hand-written for now, and
// regenerated from Standalone/Surface/surface.json in W8 - which is why every function here is written the way a
// generator would emit it (one shape, no clever special cases) rather than the way a person would.
//
// THE SIX RULES OF SECTION 1.1 ARE THE WHOLE OF THE DESIGN, and each one is a property of this file:
//
//   R1  every interactive control takes a key first; nothing else does.  `trie.control(key)` vs no call at all.
//   R2  argument order is key, value, data, options, and a body comes last. Every signature below, no overloads.
//   R3  values go in and come back out.  `return R.value(rid, incoming)` - see section 6.4 and `value` below.
//   R4  scopes are callbacks.  `open; try { body(); } finally { close(); }` - four lines, and the same four for
//       all of them, which is what makes balance a property of the JavaScript call stack (section 4.1).
//   R5  boolean-returning controls are events.  `R.event(rid, F_CLICKED)` latches and consumes (section 6.3).
//   R6  everything a control returns is one frame old; everything under `ui.frame` is current (section 6.2).
//
// WHAT IS NOT HERE, and why, stated once rather than discovered at a call site. THREE of section 2's functions
// throw with a message naming the reason, rather than silently drawing nothing - and the list is not prose, it is
// the exported NOT_IMPLEMENTED array below, which notInThisRelease asserts against at module load. That is the
// structural fix for how this went wrong the first time: the spec and this file disagreed about which functions
// existed, and an AI reading the spec wrote code that threw.
//
//   ui.reset         hot reload - a coordinated clear of four caches. W7.
//   ui.overlay       W11 - it is the sole consumer of the recorded subtrees of section 5.8.
//   ui.contextMenu   absent, and its stated blocker is UNVERIFIED rather than settled. See the entry.
//
// ui.theme and ui.split were on that list until W10 and are now real: neither stated blocker survived reading the
// source. NowTheme already builds both a light and a dark asset, so a browser host's "resource manifest" is a
// two-entry table; and scope brackets NEST, so a split is SPLIT{ PANE(0){..} PANE(1){..} } rather than a second
// kind of bracket the ABI does not have.
//
// W10 ALSO ADDS DRAWING: ui.canvas, ui.mask and eight shape functions, over the opcodes W9 put on the wire. The
// one thing an author must know about them is the coordinate model, and it is stated once, at ui.canvas.

import {
    OPS,
    OPT_WIDTH, OPT_HEIGHT, OPT_MIN_WIDTH, OPT_MAX_WIDTH, OPT_MIN_HEIGHT, OPT_MAX_HEIGHT,
    OPT_GROW, OPT_GAP, OPT_PADDING, OPT_ALIGN, OPT_JUSTIFY, OPT_STYLE, OPT_TEXT_STYLE, OPT_STEP,
    OPT_RECT, OPT_DISABLED,
    OPT_COLOR, OPT_STROKE, OPT_STROKE_COLOR, OPT_RADIUS, OPT_BLUR,
    OPT_CAP, OPT_DASH, OPT_SEGMENTS, OPT_FILL, OPT_SPREAD, OPT_ANGLE,
    ALIGN, JUSTIFY, RECT_STYLE, TEXT_STYLE,
    PAINT_LITERAL, PAINT_TOKEN, COLOR_TOKEN,
    MASK_KIND, GRADIENT_KIND, GRADIENT_SPREAD, LINE_CAP, SPLIT_AXIS, THEME_MODE, IMAGE_FIT,
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
///
/// W10's drawing options are deliberately NOT in here. This set is one global answer to "is that a real option",
/// and it cannot say `segments` is meaningless on a line; every function that takes a W10 option passes its own
/// allowed set instead, so the message names the function and lists that function's options rather than all
/// thirty-eight. Controls and containers keep the global set, byte for byte.
const KNOWN_OPTIONS = new Set([
    'key', 'label', 'width', 'height', 'minWidth', 'maxWidth', 'minHeight', 'maxHeight', 'grow', 'gap',
    'padding', 'align', 'justify', 'style', 'textStyle', 'step', 'placeholder', 'rect', 'disabled',
    'onSubmit', 'onRemove', 'min', 'max', 'format', 'removable', 'selected',
]);

/// The layout half of section 2.7, which every W10 scope accepts and no W10 drawing does. A drawing has no box:
/// it carries its own coordinates (see ui.canvas), so `width` on a circle would be a second, contradicting answer
/// to where it is.
const LAYOUT_OPTIONS = [
    'key', 'width', 'height', 'minWidth', 'maxWidth', 'minHeight', 'maxHeight', 'grow', 'gap', 'padding',
    'align', 'justify',
];

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

// ------------------------------------------------------------------------------------- W10: argument validation
//
// ONE RULE, EVERYWHERE, because the old one was three rules and an accident:
//
//   ABSENT (undefined | null)  -> the documented zero: false, 0, ''. Legal, and it has to stay legal:
//                                 `ui.checkbox('a', state.notYetSet)` on frame one is normal and correct.
//   WRONG TYPE                 -> NowUIAuthorError naming the function, the parameter and what actually arrived.
//   NaN or +/-Infinity         -> NowUIAuthorError. This is the hole the other two were hiding: `typeof NaN` is
//                                 'number', so a NaN passed every guard in this file, reached W.f32, and poisoned
//                                 a layout with no message anywhere.
//
// What it replaced, for the record: `value === true` turned the string 'yes' into false, and
// `typeof value === 'number' ? value : 0` turned the string '50' into 0 - two lines away from `num()`, which
// threw a well-written named error for exactly the same class of mistake. This is a deliberate BREAKING change:
// code that silently drew a zero now says why.

function boolArg(value, fn, param) {
    if (value === undefined || value === null) return false;
    if (typeof value !== 'boolean')
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - ' + param + ' must be true or false, and this is ' + describe(value) + '.\n' +
            'An absent value (undefined or null) is legal and means false; anything else is a mistake rather ' +
            'than a coercion.');
    return value;
}

function numArg(value, fn, param) {
    if (value === undefined || value === null) return 0;
    if (typeof value !== 'number')
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - ' + param + ' must be a number, and this is ' + describe(value) + '.\n' +
            'An absent value (undefined or null) is legal and means 0.');
    if (!isFinite(value))
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - ' + param + ' is ' + String(value) + '.\n' +
            'NaN and Infinity reach the layout engine as coordinates and poison every box downstream of them, ' +
            'silently. This is the one number that has to be refused here rather than there.');
    return value;
}

function textArg(value, fn, param) {
    if (value === undefined || value === null) return '';
    const kind = typeof value;
    if (kind === 'string') return value;
    if (kind === 'number' || kind === 'boolean' || kind === 'bigint') return String(value);
    throw new NowUIAuthorError(
        'NowUI: ' + fn + ' - ' + param + ' must be text, and this is ' + describe(value) + '.\n' +
        'A number or a boolean is converted; an object, an array or a function is not, because "[object ' +
        'Object]" on screen is a bug report nobody files.');
}

/// What arrived, said in a way that is useful in a message. JSON.stringify alone returns undefined for a
/// function and for undefined itself, which is exactly the case the reader most needs named.
function describe(value) {
    if (value === undefined) return 'undefined';
    if (typeof value === 'function') return 'a function';
    if (typeof value === 'symbol') return 'a symbol';
    try {
        const text = JSON.stringify(value);
        return text === undefined ? String(value) : text;
    } catch (e) {
        return String(value);
    }
}

// ---------------------------------------------------------------------------------------------- W10: paint
//
// One author-facing `color`, discriminated by SHAPE rather than by a second option:
//
//   '#rgb' / '#rrggbb' / '#rrggbbaa'   a literal
//   [r, g, b] / [r, g, b, a], 0..1     a literal - and the same array ui.colorField returns, so
//                                      `{ color: ui.colorField('c', c) }` composes with no conversion at all
//   a bare name                        one of the 27 theme tokens, which follows light/dark where hex does not
//
// On the wire it is section 5.3's `paint`: two slots, [tag, value]. The literal packing is byte-identical to the
// COLOR_FIELD op's, which is what makes the composition above free rather than merely convenient.

/// The scratch pair every paint is parsed into. Reused, and therefore READ IMMEDIATELY: a caller that needs two
/// paints at once (ui.gradient) copies the first out before parsing the second.
const PAINT = [0, 0];

function parsePaint(value, fn, param) {
    if (typeof value === 'string') {
        if (value.charCodeAt(0) === 35 /* # */) {
            PAINT[0] = PAINT_LITERAL;
            PAINT[1] = parseHex(value, fn, param);
            return PAINT;
        }

        const token = COLOR_TOKEN[value];
        if (token === undefined) throw paintError(value, fn, param);

        PAINT[0] = PAINT_TOKEN;
        PAINT[1] = token;
        return PAINT;
    }

    if (Array.isArray(value)) {
        if (value.length < 3 || value.length > 4) throw paintError(value, fn, param);
        for (let i = 0; i < value.length; i++) numArg(value[i], fn, param + '[' + i + ']');

        PAINT[0] = PAINT_LITERAL;
        PAINT[1] = pack(value);
        return PAINT;
    }

    throw paintError(value, fn, param);
}

function parseHex(text, fn, param) {
    const body = text.slice(1);
    if (!/^[0-9a-fA-F]+$/.test(body) || (body.length !== 3 && body.length !== 6 && body.length !== 8))
        throw paintError(text, fn, param);

    const wide = body.length === 3
        ? body[0] + body[0] + body[1] + body[1] + body[2] + body[2] + 'ff'
        : (body.length === 6 ? body + 'ff' : body);

    const r = parseInt(wide.slice(0, 2), 16);
    const g = parseInt(wide.slice(2, 4), 16);
    const b = parseInt(wide.slice(4, 6), 16);
    const a = parseInt(wide.slice(6, 8), 16);

    // Red in the LOW byte, exactly as `pack` does it, exactly as COLOR_FIELD does it.
    return (r | (g << 8) | (b << 16) | (a << 24)) | 0;
}

function paintError(value, fn, param) {
    return new NowUIAuthorError(
        'NowUI: ' + fn + ' - ' + param + ' is not a colour. It was ' + describe(value) + '.\n' +
        'A colour is one of three things:\n' +
        "  a theme token, by name      { color: 'accent' }      - follows light and dark\n" +
        "  a hex literal               { color: '#3B82F6' }     - '#rgb', '#rrggbb' or '#rrggbbaa'\n" +
        '  RGBA floats 0..1            { color: [0.2, 0.5, 1] } - the same array ui.colorField returns\n' +
        'The theme tokens are: ' + Object.keys(COLOR_TOKEN).join(', ') + '.');
}

/// Encodes `opts` into the scratch buffer and returns the number of PAYLOAD slots. `optMask` holds the bitmask.
/// Fields are written in ascending bit order, which is the only ordering rule the decoder needs.
///
/// `allowed` is the per-function name set of W10's drawing surface; `fn` names the function in its diagnostics.
/// Omit both and the global KNOWN_OPTIONS applies, which is what every control and container does - their
/// encoding is unchanged, byte for byte.
function encodeOptions(opts, forcedTextStyle, allowed, fn) {
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
            if (allowed !== undefined) {
                if (allowed.indexOf(name) < 0) throw unknownOption(name, allowed, fn);
            } else if (!KNOWN_OPTIONS.has(name)) {
                throw new NowUIAuthorError(
                    'NowUI: "' + name + '" is not an option.\n' +
                    'The options are: ' + Array.from(KNOWN_OPTIONS).sort().join(', ') + '.');
            }
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

        // The two flag-only bits. `=== true` here would silently swallow `{ rect: 'yes' }` and `{ disabled: 1 }`,
        // which is the coercion this unit removed everywhere else; a flag can only SAY true, but it can still be
        // asked wrongly.
        if (opts.rect !== undefined && boolArg(opts.rect, fn || 'options', 'rect')) optMask |= OPT_RECT;
        if (opts.disabled !== undefined && boolArg(opts.disabled, fn || 'options', 'disabled'))
            optMask |= OPT_DISABLED;

        // --- W10: bits 16-27, still in ascending order and still appended behind the fourteen above.
        if (opts.color !== undefined) optMask |= OPT_COLOR;
        if (opts.stroke !== undefined) optMask |= OPT_STROKE;
        if (opts.strokeColor !== undefined) optMask |= OPT_STROKE_COLOR;
        if (opts.radius !== undefined) optMask |= OPT_RADIUS;
        if (opts.blur !== undefined) optMask |= OPT_BLUR;
        if (opts.cap !== undefined) optMask |= OPT_CAP;
        if (opts.dash !== undefined) optMask |= OPT_DASH;
        if (opts.segments !== undefined) optMask |= OPT_SEGMENTS;
        if (opts.fill !== undefined) optMask |= OPT_FILL;
        if (opts.spread !== undefined) optMask |= OPT_SPREAD;
        if (opts.angle !== undefined) optMask |= OPT_ANGLE;
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

    // Bits 14 and 15 carry nothing. W10's fields start at bit 16 and continue in ascending order, which is why
    // nothing above this line moved.

    if ((optMask & OPT_COLOR) !== 0) {
        const paint = parsePaint(opts.color, fn || 'options', 'color');
        pushI(paint[0]);
        pushI(paint[1]);
    }

    if ((optMask & OPT_STROKE) !== 0) pushF(num(opts.stroke, 'stroke'));

    if ((optMask & OPT_STROKE_COLOR) !== 0) {
        const paint = parsePaint(opts.strokeColor, fn || 'options', 'strokeColor');
        pushI(paint[0]);
        pushI(paint[1]);
    }

    if ((optMask & OPT_RADIUS) !== 0) {
        // HUMAN order - topLeft, topRight, bottomRight, bottomLeft - which is what BridgeOptions.radius carries
        // and what the four-float setters take. The renderer's own packed Vector4 order is different and the
        // decoder never uses it, for exactly this reason.
        const r = opts.radius;

        if (typeof r === 'number') {
            const v = num(r, 'radius');
            pushF(v); pushF(v); pushF(v); pushF(v);
        } else if (Array.isArray(r) && r.length === 4) {
            for (let i = 0; i < 4; i++) pushF(num(r[i], 'radius[' + i + ']'));
        } else {
            throw new NowUIAuthorError(
                'NowUI: radius is a number or [topLeft, topRight, bottomRight, bottomLeft]; this is ' +
                describe(r) + '.');
        }
    }

    if ((optMask & OPT_BLUR) !== 0) pushF(num(opts.blur, 'blur'));

    // Bit 21, fontSize, is RESERVED on the wire and applied by nothing: NowUI's label path takes a resolved
    // NowTextStyle rather than a size. It is not in any allowed set, so asking for it is an error with a name
    // (see unknownOption) rather than a value that vanishes.

    if ((optMask & OPT_CAP) !== 0) pushI(enumValue(LINE_CAP, opts.cap, 'line cap'));

    if ((optMask & OPT_DASH) !== 0) {
        const d = opts.dash;
        if (!Array.isArray(d) || d.length < 2 || d.length > 3)
            throw new NowUIAuthorError(
                'NowUI: dash is [length, gap] or [length, gap, offset]; this is ' + describe(d) + '.');

        pushF(num(d[0], 'dash[0]'));
        pushF(num(d[1], 'dash[1]'));
        pushF(d.length === 3 ? num(d[2], 'dash[2]') : 0);
    }

    if ((optMask & OPT_SEGMENTS) !== 0) pushI(Math.round(num(opts.segments, 'segments')));

    // A payload slot rather than a flag bit, and this is the whole reason it costs one: a flag can only say
    // true, and `fill: false` - an unfilled ring, an outlined polygon - is the thing an author most wants to say.
    if ((optMask & OPT_FILL) !== 0) pushI(boolArg(opts.fill, fn || 'options', 'fill') ? 1 : 0);

    if ((optMask & OPT_SPREAD) !== 0) pushI(enumValue(GRADIENT_SPREAD, opts.spread, 'gradient spread'));
    if ((optMask & OPT_ANGLE) !== 0) pushF(num(opts.angle, 'angle'));

    return optCount;
}

/// The message for an option a particular function does not take. It names the function and lists that
/// function's options - the global list is thirty-eight names long and telling an author writing `ui.line` that
/// `placeholder` exists somewhere is not help.
function unknownOption(name, allowed, fn) {
    // `style` on a shape is the one wrong option worth a sentence of its own, because it is a REASONABLE
    // mistake: ui.rect takes it, and NowCircle, NowLine, NowTriangle and NowPolygon have no SetStyle at all -
    // there is nothing in the C# library to map it to. Same precedent as align's 'stretch': rejected by name,
    // with the pointer, rather than silently drawing nothing.
    if (name === 'style' && allowed.indexOf('color') >= 0) {
        return new NowUIAuthorError(
            'NowUI: ' + fn + " does not take `style`. A semantic style sets colour, radius and outline together, " +
            'and only a rectangle has all three - NowCircle, NowLine, NowTriangle and NowPolygon have no ' +
            'SetStyle to map it to.\n' +
            "Name the colour instead: { color: 'accent' }, or draw the box with ui.rect(box, { style: 'accent' }).");
    }

    if (name === 'fontSize') {
        return new NowUIAuthorError(
            'NowUI: `fontSize` is reserved on the wire and applied by nothing in this build - NowUI resolves a ' +
            'label through a NowTextStyle rather than a size, so there is nothing to set.\n' +
            "Use { textStyle: 'caption' } .. { textStyle: 'display' } for size.");
    }

    return new NowUIAuthorError(
        'NowUI: "' + name + '" is not an option for ' + fn + '.\n' +
        'Its options are: ' + allowed.slice().sort().join(', ') + '.');
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
function options(opts, forcedTextStyle, allowed, fn) {
    emitOptions(encodeOptions(opts, forcedTextStyle, allowed, fn));
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

// -------------------------------------------------------------------------------------- W10: drawing geometry
//
// A drawing's coordinates are VALIDATED FIRST AND EMITTED SECOND, into this scratch buffer, and the order is
// load-bearing rather than tidy. `W.op(record, n)` writes an op header that PROMISES n argument slots; a throw
// after it and before the nth slot leaves the stream one op short of its own header, which is a corrupt frame
// rather than a caught author error. So every ui.rect / ui.circle / ui.polygon below reads its arguments into
// here, where a bad one throws with nothing yet emitted, and only then opens the op.
//
// Reused across calls and grown, never reallocated per frame: a 500-point polyline is one op and no garbage.

let geom = new Float64Array(64);
let geomCount = 0;

function geomReset() {
    geomCount = 0;
}

function geomPush(value) {
    if (geomCount === geom.length) {
        const grown = new Float64Array(geom.length * 2);
        grown.set(geom);
        geom = grown;
    }
    geom[geomCount++] = value;
}

/// A point: [x, y].
function geomPoint(value, fn, param) {
    if (!Array.isArray(value) || value.length !== 2)
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - ' + param + ' is a point, [x, y], and this is ' + describe(value) + '.\n' +
            'Coordinates are in CANVAS-LOCAL pixels: relative to the top-left of the enclosing ui.canvas, or to ' +
            'the screen when there is none.');

    geomPush(numArg(value[0], fn, param + '[0]'));
    geomPush(numArg(value[1], fn, param + '[1]'));
}

/// A box: [x, y, width, height].
function geomBox(value, fn, param) {
    if (!Array.isArray(value) || value.length !== 4)
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - ' + param + ' is a box, [x, y, width, height], and this is ' + describe(value) +
            '.\nCoordinates are in CANVAS-LOCAL pixels: relative to the top-left of the enclosing ui.canvas, or ' +
            'to the screen when there is none.');

    for (let i = 0; i < 4; i++) geomPush(numArg(value[i], fn, param + '[' + i + ']'));
}

function geomFlush() {
    for (let i = 0; i < geomCount; i++) W.f32(geom[i]);
}

// ------------------------------------------------------------------------------------------------ helpers

/// Turns whatever an author wrote into the absolute http(s) URL the cache on the other side needs.
///
/// THIS IS NOT A CONVENIENCE. NowMarkdownImages and NowLottieCache both decide what to do with a string by
/// asking whether it is an http(s) URL; anything else is treated as a Unity Resources path and fails without a
/// single network request. So `ui.image('/logo.png')` - the most natural thing an author can write, and the form
/// every example uses - would silently draw a placeholder forever. Resolving here is also the only place it CAN
/// happen: the page knows its own origin and the WebAssembly side does not.
///
/// Resolving also makes the absolute URL the CACHE KEY, which is what stops '/a.png' and './a.png' from being
/// downloaded twice.
function absoluteUrl(value, fn) {
    const raw = textArg(value, fn, 'url');
    if (raw === '' || typeof location === 'undefined') return raw;

    try {
        return new URL(raw, location.href).href;
    } catch (e) {
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' - "' + raw + '" is not a url this page can resolve. Use a path like ' +
            '"/art/logo.png", or a full "https://..." address.');
    }
}

/// Every string that reaches the wire goes through here, and since W10 that means every one of them obeys the
/// same rule: absent is '', a number or a boolean converts, an object does not.
function text(value, fn, param) {
    return textArg(value, fn || 'NowUI', param || 'content');
}

function labelOf(opts, key) {
    return opts && opts.label !== undefined ? String(opts.label) : String(key);
}

/// The other half of `labelOf`, and the reason it needs one. Four controls draw a label - button, checkbox,
/// selectable and foldout - because their NowUI op carries a string slot for one. The rest have no such slot,
/// and NowUI's own NowSlider and NowDropdown have no label concept at all: a caller draws the text beside the
/// control. So `{ label }` on those was ACCEPTED by the global option set and then silently dropped, which is the
/// invisible no-op this surface exists to refuse. It is refused by name instead, with the thing to write instead.
function refuseLabel(opts, fn) {
    if (opts && opts.label !== undefined)
        throw new NowUIAuthorError(
            'NowUI: ' + fn + ' has no label of its own, so { label: ' + JSON.stringify(String(opts.label)) +
            ' } would be silently ignored. NowUI draws the text beside the control, not inside it:\n' +
            "  ui.row(() => { ui.text('" + String(opts.label) + "'); " + fn + "(...); });" + '\n' +
            'The controls that DO take a label are button, checkbox, selectable and foldout.');
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
    for (let i = 0; i < list.length; i++) W.i32(W.str(text(list[i], 'the option list', 'options[' + i + ']')));
}

// ------------------------------------------------------------------------------ W10: per-function option sets
//
// One list per drawing function, and each is exactly what its DECODER reads - not what the option mask can
// carry. `segments` on a line, `dash` on a triangle and `style` on a circle are all encodable and all ignored
// by the replay, and an option that is silently ignored is the failure this whole surface is built to avoid. So
// they are refused by name, with that function's own list in the message.

/// ui.rect: the only shape with a semantic style, because NowRectangle is the only one with a SetStyle.
const RECT_OPTIONS = ['color', 'stroke', 'strokeColor', 'radius', 'blur', 'style', 'disabled'];

/// ui.image: a rect's geometry options, minus the ones that would fight the picture. No `style`, because a
/// theme style sets a colour and an image's colour is its pixels; no `blur`, because the blur is the shape's
/// edge falloff and it would fray the picture rather than soften it. `color` survives as a deliberate tint.
///
/// `fit` is permitted here but carries no option flag: ui.image reads it and sends it as a positional enum, the
/// same arrangement `kind` has on ui.gradient.
const IMAGE_OPTIONS = ['color', 'stroke', 'strokeColor', 'radius', 'fit'];

/// ui.rule: what a hairline can be told. No `grow` - see ui.rule for why it is refused rather than
/// ignored - and no `gap`, `align` or `justify`, which belong to a container and a rule is not one.
const RULE_OPTIONS = ['width', 'height', 'minWidth', 'maxWidth', 'padding', 'style', 'color', 'key'];

/// ui.lottie: a tint, and the playback position. Nothing else - a Lottie draws its own shapes, so a stroke, a
/// radius or a style would have nothing to apply to.
///
/// `time` is in this list but has no option flag, and that is not an inconsistency. The list is what an author
/// is ALLOWED to write - anything outside it is refused by name rather than silently ignored - while the flags
/// below are what gets encoded into the options block. ui.lottie reads `time` itself and sends it as a
/// positional argument, so it must be permitted here and must not be encoded there. Leaving it out is what made
/// the first version of this throw "time is not an option" on every call.
const LOTTIE_OPTIONS = ['color', 'time'];

/// ui.markdown: the layout options every flow child takes, plus its own font size. No `color` or `style` - a
/// document's colours come from the ambient theme, which is what makes ui.theme('dark') reach it.
const MARKDOWN_OPTIONS = LAYOUT_OPTIONS.concat(['fontSize']);

/// ui.circle: segments is its tessellation; fill: false makes a ring.
const CIRCLE_OPTIONS = ['color', 'stroke', 'strokeColor', 'fill', 'segments'];

/// ui.line and ui.bezier: a stroke has a width, a cap and a dash, and nothing else.
const LINE_OPTIONS = ['color', 'stroke', 'cap', 'dash'];

/// ui.triangle and ui.polygon.
const SHAPE_OPTIONS = ['color', 'stroke', 'strokeColor', 'fill'];

/// ui.gradient. `kind` is the op's own enum argument rather than an option bit, and rides here so that the
/// author writes one object; `radius`, `blur` and the stroke pair are NowGradient's own setters.
const GRADIENT_OPTIONS = ['kind', 'angle', 'spread', 'radius', 'blur', 'stroke', 'strokeColor'];

/// ui.canvas is a layout container and takes the layout half of section 2.7 and nothing else: it draws no
/// rectangle of its own, so a colour or a style on it would set something that is never read.
const CANVAS_OPTIONS = LAYOUT_OPTIONS;

/// ui.split, plus the axis its divider runs along.
const SPLIT_OPTIONS = LAYOUT_OPTIONS.concat(['axis']);

/// The five shapes ui.mask accepts, plus the two modifiers. Listed so a typo is named rather than ignored.
const MASK_SHAPE_KEYS = ['rect', 'ellipse', 'circle', 'capsule', 'radius', 'feather'];

/// Four corner radii into the geometry scratch, in HUMAN order - topLeft, topRight, bottomRight, bottomLeft.
/// Absent is four zeros, which is what a plain rectangular mask carries.
function pushRadii(value, fn) {
    if (value === undefined || value === null) {
        geomPush(0); geomPush(0); geomPush(0); geomPush(0);
        return;
    }

    if (typeof value === 'number') {
        const r = numArg(value, fn, 'radius');
        geomPush(r); geomPush(r); geomPush(r); geomPush(r);
        return;
    }

    if (Array.isArray(value) && value.length === 4) {
        for (let i = 0; i < 4; i++) geomPush(numArg(value[i], fn, 'radius[' + i + ']'));
        return;
    }

    throw new NowUIAuthorError(
        'NowUI: ' + fn + ' - radius is a number or [topLeft, topRight, bottomRight, bottomLeft], and this is ' +
        describe(value) + '.');
}

/// The radius a circular or capsule mask cannot do without. Absent is an error here rather than the documented
/// zero: a zero-radius circle is not a degenerate mask, it is an empty one, and it would clip everything away.
function requireRadius(value, fn, shape) {
    if (value === undefined || value === null)
        throw new NowUIAuthorError(
            'NowUI: ' + fn + " - a " + shape + ' mask needs a radius, and this one has none.\n' +
            'Without it the mask has zero extent and clips away everything inside it.');

    return numArg(value, fn, 'radius');
}

function maskShapeError(shape) {
    return new NowUIAuthorError(
        'NowUI: ui.mask(shape, body) - the shape is not one this build knows. It was ' + describe(shape) + '.\n' +
        'A shape is exactly one of:\n' +
        '  { rect: [x, y, w, h] }                        a rectangle\n' +
        '  { rect: [x, y, w, h], radius: r }             a rounded rectangle\n' +
        '  { ellipse: [x, y, w, h] }                     an ellipse inscribed in the box\n' +
        '  { circle: [cx, cy], radius: r }               a circle\n' +
        '  { capsule: [[x0,y0],[x1,y1]], radius: r }     a capsule between two points\n' +
        'plus an optional { feather: pixels } on any of them.');
}

/// One pane of a ui.split. It consumes an anonymous ordinal so the two panes' contents cannot share a path, and
/// carries only its index on the wire: the identity that matters is the split's own, which NowLayout.Area
/// derives per pane on the managed side.
function pane(index, body) {
    const node = trie.pushAnon();
    scope(OPS.PANE, node, () => W.i32(index), body);
}

/// THE SINGLE SOURCE OF TRUTH FOR WHAT IS ABSENT, exported so that the spec, the doc tooling and the runtime read
/// one array rather than three prose lists that drift.
///
/// The failure this fixes actually happened: M3-Spec.md section 2 tabled five functions with no absence marker
/// while nowui.js threw for all five, so an AI reading the spec wrote code that threw. Two of the five turned out
/// to be implementable and are now real; the three that remain are named here, once, and the spec's section 2.0
/// is generated from the same three names.
export const NOT_IMPLEMENTED = ['reset', 'overlay', 'contextMenu'];

function notInThisRelease(name, why) {
    // Asserted at MODULE LOAD, not at call time. A function marked absent here but missing from the array above -
    // or an array entry with no throwing function - is a bug in this file that must not survive its first import,
    // because the whole point of the array is that it cannot disagree with the runtime.
    if (NOT_IMPLEMENTED.indexOf(name) < 0)
        throw new Error(
            'nowui.js: ui.' + name + ' is declared not-in-this-release but is missing from NOT_IMPLEMENTED. ' +
            'Add it there, or implement it - the exported array is what the specification is generated from.');

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

    /// Section 2.1, in the only form a browser host can serve: 'light' or 'dark'. Everything drawn inside the
    /// body resolves its theme colours against that asset, which is what makes `color: 'accent'` mean two
    /// different pixels in two different scopes and the same pixel as every NowUI control beside it.
    ///
    /// NOT the general named-asset lookup section 2.1 describes. That needs a host resource manifest, which is
    /// the same missing piece that blocks textures. The blocker this function USED to name - "the host serves
    /// exactly one theme" - was simply false: NowTheme has always built both a light and a dark asset.
    theme(name, body) {
        const mode = enumValue(THEME_MODE, name, 'theme name');
        const node = trie.pushAnon();

        scope(OPS.THEME, node, () => {
            segArgs(node);
            W.i32(mode);
        }, body);
    },

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
        const resolved = R.value(node.rid, boolArg(open, 'ui.foldout', 'open'));

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

    /// Section 2.2's two-bodied scope: two resizable panes and a divider the user drags. Returns the new ratio,
    /// under section 6.4's reconciliation rule, so `state.ratio = ui.split('main', state.ratio, ...)` is the
    /// whole of holding a draggable divider's state.
    ///
    /// It needed no second kind of scope bracket after all. Brackets NEST, so a split is
    /// SPLIT{ PANE(0){ first() } PANE(1){ second() } } - three ops and a structural rule that already existed.
    /// Identity: one keyed node for the split and one anonymous ordinal per pane, both stable across frames.
    split(key, ratio, opts, first, second) {
        if (typeof opts === 'function') { second = first; first = opts; opts = undefined; }

        const axis = opts && opts.axis !== undefined
            ? enumValue(SPLIT_AXIS, opts.axis, 'split axis')
            : SPLIT_AXIS.horizontal;

        const payload = encodeOptions(opts, undefined, SPLIT_OPTIONS, 'ui.split');
        const node = trie.pushScope(key);
        const resolved = R.value(node.rid, numArg(ratio, 'ui.split', 'ratio'));

        emitOptions(payload);
        W.beginScope(OPS.SPLIT, node, () => {
            W.i32(node.rid);
            W.i32(node.seg);
            W.f32(resolved);
            W.i32(axis);
        });

        try {
            pane(0, first);
            pane(1, second);
        } finally {
            W.endScope();
        }

        return resolved;
    },

    overlay: notInThisRelease('overlay',
        'It is W11, and the sole consumer of the recorded subtrees of section 5.8 - OP_CALLBACK_BEGIN and ' +
        'OP_CALLBACK_END are reserved opcodes 2 and 3 and nothing emits them yet.'),

    // ------------------------------------------------------------------------------ 2.2b drawing scopes (W10)

    /// A CANVAS: one layout box, a coordinate origin, and a clip. This is where drawing starts, and the two
    /// paragraphs below are the whole of what an author has to know.
    ///
    /// THE COORDINATE MODEL. Every coordinate on every drawing function is CANVAS-LOCAL: relative to the
    /// top-left of the innermost enclosing canvas, so (0, 0) is the canvas's own corner and a drawing does not
    /// move when the canvas does. The DECODER adds the origin, not this side - which is why a drawing lands in
    /// the right place even on the frame a window is resized, when JavaScript's idea of the canvas SIZE is one
    /// frame old. Outside any canvas the same numbers are screen coordinates, the space `ui.frame.width` is in.
    ///
    /// THE BOX IS ONE FRAME OLD, UNLESS YOU DECLARED IT. `body(box)` and the return value are the same object:
    /// `{ width, height, stale }`. Each AXIS is taken from the option when that option was a number, and from
    /// last frame's measured rect otherwise - so `{ height: 240 }` with a stretched width gives an exact height
    /// and a measured width. `stale` is false only when BOTH were declared, and on the very first frame a
    /// measured axis is 0. That is section 6.2's R6, not a new exception, and it is exact for the common case:
    /// a chart with a declared size.
    ///
    /// There is no `box.x`: local space means the origin is always zero, and saying so removes a whole class of
    /// "why is it offset" bug.
    ///
    /// A canvas is a real column, so it may also hold CONTROLS: they flow normally while drawings paint at
    /// absolute coordinates over them. A chart with a legend row is one canvas.
    canvas(key, opts, body) {
        if (typeof opts === 'function') { body = opts; opts = undefined; }

        // A canvas has no children to size it, so a column with no height source collapses to zero and draws an
        // invisible nothing. That is the single most likely first-try failure of this whole surface, and it is
        // worth a named error rather than a blank screen - and rather than a default height the author did not
        // choose and cannot see.
        const sized = opts && (opts.height !== undefined || opts.minHeight !== undefined || opts.grow !== undefined);
        if (!sized)
            throw new NowUIAuthorError(
                "NowUI: ui.canvas('" + key + "', ...) needs a height, and this one has none.\n" +
                'A canvas has no children to size it - unlike a column, whose text and controls give it a ' +
                'height - so without one it collapses to zero and everything drawn inside it is clipped away ' +
                'invisibly.\n' +
                "Give it { height: 240 }, { minHeight: 120 } or { grow: 1 }.");

        const payload = encodeOptions(opts, undefined, CANVAS_OPTIONS, 'ui.canvas');
        const node = trie.pushScope(key);

        // Per AXIS, because a canvas is often `{ height: 240 }` with a stretched width: the declared half is
        // exact this frame and only the measured half is stale.
        const measured = R.rectOf(node.rid);
        const declaredWidth = opts && typeof opts.width === 'number';
        const declaredHeight = opts && typeof opts.height === 'number';

        const box = {
            width: declaredWidth ? opts.width : (measured ? measured[2] : 0),
            height: declaredHeight ? opts.height : (measured ? measured[3] : 0),
            stale: !(declaredWidth && declaredHeight),
        };

        emitOptions(payload);

        // The body ALWAYS runs, even at zero size, for section 3.5's reason: a scope that sometimes does not run
        // renumbers every anonymous sibling after it. A zero-extent shape simply draws nothing.
        scope(OPS.CANVAS, node, () => segArgs(node), () => { if (typeof body === 'function') body(box); });

        return box;
    },

    /// An analytic MASK over a subset of the enclosing canvas: a circular avatar clip, a capsule, a rounded
    /// panel. A canvas already clips to its own box, so this is for the shapes that box cannot express.
    ///
    /// `shape` is exactly one of, plus an optional `feather` in pixels:
    ///
    ///     { rect: [x, y, w, h] }                        a rectangle
    ///     { rect: [x, y, w, h], radius: r | [4] }       a rounded rectangle
    ///     { ellipse: [x, y, w, h] }                     an ellipse inscribed in the box
    ///     { circle: [cx, cy], radius: r }               a circle
    ///     { capsule: [[x0,y0],[x1,y1]], radius: r }     a capsule between two points
    ///
    /// ONE CONSEQUENCE WORTH KNOWING BEFORE YOU USE IT: a mask opens an identity scope, like every other scope
    /// here. Wrapping EXISTING controls in one changes their canonical path, so their keyed state - a scroll
    /// offset, a caret, a foldout - resets once. That is the same cost as wrapping them in a ui.column.
    mask(shape, body) {
        if (shape === null || typeof shape !== 'object' || Array.isArray(shape))
            throw maskShapeError(shape);

        for (const name in shape) {
            if (MASK_SHAPE_KEYS.indexOf(name) < 0) throw maskShapeError(shape);
        }

        // Validated and buffered BEFORE the trie is touched and before the op header is written, so a bad shape
        // throws with the stream untouched. geom holds nine floats: the four rect slots, the four vec4 slots,
        // and the feather.
        geomReset();

        let kind;

        if (shape.rect !== undefined) {
            kind = shape.radius === undefined ? MASK_KIND.rectangle : MASK_KIND.roundedRect;
            geomBox(shape.rect, 'ui.mask', 'rect');
            pushRadii(shape.radius, 'ui.mask');
        } else if (shape.ellipse !== undefined) {
            kind = MASK_KIND.ellipse;
            geomBox(shape.ellipse, 'ui.mask', 'ellipse');
            pushRadii(undefined, 'ui.mask');
        } else if (shape.circle !== undefined) {
            // The rect slots carry cx, cy, r for this kind: a circle has no width and height to send.
            kind = MASK_KIND.circle;
            geomPoint(shape.circle, 'ui.mask', 'circle');
            geomPush(requireRadius(shape.radius, 'ui.mask', 'circle'));
            geomPush(0);
            pushRadii(undefined, 'ui.mask');
        } else if (shape.capsule !== undefined) {
            kind = MASK_KIND.capsule;
            const ends = shape.capsule;
            if (!Array.isArray(ends) || ends.length !== 2) throw maskShapeError(shape);

            geomPoint(ends[0], 'ui.mask', 'capsule[0]');
            geomPoint(ends[1], 'ui.mask', 'capsule[1]');

            // The radius rides in the vec4's first slot for this kind; the rect slots are both endpoints.
            geomPush(requireRadius(shape.radius, 'ui.mask', 'capsule'));
            geomPush(0); geomPush(0); geomPush(0);
        } else {
            throw maskShapeError(shape);
        }

        geomPush(numArg(shape.feather, 'ui.mask', 'feather'));

        const node = trie.pushAnon();
        scope(OPS.MASK, node, () => {
            segArgs(node);
            W.i32(kind);
            geomFlush();
        }, body);
    },

    // ----------------------------------------------------------------------------------------- 2.3 drawings

    text(content, opts) {
        // Resolved BEFORE the op header: W.op promises n argument slots, so a throw between it and
        // the nth slot is a corrupt frame rather than a caught author error.
        const resolved = text(content, 'ui.text', 'content');
        drawingOptions(opts);
        W.op(OPS.TEXT);
        W.i32(W.str(resolved));
    },

    heading(content, opts) {
        // Resolved BEFORE the op header: W.op promises n argument slots, so a throw between it and
        // the nth slot is a corrupt frame rather than a caught author error.
        const resolved = text(content, 'ui.heading', 'content');
        drawingOptions(opts, 'heading');
        W.op(OPS.TEXT);
        W.i32(W.str(resolved));
    },

    subheading(content, opts) {
        // Resolved BEFORE the op header: W.op promises n argument slots, so a throw between it and
        // the nth slot is a corrupt frame rather than a caught author error.
        const resolved = text(content, 'ui.subheading', 'content');
        drawingOptions(opts, 'subheading');
        W.op(OPS.TEXT);
        W.i32(W.str(resolved));
    },

    caption(content, opts) {
        // Resolved BEFORE the op header: W.op promises n argument slots, so a throw between it and
        // the nth slot is a corrupt frame rather than a caught author error.
        const resolved = text(content, 'ui.caption', 'content');
        drawingOptions(opts, 'caption');
        W.op(OPS.TEXT);
        W.i32(W.str(resolved));
    },

    space(pixels) {
        const resolved = numArg(pixels, 'ui.space', 'pixels');
        W.op(OPS.SPACE);
        W.f32(resolved);
    },

    flexSpace(weight) {
        const resolved = weight === undefined || weight === null ? 1 : numArg(weight, 'ui.flexSpace', 'weight');
        W.op(OPS.FLEX_SPACE);
        W.f32(resolved);
    },

    /// A hairline. Inside a row it fills whatever width is left; inside a column it spans the full width.
    ///
    /// `grow` is REFUSED rather than accepted, because a rule already stretches and the option has nowhere to
    /// go: it collides with the rule's own sizing, and NowLayout answers a growing element that also has a
    /// fixed main-axis size by THROWING - out of the decode, taking every control after the rule with it. An
    /// option that silently blanks the rest of the page is worse than one that says no.
    rule(opts) {
        options(opts, undefined, RULE_OPTIONS, 'ui.rule');
        W.op(OPS.RULE);
    },

    badge(content, opts) {
        // A badge DOES have a rectangle (NowBadge.SetStyle, Controls/NowBadge.cs:50), so `style` keeps section
        // 2.7's meaning here and `textStyle` is separate. It is the one drawing that is not a bare label.
        const resolved = text(content, 'ui.badge', 'content');
        options(opts);
        W.op(OPS.BADGE);
        W.i32(W.str(resolved));
    },

    // ------------------------------------------------------------------------------------ 2.3b shapes (W10)
    //
    // Eight functions, no keys, no returns. A drawing has no state and no interaction, so it has no identity
    // either - which is the same reasoning section 2.3 already applies to ui.text.
    //
    // Every coordinate is CANVAS-LOCAL. See ui.canvas; it is stated once there and nowhere else, on purpose.
    //
    // COLOUR, ONCE, FOR ALL OF THEM: `{ color: 'accent' }` is a theme token and follows light and dark;
    // `{ color: '#3B82F6' }` and `{ color: [0.2, 0.5, 1] }` are literals. With no colour at all a shape is drawn
    // in the theme's text colour rather than in white, because white on the light theme's white ground is the
    // invisible-and-correct failure this library has already hit once.

    /// A rectangle. The one shape with a semantic `style`, because NowRectangle is the one NowUI shape with a
    /// SetStyle - and it layers correctly: the style applies first and an explicit colour, radius, stroke or
    /// blur overrides it, so `{ style: 'accent', radius: 0 }` means what it reads as.
    rect(box, opts) {
        geomReset();
        geomBox(box, 'ui.rect', 'box');

        emitOptions(encodeOptions(opts, undefined, RECT_OPTIONS, 'ui.rect'));
        W.op(OPS.RECT);
        geomFlush();
    },

    /// A picture from a URL, drawn into `box` exactly as ui.rect draws a colour into it.
    ///
    /// THE FIRST FRAME DOES NOT HAVE THE IMAGE, and the surface does not pretend otherwise. Naming a URL starts
    /// the download; until it lands the box draws as a muted placeholder, and the frame after it arrives draws
    /// the picture. Nothing blocks and nothing is awaited - the page is already running frames continuously, so
    /// the image simply appears. A URL that fails keeps drawing the placeholder, so a layout never collapses
    /// around a hole, and says why on the console once rather than once per frame.
    ///
    /// `box` is [x, y, width, height], canvas-local inside ui.canvas and screen-space outside it - the same rule
    /// ui.rect follows.
    ///
    /// `fit` decides how the picture meets the box, and DEFAULTS TO 'contain' so a photograph is never silently
    /// squashed:
    ///
    ///   'contain'  the whole picture, centred, letterboxed in the box. The drawn shape shrinks to the picture,
    ///              so `radius` and `stroke` follow the PICTURE's edges and the leftover space is empty.
    ///   'cover'    the box is filled and the picture is cropped, centred, on whichever axis has spare. The
    ///              shape stays the full box, so `radius` and `stroke` follow the BOX.
    ///   'stretch'  the picture is distorted to the box. Nothing is cropped and nothing is left empty.
    ///
    /// Which one you want is usually decided by the corners: a rounded avatar wants 'cover', an illustration
    /// with its own margins wants 'contain'.
    image(box, url, opts) {
        geomReset();
        geomBox(box, 'ui.image', 'box');

        // THE DEFAULT IS `contain`, not `stretch`, and that is a deliberate disagreement with the C# default.
        // NowRectangle.preserveAspect is false because a NowRectangle is usually a SHAPE that happens to carry a
        // texture - a nine-slice, a ramp, an atlas cell - where stretching is the point. ui.image is the other
        // thing: an author who writes a URL means "show me this picture", and the failure mode of stretching is a
        // silently squashed photograph that looks like a rendering bug rather than a choice. `fit: 'stretch'`
        // is one word away when the distortion IS the intent.
        const fit = opts && opts.fit !== undefined
            ? enumValue(IMAGE_FIT, opts.fit, 'image fit')
            : IMAGE_FIT.contain;

        emitOptions(encodeOptions(opts, undefined, IMAGE_OPTIONS, 'ui.image'));
        W.op(OPS.IMAGE);
        geomFlush();
        W.i32(W.str(absoluteUrl(url, 'ui.image')));
        W.i32(fit);
    },

    /// A Lottie animation from a URL, played into `box`.
    ///
    /// It plays by itself: with no `time` option the animation runs from the page's own clock, which is what you
    /// want for a spinner, a tick or a looping flourish. Pass `time` in SECONDS to drive it yourself - from a
    /// scrubber, from a paused value, or from the same clock as something else you are animating - and the same
    /// number always draws the same frame, which is what makes a Lottie testable and capturable.
    ///
    /// Until the file arrives, NOTHING is drawn - not a placeholder box. A Lottie is usually an accent over a
    /// layout rather than content inside one, and a grey rectangle flashing before a tick animation looks like a
    /// bug. If you want the space reserved, draw your own ui.rect behind it.
    lottie(box, url, opts) {
        geomReset();
        geomBox(box, 'ui.lottie', 'box');

        // frameInfo.time, not a fresh clock read: it is sampled ONCE at the top of the frame, so two Lotties
        // drawn in the same frame are at the same instant. Reading the clock per call would drift them apart by
        // whatever the recording took, which is small, real, and impossible to debug from the outside.
        const time = opts && opts.time !== undefined
            ? numArg(opts.time, 'ui.lottie', 'time')
            : frameInfo.time;

        emitOptions(encodeOptions(opts, undefined, LOTTIE_OPTIONS, 'ui.lottie'));
        W.op(OPS.LOTTIE);
        geomFlush();
        W.i32(W.str(absoluteUrl(url, 'ui.lottie')));
        W.f32(time);
    },

    /// A circle, or an ellipse: `radius` is one number broadcast to both axes, or `[rx, ry]`. The radius is a
    /// SIZE and is not translated by the canvas origin - only the centre moves.
    circle(center, radius, opts) {
        geomReset();
        geomPoint(center, 'ui.circle', 'center');

        if (Array.isArray(radius)) {
            if (radius.length !== 2)
                throw new NowUIAuthorError(
                    'NowUI: ui.circle - radius is a number or [rx, ry], and this is ' + describe(radius) + '.');
            geomPush(numArg(radius[0], 'ui.circle', 'radius[0]'));
            geomPush(numArg(radius[1], 'ui.circle', 'radius[1]'));
        } else {
            const r = numArg(radius, 'ui.circle', 'radius');
            geomPush(r);
            geomPush(r);
        }

        emitOptions(encodeOptions(opts, undefined, CIRCLE_OPTIONS, 'ui.circle'));
        W.op(OPS.CIRCLE);
        geomFlush();
    },

    /// A straight line. `stroke` is its width, `cap` its end shape, `dash` its pattern.
    line(from, to, opts) {
        geomReset();
        geomPoint(from, 'ui.line', 'from');
        geomPoint(to, 'ui.line', 'to');

        emitOptions(encodeOptions(opts, undefined, LINE_OPTIONS, 'ui.line'));
        W.op(OPS.LINE);
        geomFlush();
    },

    /// A cubic bezier: two endpoints and the two control points between them. The same NowLine as ui.line, so
    /// the same width, cap and dash options apply.
    bezier(from, control1, control2, to, opts) {
        geomReset();
        geomPoint(from, 'ui.bezier', 'from');
        geomPoint(control1, 'ui.bezier', 'control1');
        geomPoint(control2, 'ui.bezier', 'control2');
        geomPoint(to, 'ui.bezier', 'to');

        emitOptions(encodeOptions(opts, undefined, LINE_OPTIONS, 'ui.bezier'));
        W.op(OPS.BEZIER);
        geomFlush();
    },

    triangle(a, b, c, opts) {
        geomReset();
        geomPoint(a, 'ui.triangle', 'a');
        geomPoint(b, 'ui.triangle', 'b');
        geomPoint(c, 'ui.triangle', 'c');

        emitOptions(encodeOptions(opts, undefined, SHAPE_OPTIONS, 'ui.triangle'));
        W.op(OPS.TRIANGLE);
        geomFlush();
    },

    /// A closed polygon. `points` is [[x, y], ...] OR a flat [x, y, x, y, ...], discriminated by the first
    /// element - both, because the nested form is what a person writes and the flat form is what a 500-point
    /// chart should send when allocating 500 arrays every frame is not acceptable.
    ///
    /// Fewer than three points draws nothing, deliberately and silently: a chart whose data has not loaded yet
    /// legitimately has none.
    polygon(points, opts) {
        if (!Array.isArray(points))
            throw new NowUIAuthorError(
                'NowUI: ui.polygon - points is [[x, y], ...] or a flat [x, y, x, y, ...], and this is ' +
                describe(points) + '.');

        geomReset();

        if (points.length > 0 && Array.isArray(points[0])) {
            for (let i = 0; i < points.length; i++) geomPoint(points[i], 'ui.polygon', 'points[' + i + ']');
        } else {
            if ((points.length & 1) !== 0)
                throw new NowUIAuthorError(
                    'NowUI: ui.polygon - a flat point list has an even length, x, y, x, y, and this one has ' +
                    points.length + ' entries.');

            for (let i = 0; i < points.length; i++) geomPush(numArg(points[i], 'ui.polygon', 'points[' + i + ']'));
        }

        emitOptions(encodeOptions(opts, undefined, SHAPE_OPTIONS, 'ui.polygon'));
        W.op(OPS.POLYGON, 1 + geomCount);
        W.i32(geomCount / 2);
        geomFlush();
    },

    /// A gradient-filled box. The two ramp ends are ARGUMENTS rather than options, because a gradient with one
    /// colour is not a gradient - they are required, not decoration. Both take any colour form ui.rect's
    /// `color` takes, tokens included.
    ///
    /// `kind` is 'linear' (the default), 'radial' or 'conic'. `angle` is in CSS's convention - 0 points up, 90
    /// right, clockwise - so a value copied out of a CSS gradient needs no conversion.
    gradient(box, from, to, opts) {
        geomReset();
        geomBox(box, 'ui.gradient', 'box');

        // PAINT is a reused scratch pair, so the first one is copied out before the second is parsed.
        const a = parsePaint(from, 'ui.gradient', 'from');
        const fromTag = a[0];
        const fromValue = a[1];

        const b = parsePaint(to, 'ui.gradient', 'to');
        const toTag = b[0];
        const toValue = b[1];

        const kind = opts && opts.kind !== undefined
            ? enumValue(GRADIENT_KIND, opts.kind, 'gradient kind')
            : GRADIENT_KIND.linear;

        emitOptions(encodeOptions(opts, undefined, GRADIENT_OPTIONS, 'ui.gradient'));
        W.op(OPS.GRADIENT);
        geomFlush();
        W.i32(fromTag);
        W.i32(fromValue);
        W.i32(toTag);
        W.i32(toValue);
        W.i32(kind);
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

        const resolved = boolArg(selected, 'ui.selectable', 'selected');

        emitOptions(payload);
        W.op(OPS.SELECTABLE);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved ? 1 : 0);
        W.i32(W.str(labelOf(opts, key)));

        return R.event(node.rid, F_CLICKED);
    },

    /// `removed` is delivered through opts.onRemove and never as a returned object (section 2.4): a bag of flags
    /// would reintroduce the `if (obj)` trap that R5 exists to remove.
    chip(key, label, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const removable = !!(opts && typeof opts.onRemove === 'function');

        // Both arguments resolved before the header, for the reason ui.text records.
        const resolved = text(label, 'ui.chip', 'label');
        const selected = opts !== undefined && opts !== null && boolArg(opts.selected, 'ui.chip', 'selected');

        emitOptions(payload);
        W.op(OPS.CHIP);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.str(resolved));
        W.i32(selected ? 1 : 0);
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
        const resolved = R.value(node.rid, text(value, 'ui.textField', 'value'));

        emitOptions(payload);
        W.op(OPS.TEXT_FIELD);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.volatileStr(resolved));
        W.i32(W.str(opts && opts.placeholder !== undefined ? String(opts.placeholder) : ''));

        rememberHandlers(node.rid, opts);
        return resolved;
    },

    /// A rendered Markdown document, laid out where it sits.
    ///
    /// Put it inside a ui.scroll and the scroll does the rest - the document measures itself in the flow, so
    /// nothing has to know its height in advance. `fontSize` scales the whole document; omit it for the
    /// style's own size.
    ///
    /// Returns the LINK that was clicked this frame, or null. Nothing is opened for you: a link is reported and
    /// the application decides, which is what lets "[Layout](Layout.md)" navigate inside a viewer rather than
    /// leaving the page.
    ///
    /// A document that stays the same is sent once, not once a frame: a `str` position is volatile on its
    /// first sighting and interned on its second, and the handle then lasts the session. A document that
    /// changes every frame never reaches that second sighting, so it is re-encoded each frame and leaks
    /// nothing. Both cases are handled without the author choosing.
    markdown(key, source, opts) {
        const payload = encodeOptions(opts, undefined, MARKDOWN_OPTIONS, 'ui.markdown');
        const node = trie.control(key);
        const clicked = R.event(node.rid, F_CLICKED);
        const link = R.value(node.rid, '');

        emitOptions(payload);
        W.op(OPS.MARKDOWN);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.str(text(source, 'ui.markdown', 'source')));
        W.f32(opts && opts.fontSize !== undefined ? numArg(opts.fontSize, 'ui.markdown', 'fontSize') : 0);

        return clicked && link ? link : null;
    },

    textArea(key, value, opts) {
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, text(value, 'ui.textArea', 'value'));

        emitOptions(payload);
        W.op(OPS.TEXT_AREA);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(W.volatileStr(resolved));
        W.i32(W.str(opts && opts.placeholder !== undefined ? String(opts.placeholder) : ''));

        return resolved;
    },

    numberField(key, value, opts) {
        refuseLabel(opts, 'ui.numberField');
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        let resolved = R.value(node.rid, numArg(value, 'ui.numberField', 'value'));

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
        const resolved = R.value(node.rid, boolArg(value, 'ui.checkbox', 'value'));

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
        const resolved = R.value(node.rid, boolArg(value, 'ui.switch', 'value'));

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
        refuseLabel(opts, 'ui.slider');
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, numArg(value, 'ui.slider', 'value'));

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
        refuseLabel(opts, 'ui.intSlider');
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(numArg(value, 'ui.intSlider', 'value')));

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
        refuseLabel(opts, 'ui.dropdown');
        return emitChoice(OPS.DROPDOWN, key, value, list, opts, 'ui.dropdown(key, value, options, opts?)');
    },

    combo(key, value, list, opts) {
        refuseLabel(opts, 'ui.combo');
        return emitChoice(OPS.COMBO, key, value, list, opts, 'ui.combo(key, value, options, opts?)');
    },

    /// Section 2.5. The one tier-1 function that returns a non-primitive, and it is a VALUE rather than an event:
    /// `if (ui.colorField(...))` is as meaningless as `if (someArray)` anywhere else in JavaScript.
    colorField(key, value, opts) {
        refuseLabel(opts, 'ui.colorField');
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
        refuseLabel(opts, 'ui.datePicker');
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, numArg(value, 'ui.datePicker', 'value'));

        emitOptions(payload);
        W.op(OPS.DATE_PICKER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved | 0);                              // low word
        W.i32(Math.floor(resolved / 4294967296) | 0);     // high word

        return resolved;
    },

    timePicker(key, value, opts) {
        refuseLabel(opts, 'ui.timePicker');
        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(numArg(value, 'ui.timePicker', 'value')));

        emitOptions(payload);
        W.op(OPS.TIME_PICKER);
        W.i32(node.rid);
        W.i32(node.seg);
        W.i32(resolved);

        return resolved;
    },

    tabs(key, selected, labels, opts) {
        refuseLabel(opts, 'ui.tabs');
        requireList(labels, 'ui.tabs(key, selected, labels, opts?)');

        const payload = encodeOptions(opts);
        const node = trie.control(key);
        const resolved = R.value(node.rid, Math.round(numArg(selected, 'ui.tabs', 'selected')));

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
        refuseLabel(opts, 'ui.progress');
        const resolved = numArg(value01, 'ui.progress', 'value01');
        options(opts);
        W.op(OPS.PROGRESS);
        W.f32(resolved);
    },

    /// Absent - and the reason is recorded as UNVERIFIED rather than as settled, because it probably is not
    /// true. The stated blocker is that a NowResolvedId cannot be obtained on the bridge side; but
    /// Replay.Identity.cs's DuplicateIdBackstop already maps a NowResolvedId back to a rid, so resolved ids are
    /// demonstrably obtainable there. Nobody has read NowContextMenu.cs since, so nothing here is designed over
    /// it - but the next person to look should expect to find this implementable, not to find a wall.
    contextMenu: notInThisRelease('contextMenu',
        'It is said to need a NowResolvedId rather than a NowId - NowContextMenu.Begin takes one and Begin(int) ' +
        'is [Obsolete(error)] - through a begin/item/end triple the command stream does not carry yet (section ' +
        '2.6). THAT BLOCKER IS UNVERIFIED: Replay.Identity.cs already maps a NowResolvedId back to a rid, so it ' +
        'is worth re-examining rather than treating as settled.'),
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

    // A value that is not in the list used to send index 0 and hand the author their own value straight back, so
    // the control displayed the WRONG option forever while the author's state looked like it round-tripped. That
    // is the silent-wrong-value failure the rest of this surface refuses, and it is easiest to hit by passing an
    // INDEX, because ui.tabs beside it does take one. ui.combo is the exception by design: its free-text commit
    // means a value outside the list is a legitimate state, so only a non-string is an error there.
    if (found < 0 && value !== undefined && value !== null) {
        if (record !== OPS.COMBO) {
            throw new NowUIAuthorError(
                'NowUI: ' + signature + ' was given ' + JSON.stringify(value) + ', which is not one of its ' +
                list.length + ' options. This takes the selected OPTION, not its index - ui.tabs is the one that ' +
                'takes an index. Options are ' + JSON.stringify(list.slice(0, 4)) +
                (list.length > 4 ? ' and ' + (list.length - 4) + ' more.' : '.'));
        }

        if (typeof value !== 'string') {
            throw new NowUIAuthorError(
                'NowUI: ' + signature + ' takes a string, and this is ' + typeof value + '. A combo box accepts a ' +
                'value outside its option list, because the author can type one - but it must still be text.');
        }
    }

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
///
/// EXPORTED since W10 so that a test can bind the real surface to a bare Recorder and read the slots it emits,
/// without a browser and without the wasm. Not part of the authoring surface; an author calls `start`.
export function install(recorder) {
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
        // Still throws, and the specification says so beside ui.reset rather than only here: a handle member
        // that throws is a second liar in the same place, and NOT_IMPLEMENTED names 'reset' for both of them.
        reset() { ui.reset(); },
    };
}

export default ui;
