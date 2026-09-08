// W5 - the result table, managed half. Docs/Standalone/M3-Spec.md section 6.
//
// One record per interactive control drawn in the last REAL replay pass, in draw order, written into a buffer that
// rides back to JavaScript inside the same `record` call the ops go out on (section 6.1) - so the whole answer half
// of the bridge costs no boundary crossing of its own.
//
// Three properties this file exists to hold, each of which is silent when it breaks:
//
//   1. MEASURE-PASS SUPPRESSION (section 5.6). Under exactLayout the buffer is decoded twice and the FIRST pass is
//      passive: NowLayout.isMeasurePass is true, NowInput.isPassive is true, and every interaction result the
//      controls hand back is inert. A writer that recorded that pass would overwrite real results with zeroes,
//      and it would do it on the pass that runs FIRST, so the damage would look like "the click was never seen".
//      Write() refuses while either flag is set, and counts what it refused, so the suppression is a measured
//      number in a test rather than a claim in a comment.
//
//   2. CONTROLS NOT DRAWN PRODUCE NO RECORD (section 6.1). The cursor is reset at the start of every pass and the
//      table is sealed at the end of the frame, so the table is exactly what this frame drew - never a merge with
//      what the last one did. That is what stops a `clicked` being delivered for a button that stopped existing.
//
//   3. THE HEADER. Section 6.1's layout starts at the first record; this adds two slots in front of it, holding
//      the record count and the byte count of the string section. The reason is the ABI, not the table: `record`
//      takes ONE resultSlots argument (section 5.1) and the strings live in a second view whose used length would
//      otherwise be unknowable to JavaScript - it would have to slice() the whole text buffer every frame, which
//      grows without bound as soon as one text field holds a long string. Two slots buy that back. The records
//      themselves are section 6.1's, unchanged, and start at HeaderSlots.
//
// What this file does NOT do is decide which flags a control reports. That is the decoder's job, because it is the
// decoder that holds the value the control's consumer returned - and for several tier-1 controls that value is one
// bool. See BridgeReplay for the honest note about what NowButton.Draw() can and cannot tell the table.

using System;
using System.Text;
using NowUI;

namespace NowUI.Bridge
{
    /// <summary>Section 6.1's value kinds, with the number of slots each occupies after the two-slot record head.</summary>
    public enum BridgeValueKind
    {
        /// <summary>No value slots. An action control: the flags are the whole answer.</summary>
        None = 0,

        /// <summary>One slot, read through the Float32 view.</summary>
        F32 = 1,

        /// <summary>One slot.</summary>
        I32 = 2,

        /// <summary>One slot, 0 or 1.</summary>
        Bool = 3,

        /// <summary>Two slots: byteOffset and byteLength into the results text buffer.</summary>
        Str = 4,

        /// <summary>One slot, RGBA8 packed.</summary>
        Color = 5,

        /// <summary>
        /// W6. Two slots, low word first. <c>ui.datePicker</c>'s value is a Unix epoch in milliseconds and does
        /// not survive an f32 - 2026 is about 1.77e12, and a float carries 24 bits of mantissa, so the nearest
        /// representable neighbours are a quarter of a million milliseconds apart. Every date in the picker's
        /// range would snap to the same handful of days.
        /// </summary>
        I64 = 6,
    }

    /// <summary>
    /// Section 6.1's flag set. Twenty-four bits are available and fourteen are named; the rest are reserved so a
    /// later flag does not renumber one JavaScript already reads.
    /// </summary>
    [Flags]
    public enum BridgeFlags
    {
        None = 0,

        // --- one-shot events (section 6.3): the loader marks them unread and the first read clears them --------
        Clicked = 1 << 0,
        Submitted = 1 << 1,
        DragStarted = 1 << 2,
        DragEnded = 1 << 3,
        Cancelled = 1 << 4,

        // --- states -------------------------------------------------------------------------------------------
        Changed = 1 << 5,
        Focused = 1 << 6,
        Hovered = 1 << 7,
        Pressed = 1 << 8,
        Held = 1 << 9,
        Released = 1 << 10,
        Dragging = 1 << 11,

        // --- structure ----------------------------------------------------------------------------------------
        /// <summary>Four slots of rect follow the value slots.</summary>
        HasRect = 1 << 12,

        /// <summary>The control was drawn. Always set on a written record; it is what a reader tests.</summary>
        Present = 1 << 13,
    }

    /// <summary>The result table: record layout, the write path, and measure-pass suppression.</summary>
    public sealed class BridgeResults
    {
        /// <summary>Slot 0 of the buffer: how many records follow the header.</summary>
        public const int HdrRecordCount = 0;

        /// <summary>Slot 1: how many bytes of the results text buffer this frame used.</summary>
        public const int HdrTextBytes = 1;

        /// <summary>The number of header slots in front of the first record.</summary>
        public const int HeaderSlots = 2;

        /// <summary>Section 6.1's second slot: flags in the high 24 bits, valueKind in the low 8.</summary>
        public const int ValueKindMask = 0xFF;

        /// <summary>How far the flag field is shifted up in the header slot.</summary>
        public const int FlagsShift = 8;

        // Sized so the 23 controls of section 1 (about 100 slots, section 6.1) fit without a growth.
        private const int InitialSlots = 256;
        private const int InitialTextBytes = 1024;

        private int[] m_Slots = new int[InitialSlots];
        private byte[] m_Text = new byte[InitialTextBytes];

        private int m_Cursor;
        private int m_TextCursor;
        private int m_Records;
        private int m_Sealed;
        private int m_Suppressed;

        /// <summary>The slot buffer, for the span handed to <c>record</c>. Re-read every frame: it grows.</summary>
        public int[] slots => m_Slots;

        /// <summary>The string bytes every <see cref="BridgeValueKind.Str"/> value points into.</summary>
        public byte[] text => m_Text;

        /// <summary>
        /// How many slots of <see cref="slots"/> the last sealed frame filled, header included. This is the
        /// <c>resultSlots</c> argument of section 5.1's <c>record</c>, and it is zero before the first frame has
        /// been replayed - which JavaScript reads as "no table", not as an error.
        /// </summary>
        public int sealedSlots => m_Sealed;

        /// <summary>How many records the last sealed frame wrote.</summary>
        public int recordCount { get; private set; }

        /// <summary>
        /// How many writes were refused because the pass was passive. Under <c>exactLayout</c> this is one per
        /// record per frame and under a single-pass frame it is zero; a test asserts exactly that, which is what
        /// makes "a result written during a measure pass never reaches JavaScript" a measurement.
        /// </summary>
        public int suppressedWrites => m_Suppressed;

        /// <summary>
        /// True while NowUI is measuring rather than drawing. Both halves matter and neither implies the other:
        /// <c>isMeasurePass</c> is NowLayout's own flag (NowLayout.cs:1256) and <c>isPassive</c> is the input
        /// system's (NowInput.cs:1317), and a control asked to draw while either is set reports nothing real.
        /// </summary>
        public static bool passive => NowLayout.isMeasurePass || NowInput.isPassive;

        // ------------------------------------------------------------------------------------------- the frame

        /// <summary>Starts a frame. Resets the counters the whole frame accumulates.</summary>
        public void BeginFrame()
        {
            m_Suppressed = 0;
            BeginPass();
        }

        /// <summary>
        /// Starts one replay pass. Resets the write cursor, so a second pass overwrites the first rather than
        /// appending to it - the property that makes the table "what this frame drew" rather than a merge.
        /// </summary>
        public void BeginPass()
        {
            m_Cursor = HeaderSlots;
            m_TextCursor = 0;
            m_Records = 0;
        }

        /// <summary>
        /// Closes the frame and writes the header. Called after the last pass; nothing crosses the boundary until
        /// it has run, so a frame that threw mid-replay hands JavaScript the previous table rather than half of a
        /// new one.
        /// </summary>
        public void Seal()
        {
            m_Slots[HdrRecordCount] = m_Records;
            m_Slots[HdrTextBytes] = m_TextCursor;
            m_Sealed = m_Cursor;
            recordCount = m_Records;
        }

        // ------------------------------------------------------------------------------------------- writing

        /// <summary>An action control: flags only, no value (section 6.1's <c>None</c> kind).</summary>
        public void WriteEvent(int rid, BridgeFlags flags)
        {
            Write(rid, flags, BridgeValueKind.None, 0, 0, null);
        }

        /// <summary>A boolean-valued control.</summary>
        public void WriteBool(int rid, BridgeFlags flags, bool value)
        {
            Write(rid, flags, BridgeValueKind.Bool, value ? 1 : 0, 0, null);
        }

        /// <summary>A float-valued control. The value is stored bit-for-bit and read through the Float32 view.</summary>
        public void WriteF32(int rid, BridgeFlags flags, float value)
        {
            Write(rid, flags, BridgeValueKind.F32, BitConverter.SingleToInt32Bits(value), 0, null);
        }

        /// <summary>An int-valued control.</summary>
        public void WriteI32(int rid, BridgeFlags flags, int value)
        {
            Write(rid, flags, BridgeValueKind.I32, value, 0, null);
        }

        /// <summary>A 64-bit-valued control: two slots, low word first. See <see cref="BridgeValueKind.I64"/>.</summary>
        public void WriteI64(int rid, BridgeFlags flags, long value)
        {
            Write(rid, flags, BridgeValueKind.I64, unchecked((int)(value & 0xFFFFFFFF)), (int)(value >> 32), null);
        }

        /// <summary>
        /// A string-valued control. The bytes go into the text buffer and the record carries (offset, length),
        /// which is the same shape the op stream's volatile table uses in the other direction (section 5.4).
        /// </summary>
        public void WriteString(int rid, BridgeFlags flags, string value)
        {
            Write(rid, flags, BridgeValueKind.Str, 0, 0, value ?? string.Empty);
        }

        /// <summary>Adds the four rect slots to the record just written, and sets <see cref="BridgeFlags.HasRect"/>.</summary>
        /// <remarks>
        /// Called immediately after a Write, and only then. Rects are opt-in (section 6.1) because
        /// <c>bool Draw()</c> discards them; a consumer that returns a result struct hands one back for free, and
        /// this is how it reaches the table without a second call shape.
        /// </remarks>
        public void AppendRect(NowRect rect)
        {
            if (passive || m_Records == 0) return;

            // The record head is at the slot the last Write started at; find it by walking back is not possible,
            // so the flag is set on the head slot remembered by Write.
            m_Slots[m_LastHead] |= ((int)BridgeFlags.HasRect) << FlagsShift;

            EnsureSlots(m_Cursor + 4);
            m_Slots[m_Cursor++] = BitConverter.SingleToInt32Bits(rect.x);
            m_Slots[m_Cursor++] = BitConverter.SingleToInt32Bits(rect.y);
            m_Slots[m_Cursor++] = BitConverter.SingleToInt32Bits(rect.width);
            m_Slots[m_Cursor++] = BitConverter.SingleToInt32Bits(rect.height);
        }

        private int m_LastHead;

        private void Write(int rid, BridgeFlags flags, BridgeValueKind kind, int a, int b, string str)
        {
            // Property 1. This is the whole of measure-pass suppression, and it is one branch because it has to be
            // on the hot path of every control in the frame.
            if (passive)
            {
                ++m_Suppressed;
                return;
            }

            int valueSlots = ValueSlots(kind);
            EnsureSlots(m_Cursor + 2 + valueSlots);

            m_Slots[m_Cursor++] = rid;
            m_LastHead = m_Cursor;
            m_Slots[m_Cursor++] = (((int)(flags | BridgeFlags.Present)) << FlagsShift) | ((int)kind & ValueKindMask);

            switch (kind)
            {
                case BridgeValueKind.None:
                    break;

                case BridgeValueKind.Str:
                {
                    int offset = m_TextCursor;
                    int length = WriteText(str);
                    m_Slots[m_Cursor++] = offset;
                    m_Slots[m_Cursor++] = length;
                    break;
                }

                case BridgeValueKind.I64:
                    m_Slots[m_Cursor++] = a;
                    m_Slots[m_Cursor++] = b;
                    break;

                default:
                    m_Slots[m_Cursor++] = a;
                    break;
            }

            ++m_Records;
        }

        /// <summary>How many value slots a record of this kind carries after its two-slot head.</summary>
        private static int ValueSlots(BridgeValueKind kind)
        {
            switch (kind)
            {
                case BridgeValueKind.None: return 0;
                case BridgeValueKind.Str: return 2;
                case BridgeValueKind.I64: return 2;
                default: return 1;
            }
        }

        private int WriteText(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
            EnsureText(m_TextCursor + maxBytes);

            int written = Encoding.UTF8.GetBytes(value, 0, value.Length, m_Text, m_TextCursor);
            m_TextCursor += written;
            return written;
        }

        // ------------------------------------------------------------------------------------------- growth
        //
        // Growth here needs no protocol, unlike the op stream's (section 5.1). The managed side owns both buffers
        // and writes them BETWEEN crossings - the table is written during frame N's replay and read at frame
        // N+1's record - so a grow is an array copy and the next crossing simply carries a longer span.

        private void EnsureSlots(int need)
        {
            if (need <= m_Slots.Length) return;
            int size = m_Slots.Length;
            while (size < need) size *= 2;
            var grown = new int[size];
            Array.Copy(m_Slots, grown, m_Cursor);
            m_Slots = grown;
        }

        private void EnsureText(int need)
        {
            if (need <= m_Text.Length) return;
            int size = m_Text.Length;
            while (size < need) size *= 2;
            var grown = new byte[size];
            Array.Copy(m_Text, grown, m_TextCursor);
            m_Text = grown;
        }

        // ------------------------------------------------------------------------------------------- reading
        //
        // The bridge itself never reads the table - JavaScript does. These exist so a test can assert what was
        // written without re-implementing the walk, and so a diagnostic can print it.

        /// <summary>One decoded record. What JavaScript's loader builds, in C#, for tests and diagnostics.</summary>
        public readonly struct Record
        {
            internal Record(int rid, BridgeFlags flags, BridgeValueKind kind, int raw, string text, NowRect rect, bool hasRect, long wide = 0)
            {
                this.wide = wide;
                this.rid = rid;
                this.flags = flags;
                this.kind = kind;
                this.raw = raw;
                this.text = text;
                this.rect = rect;
                this.hasRect = hasRect;
            }

            public int rid { get; }
            public BridgeFlags flags { get; }
            public BridgeValueKind kind { get; }

            /// <summary>The value slot, unconverted. Use <see cref="f32"/> / <see cref="boolean"/> for the rest.</summary>
            public int raw { get; }

            public string text { get; }
            public NowRect rect { get; }
            public bool hasRect { get; }

            /// <summary>The 64-bit value of an <see cref="BridgeValueKind.I64"/> record; zero otherwise.</summary>
            public long wide { get; }

            public float f32 => BitConverter.Int32BitsToSingle(raw);
            public bool boolean => raw != 0;
            public bool Has(BridgeFlags flag) => (flags & flag) != 0;
        }

        /// <summary>Walks the sealed table. Empty before the first frame has been replayed.</summary>
        public Record[] Decode()
        {
            if (m_Sealed < HeaderSlots) return Array.Empty<Record>();

            int count = m_Slots[HdrRecordCount];
            var records = new Record[count];
            int slot = HeaderSlots;

            for (int i = 0; i < count; ++i)
            {
                int rid = m_Slots[slot++];
                int head = m_Slots[slot++];
                var kind = (BridgeValueKind)(head & ValueKindMask);
                var flags = (BridgeFlags)(head >> FlagsShift);

                int raw = 0;
                string str = null;
                long wide = 0;

                if (kind == BridgeValueKind.Str)
                {
                    int offset = m_Slots[slot++];
                    int length = m_Slots[slot++];
                    str = Encoding.UTF8.GetString(m_Text, offset, length);
                }
                else if (kind == BridgeValueKind.I64)
                {
                    uint low = unchecked((uint)m_Slots[slot++]);
                    int high = m_Slots[slot++];
                    wide = unchecked(((long)high << 32) | low);
                    raw = unchecked((int)low);
                }
                else if (kind != BridgeValueKind.None)
                {
                    raw = m_Slots[slot++];
                }

                NowRect rect = default;
                bool hasRect = (flags & BridgeFlags.HasRect) != 0;
                if (hasRect)
                {
                    rect = new NowRect(
                        BitConverter.Int32BitsToSingle(m_Slots[slot]),
                        BitConverter.Int32BitsToSingle(m_Slots[slot + 1]),
                        BitConverter.Int32BitsToSingle(m_Slots[slot + 2]),
                        BitConverter.Int32BitsToSingle(m_Slots[slot + 3]));
                    slot += 4;
                }

                records[i] = new Record(rid, flags, kind, raw, str, rect, hasRect, wide);
            }

            return records;
        }
    }
}
