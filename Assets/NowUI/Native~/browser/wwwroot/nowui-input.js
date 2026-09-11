// Internal event transport only. Scenes and controls are authored in C# through INowScene/NowUI.
// Records are [kind,a,b,c,d]; BrowserInput.cs defines the matching control-facing reducer.
let canvas, editor, abort, scale = 1, mac = false, keyMap;
let events = [], characters = '', preedit = '', composing = false, ime = false;
let clipboard = '', activeId = null, activeType = null, lastType = null;
let releasePending = false, releasePublished = false, committedData = null, commitTimer, cancelledComposition = false;
let frameCallback = null, inFrameCallback = false;
const touches = new Map(), held = new Set(), gamepads = new Map();
const seenGamepads = new Set();
const button = [0, 2, 1, 3, 4];
const namedCodes = {
    ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow', ArrowUp: 'UpArrow', ArrowDown: 'DownArrow',
    ShiftLeft: 'LeftShift', ShiftRight: 'RightShift', ControlLeft: 'LeftCtrl', ControlRight: 'RightCtrl',
    AltLeft: 'LeftAlt', AltRight: 'RightAlt', MetaLeft: 'LeftMeta', MetaRight: 'RightMeta',
    OSLeft: 'LeftMeta', OSRight: 'RightMeta', Backquote: 'Backquote', Equal: 'Equals',
    BracketLeft: 'LeftBracket', BracketRight: 'RightBracket', NumpadDecimal: 'NumpadPeriod',
    NumpadAdd: 'NumpadPlus', NumpadSubtract: 'NumpadMinus', NumpadEqual: 'NumpadEquals',
    IntlBackslash: 'OEM1', IntlRo: 'OEM2', IntlYen: 'OEM3', Convert: 'OEM4', NonConvert: 'OEM5',
    MediaTrackPrevious: 'MediaRewind', MediaTrackNext: 'MediaForward',
};
const preventPlain = new Set(['Tab', 'Space', 'Enter', 'NumpadEnter', 'Escape', 'Backspace', 'Delete',
    'Home', 'End', 'PageUp', 'PageDown', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);
const preventChord = new Set(['KeyA', 'KeyZ', 'KeyY', 'KeyD', 'KeyG', 'Slash']);

function push(kind, a = 0, b = 0, c = 0, d = 0) { events.push(kind, a, b, c, d); }
function owner(target) { return target === canvas || target === editor; }
function mods(e) { return (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0); }
function key(e) { return keyMap[namedCodes[e.code] || (e.code.startsWith('Key') ? e.code.slice(3) : e.code)] || 0; }
function point(e) {
    // Keep client coordinates until drain, after the host updates backing size/DPR for this frame.
    return [e.clientX, e.clientY];
}
function prevent(e) { if (e.cancelable) e.preventDefault(); }
function requestInputFrame() {
    if (!frameCallback || inFrameCallback) return;
    inFrameCallback = true;
    try { frameCallback(); } finally { inFrameCallback = false; }
}
function activate(id, type, p) {
    if (lastType !== type || type === 'touch') push(15);
    lastType = activeType = type; activeId = id; releasePublished = false;
    push(2, p[0], p[1], 0, 1);
}
function focusInput() {
    (ime ? editor : canvas).focus({ preventScroll: true });
    if (ime && navigator.virtualKeyboard?.show) {
        try { navigator.virtualKeyboard.show(); } catch { /* User activation/OS policy decides availability. */ }
    }
}
function pointerDown(e) {
    const p = point(e);
    if (e.pointerType === 'touch') {
        touches.set(e.pointerId, p);
        if (activeId === null && !releasePending && !releasePublished) activate(e.pointerId, 'touch', p);
    } else if (activeId === null || activeId === e.pointerId) {
        if (lastType !== e.pointerType) push(15);
        lastType = activeType = e.pointerType; activeId = e.pointerId; releasePublished = false;
        const b = button[e.button];
        if (b !== undefined) push(2, p[0], p[1], b, e.buttons);
    }
    try { canvas.setPointerCapture(e.pointerId); } catch { /* Synthetic DOM events may not have a native pointer. */ }
    push(10, 1);
    prevent(e);
    // The host may run its ordinary C# frame synchronously here, preserving the trusted gesture needed by iOS.
    requestInputFrame();
    focusInput();
}
function pointerMove(e) {
    const p = point(e);
    if (e.pointerType === 'touch') {
        if (touches.has(e.pointerId)) touches.set(e.pointerId, p);
        if (activeId === e.pointerId) push(1, p[0], p[1], 1);
        return;
    }
    if (activeId !== null && activeId !== e.pointerId || releasePending || touches.size && activeId !== e.pointerId) return;
    if (lastType !== e.pointerType) { push(15); lastType = e.pointerType; releasePublished = false; }
    push(1, p[0], p[1], e.buttons);
}
function pointerUp(e) {
    const p = point(e);
    let changed = false;
    touches.delete(e.pointerId);
    if (activeId === e.pointerId) {
        const b = e.pointerType === 'touch' ? 0 : button[e.button];
        if (b !== undefined) { push(3, p[0], p[1], b, e.pointerType === 'touch' ? 0 : e.buttons); changed = true; }
        if (e.pointerType === 'touch' || e.buttons === 0) {
            activeId = activeType = null;
            if (e.pointerType === 'touch' || touches.size) releasePending = true;
        }
    }
    if (activeId !== e.pointerId) {
        try { canvas.releasePointerCapture(e.pointerId); } catch { }
    }
    prevent(e);
    // Publish release at its own position before another mouse move can overwrite it.
    if (changed) requestInputFrame();
}
function pointerCancel(e) {
    touches.delete(e.pointerId);
    if (activeId !== e.pointerId) return;
    push(4); activeId = activeType = null; releasePending = true;
    requestInputFrame();
}
function blur() {
    held.clear(); touches.clear(); activeId = activeType = null;
    releasePending = releasePublished = false;
    cancelledComposition ||= composing;
    composing = false; preedit = ''; characters = ''; editor.value = '';
    push(10, 0);
}
function keyDown(e) {
    if (!owner(e.target)) return;
    const first = !held.has(e.code); held.add(e.code);
    if (e.isComposing && !composing) { composing = true; push(16, 1); }
    push(8, key(e), mods(e), e.repeat || !first ? 1 : 0);
    const command = mac ? e.metaKey : e.ctrlKey;
    const functionKey = /^F(?:[1-9]|1[0-9]|2[0-4])$/.test(e.code);
    if (!e.isComposing && !composing && (functionKey || preventPlain.has(e.code) || command && !e.altKey && preventChord.has(e.code))) prevent(e);
}
function keyUp(e) {
    if (!owner(e.target)) return;
    held.delete(e.code); push(9, key(e), mods(e));
}
function beforeInput(e) {
    if (e.isComposing || composing) return;
    if (e.inputType === 'deleteContentBackward' || e.inputType === 'deleteContentForward') {
        const code = e.inputType === 'deleteContentBackward' ? 'Backspace' : 'Delete';
        if (!held.has(code)) push(22, code === 'Backspace' ? 1 : 2);
        prevent(e);
    } else if (e.inputType === 'insertLineBreak' || e.inputType === 'insertParagraph') {
        if (!held.has('Enter') && !held.has('NumpadEnter')) push(22, 3);
        prevent(e);
    }
}
function textInput(e) {
    if (e.isComposing || composing) { preedit = e.data ?? editor.value; return; }
    if (committedData !== null && (e.data === committedData || e.inputType === 'insertFromComposition')) {
        committedData = null; editor.value = ''; return;
    }
    if (e.inputType !== 'insertFromPaste' && e.inputType !== 'insertLineBreak' && e.inputType !== 'insertParagraph')
        characters += e.data ?? editor.value;
    editor.value = '';
}
function startComposition() { composing = true; cancelledComposition = false; preedit = ''; push(16, 1); }
function updateComposition(e) { composing = true; preedit = e.data || ''; }
function endComposition(e) {
    const data = e.data || '', value = cancelledComposition ? '' : data;
    cancelledComposition = false;
    composing = false; preedit = ''; characters += value; editor.value = '';
    push(16, 0);
    committedData = data;
    clearTimeout(commitTimer);
    commitTimer = setTimeout(() => { committedData = null; }, 0);
}
function onPaste(e) {
    if (!owner(e.target)) return;
    clipboard = e.clipboardData?.getData('text/plain') ?? '';
    push(17); prevent(e);
}
function onCopy(e, cut) {
    if (!owner(e.target)) return;
    push(cut ? 19 : 18);
    requestInputFrame();
    e.clipboardData?.setData('text/plain', clipboard);
    prevent(e);
}
function pollGamepads() {
    const seen = seenGamepads; seen.clear();
    const values = navigator.getGamepads?.() || [];
    for (const pad of values) {
        if (!pad || !pad.connected || pad.mapping !== 'standard' || pad.index >= 16) continue;
        seen.add(pad.index);
        let mask = 0;
        for (let i = 0; i < Math.min(16, pad.buttons.length); i++) if (pad.buttons[i].pressed) mask |= 1 << i;
        const x = pad.axes[0] || 0, y = -(pad.axes[1] || 0), previous = gamepads.get(pad.index);
        if (!previous || previous[0] !== x || previous[1] !== y || previous[2] !== mask) {
            gamepads.set(pad.index, [x, y, mask]); push(20, pad.index, x, y, mask);
        }
    }
    for (const index of gamepads.keys()) if (!seen.has(index)) { gamepads.delete(index); push(21, index); }
}

export function init(selector, uiScale, keys) {
    detach();
    canvas = document.querySelector(selector);
    if (!(canvas instanceof HTMLCanvasElement)) throw new Error(`NowUI input canvas not found: ${selector}`);
    scale = uiScale; mac = /Mac|iPhone|iPad|iPod/.test(navigator.platform || navigator.userAgent);
    keyMap = Object.create(null);
    for (const pair of keys.split('|')) { const [name, value] = pair.split(':'); if (name) keyMap[name] = Number(value); }
    // Enum aliases may choose a canonical name during ToString; all meta aliases have the same shared identity.
    keyMap.LeftMeta ??= keyMap.LeftCommand ?? keyMap.LeftWindows ?? keyMap.LeftApple;
    keyMap.RightMeta ??= keyMap.RightCommand ?? keyMap.RightWindows ?? keyMap.RightApple;
    editor = document.createElement('textarea');
    editor.setAttribute('aria-label', 'NowUI text input');
    editor.setAttribute('autocorrect', 'off');
    editor.autocomplete = 'off'; editor.autocapitalize = 'off'; editor.spellcheck = false;
    editor.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;padding:0;border:0;opacity:0.01;z-index:-1;resize:none;font-size:16px;pointer-events:none';
    document.body.appendChild(editor);
    canvas.tabIndex = 0; canvas.style.touchAction = 'none';
    abort = new AbortController();
    const on = (target, type, fn, options = {}) => target.addEventListener(type, fn, { ...options, signal: abort.signal });
    on(canvas, 'pointerdown', pointerDown); on(canvas, 'pointermove', pointerMove); on(canvas, 'pointerup', pointerUp);
    on(canvas, 'pointercancel', pointerCancel); on(canvas, 'lostpointercapture', pointerCancel);
    on(canvas, 'pointerleave', e => { if (activeId === null && e.pointerType !== 'touch') push(6); });
    on(canvas, 'wheel', e => {
        const unit = e.deltaMode === 1 ? 3 : e.deltaMode === 2 ? Math.max(1, canvas.clientHeight) : 100;
        push(7, e.deltaX / unit, -e.deltaY / unit); prevent(e);
    }, { passive: false });
    on(canvas, 'contextmenu', prevent);
    on(window, 'keydown', keyDown); on(window, 'keyup', keyUp);
    on(window, 'blur', blur);
    on(document, 'visibilitychange', () => { if (document.hidden) blur(); });
    on(document, 'focusout', e => { if (owner(e.target) && !owner(e.relatedTarget)) blur(); });
    on(document, 'focusin', e => { if (owner(e.target)) push(10, 1); });
    on(editor, 'beforeinput', beforeInput); on(editor, 'input', textInput);
    on(editor, 'compositionstart', startComposition); on(editor, 'compositionupdate', updateComposition); on(editor, 'compositionend', endComposition);
    on(document, 'paste', onPaste); on(document, 'copy', e => onCopy(e, false)); on(document, 'cut', e => onCopy(e, true));
    canvas.focus({ preventScroll: true });
    return mac;
}

export function drain() {
    if (releasePublished) { releasePublished = false; push(6); }
    if (activeId === null && !releasePending && touches.size) {
        const [id, p] = touches.entries().next().value; activate(id, 'touch', p);
    }
    pollGamepads();
    const result = events.length ? events : null;
    if (result) {
        const r = canvas.getBoundingClientRect();
        const sx = canvas.width / Math.max(1, r.width) / scale, sy = canvas.height / Math.max(1, r.height) / scale;
        for (let i = 0; i < result.length; i += 5) if (result[i] >= 1 && result[i] <= 3) {
            result[i + 1] = (result[i + 1] - r.left) * sx;
            result[i + 2] = (result[i + 2] - r.top) * sy;
        }
        events = [];
    }
    releasePublished = releasePending; releasePending = false;
    return result;
}
export function drainCharacters() { const value = characters; characters = ''; return value; }
export function composition() { return preedit; }
export function setUiScale(value) { scale = value; }
export function setImeEnabled(value) {
    ime = value;
    if (!editor) return;
    if (!value) {
        cancelledComposition ||= composing;
        if (composing) push(16, 0);
        composing = false; preedit = ''; editor.value = '';
        if (document.activeElement === editor) canvas.focus({ preventScroll: true });
    } else if (owner(document.activeElement)) editor.focus({ preventScroll: true });
}
export function setCompositionCursor(x, y) {
    if (!editor) return;
    const r = canvas.getBoundingClientRect();
    editor.style.left = `${Math.max(0, Math.min(innerWidth - 1, r.left + x * scale * r.width / canvas.width))}px`;
    editor.style.top = `${Math.max(0, Math.min(innerHeight - 1, r.top + y * scale * r.height / canvas.height))}px`;
}
export function discardPendingText() {
    cancelledComposition ||= composing;
    characters = ''; preedit = ''; composing = false;
    if (editor) editor.value = '';
    push(16, 0);
}
export function getClipboard() { return clipboard; }
export function setClipboard(value) {
    clipboard = value || '';
    if (navigator.clipboard?.writeText) navigator.clipboard.writeText(clipboard).catch(() => {});
}
// The browser host can use its ordinary exported Frame callback during trusted input gestures.
export function setInputFrameCallback(callback) { frameCallback = callback; }
export function detach() {
    abort?.abort(); abort = null;
    editor?.remove(); editor = null; canvas = null;
    events = []; characters = preedit = ''; composing = ime = false;
    activeId = activeType = lastType = null; releasePending = releasePublished = false;
    held.clear(); touches.clear(); gamepads.clear(); clearTimeout(commitTimer); committedData = null; cancelledComposition = false;
    frameCallback = null; inFrameCallback = false;
}
