// W9 - the drawing and styling ops, decoded and drawn.
//
// WHAT "AND DRAWN" MEANS HERE, because a test that only proves the decoder ran is worth very little on this
// milestone. Every op in this file replays into a real NowUI member against the real fixtures, and the assertion
// is on the GEOMETRY the render backend received - the vertex count for "something was tessellated", and the
// submitted XY extent for "it landed where the coordinate model says it should". NowUI batches, so a DrawMesh
// count cannot tell a circle that filled from a circle whose builder was configured and then dropped; a vertex
// count can. See VertexCountingBackend for the longer version of that argument.
//
// The frames are built with TestFrame rather than recorded through node. That is the right trade for these ops:
// the JavaScript surface for them is the NEXT unit's work, so there is no author-facing function to record, and
// what needs proving here is that the wire shape both halves of the ABI agree on decodes into the right NowUI
// call. js/run.mjs's own checks cover the recorder; AbiTests covers the two tables agreeing.

using System;
using System.Collections.Generic;
using NowUI;
using NowUI.Bridge;
using NowUI.Markdown;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class DrawingTests
    {
        private static readonly NowRect k_Screen = new NowRect(0f, 0f, 640f, 480f);

        // ------------------------------------------------------------------------------------------- plumbing

        private static BridgeReplay Run(TestFrame frame, out List<string> log)
        {
            var recorder = new BridgeRecorder();
            int used = recorder.Record(frame.AsRecordFunc(), 1);
            recorder.Preamble(used);

            var messages = new List<string>();
            var replay = new BridgeReplay(recorder, messages.Add) { screen = k_Screen };

            BridgeTestHost.counting.ResetCounters();

            using (Now.StartUI(1f))
                replay.RunFrame();

            log = messages;
            return replay;
        }

        /// <summary>Runs the frame and returns the vertices NowUI submitted for it.</summary>
        private static long Vertices(TestFrame frame)
        {
            List<string> log;
            Run(frame, out log);
            CollectionAssert.IsEmpty(log, "the replay reported something");
            return BridgeTestHost.counting.vertices;
        }

        private static void RequireFixtures()
        {
            if (!BridgeTestHost.hasResources)
                Assert.Ignore("the NowUI material and font fixtures are not on disk, so nothing tessellates and " +
                              "every geometry assertion in this fixture would be vacuous.");
        }

        /// <summary>
        /// An empty canvas of the given size, and the ops the caller adds inside it. Sized rather than grown, so
        /// its box is exact on the first frame - a canvas that grows is one frame stale in its SIZE, which is
        /// correct and is not what these tests are measuring.
        /// </summary>
        private static TestFrame Canvas(out int canvasRid, float width = 400f, float height = 300f, float top = 0f)
        {
            var frame = new TestFrame();
            int seg = frame.Intern("canvas");
            canvasRid = 1;

            if (top > 0f)
            {
                // A plain SPACE pushes the canvas down the root column, which is how the origin becomes something
                // other than zero without the test having to reach into NowUI's layout.
                frame.Op(Abi.Op("SPACE"), TestFrame.F(top));
            }

            frame.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(width), TestFrame.F(height));
            frame.Op(Abi.Op("CANVAS"), canvasRid, seg);
            return frame;
        }

        /// <summary>An OPTS op carrying one literal-RGBA paint under <c>color</c>.</summary>
        private static int[] ColorOpts(Color color)
        {
            return TestFrame.Args(Abi.OptColor, BridgePaintPack(color), 0);
        }

        private static int BridgePaintPack(Color c)
        {
            uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return unchecked((int)(r | (g << 8) | (b << 16) | (a << 24)));
        }

        // ------------------------------------------------------------------------------ the eight drawing ops

        [Test]
        public void AnEmptyCanvasDecodesAndOpensAndClosesOneScope()
        {
            int rid;
            TestFrame frame = Canvas(out rid);
            frame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            CollectionAssert.IsEmpty(log);
            // decodedOps is reset at the top of every pass, so this is one pass's count, not the frame's.
            Assert.AreEqual(3, replay.decodedOps, "OPTS, CANVAS and OP_SCOPE_CLOSE");
        }

        [Test]
        public void ACanvasHandsItsMeasuredBoxBackThroughTheResultTable()
        {
            int rid;
            TestFrame frame = Canvas(out rid, width: 320f, height: 180f);
            frame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            BridgeResults.Record record;
            Assert.IsTrue(TryFind(replay, rid, out record), "the canvas wrote no result record at all");
            Assert.IsTrue((record.flags & BridgeFlags.HasRect) != 0, "and no rect rode with it");
            Assert.AreEqual(320f, record.rect.width, 0.5f);
            Assert.AreEqual(180f, record.rect.height, 0.5f);
        }

        [TestCase("RECT")]
        [TestCase("CIRCLE")]
        [TestCase("LINE")]
        [TestCase("BEZIER")]
        [TestCase("TRIANGLE")]
        [TestCase("POLYGON")]
        [TestCase("GRADIENT")]
        public void EveryDrawingOpSubmitsGeometry(string op)
        {
            RequireFixtures();

            int rid;
            TestFrame empty = Canvas(out rid);
            empty.ScopeClose();
            long baseline = Vertices(empty);

            TestFrame drawn = Canvas(out rid);
            Draw(drawn, op);
            drawn.ScopeClose();
            long withOp = Vertices(drawn);

            TestContext.WriteLine(op + ": baseline " + baseline + " vertices, with the op " + withOp);

            Assert.Greater(withOp, baseline,
                op + " decoded without complaint and added no vertices, which means the builder was configured " +
                "and never reached the mesh." +
                (op == "GRADIENT"
                    ? " For GRADIENT specifically, check BridgeTestHost.CanvasMaterialAliases first: " +
                      "NowGradientMaterials.TryGet needs Resources/NowUI/GradientMaterialUGUI, which the fixture " +
                      "exporter drops and that alias mints."
                    : string.Empty));
        }

        /// <summary>One of each drawing op, at coordinates that fit inside the 400x300 canvas above.</summary>
        private static void Draw(TestFrame frame, string op)
        {
            // A colour on every one, so that none of them is testing the theme fallback by accident.
            frame.Op(Abi.Op("OPTS"), ColorOpts(new Color(0.2f, 0.6f, 0.9f, 1f)));

            switch (op)
            {
                case "RECT":
                    frame.Op(Abi.Op("RECT"), TestFrame.V4(10f, 10f, 120f, 80f));
                    break;

                case "CIRCLE":
                    frame.Op(Abi.Op("CIRCLE"), TestFrame.Args(TestFrame.V2(100f, 100f), TestFrame.V2(40f, 40f)));
                    break;

                case "LINE":
                    frame.Op(Abi.Op("LINE"), TestFrame.Args(TestFrame.V2(10f, 10f), TestFrame.V2(200f, 150f)));
                    break;

                case "BEZIER":
                    frame.Op(Abi.Op("BEZIER"), TestFrame.Args(
                        TestFrame.V2(10f, 10f), TestFrame.V2(60f, 200f),
                        TestFrame.V2(180f, 0f), TestFrame.V2(240f, 150f)));
                    break;

                case "TRIANGLE":
                    frame.Op(Abi.Op("TRIANGLE"), TestFrame.Args(
                        TestFrame.V2(20f, 20f), TestFrame.V2(180f, 40f), TestFrame.V2(100f, 200f)));
                    break;

                case "POLYGON":
                    frame.Op(Abi.Op("POLYGON"), TestFrame.Args(
                        4,
                        TestFrame.V2(20f, 20f), TestFrame.V2(200f, 30f),
                        TestFrame.V2(220f, 180f), TestFrame.V2(40f, 160f)));
                    break;

                case "GRADIENT":
                    frame.Op(Abi.Op("GRADIENT"), TestFrame.Args(
                        TestFrame.V4(20f, 20f, 200f, 120f),
                        BridgePaintPack(Color.red), 0,
                        1, (int)NowColorToken.Accent,          // the second stop as a THEME TOKEN
                        (int)NowGradientKind.Linear));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op);
            }
        }

        /// <summary>
        /// AN UNFILLED SHAPE IS AN OUTLINE IN ITS OWN COLOUR, not an invisible one.
        /// </summary>
        /// <remarks>
        /// This is a regression test for a defect that only the browser found. Every shape builder leaves
        /// <c>outlineColor</c> at <c>default</c>, which is transparent black rather than "unset", and
        /// <c>SetColor</c> does not touch it - so <c>{ color: 'danger', fill: false, stroke: 6 }</c> drew a ring
        /// in transparent black: no error, no warning, and the one shape missing from W9's first capture. The
        /// decoder now fills the outline in from the fill colour when the author named no strokeColor.
        /// </remarks>
        [Test]
        public void AnUnfilledShapeDrawsItsOutlineInItsOwnColourRatherThanInvisibly()
        {
            RequireFixtures();

            int rid;
            TestFrame frame = Canvas(out rid);

            // color + fill:false + stroke, and deliberately NO strokeColor.
            frame.Op(Abi.Op("OPTS"), TestFrame.Args(
                Abi.OptColor | Abi.OptStroke | Abi.OptFill,
                BridgePaintPack(new Color(0.9f, 0.2f, 0.2f, 1f)), 0,
                6f,
                0));
            frame.Op(Abi.Op("CIRCLE"), TestFrame.Args(TestFrame.V2(100f, 100f), TestFrame.V2(48f, 32f)));
            frame.ScopeClose();

            long ring = Vertices(frame);

            int emptyRid;
            TestFrame empty = Canvas(out emptyRid);
            empty.ScopeClose();
            long baseline = Vertices(empty);

            Assert.Greater(ring, baseline, "the unfilled ring produced no geometry at all");

            // Geometry alone would pass even with a transparent outline, because the vertices exist either way.
            // What the fix changed is the COLOUR, so assert that directly against the decode.
            var options = default(BridgeOptions);
            options.mask = Abi.OptColor | Abi.OptStroke | Abi.OptFill;
            options.colorTag = BridgePaint.TagLiteral;
            options.colorValue = BridgePaintPack(new Color(0.9f, 0.2f, 0.2f, 1f));
            options.stroke = 6f;
            options.fill = false;

            Assert.IsTrue(options.NeedsStrokeColor, "an unfilled, stroked shape must be given an outline colour");

            Color outline = options.StrokeColor(NowTheme.themeAsset, NowColorToken.Text);
            Assert.Greater(outline.a, 0.9f, "the outline was left transparent, which is the whole defect");
            Assert.AreEqual(0.9f, outline.r, 1f / 255f, "and it is the FILL colour, which is what the author meant");
        }

        // --------------------------------------------------------------------------------- the coordinate model

        /// <summary>
        /// THE ONE THAT MATTERS. A drawing's coordinates are canvas-local and the DECODER adds the origin, so a
        /// rect at (10, 0) inside a canvas whose top is 200px down the page must land in exactly the same place as
        /// a rect at screen (10, 200) drawn with no canvas at all.
        /// </summary>
        /// <remarks>
        /// Asserted as an EQUALITY of submitted geometry rather than against an expected coordinate, because
        /// NowUI's vertex positions carry its own sign and offset conventions and a test that hard-coded them
        /// would be asserting the renderer rather than the bridge.
        /// </remarks>
        [Test]
        public void ADrawingsCoordinatesAreCanvasLocalAndTheDecoderAddsTheOrigin()
        {
            RequireFixtures();

            // Canvas space: a rect at canvas-local (10, 0), inside a canvas pushed 200px down the page.
            int rid;
            TestFrame canvasFrame = Canvas(out rid, width: 400f, height: 200f, top: 200f);
            canvasFrame.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            canvasFrame.Op(Abi.Op("RECT"), TestFrame.V4(10f, 0f, 40f, 40f));
            canvasFrame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(canvasFrame, out log);
            CollectionAssert.IsEmpty(log);

            Assert.IsTrue(BridgeTestHost.counting.hasGeometry,
                "no vertex positions were read back, so this assertion could not be made");
            Rect inCanvasSpace = BridgeTestHost.counting.geometry;

            // Where the canvas actually is, taken from the box the surface itself reported rather than from the
            // test's own arithmetic - the root area has padding of its own, and a test that assumed otherwise
            // would be asserting NowUI's default insets rather than the bridge's translation.
            BridgeResults.Record canvas;
            Assert.IsTrue(TryFind(replay, rid, out canvas));
            NowRect box = canvas.rect;
            TestContext.WriteLine("the canvas reported itself at " + box);

            // Screen space: no canvas, and the same rect asked for at the absolute coordinates the canvas implies.
            var screenFrame = new TestFrame();
            screenFrame.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            screenFrame.Op(Abi.Op("RECT"), TestFrame.V4(box.x + 10f, box.y, 40f, 40f));

            Vertices(screenFrame);
            Rect inScreenSpace = BridgeTestHost.counting.geometry;

            TestContext.WriteLine("canvas-local " + inCanvasSpace + "   the same place in screen space " + inScreenSpace);

            Assert.AreEqual(inScreenSpace.xMin, inCanvasSpace.xMin, 0.5f,
                "the canvas origin's x was not added to the drawing's x");
            Assert.AreEqual(inScreenSpace.yMin, inCanvasSpace.yMin, 0.5f,
                "the canvas origin's y was not added, so a canvas-local drawing landed at the top of the screen");
            Assert.AreEqual(inScreenSpace.width, inCanvasSpace.width, 0.5f);
            Assert.AreEqual(inScreenSpace.height, inCanvasSpace.height, 0.5f);
        }

        /// <summary>
        /// And the origin UNWINDS. A drawing after a nested canvas closes must be back in the outer canvas's
        /// space, which is what CloseScope's savedOrigin is for.
        /// </summary>
        [Test]
        public void ANestedCanvasRestoresTheEnclosingOriginWhenItCloses()
        {
            RequireFixtures();

            // Outer canvas at the top; a rect at outer-local (10, 20).
            int rid;
            TestFrame reference = Canvas(out rid, width: 400f, height: 300f);
            reference.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            reference.Op(Abi.Op("RECT"), TestFrame.V4(10f, 20f, 40f, 40f));
            reference.ScopeClose();

            Vertices(reference);
            Rect expected = BridgeTestHost.counting.geometry;

            // The same, with an inner canvas opened and closed before the rect. The inner canvas is a real box in
            // the outer column, so it moves the rect down - which is why the rect is drawn at a local y reduced by
            // the inner canvas's height, and must land in the same place.
            var nested = new TestFrame();
            int outerSeg = nested.Intern("outer");
            int innerSeg = nested.Intern("inner");

            nested.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(400f), TestFrame.F(300f));
            nested.Op(Abi.Op("CANVAS"), 1, outerSeg);

            nested.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(100f), TestFrame.F(50f));
            nested.Op(Abi.Op("CANVAS"), 2, innerSeg);
            nested.ScopeClose();

            // Back in the OUTER canvas's space. The inner canvas consumed 50px of the outer column, so a rect that
            // must land at outer-local y = 20 has to be asked for at y = 20 - and if the origin had not unwound it
            // would be drawn 50px further down, against the inner canvas's top.
            nested.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            nested.Op(Abi.Op("RECT"), TestFrame.V4(10f, 20f, 40f, 40f));
            nested.ScopeClose();

            Vertices(nested);
            Rect actual = BridgeTestHost.counting.geometry;

            TestContext.WriteLine("expected " + expected + "   after the nested canvas " + actual);

            Assert.AreEqual(expected.xMin, actual.xMin, 0.5f);
            Assert.AreEqual(expected.yMin, actual.yMin, 0.5f,
                "the inner canvas's origin was still in force after it closed");
        }

        // ------------------------------------------------------------------------------------------ the scopes

        [Test]
        public void AMaskOpensAndClosesAndTheDrawingInsideItStillReachesTheMesh()
        {
            RequireFixtures();

            int rid;
            TestFrame frame = Canvas(out rid);
            int maskSeg = frame.Intern("mask");

            frame.Op(Abi.Op("MASK"), TestFrame.Args(
                2, maskSeg,
                1,                                          // roundedRect
                TestFrame.V4(0f, 0f, 200f, 150f),
                TestFrame.V4(12f, 12f, 12f, 12f),
                8f));                                       // feather

            frame.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            frame.Op(Abi.Op("RECT"), TestFrame.V4(0f, 0f, 200f, 150f));
            frame.ScopeClose();   // the mask
            frame.ScopeClose();   // the canvas

            long vertices = Vertices(frame);
            Assert.Greater(vertices, 0, "the rect inside the mask reached no mesh");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EveryMaskShapeKindDecodes(int kind)
        {
            int rid;
            TestFrame frame = Canvas(out rid);
            int maskSeg = frame.Intern("mask");

            // The rect slot means different things per kind (see abi.js's MASK_KIND): x,y,w,h for the box kinds,
            // cx,cy,r for a circle, and x0,y0,x1,y1 for a capsule. All five are legal readings of these numbers.
            frame.Op(Abi.Op("MASK"), TestFrame.Args(
                2, maskSeg, kind,
                TestFrame.V4(20f, 20f, 120f, 90f),
                TestFrame.V4(8f, 8f, 8f, 8f),
                0f));

            frame.ScopeClose();
            frame.ScopeClose();

            List<string> log;
            Run(frame, out log);
            CollectionAssert.IsEmpty(log, "mask kind " + kind + " reported something");
        }

        [Test]
        public void AnUnknownMaskKindIsReportedAndFallsBackRatherThanDrawingNothing()
        {
            int rid;
            TestFrame frame = Canvas(out rid);
            int maskSeg = frame.Intern("mask");

            frame.Op(Abi.Op("MASK"), TestFrame.Args(
                2, maskSeg, 99,
                TestFrame.V4(20f, 20f, 120f, 90f), TestFrame.V4(0f, 0f, 0f, 0f), 0f));
            frame.ScopeClose();
            frame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            // Once per PASS: the report is not deduplicated, and the frame runs twice under exactLayout. That is
            // the existing convention for a decode diagnostic (only a fault is deduplicated, Replay.cs:246), so
            // the count is asserted against the pass count rather than pinned at one.
            Assert.AreEqual(replay.passes, log.Count, "one report per decode pass");
            foreach (string message in log) StringAssert.Contains("shape kind 99", message);
        }

        [Test]
        public void ASplitOpensTwoPanesAndTheRatioComesBackInTheResultTable()
        {
            var frame = new TestFrame();
            int seg = frame.Intern("split");

            frame.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(400f), TestFrame.F(200f));
            frame.Op(Abi.Op("SPLIT"), 1, seg, TestFrame.F(0.25f), (int)NowSplitAxis.Horizontal);

            frame.Op(Abi.Op("PANE"), 0);
            frame.Op(Abi.Op("TEXT"), frame.Volatile("left"));
            frame.ScopeClose();

            frame.Op(Abi.Op("PANE"), 1);
            frame.Op(Abi.Op("TEXT"), frame.Volatile("right"));
            frame.ScopeClose();

            frame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            CollectionAssert.IsEmpty(log);

            BridgeResults.Record record;
            Assert.IsTrue(TryFind(replay, 1, out record), "the split wrote no result record");
            Assert.AreEqual(BridgeValueKind.F32, record.kind);
            Assert.AreEqual(0.25f, record.f32, 0.05f, "the ratio came back changed by something other than a drag");
        }

        [Test]
        public void APaneOutsideASplitIsReportedAndStillBalancesItsBracket()
        {
            var frame = new TestFrame();

            frame.Op(Abi.Op("PANE"), 0);
            frame.Op(Abi.Op("TEXT"), frame.Volatile("orphan"));
            frame.ScopeClose();

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            Assert.AreEqual(replay.passes, log.Count, "one report per decode pass");
            foreach (string message in log) StringAssert.Contains("PANE appeared outside a SPLIT", message);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void AThemeScopeOpensAndClosesAndChangesTheColourAControlDrawsIn(int mode)
        {
            var frame = new TestFrame();
            int seg = frame.Intern("theme");

            frame.Op(Abi.Op("THEME"), 1, seg, mode);
            frame.Op(Abi.Op("TEXT"), frame.Volatile("themed"));
            frame.ScopeClose();

            List<string> log;
            Run(frame, out log);
            CollectionAssert.IsEmpty(log);
        }

        [Test]
        public void TheTwoThemesResolveTheSameTokenToDifferentColours()
        {
            // Not a decode assertion: it is the check that ui.theme('dark') names a real second theme rather than
            // the same asset twice, which is the whole of what the "the host has no resource manifest" blocker was
            // about. The decoder builds its two assets exactly this way (Replay.Controls.cs, ThemeAssets).
            NowThemeAsset a = ScriptableObject.CreateInstance<NowThemeAsset>();
            a.ResetToDefaults(dark: false);
            NowThemeAsset b = ScriptableObject.CreateInstance<NowThemeAsset>();
            b.ResetToDefaults(dark: true);

            Assert.AreNotEqual(
                a.GetColor(NowColorToken.Background), b.GetColor(NowColorToken.Background),
                "the light and dark defaults resolve Background to the same colour, so ui.theme has nothing to do");

            TestContext.WriteLine("light Background " + a.GetColor(NowColorToken.Background) +
                                  "   dark " + b.GetColor(NowColorToken.Background));
        }

        // ------------------------------------------------------------------------------------- the option mask

        [Test]
        public void TheNewOptionBitsDecodeInAscendingOrderAndDoNotDisturbTheOldOnes()
        {
            RequireFixtures();

            // Every W9 bit that has a payload, set at once, on a rect. If a single width were wrong the walk would
            // desynchronise and the later fields would be read out of the wrong slots - which shows up as a
            // rectangle of a wildly different size, so the geometry assertion is the real check here.
            int mask = Abi.OptColor | Abi.OptStroke | Abi.OptStrokeColor | Abi.OptRadius | Abi.OptBlur
                       | Abi.OptFontSize | Abi.OptCap | Abi.OptDash | Abi.OptSegments | Abi.OptFill
                       | Abi.OptSpread | Abi.OptAngle;

            int rid;
            TestFrame frame = Canvas(out rid);

            frame.Op(Abi.Op("OPTS"), TestFrame.Args(
                mask,
                BridgePaintPack(Color.white), 0,                     // color
                2f,                                                  // stroke
                1, (int)NowColorToken.Border,                        // strokeColor, as a token
                TestFrame.V4(6f, 6f, 6f, 6f),                        // radius
                0f,                                                  // blur
                14f,                                                 // fontSize (reserved; decoded, applied by nothing)
                (int)NowLineCap.Round,                               // cap
                4f, 4f, 0f,                                          // dash
                48,                                                  // segments
                1,                                                   // fill
                (int)NowGradientSpread.Clamp,                        // spread
                45f));                                               // angle

            frame.Op(Abi.Op("RECT"), TestFrame.V4(10f, 10f, 120f, 80f));
            frame.ScopeClose();

            Vertices(frame);
            Rect geometry = BridgeTestHost.counting.geometry;

            TestContext.WriteLine("geometry with every W9 option set: " + geometry);

            // The rect is 120x80 and gains a little from its 2px outline and rounded corners; a desynchronised
            // walk would read the radius out of the dash slots and produce something nothing like this.
            Assert.AreEqual(120f, geometry.width, 12f, "the rect's width came out wrong, so the option walk drifted");
            Assert.AreEqual(80f, geometry.height, 12f, "the rect's height came out wrong, so the option walk drifted");
        }

        [Test]
        public void ASecondMaskWordIsSkippedRatherThanReadAsAPayload()
        {
            RequireFixtures();

            // What a nowui.js from a later build would emit: OptMore set, a second mask word carrying bits this
            // build has never heard of, then THIS build's payload, then the unknown build's payload at the end.
            //
            // The assertion is that the colour still arrives - which it only can if the decode skipped the
            // continuation word before walking the first word's fields.
            int rid;
            TestFrame withMore = Canvas(out rid);
            withMore.Op(Abi.Op("OPTS"), TestFrame.Args(
                Abi.OptMore | Abi.OptColor,
                unchecked((int)0x0000_0007),                 // three bits of a word this build does not know
                BridgePaintPack(Color.white), 0,             // the known payload, after the continuation word
                99, 99, 99));                                // the unknown word's payload, at the very end
            withMore.Op(Abi.Op("RECT"), TestFrame.V4(10f, 10f, 120f, 80f));
            withMore.ScopeClose();

            Vertices(withMore);
            Rect withContinuation = BridgeTestHost.counting.geometry;

            TestFrame without = Canvas(out rid);
            without.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            without.Op(Abi.Op("RECT"), TestFrame.V4(10f, 10f, 120f, 80f));
            without.ScopeClose();

            Vertices(without);
            Rect plain = BridgeTestHost.counting.geometry;

            Assert.AreEqual(plain.width, withContinuation.width, 0.5f);
            Assert.AreEqual(plain.height, withContinuation.height, 0.5f);
            Assert.AreEqual(plain.xMin, withContinuation.xMin, 0.5f);
            Assert.AreEqual(plain.yMin, withContinuation.yMin, 0.5f);
        }

        [Test]
        public void APaintLiteralIsTheSameThirtyTwoBitsAColorFieldUses()
        {
            // The reason ui.rect(box, { color: ui.colorField('c', c) }) composes with no conversion: the paint
            // kind's tag-0 packing IS the color kind's, through the same function.
            var samples = new[]
            {
                new Color(0f, 0f, 0f, 1f), new Color(1f, 1f, 1f, 1f),
                new Color(0.2f, 0.6f, 0.9f, 0.5f), new Color(1f, 0f, 0.5f, 0f),
            };

            foreach (Color c in samples)
            {
                int packed = BridgePaintPack(c);
                Color viaPaint = BridgePaint.Resolve(NowTheme.themeAsset, 0, packed, NowColorToken.Text);

                Assert.AreEqual(c.r, viaPaint.r, 1f / 255f);
                Assert.AreEqual(c.g, viaPaint.g, 1f / 255f);
                Assert.AreEqual(c.b, viaPaint.b, 1f / 255f);
                Assert.AreEqual(c.a, viaPaint.a, 1f / 255f);
            }
        }

        [Test]
        public void APaintTokenResolvesAgainstTheAmbientTheme()
        {
            Color viaToken = BridgePaint.Resolve(
                NowTheme.themeAsset, 1, (int)NowColorToken.Accent, NowColorToken.Text);

            Assert.AreEqual(NowTheme.themeAsset.GetColor(NowColorToken.Accent), viaToken);
        }

        [Test]
        public void AnOutOfRangePaintTagFallsBackRatherThanThrowing()
        {
            Color fallback = BridgePaint.Resolve(NowTheme.themeAsset, 7, 12345, NowColorToken.Danger);
            Assert.AreEqual(NowTheme.themeAsset.GetColor(NowColorToken.Danger), fallback);
        }

        // ---------------------------------------------------------------------------------- section 5.6, again

        /// <summary>
        /// The two exactLayout passes must decode identically, and the three new scope kinds are the reason to
        /// re-assert it: a mask, a pane and a theme all join BridgeScopeFrame.Close()'s ordering, and a mistake
        /// there is a NowScopeGuard throw on the second pass rather than the first.
        /// </summary>
        [Test]
        public void EveryNewScopeKindNestedTogetherDecodesTheSameOnBothPasses()
        {
            var frame = new TestFrame();
            int themeSeg = frame.Intern("theme");
            int canvasSeg = frame.Intern("canvas");
            int maskSeg = frame.Intern("mask");
            int splitSeg = frame.Intern("split");

            frame.Op(Abi.Op("THEME"), 1, themeSeg, 1);

            frame.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(400f), TestFrame.F(200f));
            frame.Op(Abi.Op("SPLIT"), 2, splitSeg, TestFrame.F(0.5f), (int)NowSplitAxis.Horizontal);

            frame.Op(Abi.Op("PANE"), 0);
            frame.Op(Abi.Op("OPTS"), Abi.OptWidth | Abi.OptHeight, TestFrame.F(150f), TestFrame.F(150f));
            frame.Op(Abi.Op("CANVAS"), 3, canvasSeg);
            frame.Op(Abi.Op("MASK"), TestFrame.Args(
                4, maskSeg, 2, TestFrame.V4(0f, 0f, 100f, 100f), TestFrame.V4(0f, 0f, 0f, 0f), 0f));
            frame.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            frame.Op(Abi.Op("RECT"), TestFrame.V4(0f, 0f, 80f, 80f));
            frame.ScopeClose();   // mask
            frame.ScopeClose();   // canvas
            frame.ScopeClose();   // pane 0

            frame.Op(Abi.Op("PANE"), 1);
            frame.Op(Abi.Op("TEXT"), frame.Volatile("right"));
            frame.ScopeClose();   // pane 1

            frame.ScopeClose();   // split
            frame.ScopeClose();   // theme

            List<string> log;
            BridgeReplay replay = Run(frame, out log);

            CollectionAssert.IsEmpty(log, "nesting all five new scopes reported something");
            Assert.AreEqual(2, replay.passes, "the frame did not run under exactLayout, so this proves nothing");
            Assert.AreEqual(0, replay.faults, "a pass threw - almost certainly a scope closed out of order");
        }

        // ------------------------------------------------------------------------------------------- plumbing

        private static bool TryFind(BridgeReplay replay, int rid, out BridgeResults.Record record)
        {
            foreach (BridgeResults.Record r in replay.results.Decode())
            {
                if (r.rid == rid)
                {
                    record = r;
                    return true;
                }
            }

            record = default;
            return false;
        }

        // ------------------------------------------------------------------------------------- W12: ui.image
        //
        // An image is a rect with a texture, so what needs proving is not that a textured quad tessellates - RECT
        // already covers that - but the three things IMAGE adds: the URL reaches the cache, a picture that has not
        // arrived still occupies its box, and the geometry lands where a RECT with the same box would.

        /// <summary>The URL an image names is handed to the project's image cache, on the frame that names it.</summary>
        [Test]
        public void NamingAUrlAsksTheImageCacheForIt()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            const string url = "https://example.invalid/nowui-asks-for-this.png";
            Texture2D before;
            Assert.AreEqual(NowMarkdownImageState.Failed, NowMarkdownImages.GetState("", out before),
                "an empty url should be refused outright, which is the baseline this test reads against");
            NowMarkdownImages.Reset();
            Assert.AreEqual(0, NowMarkdownImages.cachedEntryCount, "the cache did not start empty");

            var frame = new TestFrame();
            int handle = frame.Intern(url);
            frame.Op(Abi.Op("IMAGE"), TestFrame.F(10f), TestFrame.F(10f), TestFrame.F(64f), TestFrame.F(64f), handle,
                (int)BridgeImageFit.Stretch);

            List<string> log;
            Run(frame, out log);
            CollectionAssert.IsEmpty(log);

            Assert.AreEqual(1, NowMarkdownImages.cachedEntryCount,
                "the op drew without ever asking the cache for the url, so the picture would never arrive");

            NowMarkdownImages.Reset();
        }

        /// <summary>
        /// A picture that has not arrived - or never will - still draws its box, so a layout built around an image
        /// does not collapse while the download runs and does not silently lose a slot when the url is wrong.
        /// </summary>
        [Test]
        public void AnImageThatHasNotArrivedStillDrawsItsBox()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            var frame = new TestFrame();
            int handle = frame.Intern("https://example.invalid/never-arrives.png");
            frame.Op(Abi.Op("IMAGE"), TestFrame.F(10f), TestFrame.F(10f), TestFrame.F(64f), TestFrame.F(64f), handle,
                (int)BridgeImageFit.Stretch);

            long drawn = Vertices(frame);

            Assert.Greater(drawn, 0L,
                "an image whose download has not finished tessellated nothing, so its box vanished from the " +
                "layout until the bytes arrived");

            // The tolerance is the ANTIALIASING BAND, not slack. NowUI submits geometry a couple of pixels
            // outside the shape so the edge can fade across it, so the extent the backend sees is always a little
            // larger than the box that was asked for - measured at 68 for a 64 px placeholder. What this asserts
            // is that the box is still THERE and still its own size; AnImageOccupiesTheSameBoxAsARectangleWouldHave
            // is the one that pins the position exactly, by comparing two drawn things rather than a drawn thing
            // against a literal.
            Assert.IsTrue(BridgeTestHost.counting.hasGeometry);
            Rect box = BridgeTestHost.counting.geometry;
            Assert.AreEqual(64f, box.width, 6f, "the placeholder did not occupy the width the image asked for");
            Assert.AreEqual(64f, box.height, 6f, "the placeholder did not occupy the height the image asked for");

            NowMarkdownImages.Reset();
        }

        /// <summary>
        /// An image lands exactly where a rectangle with the same box lands. Asserted as an equality against RECT
        /// rather than against literal coordinates, for the same reason the canvas-origin test is: the alternative
        /// asserts NowUI's vertex conventions rather than the bridge's.
        /// </summary>
        [Test]
        public void AnImageOccupiesTheSameBoxAsARectangleWouldHave()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            var imageFrame = new TestFrame();
            int handle = imageFrame.Intern("https://example.invalid/somewhere.png");
            imageFrame.Op(Abi.Op("IMAGE"),
                TestFrame.F(24f), TestFrame.F(36f), TestFrame.F(120f), TestFrame.F(80f), handle,
                (int)BridgeImageFit.Stretch);

            Vertices(imageFrame);
            Rect imageBox = BridgeTestHost.counting.geometry;

            var rectFrame = new TestFrame();
            rectFrame.Op(Abi.Op("OPTS"), ColorOpts(Color.white));
            rectFrame.Op(Abi.Op("RECT"), TestFrame.V4(24f, 36f, 120f, 80f));

            Vertices(rectFrame);
            Rect rectBox = BridgeTestHost.counting.geometry;

            Assert.AreEqual(rectBox.xMin, imageBox.xMin, 0.5f, "an image and a rect disagreed about x");
            Assert.AreEqual(rectBox.yMin, imageBox.yMin, 0.5f, "an image and a rect disagreed about y");
            Assert.AreEqual(rectBox.width, imageBox.width, 0.5f);
            Assert.AreEqual(rectBox.height, imageBox.height, 0.5f);

            NowMarkdownImages.Reset();
        }

        /// <summary>
        /// A texture already in the cache is drawn on the frame that names it, with no download and no wait. This
        /// is the path an Editor-served project asset takes once its first frame has landed.
        /// </summary>
        [Test]
        public void AnImageAlreadyInTheCacheDrawsImmediately()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            const string url = "https://example.invalid/already-here.png";
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            NowMarkdownImages.SetTexture(url, texture);

            Texture2D found;
            Assert.AreEqual(NowMarkdownImageState.Loaded, NowMarkdownImages.GetState(url, out found),
                "the injected texture did not register, so this test could not tell the two paths apart");

            var frame = new TestFrame();
            int handle = frame.Intern(url);
            frame.Op(Abi.Op("IMAGE"), TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(32f), TestFrame.F(32f), handle,
                (int)BridgeImageFit.Stretch);

            long drawn = Vertices(frame);
            Assert.Greater(drawn, 0L, "a cached image drew nothing");

            NowMarkdownImages.Reset();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>A Lottie's URL reaches the core's Lottie cache on the frame that names it.</summary>
        [Test]
        public void NamingAUrlAsksTheLottieCacheForIt()
        {
            RequireFixtures();
            NowLottieCache.Reset();
            Assert.AreEqual(0, NowLottieCache.cachedEntryCount, "the cache did not start empty");

            var frame = new TestFrame();
            int handle = frame.Intern("https://example.invalid/spinner.json");
            frame.Op(Abi.Op("LOTTIE"),
                TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(64f), TestFrame.F(64f), handle, TestFrame.F(0.5f));

            List<string> log;
            Run(frame, out log);
            CollectionAssert.IsEmpty(log);

            Assert.AreEqual(1, NowLottieCache.cachedEntryCount,
                "the op drew without ever asking the cache for the url, so the animation would never arrive");

            NowLottieCache.Reset();
        }

        // ------------------------------------------------------------------------------- W12: ui.image fit
        //
        // The three modes are distinguishable from the OUTSIDE, which is what makes them testable here: contain
        // shrinks the drawn quad to the source's aspect, cover and stretch both keep the whole box and differ
        // only in the UVs. So geometry proves contain, and the two that agree on geometry are separated by
        // asserting the uvRect arithmetic directly.

        /// <summary>A 2:1 picture asked to CONTAIN a square box draws half as tall, centred, and crops nothing.</summary>
        [Test]
        public void ContainShrinksTheQuadToTheSourceAspectAndCentresIt()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            const string url = "https://example.invalid/wide.png";
            var texture = new Texture2D(200, 100, TextureFormat.RGBA32, false);
            NowMarkdownImages.SetTexture(url, texture);

            var frame = new TestFrame();
            int handle = frame.Intern(url);
            frame.Op(Abi.Op("IMAGE"),
                TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(100f), TestFrame.F(100f),
                handle, (int)BridgeImageFit.Contain);

            Vertices(frame);
            Rect box = BridgeTestHost.counting.geometry;
            TestContext.WriteLine("a 200x100 picture contained in 100x100 drew " + box);

            // 6 px of tolerance is the antialiasing band, which is submitted outside the shape on every edge.
            Assert.AreEqual(100f, box.width, 6f, "contain used the full width, which a 2:1 source should");
            Assert.AreEqual(50f, box.height, 6f,
                "contain did not shrink the quad to the source's aspect - a 2:1 picture in a square box is 100x50");

            // CENTRING IS ASSERTED AGAINST A DRAWN REFERENCE, never against a literal y. NowUI's vertex
            // positions carry its own sign and origin conventions (see this file's header), so the same picture
            // stretched into the same box gives the box's true centre and the contained one must share it.
            var reference = new TestFrame();
            int referenceHandle = reference.Intern(url);
            reference.Op(Abi.Op("IMAGE"),
                TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(100f), TestFrame.F(100f),
                referenceHandle, (int)BridgeImageFit.Stretch);

            Vertices(reference);
            Rect full = BridgeTestHost.counting.geometry;

            Assert.AreEqual(full.center.x, box.center.x, 1f, "the contained picture was not centred horizontally");
            Assert.AreEqual(full.center.y, box.center.y, 1f, "the contained picture was not centred vertically");

            NowMarkdownImages.Reset();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>Cover keeps the whole box - it crops the picture instead of shrinking the shape.</summary>
        [Test]
        public void CoverKeepsTheWholeBoxWhereContainDoesNot()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            const string url = "https://example.invalid/wide-cover.png";
            var texture = new Texture2D(200, 100, TextureFormat.RGBA32, false);
            NowMarkdownImages.SetTexture(url, texture);

            var frame = new TestFrame();
            int handle = frame.Intern(url);
            frame.Op(Abi.Op("IMAGE"),
                TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(100f), TestFrame.F(100f),
                handle, (int)BridgeImageFit.Cover);

            Vertices(frame);
            Rect box = BridgeTestHost.counting.geometry;
            TestContext.WriteLine("the same picture covering the same box drew " + box);

            Assert.AreEqual(100f, box.width, 6f);
            Assert.AreEqual(100f, box.height, 6f,
                "cover shrank the quad, which is contain's behaviour - cover fills the box and crops instead");

            NowMarkdownImages.Reset();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>Stretch is the null mode: the whole box, and no cropping either.</summary>
        [Test]
        public void StretchFillsTheBoxAndSamplesTheWholeTexture()
        {
            RequireFixtures();
            NowMarkdownImages.Reset();

            const string url = "https://example.invalid/wide-stretch.png";
            var texture = new Texture2D(200, 100, TextureFormat.RGBA32, false);
            NowMarkdownImages.SetTexture(url, texture);

            var frame = new TestFrame();
            int handle = frame.Intern(url);
            frame.Op(Abi.Op("IMAGE"),
                TestFrame.F(0f), TestFrame.F(0f), TestFrame.F(100f), TestFrame.F(100f),
                handle, (int)BridgeImageFit.Stretch);

            Vertices(frame);
            Rect box = BridgeTestHost.counting.geometry;

            Assert.AreEqual(100f, box.width, 6f);
            Assert.AreEqual(100f, box.height, 6f, "stretch should have filled the box");

            NowMarkdownImages.Reset();
            UnityEngine.Object.DestroyImmediate(texture);
        }

    }
}
