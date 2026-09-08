// W2 - the managed half of the transport. Docs/Standalone/M3-Spec.md sections 5.1, 5.2, 5.4, 5.5.
//
// Four things live here and nothing else:
//
//   1. the managed buffers, and the growth protocol that resizes them without ever losing a frame (section 5.1);
//   2. the preamble - the intern-table pass that must run BEFORE decoding, once, so the decode itself allocates
//      nothing that outlives the call and is therefore idempotent under exactLayout (section 5.6);
//   3. the validator of section 5.5, which refuses a malformed buffer with a slot offset;
//   4. string access for the replay: an intern handle or a volatile-table index, resolved the same way.
//
// It does NOT call JavaScript. The one [JSImport] lives in BridgeInterop.cs, and `Record` takes it as a delegate,
// so Standalone/NowUI.Bridge.Tests can drive the whole protocol - growth, preamble and validation - against a
// hand-built frame with no browser, no wasm and no GPU.

using System;
using System.Collections.Generic;
using System.Text;

namespace NowUI.Bridge
{
    /// <summary>
    /// A malformed command buffer. Section 5.5: the decoder validates the whole buffer before it opens a single
    /// scope, and a failure names the slot offset. NowUI is never touched when this is thrown.
    /// </summary>
    public sealed class NowBridgeProtocolException : Exception
    {
        public NowBridgeProtocolException(int slot, string message)
            : base("NowUI bridge: malformed command buffer at slot " + slot + ". " + message)
        {
            Slot = slot;
        }

        /// <summary>The slot offset the failure was found at. -1 when the failure is not slot-addressed.</summary>
        public int Slot { get; }
    }

    /// <summary>
    /// What one boundary crossing looks like from the managed side. Section 5.1's <c>record</c>, as a delegate so
    /// the protocol can be tested without a browser.
    /// </summary>
    public delegate int RecordFunc(
        Span<int> ops,
        Span<byte> opsText,
        Span<int> results,
        Span<byte> resultsText,
        int resultSlots,
        int frame);

    /// <summary>The managed buffers, the growth protocol, the preamble and the validator.</summary>
    public sealed class BridgeRecorder
    {
        // Sized so a first frame of a small application fits without a single growth, and so the two-slot
        // NEED_MORE write always has somewhere to land. Section 5.1: "ops is always >= 2 slots, so this always
        // fits" - that is a promise this side keeps, not one the JavaScript side can check.
        private const int InitialOpSlots = 1024;
        private const int InitialTextBytes = 4096;

        /// <summary>Builds a recorder over a fresh result table.</summary>
        public BridgeRecorder()
            : this(null)
        {
        }

        /// <summary>Builds a recorder over <paramref name="results"/>, so a host can hold the table too.</summary>
        public BridgeRecorder(BridgeResults results)
        {
            m_ResultsTable = results ?? new BridgeResults();
        }

        private int[] m_Ops = new int[InitialOpSlots];
        private byte[] m_OpsText = new byte[InitialTextBytes];

        /// <summary>
        /// W5's table (section 6). Owned by <see cref="BridgeResults"/> rather than by this class, because the two
        /// directions have different growth rules: the op stream grows through the NEED_MORE handshake because
        /// JavaScript writes it, and the result table grows by a plain array copy because the managed side writes
        /// it between crossings.
        /// </summary>
        private readonly BridgeResults m_ResultsTable;

        /// <summary>Section 5.4: the wasm side keeps a List&lt;string&gt; at the same indices JavaScript's Map uses.</summary>
        private readonly List<string> m_Strings = new List<string>(256);

        // This frame's volatile table, as (byteOffset, byteLength) pairs into m_OpsText.
        private int[] m_VolatileOffsets = new int[64];
        private int[] m_VolatileLengths = new int[64];
        private int m_VolatileCount;

        private int m_Used;
        private int m_OpStart;
        private int m_OpEnd;
        private int m_FrameFlags;

        /// <summary>Total slots the last <see cref="Record"/> brought back, header included.</summary>
        public int used => m_Used;

        /// <summary>First slot of the op stream.</summary>
        public int opStart => m_OpStart;

        /// <summary>One past the last op slot.</summary>
        public int opEnd => m_OpEnd;

        /// <summary>Header slot 2. See <see cref="Abi.FlagFaulted"/> and friends.</summary>
        public int frameFlags => m_FrameFlags;

        /// <summary>Section 4.4: the author's draw function threw, and the buffer is a balanced prefix.</summary>
        public bool faulted => (m_FrameFlags & Abi.FlagFaulted) != 0;

        /// <summary>Section 5.7: whether this frame asks for the two-pass measured replay.</summary>
        public bool exactLayout => (m_FrameFlags & Abi.FlagExactLayout) != 0;

        /// <summary>How many strings have been interned this session. Diagnostics and tests.</summary>
        public int internedCount => m_Strings.Count;

        /// <summary>W5's table (section 6). The replay writes it; <see cref="Record"/> carries it back.</summary>
        public BridgeResults results => m_ResultsTable;

        /// <summary>The whole frame, header included. Valid only after <see cref="Preamble"/> returned.</summary>
        public ReadOnlySpan<int> frame => new ReadOnlySpan<int>(m_Ops, 0, m_Used);

        /// <summary>The op stream on its own.</summary>
        public ReadOnlySpan<int> ops => new ReadOnlySpan<int>(m_Ops, m_OpStart, m_OpEnd - m_OpStart);

        /// <summary>Raw slot access for the decoder, which walks absolute slot indices.</summary>
        public int Slot(int index) => m_Ops[index];

        /// <summary>Reinterprets a slot as an f32 (section 5.3's <c>f32</c> kind).</summary>
        public float SlotF32(int index) => BitConverter.Int32BitsToSingle(m_Ops[index]);

        // -------------------------------------------------------------------------------------------- record

        /// <summary>
        /// One boundary crossing, plus at most one growth retry. Section 5.1.
        /// </summary>
        /// <remarks>
        /// The retry does not re-run the author's draw function: the JavaScript side keeps the recorded bytes and
        /// its <c>pending</c> flag, and answers the second call with two set()s. So no handler fires twice and no
        /// state is read twice - which is the property that makes growth free rather than a dropped frame.
        /// <para>Two retries would mean the JavaScript side asked for a size and then did not fit in it, which is a
        /// bridge bug rather than a growth, and is reported as one.</para>
        /// </remarks>
        public int Record(RecordFunc record, int frameNumber)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            for (int attempt = 0; attempt < 2; ++attempt)
            {
                // The result table's spans are re-read from the table on every attempt, because a growth between
                // frames replaces the arrays. `sealedSlots` - not the buffer length - is what JavaScript slices:
                // it is the header plus this frame's records, so a table that grew once to hold a long string
                // does not cost a full-buffer copy on every frame after it.
                int returned = record(
                    m_Ops.AsSpan(),
                    m_OpsText.AsSpan(),
                    m_ResultsTable.slots.AsSpan(),
                    m_ResultsTable.text.AsSpan(),
                    m_ResultsTable.sealedSlots,
                    frameNumber);

                if (returned != Abi.NeedMore)
                {
                    if (returned < 0)
                        throw new NowBridgeProtocolException(-1,
                            "record() returned " + returned + ", and the only negative return is NEED_MORE (" +
                            Abi.NeedMore + ").");

                    if (returned > m_Ops.Length)
                        throw new NowBridgeProtocolException(-1,
                            "record() reported " + returned + " slots but the buffer it was given holds only " +
                            m_Ops.Length + ".");

                    m_Used = returned;
                    return returned;
                }

                if (attempt == 1)
                    throw new NowBridgeProtocolException(-1,
                        "record() asked to grow twice for one frame. The JavaScript side reported a size and then " +
                        "did not fit in it; this is a bridge bug, not a growth.");

                // Section 5.1: the two sizes the managed side must grow to are in the first two slots.
                int needSlots = m_Ops[0];
                int needText = m_Ops[1];

                if (needSlots <= 0 || needSlots > Abi.MaxSlots)
                    throw new NowBridgeProtocolException(0,
                        "NEED_MORE asked for " + needSlots + " op slots, which is outside 1.." + Abi.MaxSlots + ".");

                if (needText < 0)
                    throw new NowBridgeProtocolException(1,
                        "NEED_MORE asked for " + needText + " text bytes.");

                Grow(ref m_Ops, needSlots);
                Grow(ref m_OpsText, needText);
            }

            throw new InvalidOperationException("unreachable");
        }

        private static void Grow(ref int[] buffer, int need)
        {
            if (need <= buffer.Length) return;
            int size = buffer.Length;
            while (size < need) size *= 2;
            buffer = new int[size];
        }

        private static void Grow(ref byte[] buffer, int need)
        {
            if (need <= buffer.Length) return;
            int size = buffer.Length;
            while (size < need) size *= 2;
            buffer = new byte[size];
        }

        // ------------------------------------------------------------------------------------------ preamble

        /// <summary>
        /// Validates the whole buffer (section 5.5) and interns this frame's new strings, in that order, before a
        /// single scope is opened. Throws <see cref="NowBridgeProtocolException"/> naming a slot offset on any
        /// failure, with NowUI untouched.
        /// </summary>
        /// <remarks>
        /// The interning happens HERE rather than in the decoder, and that is what makes the decoder idempotent
        /// (section 5.6): under exactLayout the buffer is decoded twice per frame, and a decoder that interned as
        /// it went would append the same strings twice on the second pass.
        /// </remarks>
        public void Preamble(int usedSlots)
        {
            m_Used = usedSlots;
            Validate(usedSlots);
            InternNewStrings();
        }

        /// <summary>The validator on its own, for the tests that assert what each rule refuses.</summary>
        public void Validate(int usedSlots)
        {
            // ---- rule 1: magic and surface hash -----------------------------------------------------------
            if (usedSlots < Abi.HeaderSlots)
                throw new NowBridgeProtocolException(0,
                    "the frame is " + usedSlots + " slots long and the fixed header alone is " + Abi.HeaderSlots + ".");

            int magic = m_Ops[Abi.HdrMagic];
            if (magic != Abi.Magic)
                throw new NowBridgeProtocolException(Abi.HdrMagic,
                    "magic is 0x" + magic.ToString("X8") + ", expected 0x" + Abi.Magic.ToString("X8") + ".");

            int surfaceHash = m_Ops[Abi.HdrSurfaceHash];
            if (surfaceHash != Abi.SurfaceHash)
                throw new NowBridgeProtocolException(Abi.HdrSurfaceHash,
                    "this wasm build's surface hash is 0x" + Abi.SurfaceHash.ToString("X8") +
                    " and nowui.js sent 0x" + surfaceHash.ToString("X8") +
                    ". The JavaScript bundle and the wasm module were built from different surface manifests; " +
                    "rebuild both, or serve the nowui.js that matches this module.");

            // ---- rule 2: the header's own numbers are in bounds and mutually consistent --------------------
            m_FrameFlags = m_Ops[Abi.HdrFrameFlags];
            int internCount = m_Ops[Abi.HdrInternCount];
            int volatileCount = m_Ops[Abi.HdrVolatileCount];
            int textBytes = m_Ops[Abi.HdrTextBytes];
            int start = m_Ops[Abi.HdrOpStart];
            int end = m_Ops[Abi.HdrOpEnd];

            if (internCount < 0 || volatileCount < 0)
                throw new NowBridgeProtocolException(internCount < 0 ? Abi.HdrInternCount : Abi.HdrVolatileCount,
                    "a table count is negative (internCount " + internCount + ", volatileCount " + volatileCount + ").");

            if (textBytes < 0 || textBytes > m_OpsText.Length)
                throw new NowBridgeProtocolException(Abi.HdrTextBytes,
                    "textBytes is " + textBytes + " and the text buffer holds " + m_OpsText.Length + ".");

            long expectedStart = (long)Abi.HeaderSlots + (long)internCount * 3 + (long)volatileCount * 2;
            if (start != expectedStart)
                throw new NowBridgeProtocolException(Abi.HdrOpStart,
                    "opStart is " + start + " but the header plus " + internCount + " intern triples and " +
                    volatileCount + " volatile pairs ends at " + expectedStart + ".");

            if (end < start || end > usedSlots)
                throw new NowBridgeProtocolException(Abi.HdrOpEnd,
                    "opEnd is " + end + ", which is outside opStart (" + start + ") .. used (" + usedSlots + ").");

            // ---- rule 3: intern declarations are contiguous from the current count, and in the text buffer --
            int cursor = Abi.HeaderSlots;
            int expectedHandle = m_Strings.Count;

            for (int i = 0; i < internCount; ++i, cursor += 3)
            {
                int handle = m_Ops[cursor];
                int offset = m_Ops[cursor + 1];
                int length = m_Ops[cursor + 2];

                if (handle != expectedHandle + i)
                    throw new NowBridgeProtocolException(cursor,
                        "intern declaration " + i + " has handle " + handle + "; declarations must be contiguous " +
                        "from this session's current intern count (" + expectedHandle + "), so it should be " +
                        (expectedHandle + i) + ".");

                if (offset < 0 || length < 0 || (long)offset + length > textBytes)
                    throw new NowBridgeProtocolException(cursor + 1,
                        "intern declaration " + i + " spans bytes " + offset + ".." + (offset + length) +
                        " of a text section that is " + textBytes + " bytes long.");
            }

            for (int i = 0; i < volatileCount; ++i, cursor += 2)
            {
                int offset = m_Ops[cursor];
                int length = m_Ops[cursor + 1];

                if (offset < 0 || length < 0 || (long)offset + length > textBytes)
                    throw new NowBridgeProtocolException(cursor,
                        "volatile string " + i + " spans bytes " + offset + ".." + (offset + length) +
                        " of a text section that is " + textBytes + " bytes long.");
            }

            // ---- rule 4: walking argSlots from opStart lands exactly on opEnd -----------------------------
            // ---- rule 5: scope opens and closes balance and nest -------------------------------------------
            //
            // One walk does both, because a desynchronised walk would make the balance count meaningless anyway.
            int depth = 0;
            int slot = start;

            while (slot < end)
            {
                int header = m_Ops[slot];
                int opcode = header & 0xFFFF;
                int argSlots = (header >> 16) & 0xFFFF;

                if (opcode == Abi.OpInvalid)
                    throw new NowBridgeProtocolException(slot,
                        "opcode 0 (OP_INVALID) is never emitted; a zeroed buffer fails here.");

                long next = (long)slot + 1 + argSlots;
                if (next > end)
                    throw new NowBridgeProtocolException(slot,
                        "the op at this slot declares " + argSlots + " argument slots, which runs to " + next +
                        " and past opEnd (" + end + "). The buffer is truncated or the op stream is desynchronised.");

                OpSpec spec = Abi.Find(opcode);

                // A fixed op must be exactly its declared width; a variable one (strlist, opts - section 5.3)
                // must be at least its minimum, because its real width lives in the count or bitmask slot the
                // decoder has not read yet. Rule 4's walk stays exact either way: the width is taken from the
                // op header, not from this table.
                if (spec != null && (spec.Variable ? argSlots < spec.Slots : argSlots != spec.Slots))
                    throw new NowBridgeProtocolException(slot,
                        "op " + spec.Name + " (opcode " + opcode + ") declares " + argSlots +
                        " argument slots and this build expects " +
                        (spec.Variable ? "at least " : "") + spec.Slots + ".");

                if (opcode == Abi.OpScopeClose)
                {
                    if (--depth < 0)
                        throw new NowBridgeProtocolException(slot,
                            "OP_SCOPE_CLOSE with no scope open. Scope opens and closes must balance and nest.");
                }
                else if (spec != null && spec.OpensScope)
                {
                    ++depth;
                }

                slot = (int)next;
            }

            if (slot != end)
                throw new NowBridgeProtocolException(slot,
                    "walking the op stream landed on slot " + slot + " and opEnd is " + end + ".");

            if (depth != 0)
                throw new NowBridgeProtocolException(end,
                    depth + " scope(s) were opened and never closed. The recorder closes every scope in a finally " +
                    "block, so an unbalanced stream is a bridge bug rather than an author error.");

            // Cached for the decode, now that they are known good.
            m_OpStart = start;
            m_OpEnd = end;
            LoadVolatileTable(internCount, volatileCount);
        }

        private void LoadVolatileTable(int internCount, int volatileCount)
        {
            if (m_VolatileOffsets.Length < volatileCount)
            {
                int size = m_VolatileOffsets.Length;
                while (size < volatileCount) size *= 2;
                m_VolatileOffsets = new int[size];
                m_VolatileLengths = new int[size];
            }

            int cursor = Abi.HeaderSlots + internCount * 3;
            for (int i = 0; i < volatileCount; ++i, cursor += 2)
            {
                m_VolatileOffsets[i] = m_Ops[cursor];
                m_VolatileLengths[i] = m_Ops[cursor + 1];
            }

            m_VolatileCount = volatileCount;
        }

        private void InternNewStrings()
        {
            int internCount = m_Ops[Abi.HdrInternCount];
            int cursor = Abi.HeaderSlots;

            for (int i = 0; i < internCount; ++i, cursor += 3)
            {
                int offset = m_Ops[cursor + 1];
                int length = m_Ops[cursor + 2];
                m_Strings.Add(Encoding.UTF8.GetString(m_OpsText, offset, length));
            }
        }

        // -------------------------------------------------------------------------------------------- strings

        /// <summary>
        /// Section 5.3's <c>str</c> kind: a non-negative slot is an intern handle, a negative one is the bitwise
        /// complement of an index into this frame's volatile table. An interned string costs nothing to read; a
        /// volatile one costs one allocation per distinct string per frame, which is what keeps a text field from
        /// leaking an intern-table entry per keystroke.
        /// </summary>
        public string Text(int slot)
        {
            if (slot >= 0)
            {
                if (slot >= m_Strings.Count)
                    throw new NowBridgeProtocolException(-1,
                        "intern handle " + slot + " was used and only " + m_Strings.Count +
                        " strings have been declared this session.");

                return m_Strings[slot];
            }

            int index = ~slot;
            if (index >= m_VolatileCount)
                throw new NowBridgeProtocolException(-1,
                    "volatile string index " + index + " was used and this frame declared " + m_VolatileCount + ".");

            return Encoding.UTF8.GetString(m_OpsText, m_VolatileOffsets[index], m_VolatileLengths[index]);
        }

        /// <summary>An interned string by handle, for diagnostics that render a path segment.</summary>
        public string Interned(int handle)
        {
            return handle >= 0 && handle < m_Strings.Count ? m_Strings[handle] : null;
        }

        /// <summary>
        /// Drops every interned string. Only a hot reload calls this (W7): the JavaScript side's Map and this list
        /// are two halves of one table, and resetting one without the other is how handles come to mean different
        /// strings on the two sides.
        /// </summary>
        public void ResetStrings()
        {
            m_Strings.Clear();
        }
    }
}
