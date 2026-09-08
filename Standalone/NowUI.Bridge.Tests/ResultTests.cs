// W5's acceptance, managed half. Docs/Standalone/M3-Spec.md section 9 (W5), and section 6.
//
//   "if (ui.button('Add')) fires exactly once per click; state.name = ui.textField('name', state.name)
//    round-trips a typed character with no revert; a programmatic state.name = '' in a handler clears the field
//    on the next frame; a result written during a measure pass never reaches JavaScript."
//
// The four are split across two files on purpose, because the behaviour is.
//
//   * The LATCH and the RECONCILIATION RULE live in JavaScript (wwwroot/nowui/results.js), and js/run.mjs asserts
//     them there - fires-once, no-revert, the programmatic write, the echo, the missed read. Asserting them in C#
//     would mean re-implementing them in C#, which would assert a copy.
//   * What lives HERE is everything the managed side owns: that a real click through NowUI's own input path
//     produces a `clicked` record on exactly the right rid; that a real keystroke through NowTextField produces
//     the post-draw string; that a measure pass writes nothing; that a control which stopped being drawn leaves
//     no record behind.
//   * And one test bridges the two: TheManagedTableIsReadBackByResultsJs dumps a table a REAL replay wrote and
//     has node decode it through results.js. Without that, both halves could agree with themselves and disagree
//     with each other, and every test in both files would still be green.
//
// Input is NowInputReplay - NowUI's own synthetic provider, compiled by reference from Assets/NowUITests - so a
// click here is a pointer press and release at a coordinate, not a flag set by hand.

using System;
using System.Collections.Generic;
using System.IO;
using NowUI;
using NowUI.Bridge;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class ResultTests
    {
        private static readonly NowRect k_Screen = new NowRect(0f, 0f, 640f, 480f);

        /// <summary>
        /// The root area pads by 16 and the button is the first thing the <c>w5</c> frame draws, so this point is
        /// two pixels inside its top-left corner whatever the label measures to.
        /// </summary>
        private static readonly Vector2 k_OnTheButton = new Vector2(18f, 18f);

        private NowInputReplay m_Input;
        private INowInputProvider m_PreviousProvider;

        [SetUp]
        public void SetUp()
        {
            m_Input = new NowInputReplay();
            m_PreviousProvider = NowInput.defaultProvider;

            // Now.StartUI pulls from defaultProvider, which is how the browser host feeds it too (Program.cs sets
            // the same field from WebInput). Driving the bridge through the same door the browser uses is what
            // makes a click here mean what a click there means.
            NowInput.defaultProvider = m_Input;
            NowTextInput.source = m_Input;
        }

        [TearDown]
        public void TearDown()
        {
            NowTextInput.Reset();
            NowInput.defaultProvider = m_PreviousProvider;
        }

        // -------------------------------------------------------------------------------------------- helpers

        private static BridgeReplay Replay(BridgeRecorder recorder, List<string> log)
        {
            return new BridgeReplay(recorder, log.Add) { screen = k_Screen, trackControlIds = true };
        }

        private static void Frame(BridgeReplay replay)
        {
            NowRuntime.BeginFrame();
            NowTextInput.Invalidate();

            using (Now.StartUI(k_Screen, 1f))
                replay.RunFrame();
        }

        /// <summary>The record for a rid, or null. Draw order is button then field in the <c>w5</c> frame.</summary>
        private static BridgeResults.Record? Find(BridgeResults results, int rid)
        {
            foreach (BridgeResults.Record record in results.Decode())
            {
                if (record.rid == rid) return record;
            }

            return null;
        }

        /// <summary>The two rids the <c>w5</c> frame allocates, read out of the op stream rather than assumed.</summary>
        private static void RidsOf(BridgeRecorder recorder, out int button, out int field)
        {
            button = -1;
            field = -1;

            for (int slot = recorder.opStart; slot < recorder.opEnd;)
            {
                int header = recorder.Slot(slot);
                int opcode = header & 0xFFFF;

                if (opcode == Abi.Op("BUTTON").Opcode) button = recorder.Slot(slot + 1);
                else if (opcode == Abi.Op("TEXT_FIELD").Opcode) field = recorder.Slot(slot + 1);

                slot += 1 + ((header >> 16) & 0xFFFF);
            }

            Assert.AreNotEqual(-1, button, "the w5 frame carries a BUTTON op");
            Assert.AreNotEqual(-1, field, "and a TEXT_FIELD op");
        }

        // ------------------------------------------------------------------------------- acceptance 1: a click

        [Test]
        public void AClickProducesOneClickedRecordOnTheRightRidAndOnlyOnThatFrame()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder recorder = RecordedFrame.Load("w5", out used);
            int buttonRid;
            int fieldRid;
            RidsOf(recorder, out buttonRid, out fieldRid);

            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            // Frame 1: nothing has happened. It also establishes the button's rect, which is what the press then
            // lands on - a control has no rect until it has been laid out once.
            m_Input.Idle();
            Frame(replay);
            Assert.IsFalse(Find(replay.results, buttonRid).Value.Has(BridgeFlags.Clicked),
                "a button nobody has pressed does not report a click");

            m_Input.Press(k_OnTheButton);
            Frame(replay);

            m_Input.Release(k_OnTheButton);
            Frame(replay);

            BridgeResults.Record clicked = Find(replay.results, buttonRid).Value;
            Assert.IsTrue(clicked.Has(BridgeFlags.Clicked),
                "the press and release over the button did not produce a click. If this fails, either the button " +
                "is not where the test assumes (the root area pads by 16 and the button is drawn first) or the " +
                "input provider is not reaching Now.StartUI.");

            // ONLY that rid. The table is indexed by rid on the JavaScript side, and a click attributed to the
            // wrong control is the failure the whole identity model exists to prevent.
            Assert.IsFalse(Find(replay.results, fieldRid).Value.Has(BridgeFlags.Clicked),
                "the text field also reported a click, so the flags are not per-control");

            // And exactly one frame. The next frame's table has the record with the flag clear, which is what
            // makes the JavaScript latch a latch rather than a permanent true.
            m_Input.Idle();
            Frame(replay);
            Assert.IsFalse(Find(replay.results, buttonRid).Value.Has(BridgeFlags.Clicked),
                "the click was reported on a second frame, so it is a state rather than an event");

            CollectionAssert.IsEmpty(log);
        }

        // ------------------------------------------------------------------------------ acceptance 2: a keystroke

        [Test]
        public void ATypedCharacterComesBackAsThePostDrawStringInTheRecord()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder recorder = RecordedFrame.Load("w5", out used);
            int buttonRid;
            int fieldRid;
            RidsOf(recorder, out buttonRid, out fieldRid);

            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            m_Input.Idle();
            Frame(replay);

            BridgeResults.Record before = Find(replay.results, fieldRid).Value;
            Assert.AreEqual(BridgeValueKind.Str, before.kind, "a text field is a string-valued record");
            Assert.AreEqual("ab", before.text, "and it starts as the value the op carried");
            Assert.IsTrue(before.hasRect, "with the rect its consumer handed back for free");
            Assert.Greater(before.rect.width, 0f, "which is a real rect");

            // Focus it, at its own reported rect - no guessing, because the table gave it to us.
            // Past the end of "ab", so the caret lands after the text rather than in front of it. Measured, not
            // reasoned: clicking near the left edge put the caret at 0 and typing produced "cab".
            var inside = new Vector2(before.rect.x + before.rect.width - 8f, before.rect.y + before.rect.height * 0.5f);
            m_Input.Press(inside);
            Frame(replay);
            m_Input.Release(inside);
            Frame(replay);

            m_Input.Text("c");
            Frame(replay);

            BridgeResults.Record after = Find(replay.results, fieldRid).Value;
            Assert.AreEqual("abc", after.text,
                "the post-draw string is what the record must carry - it is the only thing that can reach the " +
                "author, because NowTextField stores no text of its own (NowTextField.cs:685)");
            Assert.IsTrue(after.Has(BridgeFlags.Changed), "and the changed flag came from the consumer's result");

            // THE REVERT, DEMONSTRATED. This recorder replays one fixed buffer forever - the JavaScript recorder
            // is not in the loop - so the next frame's op still carries "ab" and NowTextField clamps its edit
            // state back to it. That is precisely the failure section 6.4 describes, and the reason the recorder
            // must emit the RESOLVED value rather than the caller's: with results.js in the loop the op would
            // carry "abc" on this frame and the character would survive.
            m_Input.Idle();
            Frame(replay);
            Assert.AreEqual("ab", Find(replay.results, fieldRid).Value.text,
                "the character survived a frame in which the op carried the stale value, which would mean the " +
                "caller's string is NOT authoritative and section 6.4's rule is solving a problem that does not " +
                "exist");

            CollectionAssert.IsEmpty(log);
        }

        // ------------------------------------------------------------------- acceptance 4: the measure pass

        [Test]
        public void AResultWrittenDuringAMeasurePassNeverReachesJavaScript()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder exact = RecordedFrame.Load("w5", out used);

            var log = new List<string>();
            BridgeReplay replay = Replay(exact, log);

            m_Input.Idle();
            Frame(replay);

            Assert.AreEqual(2, replay.passes, "the w5 frame asks for exactLayout, so the decode ran twice");
            Assert.AreEqual(2, replay.results.recordCount,
                "and the sealed table holds ONE record per control, not two. Two would mean the measure pass " +
                "appended its own - inert - copies, and JavaScript would read a table in which every control " +
                "appears twice with the second one blank.");

            Assert.AreEqual(2, replay.results.suppressedWrites,
                "exactly the measure pass's two writes were refused. Zero would mean the suppression is not " +
                "running and the table only looks right because the second pass overwrote the first; more than " +
                "two would mean something is drawing that should not be.");

            // The suppression is NowLayout.isMeasurePass || NowInput.isPassive, and by the time the frame is over
            // neither is set - so the flag being false here is not what made the writes land.
            Assert.IsFalse(BridgeResults.passive, "the pass flags are clear outside a frame");
        }

        [Test]
        public void AOnePassFrameSuppressesNothing()
        {
            RecordedFrame.RequireNode();

            // The control for the test above. Same draw, exactLayout off: one pass, one record per control, and
            // nothing refused - so the count above is measuring the measure pass rather than some other refusal.
            int used;
            BridgeRecorder onePass = RecordedFrame.Load("w4-onepass", out used);

            var log = new List<string>();
            BridgeReplay replay = Replay(onePass, log);

            m_Input.Idle();
            Frame(replay);

            Assert.AreEqual(1, replay.passes);
            Assert.AreEqual(2, replay.results.recordCount, "the button and the text field");
            Assert.AreEqual(0, replay.results.suppressedWrites, "nothing was refused");
        }

        // ------------------------------------------------------------------ controls not drawn produce no record

        [Test]
        public void AControlThatStoppedBeingDrawnLeavesNoRecordBehind()
        {
            RecordedFrame.RequireNode();

            // Two frames of the same session, from two different recorded buffers: the second draws a label and
            // nothing else. Section 6.1: "controls not drawn produce no record", which is what stops the table
            // serving a click for a button that stopped existing.
            int usedW5;
            BridgeRecorder withControls = RecordedFrame.Load("w5", out usedW5);
            int usedHello;
            BridgeRecorder withoutControls = RecordedFrame.Load("hello", out usedHello);

            var log = new List<string>();

            BridgeReplay first = Replay(withControls, log);
            m_Input.Idle();
            Frame(first);
            Assert.AreEqual(2, first.results.recordCount);

            BridgeReplay second = Replay(withoutControls, log);
            Frame(second);
            Assert.AreEqual(0, second.results.recordCount,
                "the table still holds records for controls this frame did not draw");
            Assert.AreEqual(BridgeResults.HeaderSlots, second.results.sealedSlots,
                "and the sealed table is the header alone, so JavaScript slices two slots and loads nothing");

            CollectionAssert.IsEmpty(log);
        }

        // ------------------------------------------------------------------------- the two halves, meeting

        [Test]
        public void TheManagedTableIsReadBackByResultsJs()
        {
            RecordedFrame.RequireNode();

            int used;
            BridgeRecorder recorder = RecordedFrame.Load("w5", out used);
            int buttonRid;
            int fieldRid;
            RidsOf(recorder, out buttonRid, out fieldRid);

            var log = new List<string>();
            BridgeReplay replay = Replay(recorder, log);

            // TWO tables, not one, and the reason is worth a sentence. This recorder replays one fixed buffer
            // forever, so the op always carries "ab" and NowTextField clamps back to it on any frame the author
            // did not type - which means no single frame's table here can hold both a live keystroke and a click.
            // So the value table and the event table are dumped separately, and each is read back through
            // results.js.
            m_Input.Idle();
            Frame(replay);

            NowRect fieldRect = Find(replay.results, fieldRid).Value.rect;
            var inside = new Vector2(fieldRect.x + fieldRect.width - 8f, fieldRect.y + fieldRect.height * 0.5f);

            m_Input.Press(inside);
            Frame(replay);
            m_Input.Release(inside);
            Frame(replay);
            m_Input.Text("c");
            Frame(replay);

            Assert.AreEqual("abc", Find(replay.results, fieldRid).Value.text, "the value table under test");
            string valueJson = ReadBackThroughNode(replay.results);
            TestContext.WriteLine("results.js decoded the value table: " + valueJson);

            m_Input.Press(k_OnTheButton);
            Frame(replay);
            m_Input.Release(k_OnTheButton);
            Frame(replay);

            Assert.IsTrue(Find(replay.results, buttonRid).Value.Has(BridgeFlags.Clicked),
                "the event table under test carries a click");
            string eventJson = ReadBackThroughNode(replay.results);
            TestContext.WriteLine("results.js decoded the event table: " + eventJson);

            StringAssert.Contains("\"records\":2", valueJson,
                "results.js decoded a different number of records than BridgeResults wrote, so the two halves " +
                "disagree about the record layout of section 6.1");

            // The value the field held at the end of the C# frame, arriving at a JavaScript author as the return
            // of ui.textField - and emitted back out as the next op, which is section 6.4's whole point.
            StringAssert.Contains("\"name\":\"abc\"", valueJson, "the post-draw string did not round-trip");
            StringAssert.Contains("\"emitted\":\"abc\"", valueJson,
                "the value came back to the author but was NOT what the next frame's op carried, which is exactly " +
                "the revert section 6.4 describes");
            StringAssert.Contains("\"rect\":[", valueJson, "the rect slots did not survive the record walk");

            StringAssert.Contains("\"clicked\":true", eventJson,
                "the click BridgeResults wrote did not reach ui.button() through results.js");

            CollectionAssert.IsEmpty(log);
        }

        /// <summary>Writes the sealed table to disk in --emit-frame's format and has run.mjs decode it.</summary>
        private static string ReadBackThroughNode(BridgeResults results)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "nowui-results-" + Guid.NewGuid().ToString("N") + ".bin");

            try
            {
                int slotCount = results.sealedSlots;
                int textBytes = results.slots[BridgeResults.HdrTextBytes];

                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(slotCount);
                    writer.Write(textBytes);
                    for (int i = 0; i < slotCount; ++i) writer.Write(results.slots[i]);
                    writer.Write(results.text, 0, textBytes);
                }

                return Node.Run("--read-results", path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
