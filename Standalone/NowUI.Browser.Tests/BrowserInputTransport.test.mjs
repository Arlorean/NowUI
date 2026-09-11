import test from 'node:test';
import assert from 'node:assert/strict';
import * as input from '../NowUI.Browser/wwwroot/nowui-input.js';

// Minimal DOM event harness. The real browser smoke remains a separate integration check.
class Surface {
    listeners = new Map(); style = {}; value = ''; tabIndex = -1;
    addEventListener(type, handler, options = {}) {
        const values = this.listeners.get(type) || []; values.push(handler); this.listeners.set(type, values);
        options.signal?.addEventListener('abort', () => this.listeners.set(type, (this.listeners.get(type) || []).filter(x => x !== handler)), { once: true });
    }
    emit(type, properties = {}) {
        const e = { target: this, cancelable: true, preventDefault() { this.defaultPrevented = true; }, ...properties };
        for (const fn of [...(this.listeners.get(type) || [])]) fn(e);
        if (this !== document && this !== window) {
            for (const fn of [...(document.listeners.get(type) || [])]) fn(e);
            for (const fn of [...(window.listeners.get(type) || [])]) fn(e);
        }
        return e;
    }
    focus() {
        const previous = document.activeElement;
        if (previous === this) return;
        document.activeElement = this;
        if (previous) document.emit('focusout', { target: previous, relatedTarget: this });
        document.emit('focusin', { target: this });
    }
    setAttribute() {} remove() {} setPointerCapture() {} releasePointerCapture() {}
}
class Canvas extends Surface {
    width = 800; height = 400; clientHeight = 200;
    getBoundingClientRect() { return { left: 10, top: 20, width: 400, height: 200 }; }
}
let canvas, textarea, pads;
const keys = 'None:0|A:15|V:36|Enter:2|LeftCtrl:55|Backspace:65|F24:134|LeftMeta:57|RightMeta:58';
function setup() {
    input.detach();
    globalThis.window = new Surface(); globalThis.document = new Surface();
    globalThis.HTMLCanvasElement = Canvas; globalThis.innerWidth = 1000; globalThis.innerHeight = 700;
    canvas = new Canvas(); textarea = null; pads = [];
    document.body = { appendChild(value) { textarea = value; } };
    document.querySelector = () => canvas; document.createElement = () => new Surface();
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { platform: 'Win32', getGamepads: () => pads, clipboard: { writeText: async () => {} } } });
    input.init('#canvas', 2, keys); input.drain();
}
function records() {
    const data = input.drain() || [], result = [];
    for (let i = 0; i < data.length; i += 5) result.push(data.slice(i, i + 5));
    return result;
}
const pointer = (id, type, x, y, buttons = 1, button = 0) => ({ pointerId: id, pointerType: type, clientX: x, clientY: y, buttons, button });

test('CSS/backing scale and five mouse buttons use the shared ordering', () => {
    setup(); canvas.emit('pointerdown', pointer(1, 'mouse', 50, 60, 4, 1));
    assert.ok(records().some(x => x.join() === '2,40,40,2,4'));
    canvas.emit('pointerup', pointer(1, 'mouse', 50, 60, 0, 1));
    assert.ok(records().some(x => x.join() === '3,40,40,2,0'));
});

test('release is published at its own position before a fast subsequent pointer move', () => {
    setup(); const frames = [];
    input.setInputFrameCallback(() => frames.push(records()));
    canvas.emit('pointerdown', pointer(1, 'mouse', 50, 60));
    canvas.emit('pointerup', pointer(1, 'mouse', 50, 60, 0));
    canvas.emit('pointermove', pointer(1, 'mouse', 350, 190, 0));
    assert.equal(frames.length, 2);
    assert.ok(frames[1].some(x => x.join() === '3,40,40,0,0'));
    const remaining = records();
    assert.ok(!remaining.some(x => x[0] === 3));
    assert.ok(remaining.some(x => x[0] === 1 && x[1] === 340));
});

test('releasing one mouse button retains capture while another button is held', () => {
    setup(); let releases = 0;
    canvas.releasePointerCapture = () => { releases++; };
    canvas.emit('pointerdown', pointer(1, 'mouse', 50, 60, 1));
    canvas.emit('pointerdown', pointer(1, 'mouse', 50, 60, 3, 2));
    records();
    canvas.emit('pointerup', pointer(1, 'mouse', 50, 60, 2));
    assert.equal(releases, 0);
    assert.ok(records().some(x => x.join() === '3,40,40,0,2'));
    canvas.emit('pointerup', pointer(1, 'mouse', 50, 60, 0, 2));
    assert.equal(releases, 1);
});
test('touch release precedes next contact and cancellation is explicit', () => {
    setup(); canvas.emit('pointerdown', pointer(11, 'touch', 30, 30)); records();
    canvas.emit('pointerdown', pointer(22, 'touch', 200, 100));
    assert.ok(!records().some(x => x[0] === 2));
    canvas.emit('pointerup', pointer(11, 'touch', 30, 30, 0));
    const release = records(); assert.ok(release.some(x => x[0] === 3)); assert.ok(!release.some(x => x[0] === 2));
    assert.ok(records().some(x => x[0] === 2 && x[1] === 190));
    canvas.emit('pointercancel', pointer(22, 'touch', 200, 100));
    assert.ok(records().some(x => x[0] === 4));
});
test('queued pointer coordinates use the latest backing size after a resize', () => {
    setup();
    canvas.getBoundingClientRect = () => ({ left: 10, top: 20, width: 800, height: 200 });
    canvas.emit('pointerdown', pointer(1, 'mouse', 50, 60));
    canvas.width = 1600;
    assert.ok(records().some(x => x[0] === 2 && x[1] === 40 && x[2] === 40));
});
test('a touch cannot steal the active mouse drag', () => {
    setup(); canvas.emit('pointerdown', pointer(1, 'mouse', 30, 30)); records();
    canvas.emit('pointerdown', pointer(2, 'touch', 200, 100));
    canvas.emit('pointermove', pointer(1, 'mouse', 80, 30));
    assert.ok(records().some(x => x[0] === 1 && x[1] === 70));
    canvas.emit('pointerup', pointer(1, 'mouse', 80, 30, 0));
    assert.ok(!records().some(x => x[0] === 2));
    assert.ok(records().some(x => x[0] === 2 && x[1] === 190));
});
test('composition produces one Unicode commit and an explicit commit-frame guard', () => {
    setup(); input.setImeEnabled(true);
    textarea.emit('compositionstart'); textarea.emit('compositionupdate', { data: 'にほん' });
    assert.equal(input.composition(), 'にほん'); assert.equal(input.drainCharacters(), '');
    textarea.emit('keydown', { code: 'Enter', isComposing: true });
    textarea.emit('compositionend', { data: '日本' });
    textarea.emit('input', { inputType: 'insertFromComposition', data: '日本' });
    assert.equal(input.drainCharacters(), '日本'); assert.equal(input.drainCharacters(), '');
    assert.ok(records().some(x => x[0] === 16 && x[1] === 0));
    assert.equal(input.composition(), '');
});
test('paste text is cached synchronously and generated only by the paste gesture', () => {
    setup(); input.setImeEnabled(true);
    textarea.emit('keydown', { code: 'KeyV', ctrlKey: true });
    assert.ok(!records().some(x => x[0] === 17));
    textarea.emit('paste', { clipboardData: { getData: () => 'clipboard🙂' } });
    assert.equal(input.getClipboard(), 'clipboard🙂'); assert.equal(input.drainCharacters(), '');
    assert.ok(records().some(x => x[0] === 17));
});
test('discarding preedit prevents a late DOM commit leaking into another field', () => {
    setup(); input.setImeEnabled(true);
    textarea.emit('compositionstart'); textarea.emit('compositionupdate', { data: 'old' });
    input.discardPendingText();
    textarea.emit('compositionend', { data: 'old' });
    textarea.emit('input', { inputType: 'insertFromComposition', data: 'old' });
    assert.equal(input.drainCharacters(), ''); assert.equal(input.composition(), '');
});
test('function keys are capturable without also invoking browser reload', () => {
    setup(); const e = canvas.emit('keydown', { code: 'F5' });
    assert.equal(e.defaultPrevented, true);
});
test('trusted copy callback runs the normal frame before filling clipboard data', () => {
    setup(); input.setImeEnabled(true);
    let copied = '';
    input.setInputFrameCallback(() => input.setClipboard('selected text'));
    textarea.emit('copy', { clipboardData: { setData: (_, value) => { copied = value; } } });
    assert.equal(copied, 'selected text');
});
test('gamepad changes and disconnects arrive once and blur cancels state', () => {
    setup(); pads = [{ connected: true, mapping: 'standard', index: 0, axes: [.75, -.25], buttons: [{ pressed: true }] }];
    assert.ok(records().some(x => x.join() === '20,0,0.75,0.25,1'));
    assert.equal(records().length, 0);
    pads = []; assert.ok(records().some(x => x[0] === 21));
    window.emit('blur'); assert.ok(records().some(x => x[0] === 10 && x[1] === 0));
});
test('detach removes event handlers and the hidden input element', () => {
    setup(); input.detach(); canvas.emit('pointerdown', pointer(1, 'mouse', 20, 20));
    assert.equal((canvas.listeners.get('pointerdown') || []).length, 0);
});
