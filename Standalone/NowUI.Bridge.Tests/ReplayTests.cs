// The two halves of the bridge, meeting. Docs/Standalone/M3-Spec.md sections 5.2, 5.6, 5.7 and 9 (W2, W3).
//
// Every frame in this fixture was recorded by wwwroot/nowui/recorder.js under node and written to disk, then read
// back and replayed here. That is the point: ProtocolTests builds frames in C#, which proves the validator right
// about frames a C# writer produced, and proves nothing about whether the JavaScript recorder writes what the
// managed validator expects. These do.

using System;
using System.Collections.Generic;
using System.IO;
using NowUI;
using NowUI.Bridge;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class ReplayTests
    {
        private static readonly NowRect k_Screen = new NowRect(0f, 0f, 640f, 480f);

        /// <summary>Has node record one frame through the real recorder, and hands it to a BridgeRecorder.</summary>
        /// <remarks>W4 moved the body of this to RecordedFrame, where three fixtures share it.</remarks>
        private static BridgeRecorder RecordWithNode(string mode, out int usedSlots)
        {
            return RecordedFrame.LoadRaw(mode, out usedSlots);
        }

        private static BridgeReplay Replay(BridgeRecorder recorder, int usedSlots, out List<string> log)
        {
            var messages = new List<string>();
            log = messages;

            recorder.Preamble(usedSlots);

            var replay = new BridgeReplay(recorder, messages.Add) { screen = k_Screen, trackControlIds = true };

            // W4 moved the root scope and the root area out of the decode and into RunFrame, where section 5.7
            // puts them. `replay.replay()` is now one decode PASS and nothing else - it is the cached Action
            // RunMeasured is handed - so a caller that wants a frame asks for a frame.
            using (Now.StartUI(1f))
                replay.RunFrame();

            return replay;
        }

        // ------------------------------------------------------------------------------------------------ W2

        [Test]
        public void W2_AFrameRecordedByRecorderJsIsAcceptedAndDrawsTheWordHello()
        {
            int used;
            BridgeRecorder recorder = RecordWithNode("hello", out used);

            List<string> log;
            BridgeReplay replay = Replay(recorder, used, out log);

            Assert.AreEqual(1, replay.decodedOps, "one op crossed and one op was decoded");
            Assert.AreEqual("hello", recorder.Text(recorder.Slot(recorder.opStart + 1)),
                "and the string the op points at is the word the acceptance asks for");
            CollectionAssert.IsEmpty(log, "with nothing reported");
        }

        [Test]
        public void W2_TheHelloFrameIsTwelveSlots()
        {
            int used;
            BridgeRecorder recorder = RecordWithNode("hello", out used);
            recorder.Preamble(used);

            Assert.AreEqual(8 + 2 + 2, used,
                "eight slots of fixed header, one two-slot volatile-string entry, and a two-slot op stream");
            Assert.AreEqual(Abi.Magic, recorder.Slot(Abi.HdrMagic));
            Assert.AreEqual(Abi.SurfaceHash, recorder.Slot(Abi.HdrSurfaceHash));
        }

        // ------------------------------------------------------------------------------------------------ W3

        [Test]
        public void W3_TwoButtonsWithOneKeyInTwoColumnsResolveToTwoDifferentNowUIIdentities()
        {
            int used;
            BridgeRecorder recorder = RecordWithNode("w3", out used);

            List<string> log;
            BridgeReplay replay = Replay(recorder, used, out log);

            Assert.AreEqual(4, replay.decodedControls, "two 'shared' buttons and two 'remove' buttons");

            var identities = new HashSet<NowResolvedId>(replay.controlIds.Values);
            Assert.AreEqual(4, identities.Count,
                "four controls, four distinct NowUI identities - the two that share a KEY do not share an IDENTITY, " +
                "because their paths differ");

            CollectionAssert.IsEmpty(log,
                "and the duplicate backstop stayed silent, which is what it is supposed to do when the record-time " +
                "checks are working");
        }

        [Test]
        public void W3_TheReplayIsIdempotent_TwoPassesResolveEveryControlIdentically()
        {
            // Section 5.6, and the whole legality of exactLayout: the buffer is decoded TWICE per frame, and the
            // second decode must resolve every control to the same NowResolvedId as the first.
            int used;
            BridgeRecorder recorder = RecordWithNode("w3", out used);
            recorder.Preamble(used);

            var log = new List<string>();
            var replay = new BridgeReplay(recorder, log.Add) { screen = k_Screen, trackControlIds = true };

            Dictionary<int, NowResolvedId> first = null;
            Dictionary<int, NowResolvedId> second = null;

            // The two passes are RunMeasured's own, not two calls from here. That is the point of doing it this
            // way after W4: what is asserted is the double decode the REAL frame performs under exactLayout, on
            // the real measure/draw cycle, rather than two decodes a test arranged.
            replay.passCompleted = pass =>
            {
                if (pass == 1) first = new Dictionary<int, NowResolvedId>(replay.controlIds);
                else second = new Dictionary<int, NowResolvedId>(replay.controlIds);
            };

            using (Now.StartUI(1f))
                replay.RunFrame();

            Assert.AreEqual(2, replay.passes, "exactLayout was set on this frame, so RunMeasured ran the decode twice");
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            CollectionAssert.AreEquivalent(first, second,
                "the second pass resolved a different identity for at least one control, which would mean explicit " +
                "ids ARE being occurrence-salted and exactLayout is not legal");
            CollectionAssert.IsEmpty(log);
        }

        [Test]
        public void W3_ListItemsKeepTheirIdentityWhenTheListIsReplayedAgain()
        {
            int usedA;
            BridgeRecorder a = RecordWithNode("w3", out usedA);
            List<string> logA;
            BridgeReplay replayA = Replay(a, usedA, out logA);
            var firstRun = new Dictionary<int, NowResolvedId>(replayA.controlIds);

            int usedB;
            BridgeRecorder b = RecordWithNode("w3", out usedB);
            List<string> logB;
            BridgeReplay replayB = Replay(b, usedB, out logB);

            // A second session's recorder assigns the same rids to the same paths and the same intern handles to
            // the same strings, so the identities must match value for value.
            CollectionAssert.AreEquivalent(firstRun, new Dictionary<int, NowResolvedId>(replayB.controlIds));
        }

        // ------------------------------------------------------------------------------------------------ refusal

        [Test]
        public void ARefusedFrameNeverReachesNowUI()
        {
            // The other half of W2's acceptance. The frame is a real recorded one, truncated by a slot; Preamble
            // refuses it, and the replay - which is the only thing in the bridge that calls NowUI at all - is
            // never entered. decodedOps stays at its initial zero because Replay() was never run.
            int used;
            BridgeRecorder recorder = RecordWithNode("hello", out used);

            var log = new List<string>();
            var replay = new BridgeReplay(recorder, log.Add) { screen = k_Screen };

            NowBridgeProtocolException e = Assert.Throws<NowBridgeProtocolException>(
                () => recorder.Preamble(used - 1));

            Assert.AreEqual(0, replay.decodedOps, "nothing was decoded");
            Assert.AreEqual(0, replay.decodedControls, "and nothing was drawn");
            CollectionAssert.IsEmpty(log);
            StringAssert.Contains("malformed command buffer at slot", e.Message);
        }
    }
}
