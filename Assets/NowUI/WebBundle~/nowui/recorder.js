// W2 - the recorder. Docs/Standalone/M3-Spec.md sections 5.1 to 5.4.
//
// JavaScript owns its buffers. This file is the whole of that ownership: a growable ArrayBuffer with an Int32Array,
// a Float32Array and a Uint8Array over it, the two string tables, the op stream, and the header that describes
// them. The recorder writes at full speed into its own memory with no MemoryView proxy anywhere in the path; the
// only crossing is the pair of set() calls in bridge.js, once per frame.
//
// Why the op stream is recorded into a second buffer and assembled at flush. Section 5.2 puts the intern and
// volatile tables BETWEEN the fixed header and the op stream, and neither table's length is known until the frame
// is over - a string is interned when it is first used, which can be the last op. Recording into a separate
// buffer and copying it into place at flush costs one memcpy of the op stream (about 7 KB for a realistic frame,
// against the ~350 KB of vertex data the WebGL2 backend already moves in the same frame) and keeps the wire format
// exactly as specified. The alternative - a second set() at a target offset - depends on MemoryView.set accepting
// an offset argument, which W1 measures but which nothing else in the design needs; this does not bet on it.

import {
    MAGIC, HDR_SLOTS, HDR_MAGIC, HDR_SURFACE_HASH, HDR_FRAME_FLAGS, HDR_INTERN_COUNT, HDR_VOLATILE_COUNT,
    HDR_TEXT_BYTES, HDR_OP_START, HDR_OP_END,
    FLAG_FAULTED, FLAG_EXACT_LAYOUT, FLAG_HAS_NEW_STRINGS,
    OP_SCOPE_CLOSE, MAX_SLOTS, DEFAULT_MAX_STRINGS, SURFACE_HASH,
} from './abi.js';

import { PathTrie, NowUIAuthorError } from './trie.js';
import { Results } from './results.js';

const encoder = new TextEncoder();

export class Recorder {
    constructor(options = {}) {
        this.maxSlots = options.maxSlots || MAX_SLOTS;
        this.maxStrings = options.maxStrings || DEFAULT_MAX_STRINGS;
        this.exactLayout = options.exactLayout !== false;

        // The op stream, recorded here and copied into `out` at flush.
        this._allocOps(1024);
        this.opsUsed = 0;

        // The assembled frame: header + tables + op stream. What crosses the boundary.
        this.out = new Int32Array(1024);
        this.used = 0;

        // UTF-8 bytes for this frame's new interns and this frame's volatile strings, in that order.
        this.u8 = new Uint8Array(4096);
        this.textUsed = 0;

        // Section 5.4. handle -> permanent for the session; the wasm side keeps a List<string> at the same indices.
        this.interned = new Map();
        this.internCount = 0;
        this.newInterns = [];       // flat triples: handle, byteOffset, byteLength
        this.volatiles = [];        // flat pairs:   byteOffset, byteLength

        this.frame = 0;
        this.faulted = false;
        this.faultError = null;
        this.pending = false;       // section 5.1: a NEED_MORE retry re-uses the bytes already recorded

        this.openScopes = [];       // labels, for the cap message and for closeAll
        this.overflowed = false;

        this.trie = new PathTrie(key => this.intern(key), {
            maxStrings: this.maxStrings,
            onReport: options.onReport,
        });

        // W5's table (section 6). It lives on the recorder because the two directions are one conversation: a
        // value control resolves its answer out of `results` and emits the resolved value into `ops` in the same
        // call (section 6.4), and separating the two would mean the surface holds two objects that must never
        // disagree about which frame it is.
        this.results = new Results({
            debug: options.debug === true,
            onReport: options.onResultReport,
        });
    }

    // ------------------------------------------------------------------------------------------ frame bracket

    /// Starts a new recording. Called only when `pending` is false, so a growth retry never re-runs the author's
    /// draw function (section 5.1) and no handler fires twice.
    reset(frame) {
        this.frame = frame;
        this.opsUsed = 0;
        this.textUsed = 0;
        this.newInterns.length = 0;
        this.volatiles.length = 0;
        this.openScopes.length = 0;
        this.faulted = false;
        this.faultError = null;
        this.overflowed = false;
        this.trie.beginFrame(frame);
    }

    /// Section 4.4. The author's draw function threw. The frame is marked faulted and whatever was recorded before
    /// the throw is kept: it is a balanced prefix, because closeAll() runs on every path out.
    fault(error) {
        this.faulted = true;
        this.faultError = error;
    }

    /// Section 4.1: "the buffer is balanced on every path". Closes every scope the draw function left open - which
    /// only happens when it threw, since every scope helper closes in a finally.
    closeAll() {
        while (this.openScopes.length > 0) this.endScope();
        this.trie.endFrame();
    }

    // ------------------------------------------------------------------------------------------ emitting

    /// Reserves one op header slot plus `slots` argument slots, and writes the header. Callers then write exactly
    /// `slots` argument slots with i32/f32. `argSlots` is carried in the high 16 bits so a decoder meeting an
    /// unknown opcode can skip it rather than desynchronise (section 5.2).
    op(record, slots = record.slots) {
        this._ensureOps(this.opsUsed + 1 + slots);
        this.ops[this.opsUsed++] = (record.opcode & 0xffff) | ((slots & 0xffff) << 16);
    }

    i32(value) {
        this.ops[this.opsUsed++] = value | 0;
    }

    f32(value) {
        this.opsF32[this.opsUsed++] = value;
    }

    /// Opens a scope: emits its op, then remembers it so the matching OP_SCOPE_CLOSE is emitted even if the body
    /// throws. `node` is the trie node the scope pushed; its label is what the cap message prints.
    beginScope(record, node, writeArgs) {
        this.op(record);
        writeArgs();
        this.openScopes.push(node.label);
    }

    endScope() {
        this.openScopes.pop();

        // Once the slot cap has been hit the buffer is being abandoned, and asking to grow it again would throw a
        // SECOND time - out of the finally block that closeAll() runs in, replacing the cap message (which names
        // the open scopes) with an identical one that names none, because they have just been popped. Measured,
        // not reasoned: that is exactly what the first run of the cap test produced.
        if (!this.overflowed) {
            this._ensureOps(this.opsUsed + 1);
            this.ops[this.opsUsed++] = OP_SCOPE_CLOSE & 0xffff;   // argSlots 0
        }

        this.trie.pop();
    }

    // ------------------------------------------------------------------------------------------ strings

    /// Section 5.4, interned. A string is interned on first use and its handle is permanent for the session.
    /// Handles introduced this frame are declared in the header's intern table, contiguously from the wasm side's
    /// current count, so the decoder adds them in one pass before it decodes anything.
    intern(text) {
        const existing = this.interned.get(text);
        if (existing !== undefined) return existing;

        if (this.internCount >= this.maxStrings) {
            // Section 5.4's ceiling. Reached through a KEY, this throws: a UI minting unbounded distinct keys is
            // deriving identity from data, and stopping is correct. Reached through a plain string, the caller
            // (`str`) catches this and falls back to the volatile path with one warning.
            throw new NowUIAuthorError(
                'NowUI: the string intern table is full (' + this.maxStrings + ' entries).\n' +
                'Keys are interned permanently, so this means keys are being derived from data that changes -\n' +
                'a timestamp, a counter, an interpolated value. Give the control a stable key and pass the\n' +
                'changing text as an option: ui.button(\'retry\', { label: `Retry (${n})` }).');
        }

        const handle = this.internCount++;
        const { offset, length } = this._writeText(text);
        this.newInterns.push(handle, offset, length);
        this.interned.set(text, handle);
        return handle;
    }

    /// Section 5.3's `str` argument kind. A string the recorder has seen before interns; anything else this frame
    /// is volatile - written into opsText for this frame only, and referenced as ~index so the sign discriminates
    /// the two tables with no extra slot.
    str(text) {
        if (text === null || text === undefined) text = '';
        else if (typeof text !== 'string') text = String(text);

        const existing = this.interned.get(text);
        if (existing !== undefined) return existing;

        if (this.internCount < this.maxStrings && this._shouldIntern(text)) {
            return this.intern(text);
        }

        const { offset, length } = this._writeText(text);
        this.volatiles.push(offset, length);
        return ~(this.volatiles.length / 2 - 1);
    }

    /// Section 5.4's volatile path, taken unconditionally. For a control's VALUE - a text field's contents, a
    /// formatted number - the caller knows the string is per-frame data and says so, rather than leaving it to
    /// the heuristic below.
    ///
    /// W5 is where this had to exist, and the reason is worth writing down because the heuristic looks right
    /// until a value control is drawn through it.
    ///
    /// Section 5.4 says the volatile path is "the difference between a text field costing one UTF-8 encode per
    /// keystroke and permanently leaking one intern-table entry per keystroke", and that a `str` position
    /// "interns only when the same string instance or value has been seen before". Those two are inconsistent for
    /// a text field, and the inconsistency is only visible once one exists. Type "Ada": frame N emits "A", which
    /// has not been seen, so it is volatile - and then frame N+1 emits "A" AGAIN, because the author echoes the
    /// value back through section 6.4's rule, and "seen before" is now true. Every prefix of every string the user
    /// types is interned one frame after it is typed.
    ///
    /// Measured in the browser before this method existed: typing "Ada Lovelace" into wwwroot/nowui/app.stub.js's
    /// w5 field took the session's intern count from 12 to 34 - twenty-two permanent handles for twelve
    /// keystrokes - and clearing and retyping took it to 43. At section 5.4's ceiling of 65 536 that is a few
    /// minutes of typing before a key throws.
    volatileStr(text) {
        if (text === null || text === undefined) text = '';
        else if (typeof text !== 'string') text = String(text);

        const { offset, length } = this._writeText(text);
        this.volatiles.push(offset, length);
        return ~(this.volatiles.length / 2 - 1);
    }

    /// The recorder decides intern-vs-volatile by argument position (the op table's `str` kind) and by whether the
    /// value has been seen before - never by guessing at the content. `seen` is a per-session set of values that
    /// have appeared in a `str` position at least once; the second sighting interns.
    ///
    /// It is the right rule for a LABEL, which is what `str` positions mostly are: a label built by interpolation
    /// (`'Retry (' + n + ')'`) stays volatile while n changes and interns once it settles. It is the wrong rule
    /// for a VALUE, which is why `volatileStr` exists above and why the surface calls it for one.
    _shouldIntern(text) {
        if (this._seen === undefined) this._seen = new Set();
        if (this._seen.has(text)) { this._seen.delete(text); return true; }
        if (this._seen.size > 4096) this._seen.clear();
        this._seen.add(text);
        return false;
    }

    _writeText(text) {
        const offset = this.textUsed;

        // Worst case is 4 bytes per UTF-16 code unit; asking for that up front means encodeInto never has to run
        // twice, and the buffer stays allocated for the session.
        this._ensureText(offset + text.length * 4 + 4);

        const result = encoder.encodeInto(text, this.u8.subarray(offset));
        this.textUsed = offset + result.written;
        return { offset, length: result.written };
    }

    // ------------------------------------------------------------------------------------------ flush

    /// Assembles header + tables + op stream into `out`, and sets `used` / `textUsed` to the two sizes the managed
    /// buffers must be able to hold. Section 5.2's layout, in order.
    flush() {
        const internCount = this.newInterns.length / 3;
        const volatileCount = this.volatiles.length / 2;
        const opStart = HDR_SLOTS + internCount * 3 + volatileCount * 2;
        const opEnd = opStart + this.opsUsed;

        this._ensureOut(opEnd);
        const out = this.out;

        out[HDR_MAGIC] = MAGIC;
        out[HDR_SURFACE_HASH] = SURFACE_HASH;
        out[HDR_FRAME_FLAGS] =
            (this.faulted ? FLAG_FAULTED : 0) |
            (this.exactLayout ? FLAG_EXACT_LAYOUT : 0) |
            (internCount > 0 ? FLAG_HAS_NEW_STRINGS : 0);
        out[HDR_INTERN_COUNT] = internCount;
        out[HDR_VOLATILE_COUNT] = volatileCount;
        out[HDR_TEXT_BYTES] = this.textUsed;
        out[HDR_OP_START] = opStart;
        out[HDR_OP_END] = opEnd;

        let cursor = HDR_SLOTS;
        for (let i = 0; i < this.newInterns.length; i++) out[cursor++] = this.newInterns[i];
        for (let i = 0; i < this.volatiles.length; i++) out[cursor++] = this.volatiles[i];

        out.set(this.ops.subarray(0, this.opsUsed), opStart);

        this.used = opEnd;
        return this;
    }

    // ------------------------------------------------------------------------------------------ growth

    _allocOps(slots) {
        const buffer = new ArrayBuffer(slots * 4);
        this.ops = new Int32Array(buffer);
        this.opsF32 = new Float32Array(buffer);
    }

    _ensureOps(need) {
        if (need <= this.ops.length) return;

        if (need > this.maxSlots) {
            this.overflowed = true;
            const tail = this.openScopes.slice(-8);
            throw new NowUIAuthorError(
                'NowUI: the draw function emitted more than ' + this.maxSlots.toLocaleString('en-US') +
                ' command slots; this is almost always an unbounded loop or a recursive component. ' +
                'The last ' + tail.length + ' scope keys were: ' + (tail.length ? tail.join(', ') : '<none>') + '.');
        }

        let size = this.ops.length;
        while (size < need) size *= 2;
        if (size > this.maxSlots) size = this.maxSlots;

        const buffer = new ArrayBuffer(size * 4);
        const grown = new Int32Array(buffer);
        grown.set(this.ops.subarray(0, this.opsUsed));
        this.ops = grown;
        this.opsF32 = new Float32Array(buffer);
    }

    _ensureOut(need) {
        if (need <= this.out.length) return;
        let size = this.out.length;
        while (size < need) size *= 2;
        this.out = new Int32Array(size);
    }

    _ensureText(need) {
        if (need <= this.u8.length) return;
        let size = this.u8.length;
        while (size < need) size *= 2;
        const grown = new Uint8Array(size);
        grown.set(this.u8.subarray(0, this.textUsed));
        this.u8 = grown;
    }
}
