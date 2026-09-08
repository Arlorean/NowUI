// W1 - the JavaScript half of the transport spike. Docs/Standalone/M3-Spec.md section 5.1, section 9 (W1).
//
// It answers one question and measures the answer: given a [JSMarshalAs<JSType.MemoryView>] Span<T> parameter, can
// JavaScript WRITE into WASM memory with set() and READ out of it with slice(), and what does that cost per frame?
//
// Nothing here retains a view past the call that handed it over. That is the rule nowui-gl.js already states
// ("A .NET MemoryView is valid only for the duration of the call") and the rule the whole ABI is designed around:
// JavaScript owns its own ArrayBuffers, and each frame is one call with two copies inside it.

// ---------------------------------------------------------------------------------------------- the pattern
//
// A linear congruential generator, so both sides can produce the same 1 MB with no shared data and no transfer.
// Math.imul is the multiplication that wraps at 32 bits the way C#'s unchecked uint multiply does; >>> 0 puts the
// sum back into the unsigned 32-bit range before the next step, and | 0 reinterprets it as the signed int C#
// stores. Do this any other way and the two sides disagree above 2^31 only, which is exactly the bug a byte
// comparison at 1 MB exists to catch.
function fillPattern(target, count, seed) {
    let x = seed >>> 0;
    for (let i = 0; i < count; i++) {
        x = (Math.imul(x, 1664525) + 1013904223) >>> 0;
        target[i] = x | 0;
    }
    return target;
}

// JavaScript's own scratch buffers, allocated once and reused - which is what the real recorder does. Growing them
// is a JS-side concern the managed side never sees.
let scratchInts = new Int32Array(0);

function scratch(count) {
    if (scratchInts.length < count) scratchInts = new Int32Array(count);
    return scratchInts;
}

// ---------------------------------------------------------------------------------------------- [1] the probe

// What a MemoryView actually is, reported as text rather than assumed. The interesting part is not the member list
// but `setWrote`: a set() that exists and silently writes into a detached copy would pass a member check and fail
// the whole design, so this writes three known ints and the managed side reads them back.
export function probe(ops, opsText) {
    const lines = [];

    const ctor = ops && ops.constructor ? ops.constructor.name : '<none>';
    lines.push('constructor=' + ctor);
    lines.push('typeof ops=' + (typeof ops) + ', isTypedArray=' + ArrayBuffer.isView(ops));

    const members = ['set', 'slice', 'copyTo', 'subarray', 'length', 'byteLength', 'buffer', 'byteOffset'];
    const shape = members.map(name => {
        const value = ops ? ops[name] : undefined;
        if (value === undefined) return name + '=absent';
        return name + '=' + (typeof value === 'function' ? 'function' : String(value));
    });
    lines.push(shape.join(' '));

    // The one that matters. Written through set(), read back by the managed side.
    let setWrote = false;
    try {
        ops.set(Int32Array.of(111, 222, 333));
        setWrote = true;
    } catch (e) {
        lines.push('set threw: ' + e);
    }
    lines.push('setWrote=' + setWrote);

    // The byte view too, because the ABI carries UTF-8 string bytes in a Span<byte> beside the Span<int>.
    let byteSetWrote = false;
    try {
        opsText.set(Uint8Array.of(78, 79, 87));   // 'NOW'
        byteSetWrote = true;
    } catch (e) {
        lines.push('opsText.set threw: ' + e);
    }
    lines.push('byteSetWrote=' + byteSetWrote + ', opsText.length=' + (opsText ? opsText.length : -1));

    // Whether slice() gives back a real, detached typed array.
    try {
        const copy = ops.slice(0, 4);
        lines.push('slice(0,4) -> ' + copy.constructor.name + ' length=' + copy.length +
            ' detached=' + (copy.buffer !== (ops.buffer || null)));
    } catch (e) {
        lines.push('slice threw: ' + e);
    }

    return lines.join('\n');
}

// ---------------------------------------------------------------------------------------------- [2] NEED_MORE

// Section 5.1's growth path writes exactly two slots into a view that is otherwise too small for the frame, then
// returns -1. If a partial set() were to zero the rest of the view, or refuse a source shorter than the target,
// that path would not work. Returns 0 on success, or a negative code naming what refused.
export function partialSet(ops, a, b, offset) {
    try {
        if (offset === 0) ops.set(Int32Array.of(a, b));
        else ops.set(Int32Array.of(a, b), offset);
        return 0;
    } catch (e) {
        console.error('[NowUI.spike] partialSet failed: ' + e);
        return -1;
    }
}

// ---------------------------------------------------------------------------------------------- [3] round trip

// The correctness run, and the only function here that checks anything.
//
//   results  is filled by the managed side with the seeded pattern. slice() copies it OUT.
//   ops      is poisoned by the managed side. set() writes the bytes just read straight back IN.
//
// So a clean run proves both directions with one pattern: JavaScript byte-compares what it received, the managed
// side byte-compares what came back, and neither side can pass by leaving a buffer alone.
// `corruptAt` >= 0 flips one bit of one byte on the way back, and nothing else. It is the negative control: a
// comparator that reports PASS on a buffer it never looked at would report PASS here too, so the run that matters
// most in this file is the one that has to come back FAIL.
export function roundTrip(ops, results, count, seed, corruptAt) {
    // WASM -> JS. slice() is the copy; `received` is JavaScript's own array from here on.
    const received = results.slice(0, count);

    // Regenerate the same pattern independently and compare BYTES, not ints - a byte comparison catches an
    // endianness or element-stride mistake that an int comparison would step over.
    const expected = fillPattern(new Int32Array(count), count, seed >>> 0);

    const gotBytes = new Uint8Array(received.buffer, received.byteOffset, count * 4);
    const wantBytes = new Uint8Array(expected.buffer, expected.byteOffset, count * 4);

    for (let i = 0; i < wantBytes.length; i++) {
        if (gotBytes[i] !== wantBytes[i]) {
            console.error('[NowUI.spike] slice() mismatch at byte ' + i +
                ': got ' + gotBytes[i] + ', want ' + wantBytes[i]);
            return -(i + 1);
        }
    }

    if (corruptAt >= 0 && corruptAt < count * 4) gotBytes[corruptAt] ^= 0x01;

    // JS -> WASM. The bytes that were just read, so the managed side's comparison is end to end.
    ops.set(received);
    return 0;
}

// ---------------------------------------------------------------------------------------------- [4] the timings
//
// Four shapes, so the per-frame figure can be decomposed rather than believed:
//   noop       crosses the boundary and returns - marshalling only
//   setOnly    one memcpy, JS -> wasm
//   sliceOnly  one memcpy, wasm -> JS
//   copy       both, in the order section 5.1's record() does them - the per-frame cost
//
// The managed side times these; each is one call, so nothing is amortised inside JavaScript that a real frame would
// not amortise too.

export function noop(ops, results, count) {
    return count;
}

export function setOnly(ops, results, count) {
    ops.set(scratch(count).subarray(0, count));
    return count;
}

export function sliceOnly(ops, results, count) {
    const copy = results.slice(0, count);
    return copy.length;
}

export function copy(ops, results, count) {
    // Exactly the shape of record(): read last frame's results out first, then write this frame's ops in.
    const back = results.slice(0, count);
    const source = scratch(count);
    ops.set(source.subarray(0, count));
    return back.length;
}

// ---- the allocation-free readback ------------------------------------------------------------------------
//
// slice() measured five to nine times the cost of set() for the same bytes, and the difference is not the memcpy:
// slice() allocates a fresh typed array on every call, so the readback direction pays an allocation and a GC per
// frame that the write direction does not. copyTo() is on the MemoryView too (the probe lists it) and copies into a
// buffer JavaScript already owns.
//
// Measured beside slice() rather than substituted for it, because a claim that one is faster than the other is only
// worth having as two numbers from the same run.

let readback = new Int32Array(0);

function readbackBuffer(count) {
    if (readback.length < count) readback = new Int32Array(count);
    return readback;
}

export function copyToOnly(ops, results, count) {
    const target = readbackBuffer(count);
    // copyTo() copies the whole view, so the view and the target have to agree on length; the real bridge sizes
    // its managed results buffer to the slot count it asked for, which is exactly that situation.
    results.copyTo(target.subarray(0, count));
    return target[0] | 0;
}

export function copyFast(ops, results, count) {
    const target = readbackBuffer(count);
    results.copyTo(target.subarray(0, count));
    ops.set(scratch(count).subarray(0, count));
    return target[0] | 0;
}

// A realistic frame, at the sizes section 5.1 predicts: ~7 KB of ops out, ~600 bytes of results back. Both
// directions in one call, which is what "boundary crossings per frame: 1" means.
export function frame(ops, results, opsCount, resultCount) {
    const target = readbackBuffer(resultCount);
    results.copyTo(target.subarray(0, resultCount));
    ops.set(scratch(opsCount).subarray(0, opsCount));
    return opsCount;
}

// ------------------------------------------------------------------------------------- the double[] fallback
//
// WebInput.Drain's mechanism (WebInput.cs:401-405), measured beside the MemoryView so section 5.1's cost table has
// two numbers. A JS Array of Numbers marshals element by element in both directions and allocates a fresh managed
// array on the way in; that is the cost being measured, and it is the honest alternative if set() had been absent.

let fallbackSource = [];

export function fallbackOut(count) {
    if (fallbackSource.length !== count) {
        fallbackSource = new Array(count);
        const pattern = fillPattern(new Int32Array(count), count, 0x1234);
        for (let i = 0; i < count; i++) fallbackSource[i] = pattern[i];
    }
    return fallbackSource;
}

export function fallbackIn(data) {
    // Touch it, so a runtime that could otherwise elide the conversion does not.
    return data.length === 0 ? 0 : (data[0] | 0) ^ (data[data.length - 1] | 0);
}

// ---------------------------------------------------------------------------------------------- the report

// The page has no DOM to speak of (index.html is a canvas and nothing else), so the report is appended as a <pre>
// that a headless --dump-dom can read back as text. Also logged, for a run driven with a console listener.
export function report(text) {
    console.log(text);

    let node = document.getElementById('nowui-spike-report');
    if (!node) {
        node = document.createElement('pre');
        node.id = 'nowui-spike-report';
        node.style.cssText = 'position:fixed;inset:0;margin:0;padding:12px;overflow:auto;z-index:10;' +
            'background:#0d1117;color:#c9d1d9;font:12px/1.45 ui-monospace,Menlo,Consolas,monospace;' +
            'white-space:pre;';
        document.body.appendChild(node);
    }
    node.textContent = text;

    // A marker a driver can poll for without parsing the report.
    document.title = 'NowUI W1 spike: done';
    window.__nowuiSpikeDone = true;
}
