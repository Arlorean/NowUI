// The browser half of the input bridge: DOM events in, a packed event queue out, drained once per NowUI frame.
//
// Nothing here calls into .NET. Handlers only append to `queue` (and to a few pieces of level state); `drain()` is
// called from the managed frame loop, after NowRuntime.BeginFrame and before Now.StartUI. That ordering is the whole
// reason this file exists rather than a set of direct [JSExport] callbacks: NowUIToolkitInputProvider folds every
// buffered event into ONE snapshot at the first read of a frame and clears the one-shot state immediately
// (Docs/Standalone/M2-InputContract.md section 1.1), so an immediate-mode pass must see input that cannot change
// underneath it. Feeding straight from handlers happens to be safe too - JS is single threaded and a handler cannot
// interleave with requestAnimationFrame - but "safe by accident of the event loop" is not the same as "safe", and a
// queue makes the frame boundary explicit and inspectable.
//
// Three DOM-versus-NowUI mismatches are resolved here rather than in C#, because they are properties of the DOM:
//   1. PointerEvent.button 1 and 2 are swapped relative to NowUI's index (buttons, the bitmask, is identical).
//   2. WheelEvent.deltaMode has three units and no guaranteed magnitude; the provider wants UI Toolkit units.
//   3. KeyboardEvent.key is text and KeyboardEvent.code is a physical key; NowUI needs both, for different things.
//
// Design: Docs/Standalone/M2-InputContract.md. Every section reference below points at the paragraph that decides
// the behaviour, because most of these choices look arbitrary until you have read the reason.

// ------------------------------------------------------------------------------------- packed record format
//
// One record is RECORD_STRIDE doubles: [kind, a, b, c, d]. A flat array of numbers, because that is the one shape
// that crosses the managed boundary as a plain `double[]` with no serializer - and the browser host publishes
// trimmed, which is exactly what a reflection-based serializer breaks.

const RECORD_STRIDE = 5;

const KIND_POINTER_MOVE = 1;   // a=x  b=y  c=buttons
const KIND_POINTER_DOWN = 2;   // a=x  b=y  c=nowButtonIndex  d=buttons
const KIND_POINTER_UP = 3;     // a=x  b=y  c=nowButtonIndex  d=buttons
const KIND_POINTER_CANCEL = 4;
const KIND_POINTER_ENTER = 5;  // a=x  b=y
const KIND_POINTER_LEAVE = 6;  // a=x  b=y  c=buttons
const KIND_SCROLL = 7;         // a=dx b=dy   (UI Toolkit units: 3 per notch, y down-positive)
const KIND_KEY_DOWN = 8;       // a=keyCode  b=shift
const KIND_KEY_UP = 9;         // a=keyCode
const KIND_TEXT_STATE = 10;    // a=heldBits b=pressedBits c=modifierBits   (emitted last, once per drain)

// Text-frame held flags (level: true while the key is physically down).
const HELD_BACKSPACE = 1 << 0;
const HELD_DELETE = 1 << 1;
const HELD_LEFT = 1 << 2;
const HELD_RIGHT = 1 << 3;
const HELD_UP = 1 << 4;
const HELD_DOWN = 1 << 5;
const HELD_ENTER = 1 << 6;
const HELD_TAB = 1 << 7;

// Text-frame pressed flags (edge: true for exactly one frame per physical press).
const PRESSED_HOME = 1 << 0;
const PRESSED_END = 1 << 1;
const PRESSED_ENTER = 1 << 2;
const PRESSED_ESCAPE = 1 << 3;
const PRESSED_TAB = 1 << 4;
const PRESSED_RENAME = 1 << 5;
const PRESSED_COPY = 1 << 6;
const PRESSED_PASTE = 1 << 7;
const PRESSED_CUT = 1 << 8;
const PRESSED_SELECT_ALL = 1 << 9;
const PRESSED_UNDO = 1 << 10;
const PRESSED_REDO = 1 << 11;
const PRESSED_DUPLICATE = 1 << 12;
const PRESSED_COMMENT = 1 << 13;
const PRESSED_GO_TO_LINE = 1 << 14;

const MOD_SHIFT = 1 << 0;
const MOD_COMMAND = 1 << 1;
const MOD_OPTION = 1 << 2;

// ------------------------------------------------------------------------------------- DOM -> NowUI mappings

// DOM PointerEvent.button index -> NowUI's index. DOM is 0 left, 1 middle, 2 right; NowUIToolkitInputProvider's
// TryGetButton is 0 Primary, 1 Secondary, 2 Middle - UI Toolkit's ordering, which is what the provider was written
// against (M2-InputContract.md section 2.3). Only 1 and 2 move. PointerEvent.buttons, the bitmask, matches bit for
// bit and is passed through untouched.
const NOWUI_BUTTON = [0, 2, 1, 3, 4];

// The physical keys NowUIToolkitInputProvider.KeyDown actually consumes, keyed by KeyboardEvent.code so a French
// AZERTY keyboard navigates with the key in the arrow position and a dead key produces no phantom arrow. Values are
// Standalone/NowUI.Engine/Enums/KeyCode.cs.
//
// WASD is deliberately ABSENT, and not only because the host masks NowNavigationKeys.Wasd off. The provider shares
// one _leftDown/_rightDown/_upDown/_downDown flag between each arrow and its WASD twin, and KeyUp is NOT gated by
// navigationKeys the way KeyDown is - so forwarding a KeyUp(A) while ArrowLeft is physically held would clear the
// arrow's own held flag and stop navigation mid-press. Sending the letters would be a bug even with the mask on.
const NAVIGATION_KEYCODE = {
    ArrowUp: 273,
    ArrowDown: 274,
    ArrowRight: 275,
    ArrowLeft: 276,
    Enter: 13,
    NumpadEnter: 271,
    Space: 32,
    Escape: 27,
    Tab: 9,
};

// Keys whose browser default would fight the UI when the canvas has focus: Space and arrows scroll, Tab walks focus
// out of the canvas, Backspace was historically "go back", Home/End scroll the document. Preventing these is exactly
// the set NowUIToolkitInputProvider.KeyDown returns true for, plus the editing keys the text seam consumes.
const PREVENT_PLAIN = new Set([
    'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
    'Tab', 'Space', 'Enter', 'NumpadEnter', 'Escape',
    'Backspace', 'Delete', 'Home', 'End',
]);

// Command-chord keys NowTextInputFrame carries an edge for, so the browser does not also run its own version.
// KeyV is deliberately NOT here: preventing Ctrl+V would suppress the DOM `paste` event, which is the only
// synchronous source of clipboard text a page gets without a permission prompt (section 6.3). Suppressing it would
// trade a working paste for a tidier-looking rule.
const PREVENT_CHORD = new Set([
    'KeyC', 'KeyX', 'KeyA', 'KeyZ', 'KeyY', 'KeyD', 'KeyG', 'Slash',
]);

// Wheel normalisation constants (section 3.2). Chrome/Edge/Safari report pixels at roughly 100 per detent; Firefox
// reports deltaMode 1 with three lines per detent. DOM_DELTA_PAGE is not emitted by any browser in a normal
// configuration, so the page mapping below is a placeholder rather than a calibrated number, and is labelled as one.
const PX_PER_NOTCH = 100;
const LINES_PER_NOTCH = 3;

// AddScrollDelta's argument is in UI Toolkit units - three per notch, y still down-positive - and it does the
// division by three and the y negation itself. The host converts to notches and multiplies back up rather than
// pre-dividing, so the one conversion that matters stays in the provider where the contract puts it.
const UITK_UNITS_PER_NOTCH = 3;

// ------------------------------------------------------------------------------------- module state

let canvas = null;
let uiScale = 1;
let isMac = false;

// Growable, reused across frames: an idle frame allocates nothing and a busy one reuses last frame's capacity.
let queue = [];
let queueLength = 0;

// Exactly one pointer reaches NowUI, because the provider holds exactly one position and one button mask
// (section 2.5). The first pointer to go down owns the surface until it comes up; every other pointerId is dropped
// rather than averaged, so a second finger cannot teleport the cursor.
let activePointerId = null;

// Keys physically down, by KeyboardEvent.code. NowTextInputFrame's *Held fields are level, and NowUI generates its
// own key repeat from them (NowControlState.Repeat: one pulse immediately, then 20 Hz after 0.4 s). Reporting an
// edge here instead gives one character per press and no repeat; forwarding the browser's own auto-repeat on top
// gives roughly double the intended rate. Both failure modes are in the test plan, section 7.6.
const heldCodes = new Set();

let modifierShift = false;
let modifierCommand = false;
let modifierOption = false;

let pendingCharacters = '';
let pendingPressed = 0;

// The clipboard the page can answer synchronously. INowClipboard.GetText() returns a string with no way to say
// "not yet", and navigator.clipboard.readText() is a Promise behind a permission gate, so the contract cannot be
// satisfied faithfully (section 6.2). This is the honest approximation: whatever the last paste gesture carried,
// plus whatever this page itself last copied.
let clipboardCache = '';

// getBoundingClientRect is a layout read. The canvas is the only element on the page and nothing mutates the DOM,
// so it is cheap - but it is read once per frame rather than once per pointermove, and invalidated wherever the box
// can actually move.
let cachedRect = null;

// ------------------------------------------------------------------------------------- helpers

function push(kind, a, b, c, d) {
    queue[queueLength] = kind;
    queue[queueLength + 1] = a || 0;
    queue[queueLength + 2] = b || 0;
    queue[queueLength + 3] = c || 0;
    queue[queueLength + 4] = d || 0;
    queueLength += RECORD_STRIDE;
}

function rect() {
    if (cachedRect === null) cachedRect = canvas.getBoundingClientRect();
    return cachedRect;
}

// CSS pixels -> the space Now.StartUI lays out in.
//
// The general form, not the shortcut. Now.StartUI builds its surface as drawingBufferPixels / uiScale, and the host
// passes devicePixelRatio as uiScale, so with a canvas whose backing store is exactly cssBox * dpr this reduces to
// "the CSS-pixel offset inside the canvas" and one NowUI unit is one CSS pixel. That reduction is a consequence of
// main.js' own choice, not a law: a CSS-scaled canvas or a ?dpr= override that disagrees with the display breaks it,
// and the two extra divisions are what make ?dpr=2 land on the same controls as ?dpr=1 (section 2.2).
//
// offsetX/offsetY are not used: they are relative to the padding box of whatever node is under the pointer, which is
// the canvas today and silently is not the first time an overlay div appears.
function toSurfaceX(clientX) {
    const r = rect();
    return (clientX - r.left) * (canvas.width / Math.max(1, r.width)) / uiScale;
}

function toSurfaceY(clientY) {
    const r = rect();
    // No vertical flip. Unity's raw mouse API is bottom-left, but the flip happens one layer below the provider, in
    // NowScreenInputProvider; the provider contract itself is top-left in UI units and hit-tests the snapshot
    // position directly against the rect the drawing code wrote (section 2.1). DOM coordinates already point that
    // way, so the correct amount of work here is none.
    return (clientY - r.top) * (canvas.height / Math.max(1, r.height)) / uiScale;
}

// True while this pointerId is the one NowUI is listening to. Before anything is pressed there is no owner and any
// pointer may hover, which is what makes a plain mouse work.
function ownsPointer(e) {
    return activePointerId === null || activePointerId === e.pointerId;
}

function commandKey(e) {
    return isMac ? e.metaKey : e.ctrlKey;
}

function syncModifiers(e) {
    modifierShift = e.shiftKey;
    modifierCommand = commandKey(e);
    modifierOption = e.altKey;
}

function heldBits() {
    let bits = 0;
    if (heldCodes.has('Backspace')) bits |= HELD_BACKSPACE;
    if (heldCodes.has('Delete')) bits |= HELD_DELETE;
    if (heldCodes.has('ArrowLeft')) bits |= HELD_LEFT;
    if (heldCodes.has('ArrowRight')) bits |= HELD_RIGHT;
    if (heldCodes.has('ArrowUp')) bits |= HELD_UP;
    if (heldCodes.has('ArrowDown')) bits |= HELD_DOWN;
    if (heldCodes.has('Enter') || heldCodes.has('NumpadEnter')) bits |= HELD_ENTER;
    if (heldCodes.has('Tab')) bits |= HELD_TAB;
    return bits;
}

function modifierBits() {
    let bits = 0;
    if (modifierShift) bits |= MOD_SHIFT;
    if (modifierCommand) bits |= MOD_COMMAND;
    if (modifierOption) bits |= MOD_OPTION;
    return bits;
}

// ------------------------------------------------------------------------------------- clipboard

// The paste-event cache. This fires as part of the browser's own Ctrl+V / Cmd+V handling, inside a user gesture,
// with the text already available synchronously - no permission prompt, no Promise - and it fires before the next
// requestAnimationFrame, so the string is warm when the following frame consumes frame.pastePressed (section 6.3).
function onPaste(e) {
    const text = e.clipboardData ? e.clipboardData.getData('text/plain') : null;
    if (typeof text === 'string') clipboardCache = text;
    e.preventDefault();
}

// The opportunistic half, for pastes driven from a NowUI context menu - a click, which fires no DOM `paste` event
// at all. Deliberately silent and deliberately NOT a prompt: it reads only when the permission is ALREADY granted,
// so a user who has never granted it sees no popup appearing out of a render loop. Firefox and Safari do not
// implement the clipboard-read permission query, so on those browsers this never runs and the paste-event cache is
// the only path - which is the correct trade, because an unexplained browser popup in a UI that draws its own
// everything is a worse failure than a stale string.
function refreshClipboardIfPermitted() {
    try {
        if (!navigator.clipboard || !navigator.clipboard.readText) return;
        if (!navigator.permissions || !navigator.permissions.query) return;

        navigator.permissions.query({ name: 'clipboard-read' }).then(status => {
            if (status.state !== 'granted') return;
            navigator.clipboard.readText().then(text => {
                if (typeof text === 'string') clipboardCache = text;
            }, () => { });
        }, () => { });
    } catch {
        // permissions.query throws on browsers that do not know the name. Nothing to do and nothing to report.
    }
}

// ------------------------------------------------------------------------------------- pointer

function onPointerDown(e) {
    if (!ownsPointer(e)) return;

    activePointerId = e.pointerId;

    // Section 2.4: the drag latch is _activeId-based and independent of hover, so the host has to keep delivering
    // moves and the eventual release even after the pointer leaves the canvas. This is what does that.
    try {
        canvas.setPointerCapture(e.pointerId);
    } catch {
        // A pointer that ended between the event and here. The drag simply behaves as it would without capture.
    }

    // NowVisualElement.OnPointerDown calls Focus() first; the DOM equivalent is this, and without it no keydown
    // ever reaches the canvas.
    canvas.focus();
    refreshClipboardIfPermitted();

    push(KIND_POINTER_DOWN,
        toSurfaceX(e.clientX), toSurfaceY(e.clientY),
        NOWUI_BUTTON[e.button] !== undefined ? NOWUI_BUTTON[e.button] : -1,
        e.buttons);

    // Stops the text-selection drag and the browser's own focus handling. The canvas is focused explicitly above.
    e.preventDefault();
}

function onPointerMove(e) {
    if (!ownsPointer(e)) return;
    push(KIND_POINTER_MOVE, toSurfaceX(e.clientX), toSurfaceY(e.clientY), e.buttons);
}

function onPointerUp(e) {
    if (!ownsPointer(e)) return;

    push(KIND_POINTER_UP,
        toSurfaceX(e.clientX), toSurfaceY(e.clientY),
        NOWUI_BUTTON[e.button] !== undefined ? NOWUI_BUTTON[e.button] : -1,
        e.buttons);

    try {
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    } catch { }

    activePointerId = null;
}

function onPointerCancel(e) {
    if (!ownsPointer(e)) return;

    push(KIND_POINTER_CANCEL, 0, 0, 0, 0);

    try {
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    } catch { }

    activePointerId = null;
}

function onPointerEnter(e) {
    if (!ownsPointer(e)) return;
    // The one-argument overload, which keeps the current button mask rather than asserting one, exactly as
    // NowVisualElement.OnPointerEnter does.
    push(KIND_POINTER_ENTER, toSurfaceX(e.clientX), toSurfaceY(e.clientY), 0, 0);
}

function onPointerLeave(e) {
    if (!ownsPointer(e)) return;
    // Position first, then ClearPointer only when nothing is held - leaving mid-drag must not clear the pointer or
    // the drag strands (section 2.6). This is NowVisualElement.OnPointerLeave, condition included.
    push(KIND_POINTER_LEAVE, toSurfaceX(e.clientX), toSurfaceY(e.clientY), e.buttons);
}

function onContextMenu(e) {
    // No provider call: the right-press already arrived as a pointerdown. Suppressing the browser's menu is what
    // lets NowUI's own context menus be reachable at all.
    e.preventDefault();
}

// ------------------------------------------------------------------------------------- wheel

function wheelNotches(e) {
    if (e.deltaMode === 1) return [e.deltaX / LINES_PER_NOTCH, e.deltaY / LINES_PER_NOTCH];
    if (e.deltaMode === 2) return [e.deltaX, e.deltaY];  // DOM_DELTA_PAGE: a guess, and never exercised in practice.
    return [e.deltaX / PX_PER_NOTCH, e.deltaY / PX_PER_NOTCH];
}

function onWheel(e) {
    const [nx, ny] = wheelNotches(e);

    // Not scaled by devicePixelRatio: the unit is notches, and the notch-to-distance conversion is
    // NowTheme.controlStyles.scrollWheelStep, a theme value in UI units applied inside the control. Scaling here
    // would double scroll speed on a retina display for no reason. Fractions are kept, because a trackpad emits
    // many small deltas and _scrollDelta is a float - rounding would turn smooth scrolling into steps.
    push(KIND_SCROLL, nx * UITK_UNITS_PER_NOTCH, ny * UITK_UNITS_PER_NOTCH, 0, 0);

    // The listener is registered with { passive: false } so this is allowed; without it the page scrolls behind the
    // canvas. Unconditional is right for a canvas that fills the window.
    e.preventDefault();
}

// ------------------------------------------------------------------------------------- keyboard

function chordPressedBits(code, shift) {
    // The chords the default source produces under `command && !option` (NowTextInput.cs:582-596), detected from
    // e.code so they survive Dvorak and AZERTY.
    switch (code) {
        case 'KeyC': return PRESSED_COPY;
        case 'KeyV': return PRESSED_PASTE;
        case 'KeyX': return PRESSED_CUT;
        case 'KeyA': return PRESSED_SELECT_ALL;
        case 'KeyZ': return shift ? PRESSED_REDO : PRESSED_UNDO;
        case 'KeyY': return PRESSED_REDO;
        case 'KeyD': return PRESSED_DUPLICATE;
        case 'Slash': return PRESSED_COMMENT;
        case 'KeyG': return PRESSED_GO_TO_LINE;
        default: return 0;
    }
}

function plainPressedBits(code) {
    switch (code) {
        case 'Home': return PRESSED_HOME;
        case 'End': return PRESSED_END;
        case 'Enter': case 'NumpadEnter': return PRESSED_ENTER;
        case 'Escape': return PRESSED_ESCAPE;
        case 'Tab': return PRESSED_TAB;
        case 'F2': return PRESSED_RENAME;
        default: return 0;
    }
}

function onKeyDown(e) {
    syncModifiers(e);

    const code = e.code;
    const command = modifierCommand;
    const option = modifierOption;

    // A character, if this keystroke made one. e.key.length === 1 is the whole test: it is true for "a", "€" and
    // "あ" and false for "Enter", "ArrowLeft", "Shift" and "Dead", on every layout, with no table (section 5.2).
    // The guard is `command && !option`, never `command` alone - AltGr arrives as Ctrl+Alt on Windows, so the
    // stricter guard would make every AltGr character vanish on European layouts.
    if (e.key.length === 1 && !(command && !option)) {
        pendingCharacters += e.key;
    }

    // Everything below is per physical press. The browser's auto-repeat is ignored for it, because NowUI generates
    // its own from the held flags and the two would compound (section 5.3). Characters above are the exception, and
    // deliberately so: a held letter key should keep inserting, and `characters` has no repeat machinery of its own.
    if (!e.repeat) {
        heldCodes.add(code);

        const keyCode = NAVIGATION_KEYCODE[code];
        if (keyCode !== undefined) push(KIND_KEY_DOWN, keyCode, e.shiftKey ? 1 : 0, 0, 0);

        pendingPressed |= plainPressedBits(code);
        if (command && !option) pendingPressed |= chordPressedBits(code, e.shiftKey);
    }

    // preventDefault, and only where the page would otherwise scroll or the browser would take the key. Keys reach
    // this handler only while the canvas itself has focus, so nothing here can steal a shortcut from the browser
    // chrome or from another element; and F5, F12, Ctrl+R, Ctrl+T, Ctrl+W and every other key outside the two sets
    // reach the browser untouched.
    if (command) {
        if (PREVENT_CHORD.has(code)) e.preventDefault();
    } else if (!option && PREVENT_PLAIN.has(code)) {
        e.preventDefault();
    }
}

function onKeyUp(e) {
    syncModifiers(e);

    heldCodes.delete(e.code);

    const keyCode = NAVIGATION_KEYCODE[e.code];
    if (keyCode !== undefined) push(KIND_KEY_UP, keyCode, 0, 0, 0);
}

// The browser delivers no keyup when the window loses focus mid-press, so without this a held arrow or a held
// Ctrl latches forever (section 4.5). Releasing exactly what is believed held is the targeted response; the
// provider's Reset() is NOT, because it also drops the pointer position and releases overlay registration
// ownership, which is a much larger discontinuity than a blur.
function releaseHeldKeys() {
    for (const code of heldCodes) {
        const keyCode = NAVIGATION_KEYCODE[code];
        if (keyCode !== undefined) push(KIND_KEY_UP, keyCode, 0, 0, 0);
    }

    heldCodes.clear();
    modifierShift = false;
    modifierCommand = false;
    modifierOption = false;
}

function onBlur() {
    releaseHeldKeys();
}

function onFocus() {
    refreshClipboardIfPermitted();
}

function invalidateRect() {
    cachedRect = null;
}

// ------------------------------------------------------------------------------------- exports

/// Attaches every listener to the canvas `canvasSelector` matches and reports what the environment supports.
/// Returns "isMac|hasPermissionsApi" so the managed side can set NowTextInput.isMacPlatform from the user agent -
/// auto-detection cannot work, because Application.platform is WebGLPlayer on every browser including Safari on a
/// Mac, so Cmd+C would silently do nothing there (section 5.4).
export function init(canvasSelector, initialUiScale) {
    canvas = document.querySelector(canvasSelector);
    if (canvas === null) throw new Error('nowui-input: no element matches ' + canvasSelector);

    uiScale = initialUiScale > 0 ? initialUiScale : 1;

    const platform =
        (navigator.userAgentData && navigator.userAgentData.platform) ||
        navigator.platform || '';
    // iPadOS reports "MacIntel" with a touch screen; it is close enough to a Mac for Command-key conventions,
    // which is all isMacPlatform decides.
    isMac = /mac|iphone|ipad|ipod/i.test(platform);

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerCancel);
    canvas.addEventListener('pointerenter', onPointerEnter);
    canvas.addEventListener('pointerleave', onPointerLeave);
    canvas.addEventListener('contextmenu', onContextMenu);
    canvas.addEventListener('wheel', onWheel, { passive: false });

    // On the canvas rather than the document, so a key only reaches NowUI - and preventDefault only runs - while
    // the canvas is the focused element. That is what keeps the browser's own shortcuts working when the user is
    // somewhere else on the page.
    canvas.addEventListener('keydown', onKeyDown);
    canvas.addEventListener('keyup', onKeyUp);
    canvas.addEventListener('blur', onBlur);
    canvas.addEventListener('focus', onFocus);

    window.addEventListener('blur', onBlur);
    window.addEventListener('resize', invalidateRect);
    window.addEventListener('scroll', invalidateRect, true);

    // On the document: `paste` targets the focused element, and this way it is caught whether or not that is the
    // canvas.
    document.addEventListener('paste', onPaste);

    return (isMac ? '1' : '0') + '|' +
        ((navigator.permissions && navigator.permissions.query) ? '1' : '0');
}

/// The UI scale Now.StartUI is called with. Constant in practice - main.js computes it once - but read back every
/// frame rather than assumed, so a host that ever changes it does not silently halve its hit-testing.
export function setUiScale(scale) {
    if (scale > 0) uiScale = scale;
}

/// Hands the managed side everything buffered since the last call and clears the buffer. Returns null when there is
/// nothing but level state to report, so an idle frame crosses the boundary without allocating an array.
export function drain() {
    invalidateRect();

    const held = heldBits();
    const pressed = pendingPressed;
    const mods = modifierBits();

    // Emitted last and unconditionally when there is anything at all to say, so the managed side applies the text
    // frame's level state after every edge in this batch.
    if (queueLength !== 0 || held !== 0 || pressed !== 0 || mods !== 0) {
        push(KIND_TEXT_STATE, held, pressed, mods, 0);
    }

    pendingPressed = 0;

    if (queueLength === 0) return null;

    const batch = queue.slice(0, queueLength);
    queueLength = 0;
    return batch;
}

/// The printable characters typed since the last call, or null. Separate from `drain` only because a string cannot
/// travel inside an array of numbers; it is consumed in the same frame, at the same point.
export function drainCharacters() {
    if (pendingCharacters.length === 0) return null;

    const characters = pendingCharacters;
    pendingCharacters = '';
    return characters;
}

/// INowTextInputBuffer.DiscardPendingText: drops characters typed before an editor became active, so they do not
/// land in it the instant it takes focus.
export function discardPendingText() {
    pendingCharacters = '';
}

/// INowClipboard.GetText. Synchronous by contract and therefore approximate by necessity: the last paste gesture,
/// the last permitted opportunistic read, or the last thing this page copied.
export function clipboardRead() {
    return clipboardCache;
}

/// INowClipboard.SetText. writeText returns a Promise the host does not await, which matches SetText's void return
/// exactly. The local cache is updated too, so a NowUI copy followed by a NowUI paste round-trips even on a browser
/// that never grants clipboard-read.
export function clipboardWrite(text) {
    const value = typeof text === 'string' ? text : '';
    clipboardCache = value;

    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(value).catch(() => legacyWrite(value));
        return;
    }

    legacyWrite(value);
}

// execCommand('copy') for browsers without the async clipboard. It needs a selection in a real element and a live
// user gesture, so it is a fallback rather than a second path: by the time a NowUI frame runs the gesture may
// already have expired, in which case the local cache above is what still makes an in-page copy/paste work.
function legacyWrite(value) {
    try {
        const scratch = document.createElement('textarea');
        scratch.value = value;
        scratch.setAttribute('readonly', '');
        scratch.style.position = 'fixed';
        scratch.style.opacity = '0';
        scratch.style.pointerEvents = 'none';
        document.body.appendChild(scratch);
        scratch.select();
        document.execCommand('copy');
        scratch.remove();
        if (canvas !== null) canvas.focus();
    } catch {
        // Nothing left to try. The cache still holds the value for an in-page paste.
    }
}
