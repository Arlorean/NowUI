// W5 - the result table, JavaScript half. Docs/Standalone/M3-Spec.md section 6.
//
// This is the answer half of the bridge: what a JavaScript author reads synchronously when they write
//
//     if (ui.button('Add')) addPerson();
//     state.name = ui.textField('name', state.name);
//
// and it is one frame old (section 6.2). The table it reads was written by the LAST replay, rode back inside the
// same `record` call this frame's ops go out on, and describes controls that were drawn then.
//
// Four behaviours live here, and three of the four are the kind that fail silently:
//
//   1. THE GENERATION STAMP. A record is "present" only if it was written for the frame currently being loaded.
//      `results[rid]` is an array index and a comparison - no clearing pass, no allocation - and a control that
//      was NOT drawn last frame simply has a stale stamp, so it reads as absent (section 6.1). That is what stops
//      the table serving a `clicked` for a button that stopped existing three seconds ago.
//
//   2. ONE-SHOT LATCHING (section 6.3). `clicked`, `submitted`, `dragStarted`, `dragEnded` and `cancelled` are
//      events, not states. The loader marks them unread; the first read returns true and clears the bit. So a
//      control read twice in one frame fires once, and an event can never be delivered late, because the record
//      is replaced wholesale on the next load.
//
//   3. THE VALUE RECONCILIATION RULE (section 6.4). The rule that is worth reading the file for; it has its own
//      comment on `value` below, because the plausible version of it drops a keystroke.
//
//   4. THE MISSED READ (section 6.4, last paragraph). A read for a rid with no record returns the identity
//      default - false for an event, the caller's own value for a value - and under `?nowui=debug` a control that
//      has been drawn for three consecutive frames without ever producing a record is reported once. That is the
//      signature of a control the replay is silently not reaching, and it is otherwise indistinguishable from a
//      button nobody clicked.
//
// Nothing here touches the DOM or the wasm runtime, so Standalone/NowUI.Bridge.Tests/js drives it under node.

// ------------------------------------------------------------------------------------------------ the wire
//
// The twin of Standalone/NowUI.Bridge/Results.cs. Both halves have to agree about the record layout, and unlike
// the op stream there is no surface hash over it - the table is written and read by two files that ship together
// in the same wasm build, and a mismatch shows up as a decoded record with a nonsense kind, which `load` refuses.

/// Section 6.1's header, in front of the first record. Two slots, and Results.cs says why they exist.
export const HDR_RECORD_COUNT = 0;
export const HDR_TEXT_BYTES = 1;
export const HDR_SLOTS = 2;

/// Section 6.1's value kinds.
export const KIND_NONE = 0;
export const KIND_F32 = 1;
export const KIND_I32 = 2;
export const KIND_BOOL = 3;
export const KIND_STR = 4;
export const KIND_COLOR = 5;

/// W6. Two slots, low word first. ui.datePicker's value is a Unix epoch in milliseconds; an f32 carries 24 bits
/// of mantissa, so every date in the picker's range would land on the same handful of representable days.
export const KIND_I64 = 6;

/// Section 6.1's flags, as they sit in the high 24 bits of the record's second slot.
export const F_CLICKED = 1 << 0;
export const F_SUBMITTED = 1 << 1;
export const F_DRAG_STARTED = 1 << 2;
export const F_DRAG_ENDED = 1 << 3;
export const F_CANCELLED = 1 << 4;
export const F_CHANGED = 1 << 5;
export const F_FOCUSED = 1 << 6;
export const F_HOVERED = 1 << 7;
export const F_PRESSED = 1 << 8;
export const F_HELD = 1 << 9;
export const F_RELEASED = 1 << 10;
export const F_DRAGGING = 1 << 11;
export const F_HAS_RECT = 1 << 12;
export const F_PRESENT = 1 << 13;

/// How far the flag field is shifted up in that slot; the low 8 bits are the value kind.
export const FLAGS_SHIFT = 8;
export const KIND_MASK = 0xff;

/// Section 6.3: the flags that are events rather than states, and are therefore consumed by the first read.
export const ONE_SHOT = F_CLICKED | F_SUBMITTED | F_DRAG_STARTED | F_DRAG_ENDED | F_CANCELLED;

/// Section 6.4: how many consecutive drawn-but-never-recorded frames before a rid is reported under debug.
const MISS_REPORT_AT = 3;

const decoder = new TextDecoder();

export class Results {
    constructor(options = {}) {
        this.frame = -1;
        this.debug = options.debug === true;
        this.onReport = options.onReport || defaultReport;

        // rid-indexed, and grown together. Parallel typed arrays rather than an object per rid: a record is read
        // on the hot path of every control in the frame, and `this.stamp[rid] === this.frame` is the whole of the
        // presence test.
        this._grow(64);

        // Section 6.4's memory: what this side RETURNED for each rid last frame. Not what the control produced,
        // and not what the author passed - what the author was told. See `value`.
        this.lastSent = [];

        // Diagnostics. `records` is the last load's size, and `reports` is what the missed-read check said.
        this.records = 0;
        this.reports = [];
    }

    // -------------------------------------------------------------------------------------------- loading

    /// Reads one frame's table. `slots` is the Int32Array `record` sliced out of the managed results view (or null
    /// before the first replay has run), `text` the UTF-8 bytes its string values point into, `frame` the frame
    /// number that is about to be drawn.
    ///
    /// Called exactly once per frame, BEFORE the author's draw function - and not at all on a NEED_MORE retry,
    /// because a retry re-uses the bytes already recorded and must not consume the one-shot events a second time.
    load(slots, text, frame) {
        this.frame = frame;
        this.records = 0;

        if (!slots || slots.length < HDR_SLOTS) return;   // no table yet: the first frame of the session

        const count = slots[HDR_RECORD_COUNT];
        if (count <= 0) return;

        // The f32 view over the same bytes. `slice()` hands back a copy with its own buffer, so this is a view
        // over JavaScript's own memory and stays valid for as long as the load does.
        const f32 = new Float32Array(slots.buffer, slots.byteOffset, slots.length);

        let i = HDR_SLOTS;

        for (let n = 0; n < count; n++) {
            if (i + 2 > slots.length) {
                // A truncated table. Everything read so far is good and is kept; the rest is dropped rather than
                // decoded out of whatever follows it in the buffer.
                this._report('NowUI: the result table declared ' + count + ' records and ran out of slots after ' +
                    n + '. This is a bridge bug.');
                break;
            }

            const rid = slots[i++];
            const head = slots[i++];
            const kind = head & KIND_MASK;
            const flags = head >>> FLAGS_SHIFT;

            if (rid < 0 || kind > KIND_I64) {
                this._report('NowUI: the result table holds a record with rid ' + rid + ' and value kind ' + kind +
                    ', which this build does not know. This is a bridge bug.');
                break;
            }

            if (rid >= this.stamp.length) this._grow(rid + 1);

            this.stamp[rid] = frame;
            this.flags[rid] = flags;
            this.unread[rid] = flags & ONE_SHOT;
            this.kind[rid] = kind;
            this.num[rid] = 0;
            this.str[rid] = null;
            this.rect[rid] = null;
            this.seen[rid] = 1;
            this.missed[rid] = 0;

            if (kind === KIND_STR) {
                const offset = slots[i++];
                const length = slots[i++];
                this.str[rid] = length > 0 && text ? decoder.decode(text.subarray(offset, offset + length)) : '';
            } else if (kind === KIND_F32) {
                this.num[rid] = f32[i++];
            } else if (kind === KIND_I32) {
                this.num[rid] = slots[i++];
            } else if (kind === KIND_BOOL) {
                this.num[rid] = slots[i++] !== 0 ? 1 : 0;
            } else if (kind === KIND_COLOR) {
                this.num[rid] = slots[i++] >>> 0;
            } else if (kind === KIND_I64) {
                // Reassembled into a double rather than a BigInt: the values this kind carries are epoch
                // milliseconds, which stay exact to 2^53 - a quarter of a million years either side of 1970 -
                // and a BigInt would make `state.when = ui.datePicker(...)` return something an author cannot
                // hand to `new Date()` without a cast.
                const low = slots[i] >>> 0;
                const high = slots[i + 1];
                i += 2;
                this.num[rid] = high * 4294967296 + low;
            }

            if ((flags & F_HAS_RECT) !== 0) {
                this.rect[rid] = [f32[i], f32[i + 1], f32[i + 2], f32[i + 3]];
                i += 4;
            }

            this.records++;
        }
    }

    // -------------------------------------------------------------------------------------------- reading

    /// True when this rid's record was written for the frame being drawn.
    present(rid) {
        return rid < this.stamp.length && this.stamp[rid] === this.frame;
    }

    /// Section 6.3. Reads a one-shot event and consumes it, so a second read this frame is false.
    ///
    /// Every action control in the surface goes through here, which is why the identity default is in one place:
    /// a rid with no record returns false. A button that has never been drawn, a button drawn for the first time
    /// this frame, and a button nobody clicked are all indistinguishable here - and all three should be false.
    event(rid, bit) {
        if (!this.present(rid)) { this._miss(rid); return false; }

        const hit = (this.unread[rid] & bit) !== 0;
        if (hit) this.unread[rid] &= ~bit;
        return hit;
    }

    /// A state flag - hovered, focused, changed. Read as often as the author likes; never consumed.
    state(rid, bit) {
        if (!this.present(rid)) { this._miss(rid); return false; }
        return (this.flags[rid] & bit) !== 0;
    }

    /// The post-draw value the last replay recorded for this rid, without the reconciliation of `value`. Used by
    /// the handler prologue in nowui.js, which has to hand a submitted field's text to onSubmit BEFORE the author's
    /// draw function runs and therefore has no incoming value to reconcile against.
    current(rid) {
        return this.present(rid) ? this._read(rid) : undefined;
    }

    /// The rect of a control drawn with a rect in its record, or null. Section 6.1: rects are opt-in.
    rectOf(rid) {
        return this.present(rid) ? this.rect[rid] : null;
    }

    // ------------------------------------------------------------------------------------ section 6.4

    /// THE VALUE RECONCILIATION RULE. Every value control's return goes through here, and the return value is
    /// ALSO what the caller must emit - resolving before emitting is half of what makes this correct.
    ///
    /// The rule: the recorder remembers what it RETURNED for each rid last frame. If the incoming value differs,
    /// the author wrote it and the author wins. If it is identical, the author is echoing, and the freshest known
    /// value wins.
    ///
    /// Why not the obvious version. Design B emitted the caller's value and then consulted the result:
    ///
    ///     emit(OP_TEXT_FIELD, rid, value);          // WRONG
    ///     const r = results.get(rid);
    ///     return (r && r.changed) ? r.value : value;
    ///
    /// Trace a keystroke. Frame N-1's replay produced "abc" from "ab" and set `changed`. At frame N the author's
    /// state.name is still "ab" - it only receives "abc" from this call's RETURN. So the op carries "ab", and
    /// frame N's replay calls Draw(ref text) with "ab". The caller's string is authoritative: NowTextField stores
    /// no text of its own and clamps its edit state to whatever it is handed (NowTextField.cs:1398,
    /// NowTextEdit.Clamp(ref state, text)). The character is reverted, one frame after it was typed.
    ///
    /// Resolving before emitting fixes that and introduces the opposite bug on its own: a programmatic write -
    /// `state.name = ''` in a Clear handler - would be overwritten by the last frame's result forever. The
    /// `lastSent` comparison is what tells the two apart, and it is a comparison against what this side returned,
    /// not against what the control produced.
    ///
    /// Four properties, each of which the two-line version lacks:
    ///   * a keystroke is never reverted, because the emitted value is the resolved one;
    ///   * a programmatic write always lands;
    ///   * the author never observes a stale value - their own, or a fresher one;
    ///   * it is bit-identical when nothing changed: no drift, no snapping, no re-clamping.
    value(rid, incoming) {
        if (rid >= this.stamp.length) this._grow(rid + 1);

        const prev = this.lastSent[rid];
        let v = incoming;

        if (prev !== undefined && Object.is(incoming, prev)) {
            if (this.present(rid)) v = this._read(rid);
            else this._miss(rid);
        }

        this.lastSent[rid] = v;
        return v;
    }

    /// The post-draw value the last replay recorded for this rid, in the kind the record declared.
    _read(rid) {
        switch (this.kind[rid]) {
            case KIND_STR: return this.str[rid];
            case KIND_BOOL: return this.num[rid] !== 0;
            case KIND_F32:
            case KIND_I32:
            case KIND_COLOR:
            case KIND_I64: return this.num[rid];
            default: return undefined;
        }
    }

    // ---------------------------------------------------------------------------------- the missed read

    /// Section 6.4's last paragraph. Counts reads that could not be served and reports the shape that matters:
    /// a control drawn for three consecutive frames that has NEVER produced a record. A control drawn for the
    /// first time, and one whose key just changed, both miss once and then start being served - so neither is
    /// reported, which is the whole reason the check is "three consecutive AND never seen" rather than "missed".
    _miss(rid) {
        if (!this.debug || rid >= this.stamp.length) return;

        this.missed[rid]++;

        if (this.missed[rid] >= MISS_REPORT_AT && this.seen[rid] === 0 && this.reported[rid] === 0) {
            this.reported[rid] = 1;
            this._report(
                'NowUI: rid ' + rid + ' has been drawn for ' + this.missed[rid] + ' consecutive frames and the ' +
                'replay has never produced a result for it. Every read of it returns the identity default - false ' +
                'for an event, your own value for a value - so this control looks like one nobody has touched.\n' +
                'That is the signature of a control the replay is not reaching: an op with no case in the decoder, ' +
                'or a control drawn inside a scope the replay closed early.');
        }
    }

    _report(message) {
        this.reports.push(message);
        this.onReport(message);
    }

    // -------------------------------------------------------------------------------------------- growth

    _grow(need) {
        let size = this.stamp === undefined ? 64 : this.stamp.length;
        while (size < need) size *= 2;

        const stamp = new Int32Array(size).fill(-1);
        const flags = new Int32Array(size);
        const unread = new Int32Array(size);
        const kind = new Int32Array(size);
        const num = new Float64Array(size);
        const seen = new Uint8Array(size);
        const missed = new Int32Array(size);
        const reported = new Uint8Array(size);

        if (this.stamp !== undefined) {
            stamp.set(this.stamp);
            flags.set(this.flags);
            unread.set(this.unread);
            kind.set(this.kind);
            num.set(this.num);
            seen.set(this.seen);
            missed.set(this.missed);
            reported.set(this.reported);
        }

        this.stamp = stamp;
        this.flags = flags;
        this.unread = unread;
        this.kind = kind;
        this.num = num;
        this.seen = seen;
        this.missed = missed;
        this.reported = reported;

        if (this.str === undefined) { this.str = []; this.rect = []; }
    }
}

function defaultReport(message) {
    if (typeof console !== 'undefined' && console.warn) console.warn(message);
}
