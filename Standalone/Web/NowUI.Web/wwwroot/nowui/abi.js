// W2 - the ABI, JavaScript half. Docs/Standalone/M3-Spec.md section 5.2 and 5.3.
//
// This file is the JavaScript twin of Standalone/NowUI.Bridge/Abi.cs. Both build the same opcode table from the
// same signature list, and both fold that table into the same 32-bit surface hash. That hash rides in header slot 1
// of every frame, and the managed validator refuses a frame whose hash it does not recognise (gate G3). So the two
// files cannot drift silently: they drift into a refused frame naming both hashes, on the first frame.
//
// Nothing here allocates per frame. The table is built once at module load.

// ------------------------------------------------------------------------------------------------ header

/// Header magic, slot 0. The literal is section 5.2's, used verbatim.
///
/// Note for anyone reading the bytes in a hex dump: section 5.2 annotates 0x424F574E as 'NOWB', which it is not -
/// little-endian, 0x424F574E is the bytes 4E 57 4F 42, "NWOB". The constant is what matters and both sides use the
/// spec's number; the mnemonic in the spec's comment is wrong and the value is not.
export const MAGIC = 0x424f574e;

/// Fixed header slots, before the intern and volatile tables.
export const HDR_MAGIC = 0;
export const HDR_SURFACE_HASH = 1;
export const HDR_FRAME_FLAGS = 2;
export const HDR_INTERN_COUNT = 3;
export const HDR_VOLATILE_COUNT = 4;
export const HDR_TEXT_BYTES = 5;
export const HDR_OP_START = 6;
export const HDR_OP_END = 7;
export const HDR_SLOTS = 8;

/// frameFlags bits (header slot 2).
export const FLAG_FAULTED = 1 << 0;
export const FLAG_EXACT_LAYOUT = 1 << 1;
export const FLAG_HAS_NEW_STRINGS = 1 << 2;

/// The only negative value `record` returns. Section 5.1.
export const NEED_MORE = -1;

/// Section 5.1's hard cap: 4 M slots is 16 MB of ops.
export const MAX_SLOTS = 4 * 1024 * 1024;

/// Section 5.4's intern ceiling.
export const DEFAULT_MAX_STRINGS = 65536;

// ------------------------------------------------------------------------------------------------ opcodes

/// Opcodes 0-15 are reserved for structural ops and are FIXED (section 5.2). Only these five are defined; 5-15 are
/// held in reserve so a later structural op does not have to displace a content-hashed one.
export const OP_INVALID = 0;
export const OP_SCOPE_CLOSE = 1;
export const OP_CALLBACK_BEGIN = 2;
export const OP_CALLBACK_END = 3;
export const OP_NOP = 4;

/// The first opcode a content hash may take.
export const FIRST_HASHED_OP = 16;

/// Every argument kind of section 5.3, with the number of slots it occupies. Only the kinds W2 and W3 emit are
/// exercised; the rest are declared so the slot arithmetic is written down once, in one place, for W4 onwards.
export const KIND_SLOTS = {
    i32: 1,
    f32: 1,
    bool: 1,
    enum: 1,
    str: 1,
    key: 1,
    seg: 1,
    rid: 1,
    color: 1,
    vec2: 2,
    rect: 4,
    vec4: 4,

    // W6. A 64-bit value as two slots, low word first. Used by ui.datePicker, whose value is a Unix epoch in
    // milliseconds and does not survive an f32.
    i64: 2,

    // strlist and opts are variable-width. The number here is their MINIMUM: one slot for the count (strlist) or
    // the bitmask (opts), with the payload following. `record.slots` is therefore the op's size with an empty
    // list and an empty options object, and a caller with either passes the real total to `W.op(record, slots)`.
    strlist: 1,
    opts: 1,
};

/// The op table. `sig` is what section 5.2 hashes: declaring type, member name, parameter type list. It is the
/// NowUI member the op replays into, NOT the JavaScript name, because the JavaScript name is a surface decision and
/// the C# member is the thing whose drift the hash must catch.
///
/// W2 defines one op (TEXT). W3 adds the three identity-carrying ones. W4-W6 fill the rest in; the shape of an
/// entry does not change.
const SPEC = [
    // --- W2 -----------------------------------------------------------------------------------------------------
    { name: 'TEXT', sig: 'NowUI.NowLayout.Label(System.String)', args: ['str'] },

    // --- W3 -----------------------------------------------------------------------------------------------------
    // A nested vertical container. `rid` is the recorder's own key (section 3.4) and is echoed, never computed, by
    // the managed side; `seg` is the identity segment (section 3.3) - an intern handle when the scope is keyed, a
    // negative anonymous ordinal when it is not.
    { name: 'COLUMN', sig: 'NowUI.NowLayout.Column(NowUI.NowId)', args: ['rid', 'seg'] },
    { name: 'ROW', sig: 'NowUI.NowLayout.Row(NowUI.NowId)', args: ['rid', 'seg'] },
    // One item of a keyed list: the list namespace and the item key, both intern handles.
    { name: 'LIST_ITEM', sig: 'NowUI.NowControls.KeyedItemIn(NowUI.NowId,NowUI.NowId)', args: ['rid', 'seg', 'seg'] },
    // A control, so that SetId(new NowId(seg)) is exercised by something that actually draws (invariant I1).
    { name: 'BUTTON', sig: 'NowUI.NowLayout.Button(System.String,NowUI.NowId)', args: ['rid', 'seg', 'str'] },

    // --- W4 -----------------------------------------------------------------------------------------------------
    // A VALUE control, and the op section 6 is written against: it carries the caller's string out and its
    // post-draw string comes back in the result table. `value` is the reconciled string (section 6.4) and
    // `placeholder` is the grey text shown when it is empty.
    //
    // The signature is the CONSUMER, Draw(ref string), not the factory - see Abi.cs for why.
    { name: 'TEXT_FIELD', sig: 'NowUI.NowTextField.Draw(System.String&)', args: ['rid', 'seg', 'str', 'str'] },

    // --- W6 -----------------------------------------------------------------------------------------------------
    //
    // THE MODIFIER OP, and why the six ops above are untouched.
    //
    // Section 2.7 gives every tier-1 function one options object, and section 5.3 encodes it as a bitmask slot
    // followed by the named fields. The obvious place for it is a trailing `opts` argument on every op - and the
    // spec's own worked example shows exactly that: [OP_BUTTON | 5<<16, rid, seg, sid('Add'), 0x08, 3].
    //
    // It is not done that way here, for a reason about this repository rather than about the design: widening
    // TEXT, COLUMN, ROW, BUTTON and TEXT_FIELD changes their wire shape, and those five are W2 to W5's acceptance
    // evidence. Standalone/NowUI.Bridge.Tests/js/run.mjs asserts "TEXT carries one slot" and "the whole hello
    // frame is 12 slots"; surface.stub.js emits them at their current widths. Rewriting four units' evidence to
    // add an argument that is empty in every one of those frames is a bad trade.
    //
    // OPTS carries the bitmask and the payload as its own op, immediately BEFORE the op it modifies. The decoder
    // holds it in one field, and the next container or control consumes and clears it. The properties that matter:
    //
    //   * an op with no options emits nothing at all, so section 5.3's at-rest size estimate is unchanged and the
    //     six existing ops are byte-identical to what W2-W5 recorded;
    //   * argSlots still lets a decoder that does not know the op skip it;
    //   * it is idempotent, which is what section 5.6 requires: the pending field is reset at the top of every
    //     decode pass, exactly like the scope depth, and is cleared by the op that reads it.
    //
    // W8's generator emits the trailing-argument form the spec describes, from surface.json, at the same time as
    // it deletes surface.stub.js and regenerates run.mjs's expectations. This is the shape that gets there without
    // invalidating the four units underneath it.
    { name: 'OPTS', sig: 'NowUI.Bridge.PendingOptions(System.Int32)', args: ['opts'] },

    // Scopes (section 2.2). CARD and SCROLL open a layout container; IDSCOPE opens an identity scope and no box,
    // which is what ui.when needs - "no layout group, so the body's children flow in the parent".
    { name: 'CARD', sig: 'NowUI.NowLayout.Column(NowUI.NowId)+NowUI.Now.Rectangle(NowUI.NowRect)', args: ['rid', 'seg'] },
    { name: 'IDSCOPE', sig: 'NowUI.NowControls.IdScope(System.Int32)', args: ['rid', 'seg'] },
    { name: 'SCROLL', sig: 'NowUI.NowLayout.ScrollView(NowUI.NowId)', args: ['rid', 'seg'] },
    { name: 'FOLDOUT', sig: 'NowUI.NowFoldout.Draw(System.Boolean&)', args: ['rid', 'seg', 'bool', 'str'] },

    // Drawings (section 2.3). None is keyed; heading, subheading and caption are TEXT with a textStyle option.
    { name: 'SPACE', sig: 'NowUI.NowLayout.Space(System.Single)', args: ['f32'] },
    { name: 'FLEX_SPACE', sig: 'NowUI.NowLayout.FlexibleSpace(System.Single)', args: ['f32'] },
    { name: 'RULE', sig: 'NowUI.NowLayout.Row()+NowUI.Now.Rectangle(NowUI.NowRect)', args: [] },
    { name: 'BADGE', sig: 'NowUI.NowLayout.Badge(System.String)', args: ['str'] },

    // Actions (section 2.4).
    { name: 'SELECTABLE', sig: 'NowUI.NowSelectableRow.Draw()', args: ['rid', 'seg', 'bool', 'str'] },
    { name: 'CHIP', sig: 'NowUI.NowChip.Draw(System.Boolean&)', args: ['rid', 'seg', 'str', 'bool', 'bool'] },

    // Values (section 2.5). Every one carries the caller's value out and brings the post-draw value back in the
    // result table, under section 6.4's reconciliation rule.
    { name: 'TEXT_AREA', sig: 'NowUI.NowTextArea.Draw(System.String&)', args: ['rid', 'seg', 'str', 'str'] },
    { name: 'NUMBER_FIELD', sig: 'NowUI.NowTextField.Draw(System.Single&,System.String)', args: ['rid', 'seg', 'f32', 'str'] },
    { name: 'CHECKBOX', sig: 'NowUI.NowCheckbox.Draw(System.Boolean&)', args: ['rid', 'seg', 'bool', 'str'] },
    { name: 'SWITCH', sig: 'NowUI.NowSwitch.Draw(System.Boolean&)', args: ['rid', 'seg', 'bool', 'str'] },
    { name: 'RADIO_ITEM', sig: 'NowUI.NowRadio.Draw()', args: ['rid', 'seg', 'bool', 'str'] },
    { name: 'SLIDER', sig: 'NowUI.NowSlider.Draw(System.Single&)', args: ['rid', 'seg', 'f32', 'f32', 'f32'] },
    { name: 'INT_SLIDER', sig: 'NowUI.NowSlider.Draw(System.Int32&)', args: ['rid', 'seg', 'i32', 'f32', 'f32'] },
    { name: 'DROPDOWN', sig: 'NowUI.NowDropdown.Draw(System.Int32&)', args: ['rid', 'seg', 'i32', 'strlist'] },
    { name: 'COMBO', sig: 'NowUI.NowComboBox.Draw(System.Int32&)', args: ['rid', 'seg', 'i32', 'strlist'] },
    { name: 'COLOR_FIELD', sig: 'NowUI.NowColorPicker.Draw(UnityEngine.Color&)', args: ['rid', 'seg', 'color'] },
    { name: 'DATE_PICKER', sig: 'NowUI.NowDatePicker.Draw(System.DateTime&)', args: ['rid', 'seg', 'i64'] },
    { name: 'TIME_PICKER', sig: 'NowUI.NowTimePicker.Draw(System.TimeSpan&)', args: ['rid', 'seg', 'i32'] },
    { name: 'TABS', sig: 'NowUI.NowTabBar.Draw(System.Int32&)', args: ['rid', 'seg', 'i32', 'strlist'] },

    // Feedback (section 2.6).
    { name: 'PROGRESS', sig: 'NowUI.NowProgressBar.Draw()', args: ['f32'] },
];

// ------------------------------------------------------------------------------------------------ options
//
// Section 2.7's options object, on the wire. One bitmask slot names which fields follow; the fields follow in
// ASCENDING BIT ORDER, which is the only ordering rule either half needs and the reason neither carries a field
// index. Abi.cs holds the identical list and Replay.Controls.cs decodes it in the same order.
//
// A flag-only option (`rect`, `disabled`) occupies a bit and no payload slot.

export const OPT_WIDTH = 1 << 0;        // f32
export const OPT_HEIGHT = 1 << 1;       // f32
export const OPT_MIN_WIDTH = 1 << 2;    // f32
export const OPT_MAX_WIDTH = 1 << 3;    // f32
export const OPT_MIN_HEIGHT = 1 << 4;   // f32
export const OPT_MAX_HEIGHT = 1 << 5;   // f32
export const OPT_GROW = 1 << 6;         // f32
export const OPT_GAP = 1 << 7;          // f32
export const OPT_PADDING = 1 << 8;      // 4 x f32, left top right bottom
export const OPT_ALIGN = 1 << 9;        // enum NowLayoutAlign
export const OPT_JUSTIFY = 1 << 10;     // enum NowLayoutJustify
export const OPT_STYLE = 1 << 11;       // enum NowRectangleStyle
export const OPT_TEXT_STYLE = 1 << 12;  // enum NowTextStyle
export const OPT_STEP = 1 << 13;        // f32
export const OPT_RECT = 1 << 14;        // flag only
export const OPT_DISABLED = 1 << 15;    // flag only

/// Payload slots per option bit, in ascending bit order. The two zeroes are the flag-only options.
export const OPT_SLOTS = [1, 1, 1, 1, 1, 1, 1, 1, 4, 1, 1, 1, 1, 1, 0, 0];

/// Section 2.7's enum name tables. The values are the C# enums' integer values, read out of
/// Assets/NowUI/Runtime/NowThemeStyles.cs and NowLayout.cs rather than assumed.
///
/// NowLayoutAlign has THREE members - Start, Center, End (NowLayout.cs:8-13). Section 2.7 lists a fourth,
/// 'stretch', for `align`. It does not exist, and the source wins: 'stretch' is rejected by name in nowui.js
/// rather than silently coerced to one of the three.
export const ALIGN = { start: 0, center: 1, end: 2 };
export const JUSTIFY = { start: 0, center: 1, end: 2, between: 3 };
export const RECT_STYLE = {
    surface: 0, muted: 1, outline: 2, accent: 3, elevated: 4, accentSoft: 5, danger: 6, ghost: 7,
};
export const TEXT_STYLE = {
    title: 0, body: 1, muted: 2, button: 3, display: 4, heading: 5, subheading: 6, bodyStrong: 7,
    label: 8, caption: 9,
};

/// FNV-1a, 32-bit, over the UTF-16 code units of `text`. Chosen because it is four lines in both languages and
/// needs no table; the hash only has to be stable and well-mixed, not cryptographic.
export function fnv1a32(text, seed = 0x811c9dc5) {
    let h = seed >>> 0;
    for (let i = 0; i < text.length; i++) {
        h = (h ^ text.charCodeAt(i)) >>> 0;
        h = Math.imul(h, 0x01000193) >>> 0;
    }
    return h >>> 0;
}

/// Section 5.2: "the low 16 bits of a hash of declaringType + memberName + parameterTypeList", moved above the
/// reserved structural range. `salt` is the checked-in collision resolver; it is 0 for every entry today, and a
/// collision at build time is resolved by bumping one entry's salt rather than by renumbering anything.
export function opcodeFor(sig, salt = 0) {
    const h = fnv1a32(salt === 0 ? sig : sig + '#' + salt);
    return FIRST_HASHED_OP + ((h & 0xffff) % (0x10000 - FIRST_HASHED_OP));
}

function buildTable() {
    const byName = Object.create(null);
    const byOpcode = new Map();

    for (const entry of SPEC) {
        const salt = entry.salt || 0;
        const opcode = opcodeFor(entry.sig, salt);

        if (byOpcode.has(opcode)) {
            // Detected at load, exactly as section 5.2 requires, rather than discovered as decoded garbage.
            throw new Error(
                'NowUI ABI: opcode collision at ' + opcode + ' between "' + byOpcode.get(opcode).sig +
                '" and "' + entry.sig + '". Resolve it by adding a salt to one of the two entries in abi.js and ' +
                'the identical salt in Abi.cs.');
        }

        let slots = 0;
        for (const kind of entry.args) {
            const width = KIND_SLOTS[kind];
            if (width === undefined) throw new Error('NowUI ABI: unknown argument kind "' + kind + '".');
            slots += width;
        }

        const record = { name: entry.name, sig: entry.sig, salt, opcode, args: entry.args, slots };
        byOpcode.set(opcode, record);
        byName[entry.name] = record;
    }

    return { byName, byOpcode };
}

const TABLE = buildTable();

export const OPS = TABLE.byName;
export const OPS_BY_OPCODE = TABLE.byOpcode;

/// The manifest hash of section 7.4 G3: one line per op, sorted by name so the order the entries were written in
/// cannot change it, folded with FNV-1a. Abi.cs computes the same string from the same list.
export function surfaceHash() {
    const lines = Object.keys(OPS)
        .sort()
        .map(name => name + '=' + OPS[name].opcode + ':' + OPS[name].sig + ':' + OPS[name].args.join(','));
    return fnv1a32(lines.join('\n')) | 0;
}

export const SURFACE_HASH = surfaceHash();
