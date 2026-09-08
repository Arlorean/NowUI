// A recorded frame, built in C#. Docs/Standalone/M3-Spec.md section 5.2.
//
// It writes the same bytes wwwroot/nowui/recorder.js writes, and it exists for one reason: the malformed-buffer
// tests need to damage exactly one property of an otherwise correct frame, and a JavaScript recorder cannot be
// asked to emit something it is written not to emit.
//
// The frames it builds are cross-checked against the real thing. ReplayTests loads a frame that node recorded
// through recorder.js and replays it, so "this builder writes the same layout as the recorder" is a measured fact
// rather than a shared assumption between two files written by the same hand.

using System;
using System.Collections.Generic;
using System.Text;
using NowUI.Bridge;

namespace NowUI.Bridge.Tests
{
    internal sealed class TestFrame
    {
        private readonly List<int> m_Ops = new List<int>();
        private readonly List<byte> m_Text = new List<byte>();
        private readonly List<int> m_Interns = new List<int>();     // flat triples
        private readonly List<int> m_Volatiles = new List<int>();   // flat pairs

        private int m_NextHandle;

        /// <summary>The intern handle the session is at. A frame declares handles contiguously from here.</summary>
        public TestFrame(int firstHandle = 0)
        {
            m_NextHandle = firstHandle;
        }

        public int magic = Abi.Magic;
        public int surfaceHash = Abi.SurfaceHash;
        public int frameFlags = Abi.FlagExactLayout;

        /// <summary>Set to override the computed opEnd, for the truncation tests.</summary>
        public int? forcedOpEnd;


        public int Intern(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            int offset = m_Text.Count;
            m_Text.AddRange(bytes);
            int handle = m_NextHandle++;
            m_Interns.Add(handle);
            m_Interns.Add(offset);
            m_Interns.Add(bytes.Length);
            return handle;
        }

        public int Volatile(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            int offset = m_Text.Count;
            m_Text.AddRange(bytes);
            m_Volatiles.Add(offset);
            m_Volatiles.Add(bytes.Length);
            return ~(m_Volatiles.Count / 2 - 1);
        }

        public TestFrame Op(OpSpec spec, params int[] args)
        {
            return RawOp(spec.Opcode, args.Length, args);
        }

        /// <summary>An op whose declared argSlots can be made to disagree with what follows it.</summary>
        public TestFrame RawOp(int opcode, int declaredArgSlots, params int[] args)
        {
            m_Ops.Add((opcode & 0xFFFF) | ((declaredArgSlots & 0xFFFF) << 16));
            m_Ops.AddRange(args);
            return this;
        }

        public TestFrame ScopeClose()
        {
            return RawOp(Abi.OpScopeClose, 0);
        }

        /// <summary>Assembles header, tables and op stream exactly as recorder.js's flush() does.</summary>
        public void Build(out int[] ops, out byte[] text, out int used)
        {
            int internCount = m_Interns.Count / 3;
            int volatileCount = m_Volatiles.Count / 2;
            int opStart = Abi.HeaderSlots + internCount * 3 + volatileCount * 2;
            int opEnd = opStart + m_Ops.Count;

            ops = new int[opEnd];
            ops[Abi.HdrMagic] = magic;
            ops[Abi.HdrSurfaceHash] = surfaceHash;
            ops[Abi.HdrFrameFlags] = frameFlags;
            ops[Abi.HdrInternCount] = internCount;
            ops[Abi.HdrVolatileCount] = volatileCount;
            ops[Abi.HdrTextBytes] = m_Text.Count;
            ops[Abi.HdrOpStart] = opStart;
            ops[Abi.HdrOpEnd] = forcedOpEnd ?? opEnd;

            int cursor = Abi.HeaderSlots;
            for (int i = 0; i < m_Interns.Count; ++i) ops[cursor++] = m_Interns[i];
            for (int i = 0; i < m_Volatiles.Count; ++i) ops[cursor++] = m_Volatiles[i];
            for (int i = 0; i < m_Ops.Count; ++i) ops[cursor++] = m_Ops[i];

            text = m_Text.ToArray();
            used = opEnd;
        }

        /// <summary>
        /// A <see cref="RecordFunc"/> that hands this frame to a <see cref="BridgeRecorder"/>, including the
        /// NEED_MORE growth handshake when the managed buffers are too small - so a test drives the same two-call
        /// protocol the browser does.
        /// </summary>
        public RecordFunc AsRecordFunc()
        {
            int[] ops;
            byte[] text;
            int used;
            Build(out ops, out text, out used);

            return (Span<int> target, Span<byte> targetText, Span<int> results, Span<byte> resultsText, int resultSlots, int frame) =>
            {
                if (used > target.Length || text.Length > targetText.Length)
                {
                    target[0] = used;
                    target[1] = text.Length;
                    return Abi.NeedMore;
                }

                ops.AsSpan(0, Math.Min(ops.Length, used)).CopyTo(target);
                text.AsSpan().CopyTo(targetText);
                return used;
            };
        }
    }
}
