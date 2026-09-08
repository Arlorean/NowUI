// W4's acceptance, in four tests. Docs/Standalone/M3-Spec.md section 9 (W4), and sections 5.6 and 5.7.
//
//   "the six opcodes draw inside Now.StartUI; exactLayout on and off both produce the same draw list against
//    NowRecordingRenderBackend; the replay is provably called twice under RunMeasured and once without it; a
//    JavaScript throw mid-draw leaves NowUI untouched and produces a balanced short buffer."
//
// Every frame here was recorded by wwwroot/nowui/recorder.js under node (see RecordedFrame), so what is replayed
// is bytes JavaScript produced rather than bytes a second C# writer produced.
//
// ONE THING TO KNOW BEFORE READING THE DRAW-LIST TEST. The op log is only as rich as the resources the host
// serves. With no material templates a whole NowUI frame logs two ops - BeginFrame and EndFrame - because every
// mesh path is skipped, and two empty logs comparing equal is not evidence of anything. BridgeTestHost therefore
// serves the real fixtures when they are on disk, and the test refuses to pass quietly when they are not.

using System;
using System.Collections.Generic;
using NowUI;
using NowUI.Bridge;
using NowUI.Engine;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class FrameTests
    {
        private static readonly NowRect k_Screen = new NowRect(0f, 0f, 640f, 480f);

        private static BridgeReplay Replay(BridgeRecorder recorder, List<string> log)
        {
            return new BridgeReplay(recorder, log.Add) { screen = k_Screen, trackControlIds = true };
        }

        // ------------------------------------------------------------------------------ acceptance 1: six ops

        [Test]
        public void TheSixOpcodesDrawInsideStartUI()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder recorder = RecordedFrame.Load("w4", out used);

            var seen = new HashSet<int>(RecordedFrame.Opcodes(recorder));

            foreach (string name in new[] { "TEXT", "COLUMN", "ROW", "LIST_ITEM", "BUTTON", "TEXT_FIELD" })
            {
                Assert.IsTrue(seen.Contains(Abi.Op(name).Opcode),
                    "the recorded frame does not carry op " + name + ", so this test would not be exercising it");
            }

            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            using (Now.StartUI(k_Screen, 1f))
                replay.RunFrame();

            // Seven content ops - the six kinds, with TEXT twice - plus one OP_SCOPE_CLOSE for each of the three
            // scopes the frame opens (the column, the row inside it, and the one list item).
            Assert.AreEqual(7 + 3, replay.decodedOps, "every op in the stream was decoded");
            Assert.AreEqual(2, replay.decodedControls, "the button and the text field are the two controls");

            var identities = new HashSet<NowResolvedId>(replay.controlIds.Values);
            Assert.AreEqual(2, identities.Count, "and they resolved to two distinct NowUI identities");

            CollectionAssert.IsEmpty(log, "with nothing reported");

            // The point of the test's name: the draws happened inside Now.StartUI, which is what the backend saw.
            TestContext.WriteLine("render backend ops this frame: " + BridgeTestHost.backend.ops.Count +
                                  " (fixtures: " + BridgeTestHost.hasResources + ")");
            Assert.IsTrue(BridgeTestHost.backend.ops.Count > 0,
                "the render backend recorded nothing at all, which would mean Now.StartUI was never entered");
        }

        // -------------------------------------------------------------------- acceptance 2: the same draw list

        [Test]
        public void ExactLayoutOnAndOffProduceTheSameDrawList()
        {
            RecordedFrame.RequireNode();

            // The two frames differ in exactly one bit - frameFlags' exactLayout - because js/run.mjs records the
            // same draw with `new Recorder({ exactLayout: false })` for the one-pass mode.
            int usedExact;
            BridgeRecorder exact = RecordedFrame.Load("w4", out usedExact);
            int usedOnePass;
            BridgeRecorder onePass = RecordedFrame.Load("w4-onepass", out usedOnePass);

            Assert.IsTrue(exact.exactLayout, "the 'w4' frame asks for the measured path");
            Assert.IsFalse(onePass.exactLayout, "and the 'w4-onepass' frame does not");
            Assert.AreEqual(usedExact, usedOnePass, "otherwise the two frames are byte-identical in length");

            // Settled frames, not first frames. An auto group extent "resolves from the previous frame in a
            // one-pass host" (NowLayout.cs:1260-1266), so frame 1 legitimately differs between the two paths -
            // that difference is the whole point of exactLayout. What must agree is the steady state.
            string exactLog = DrawUntilSettled(exact);
            string onePassLog = DrawUntilSettled(onePass);

            if (!BridgeTestHost.hasResources)
            {
                Assert.Inconclusive(
                    "the material and font fixtures were not found, so both op logs are empty and this comparison " +
                    "proves nothing. Build Standalone/Tests once (its Fixtures/ directory is what this reads) and " +
                    "run again.");
            }

            TestContext.WriteLine("fixtures: " + BridgeTestHost.hasResources +
                                  "   backend ops in the settled frame: " + BridgeTestHost.backend.ops.Count +
                                  "   log chars: " + exactLog.Length);
            TestContext.WriteLine(exactLog.Length > 1200 ? exactLog.Substring(0, 1200) : exactLog);

            Assert.Greater(exactLog.Length, 0, "the measured path drew something");
            Assert.AreEqual(exactLog, onePassLog,
                "exactLayout changed WHAT was drawn rather than only how many passes it took to work it out. The " +
                "measure pass is supposed to be passive - draws suppressed, input inert - so a difference here " +
                "means the first pass is reaching the backend.");
        }

        /// <summary>
        /// Replays <paramref name="recorder"/> four times and returns the op log of the last frame. Four because
        /// deferred sizes need one frame to settle in the one-pass host and the comparison must be of two settled
        /// frames; the extra two are slack.
        /// </summary>
        private static string DrawUntilSettled(BridgeRecorder recorder)
        {
            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            for (int frame = 0; frame < 4; ++frame)
            {
                NowRuntime.BeginFrame();
                BridgeTestHost.backend.Clear();

                using (Now.StartUI(k_Screen, 1f))
                    replay.RunFrame();
            }

            CollectionAssert.IsEmpty(log);
            return BridgeTestHost.backend.ToLog();
        }

        // ------------------------------------------------------------------------------- acceptance 3: passes

        [Test]
        public void TheReplayRunsTwiceUnderRunMeasuredAndOnceWithoutIt()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder exact = RecordedFrame.Load("w4", out used);
            BridgeRecorder onePass = RecordedFrame.Load("w4-onepass", out used);

            var log = new List<string>();

            BridgeReplay exactReplay = Replay(exact, log);
            using (Now.StartUI(k_Screen, 1f))
                exactReplay.RunFrame();

            BridgeReplay onePassReplay = Replay(onePass, log);
            using (Now.StartUI(k_Screen, 1f))
                onePassReplay.RunFrame();

            Assert.AreEqual(2, exactReplay.passes,
                "RunMeasured runs the whole callback twice (NowLayout.cs:1578-1590) - not 'one extra decode', " +
                "which is what section 5.7 corrects. If this reads 1, the exactLayout branch is not being taken " +
                "and every deferred size is a frame late.");

            Assert.AreEqual(1, onePassReplay.passes, "and the Area branch runs it once");
            CollectionAssert.IsEmpty(log);
        }

        [Test]
        public void TheAuthorsDrawFunctionRunsOncePerFrameWhateverThePassCountIs()
        {
            // The half of section 5.7 that a pass counter cannot see, asserted where it can be: the recorder is
            // driven ONCE per frame, from outside Now.StartUI, and the replay is what runs twice. A RecordFunc
            // that counts its own calls is the only place this is observable, because the author's draw function
            // is on the other side of the boundary.
            int recordCalls = 0;

            int used;
            BridgeRecorder recorder = RecordedFrame.LoadRaw("w4", out used, () => ++recordCalls);
            recorder.Preamble(used);

            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            using (Now.StartUI(k_Screen, 1f))
                replay.RunFrame();

            Assert.AreEqual(1, recordCalls,
                "Record was called more than once for one frame. Section 5.7 puts it OUTSIDE RunMeasured for " +
                "exactly this reason: inside, the author's JavaScript would run twice, read state twice and " +
                "double every handler.");
            Assert.AreEqual(2, replay.passes, "while the replay itself ran twice, which is the intended split");
        }

        // ----------------------------------------------------------------------------- acceptance 4: the throw

        [Test]
        public void AJavaScriptThrowMidDrawLeavesABalancedShortBufferAndNowUIUntouched()
        {
            RecordedFrame.RequireNode();

            int usedThrow;
            BridgeRecorder faulted = RecordedFrame.LoadRaw("throw", out usedThrow);

            // NOWUI IS UNTOUCHED, and this is the assertion that says so: nothing below has run yet. The throw
            // happened in JavaScript, inside `record`, before a single byte crossed - and Preamble, which is the
            // first managed code to look at the buffer, has not been called either.
            Assert.IsTrue(faulted.faulted || (faulted.Slot(Abi.HdrFrameFlags) & Abi.FlagFaulted) != 0,
                "the frame does not carry the faulted flag, so the recorder did not notice the throw");

            // BALANCED: the validator's rule 5 is what proves it, and it runs before anything is replayed. The
            // draw function threw with a scope open; the recorder's finally closed it, so the stream nests.
            Assert.DoesNotThrow(() => faulted.Preamble(usedThrow),
                "the buffer left by a throw did not validate. Section 4.4 says the recorder closes every scope on " +
                "every path out, so an unbalanced stream here is a recorder bug, not an author error.");

            // SHORT: it stops at the throw. The same draw without the throw carries strictly more.
            int usedWhole;
            BridgeRecorder whole = RecordedFrame.Load("w4", out usedWhole);
            Assert.Less(faulted.opEnd - faulted.opStart, whole.opEnd - whole.opStart,
                "the faulted frame is not shorter than a complete one, so nothing was actually cut off");

            // And it replays: what was recorded before the throw draws, the rest simply is not there.
            var log = new List<string>();
            BridgeReplay replay = Replay(faulted, log);

            using (Now.StartUI(k_Screen, 1f))
                replay.RunFrame();

            Assert.AreEqual(4, replay.decodedOps,
                "two TEXT ops, the COLUMN, and the OP_SCOPE_CLOSE the recorder's finally emitted for the author - " +
                "the prefix, and nothing after it");
            Assert.AreEqual(0, replay.decodedControls, "the throw happened before any control was reached");
            CollectionAssert.IsEmpty(log, "and the decoder reported nothing: a faulted frame is a SHORT frame, not a bad one");
        }
    }
}
