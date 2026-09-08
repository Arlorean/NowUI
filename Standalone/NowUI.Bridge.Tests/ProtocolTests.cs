// W2's acceptance, second half: "A deliberately truncated buffer is refused by section 5.5's validator with a slot
// offset, and NowUI is never touched."
//
// Section 5.5 lists five rules. There is a test per rule, each damaging exactly one property of an otherwise
// correct frame, and each asserting the SLOT the failure is reported at rather than only that it failed - a
// validator that refuses everything with "invalid buffer" would pass a weaker test and be useless in a browser.
//
// The "NowUI is never touched" half is asserted structurally: the validator runs in Preamble, which the host calls
// BEFORE Now.StartUI (section 5.7), and the replay's own op counter stays at zero. There is no way to reach NowUI
// from a frame that Preamble refused, because nothing between the two exists.

using System;
using NowUI.Bridge;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class ProtocolTests
    {
        private static TestFrame HelloFrame()
        {
            var frame = new TestFrame();
            int hello = frame.Intern("hello");
            frame.Op(Abi.Op("TEXT"), hello);
            return frame;
        }

        private static BridgeRecorder Load(TestFrame frame)
        {
            var recorder = new BridgeRecorder();
            int used = recorder.Record(frame.AsRecordFunc(), 1);
            recorder.Preamble(used);
            return recorder;
        }

        private static NowBridgeProtocolException Refuse(TestFrame frame)
        {
            var recorder = new BridgeRecorder();
            int used = recorder.Record(frame.AsRecordFunc(), 1);
            return Assert.Throws<NowBridgeProtocolException>(() => recorder.Preamble(used));
        }

        // ------------------------------------------------------------------------------------- the happy path

        [Test]
        public void AWellFormedHelloFrameIsAccepted()
        {
            BridgeRecorder recorder = Load(HelloFrame());

            Assert.AreEqual(Abi.HeaderSlots + 3, recorder.opStart, "opStart is past the one intern triple");
            Assert.AreEqual(recorder.used, recorder.opEnd, "opEnd is the end of the frame");
            Assert.AreEqual(1, recorder.internedCount, "one string interned");
            Assert.AreEqual("hello", recorder.Text(0), "and it is 'hello'");
        }

        [Test]
        public void InternHandlesAreSessionStableAcrossFrames()
        {
            var recorder = new BridgeRecorder();

            var first = new TestFrame();
            int hello = first.Intern("hello");
            first.Op(Abi.Op("TEXT"), hello);
            recorder.Preamble(recorder.Record(first.AsRecordFunc(), 1));

            // The second frame declares nothing new and reuses handle 0, which is the whole point of interning:
            // "Keys, labels, style names, placeholders and option lists all intern on frame 1 and cost one slot
            // each thereafter" (section 5.4).
            var second = new TestFrame(firstHandle: 1);
            second.Op(Abi.Op("TEXT"), 0);
            recorder.Preamble(recorder.Record(second.AsRecordFunc(), 2));

            Assert.AreEqual(1, recorder.internedCount, "nothing new was interned");
            Assert.AreEqual("hello", recorder.Text(0), "and handle 0 still means what it meant");
        }

        [Test]
        public void AVolatileStringIsResolvedByTheComplementOfItsIndex()
        {
            var frame = new TestFrame();
            int typed = frame.Volatile("Added Ada.");
            frame.Op(Abi.Op("TEXT"), typed);

            BridgeRecorder recorder = Load(frame);

            Assert.Less(typed, 0, "a volatile reference is negative");
            Assert.AreEqual("Added Ada.", recorder.Text(typed));
            Assert.AreEqual(0, recorder.internedCount, "and it leaked no intern-table entry");
        }

        // ------------------------------------------------------------------------------------- rule 1

        [Test]
        public void Rule1_WrongMagicIsRefusedAtSlot0()
        {
            TestFrame frame = HelloFrame();
            frame.magic = 0x4E554C4C;

            NowBridgeProtocolException e = Refuse(frame);

            Assert.AreEqual(Abi.HdrMagic, e.Slot);
            StringAssert.Contains("magic is 0x4E554C4C", e.Message);
        }

        [Test]
        public void Rule1_AStaleSurfaceHashIsRefusedNamingBothHashes()
        {
            TestFrame frame = HelloFrame();
            frame.surfaceHash = Abi.SurfaceHash ^ 0x5A5A5A5A;

            NowBridgeProtocolException e = Refuse(frame);

            Assert.AreEqual(Abi.HdrSurfaceHash, e.Slot);
            StringAssert.Contains(Abi.SurfaceHash.ToString("X8"), e.Message, "the wasm build's hash");
            StringAssert.Contains(frame.surfaceHash.ToString("X8"), e.Message, "and the one nowui.js sent");
            StringAssert.Contains("built from different surface manifests", e.Message);
        }

        // ------------------------------------------------------------------------------------- rule 2

        [Test]
        public void Rule2_OpEndPastTheEndOfTheBufferIsRefusedAtSlot7()
        {
            TestFrame frame = HelloFrame();
            frame.forcedOpEnd = 1000;

            NowBridgeProtocolException e = Refuse(frame);

            Assert.AreEqual(Abi.HdrOpEnd, e.Slot);
            StringAssert.Contains("opEnd is 1000", e.Message);
        }

        [Test]
        public void Rule2_AnOpStartThatDoesNotFollowTheTablesIsRefusedAtSlot6()
        {
            var frame = new TestFrame();
            frame.Intern("hello");
            frame.Op(Abi.Op("TEXT"), 0);

            int[] ops;
            byte[] text;
            int used;
            frame.Build(out ops, out text, out used);
            ops[Abi.HdrOpStart] = Abi.HeaderSlots;      // claims no tables, while declaring one intern triple

            NowBridgeProtocolException e = RefuseRaw(ops, text, used);

            Assert.AreEqual(Abi.HdrOpStart, e.Slot);
            StringAssert.Contains("intern triples", e.Message);
        }

        // ------------------------------------------------------------------------------------- rule 3

        [Test]
        public void Rule3_AnInternDeclarationOutOfSequenceIsRefusedAtItsTripleSlot()
        {
            var frame = new TestFrame(firstHandle: 7);   // the session is at 0, so 7 is not contiguous
            frame.Intern("hello");
            frame.Op(Abi.Op("TEXT"), 7);

            NowBridgeProtocolException e = Refuse(frame);

            Assert.AreEqual(Abi.HeaderSlots, e.Slot, "the offending triple's first slot");
            StringAssert.Contains("must be contiguous", e.Message);
        }

        [Test]
        public void Rule3_AnInternDeclarationPastTheTextSectionIsRefused()
        {
            TestFrame frame = HelloFrame();

            int[] ops;
            byte[] text;
            int used;
            frame.Build(out ops, out text, out used);
            ops[Abi.HeaderSlots + 2] = 4096;            // length far past the five bytes of "hello"

            NowBridgeProtocolException e = RefuseRaw(ops, text, used);

            Assert.AreEqual(Abi.HeaderSlots + 1, e.Slot);
            StringAssert.Contains("text section", e.Message);
        }

        [Test]
        public void AnUndeclaredInternHandleIsRefusedWhenItIsRead()
        {
            // Not a validator rule: rule 3 checks the DECLARATIONS, and an op referring to a handle nobody ever
            // declared is only detectable when the string is read. It still refuses rather than returning garbage.
            var frame = new TestFrame();
            frame.Op(Abi.Op("TEXT"), 999999);

            BridgeRecorder recorder = Load(frame);

            NowBridgeProtocolException e = Assert.Throws<NowBridgeProtocolException>(() => recorder.Text(999999));
            StringAssert.Contains("intern handle 999999", e.Message);
        }

        // ------------------------------------------------------------------------------------- rule 4

        [Test]
        public void Rule4_ATruncatedBufferIsRefusedAtTheSlotOfTheOpThatOverruns()
        {
            // The acceptance case. A correct frame, then one slot removed from the end: the final op's declared
            // argSlots now run one slot past opEnd, which is precisely what a buffer cut short looks like.
            TestFrame frame = HelloFrame();

            int[] ops;
            byte[] text;
            int used;
            frame.Build(out ops, out text, out used);

            int opStart = ops[Abi.HdrOpStart];
            ops[Abi.HdrOpEnd] = ops[Abi.HdrOpEnd] - 1;
            used -= 1;

            NowBridgeProtocolException e = RefuseRaw(ops, text, used);

            Assert.AreEqual(opStart, e.Slot, "the slot named is the op that overruns, not the header");
            StringAssert.Contains("declares 1 argument slots", e.Message);
            StringAssert.Contains("past opEnd", e.Message);
            StringAssert.Contains("truncated", e.Message);
        }

        [Test]
        public void Rule4_AnArgSlotCountThatDisagreesWithThisBuildIsRefused()
        {
            var frame = new TestFrame();
            frame.Intern("hello");
            frame.RawOp(Abi.Op("TEXT").Opcode, 2, 0, 0);   // TEXT takes one slot in this build, not two

            NowBridgeProtocolException e = Refuse(frame);

            StringAssert.Contains("op TEXT", e.Message);
            StringAssert.Contains("this build expects 1", e.Message);
        }

        [Test]
        public void Rule4_AZeroedBufferFailsImmediately()
        {
            var ops = new int[32];
            ops[Abi.HdrMagic] = Abi.Magic;
            ops[Abi.HdrSurfaceHash] = Abi.SurfaceHash;
            ops[Abi.HdrOpStart] = Abi.HeaderSlots;
            ops[Abi.HdrOpEnd] = 32;

            NowBridgeProtocolException e = RefuseRaw(ops, Array.Empty<byte>(), 32);

            Assert.AreEqual(Abi.HeaderSlots, e.Slot);
            StringAssert.Contains("OP_INVALID", e.Message);
        }

        // ------------------------------------------------------------------------------------- rule 5

        [Test]
        public void Rule5_AnExtraScopeCloseIsRefusedAtItsSlot()
        {
            var frame = new TestFrame();
            frame.Op(Abi.Op("COLUMN"), 1, -1);
            frame.ScopeClose();
            frame.ScopeClose();

            NowBridgeProtocolException e = Refuse(frame);

            StringAssert.Contains("OP_SCOPE_CLOSE with no scope open", e.Message);
            Assert.AreEqual(Abi.HeaderSlots + 4, e.Slot, "the second close's slot");
        }

        [Test]
        public void Rule5_AnUnclosedScopeIsRefusedAtOpEnd()
        {
            var frame = new TestFrame();
            frame.Op(Abi.Op("COLUMN"), 1, -1);
            frame.Op(Abi.Op("ROW"), 2, -1);
            frame.ScopeClose();

            NowBridgeProtocolException e = Refuse(frame);

            StringAssert.Contains("1 scope(s) were opened and never closed", e.Message);
        }

        // ------------------------------------------------------------------------------------- growth

        [Test]
        public void TheGrowthHandshakeResizesBothBuffersAndSucceedsOnTheRetry()
        {
            // 8 KB of text and 3000 ops is well past the recorder's 1024-slot / 4096-byte starting size.
            var frame = new TestFrame();
            for (int i = 0; i < 3000; ++i) frame.Op(Abi.Op("TEXT"), frame.Volatile("string number " + i));

            var recorder = new BridgeRecorder();
            int calls = 0;
            RecordFunc inner = frame.AsRecordFunc();
            RecordFunc counted = (ops, text, results, resultsText, resultSlots, f) =>
            {
                ++calls;
                return inner(ops, text, results, resultsText, resultSlots, f);
            };

            int used = recorder.Record(counted, 1);
            recorder.Preamble(used);

            Assert.AreEqual(2, calls, "one NEED_MORE and one successful retry");
            Assert.AreEqual("string number 2999", recorder.Text(~2999), "and every string survived the growth");
        }

        [Test]
        public void RecordRefusesToGrowTwiceForOneFrame()
        {
            var recorder = new BridgeRecorder();
            RecordFunc alwaysNeedsMore = (ops, text, results, resultsText, resultSlots, f) =>
            {
                ops[0] = 4096;
                ops[1] = 16;
                return Abi.NeedMore;
            };

            NowBridgeProtocolException e = Assert.Throws<NowBridgeProtocolException>(
                () => recorder.Record(alwaysNeedsMore, 1));

            StringAssert.Contains("asked to grow twice", e.Message);
            StringAssert.Contains("bridge bug", e.Message);
        }

        // ------------------------------------------------------------------------------------- helper

        private static NowBridgeProtocolException RefuseRaw(int[] ops, byte[] text, int used)
        {
            var recorder = new BridgeRecorder();

            RecordFunc feed = (target, targetText, results, resultsText, resultSlots, frame) =>
            {
                if (used > target.Length || text.Length > targetText.Length)
                {
                    target[0] = Math.Max(used, ops.Length);
                    target[1] = text.Length;
                    return Abi.NeedMore;
                }

                ops.AsSpan(0, Math.Min(ops.Length, used)).CopyTo(target);
                text.AsSpan().CopyTo(targetText);
                return used;
            };

            int returned = recorder.Record(feed, 1);
            return Assert.Throws<NowBridgeProtocolException>(() => recorder.Preamble(returned));
        }
    }
}
