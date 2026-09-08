// W2/W3/W4 - the decoder and the frame. Docs/Standalone/M3-Spec.md sections 5.2, 5.6, 5.7.
//
// It walks the op stream and calls NowUI. W2 gave it one opcode; W3 gave it the three identity-carrying ones and
// the control that proves invariant I1; W4 adds the sixth op, the result-table writes, and section 5.7's frame.
// W8 replaces the switch below with the generated one; the shape does not change.
//
// SECTION 5.7's FRAME, AND WHY IT IS SPLIT IN TWO HERE.
//
//   RunFrame()   the root: one identity scope, then RunMeasured (two passes) or Area (one). Called ONCE per frame,
//                from inside Now.StartUI, by the host.
//   replay       one decode pass, and nothing else. This is the cached Action RunMeasured is handed, and the
//                reason the split exists at all: RunMeasured calls its delegate TWICE (NowLayout.cs:1578-1590),
//                so anything that must happen once per frame - the root scope, the result table's seal - cannot
//                live inside it.
//
// The author's draw function is not in this file and never runs under RunMeasured: it runs in JavaScript, once,
// before Now.StartUI is even opened. That is the ordering section 5.7 exists to pin, and putting Record inside
// RunMeasured's delegate - which is what Design B's frame diagram did - would double every handler.
//
// The one property that must survive every later edit is IDEMPOTENCE (section 5.6). Under exactLayout the buffer
// is decoded TWICE per frame, so decoding must have no side effects of its own. That holds here by construction:
//
//   * nothing is allocated that outlives the call except the strings interned in the preamble, which run once,
//     before decoding, and not from this file;
//   * every cursor is a local;
//   * explicit ids are never occurrence-salted (NowControls.cs:457-462), so the measure pass and the real pass
//     resolve every control to the same NowResolvedId, which is the whole reason a second decode is legal.
//
// INVARIANT I1 (section 3.3, gate G10): every container and every control opened here carries exactly one SetId.
// A container the bridge forgets to SetId does NOT fail loudly - NowLayout.Column() captures the BRIDGE's own
// [CallerFilePath]/[CallerLineNumber] (NowLayoutContainers.cs:251-256), so it falls into
// ResolveGroupSiteOccurrence with a single site token shared by every container in the application, which is flat
// per-depth occurrence numbering and the worst identity available. InvariantI1.targets scans this file at build
// time and fails the build on a chain without one.

using System;
using System.Collections.Generic;
using NowUI;
using UnityEngine;

namespace NowUI.Bridge
{
    /// <summary>Decodes one recorded frame into NowUI calls.</summary>
    /// <remarks>
    /// Split across two files. This one holds W2-W5's six ops and section 5.7's frame; Replay.Controls.cs holds
    /// the rest of section 2's surface and section 2.7's options object. Both are scanned by
    /// InvariantI1.targets, which is why the split is `partial` rather than a second class.
    /// </remarks>
    public sealed partial class BridgeReplay
    {
        private readonly BridgeRecorder m_Recorder;
        private readonly DuplicateIdBackstop m_Backstop = new DuplicateIdBackstop();
        private readonly Action<string> m_Log;

        private BridgeScopeFrame[] m_Scopes = new BridgeScopeFrame[64];
        private int m_Depth;
        private NowRect m_Screen;

        /// <summary>
        /// The cached replay delegate. Section 5.7 pins this: <c>RunMeasured</c>'s own documentation warns that
        /// "a lambda that captures locals allocates a closure every rebuild - cache the delegate in a field"
        /// (NowLayout.cs:1489-1491), and W4 hands exactly this field to <c>RunMeasured</c>.
        /// </summary>
        public Action replay { get; }

        public BridgeReplay(BridgeRecorder recorder, Action<string> log = null)
        {
            m_Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            m_Results = recorder.results;
            m_Log = log;
            replay = Replay;
        }

        private readonly BridgeResults m_Results;

        /// <summary>W5's table for the frame just replayed (section 6).</summary>
        public BridgeResults results => m_Results;

        /// <summary>
        /// How many decode passes the last <see cref="RunFrame"/> ran: two under <c>exactLayout</c>, one without
        /// it. Section 5.7's "RunMeasured runs the whole callback twice" as a number a test can read, rather than
        /// as a claim about a method in a frozen tree.
        /// </summary>
        public int passes { get; private set; }

        /// <summary>
        /// Invoked at the end of each decode pass with the pass number (1, then 2 under <c>exactLayout</c>).
        /// </summary>
        /// <remarks>
        /// A test hook, and a narrow one: section 5.6's idempotence is a claim about what the SECOND pass
        /// resolves, and the only place that is observable is between the two passes, inside <c>RunMeasured</c>.
        /// Null on every non-test path, so it costs one null check per pass.
        /// </remarks>
        public Action<int> passCompleted { get; set; }

        /// <summary>How many ops the last replay decoded. Evidence, and a test's cheapest assertion.</summary>
        public int decodedOps { get; private set; }

        private int m_Faults;

        private readonly HashSet<string> m_Reported = new HashSet<string>();

        /// <summary>How many decode passes have thrown this session. Zero is what a working frame looks like.</summary>
        public int faults => m_Faults;

        /// <summary>How many controls the last replay drew.</summary>
        public int decodedControls { get; private set; }

        /// <summary>
        /// When set, the replay remembers which <see cref="NowResolvedId"/> each rid resolved to. Off by default:
        /// the decode is on the per-frame path and section 5.6 asks it to allocate nothing of its own. W7's
        /// <c>?nowui=debug</c> and this assembly's tests are the two callers.
        /// </summary>
        public bool trackControlIds { get; set; }

        private readonly Dictionary<int, NowResolvedId> m_ControlIds = new Dictionary<int, NowResolvedId>();

        /// <summary>What <see cref="trackControlIds"/> collected, keyed by rid.</summary>
        public IReadOnlyDictionary<int, NowResolvedId> controlIds => m_ControlIds;

        /// <summary>The rect the root container area covers. Set by the host before each replay.</summary>
        public NowRect screen
        {
            get => m_Screen;
            set => m_Screen = value;
        }

        // ------------------------------------------------------------------------------------------- the frame

        /// <summary>The root area's padding and gap. One pair, used by BOTH branches of <see cref="RunFrame"/>.</summary>
        /// <remarks>
        /// One pair rather than two literals, because W4's acceptance is that the two branches produce the same
        /// draw list: two constants that happen to agree today are a thing that can stop agreeing in an edit that
        /// looks like formatting.
        /// </remarks>
        private const float k_RootPadding = 16f;

        private const float k_RootGap = 8f;

        /// <summary>
        /// One frame, as section 5.7 orders it. Called from inside <c>Now.StartUI</c>, after the buffer has been
        /// validated and its strings interned, and never from inside <c>RunMeasured</c>.
        /// </summary>
        /// <remarks>
        /// <para>The two branches differ only in how many times <see cref="replay"/> runs. The root is a container
        /// area either way (section 5.7), which is what makes the author's outermost <c>ui.column</c> a NESTED
        /// container - and therefore what makes <c>grow</c> and <c>fill</c> legal on it.</para>
        /// <para><c>exactLayout</c> costs a second full execution of the UI in wasm and buys deferred sizes -
        /// flexible space, stretch shares, auto group extents - resolved from THIS frame's measurements rather
        /// than the last one's (NowLayout.cs:1260-1266). It is legal because the decode has no side effects of its
        /// own (section 5.6), and because the result table refuses to write while the pass is passive
        /// (BridgeResults.passive), so the measure pass cannot overwrite real results with inert ones.</para>
        /// </remarks>
        public void RunFrame()
        {
            passes = 0;
            m_Results.BeginFrame();

            try
            {
                // The root identity scope wraps BOTH passes and the area itself: one string segment, hashed once
                // per frame, so a JavaScript UI can never collide with a C# UI drawn on the same surface in the
                // same frame (section 3.3, "The root").
                using (BridgeIdentity.Root())
                {
                    if (m_Recorder.exactLayout)
                    {
                        // `replay` is a cached field, not a lambda. RunMeasured's own documentation asks for
                        // exactly that: "A lambda that captures locals allocates a closure every rebuild - cache
                        // the delegate in a field" (NowLayout.cs:1489-1491).
                        NowLayout.RunMeasured(
                            new NowId(BridgeIdentity.RootAreaSegment), m_Screen, replay,
                            spacing: k_RootGap, padding: k_RootPadding);
                    }
                    else
                    {
                        using (NowLayout.Area(
                            new NowId(BridgeIdentity.RootAreaSegment), m_Screen,
                            spacing: k_RootGap, padding: k_RootPadding))
                        {
                            replay();
                        }
                    }
                }
            }
            finally
            {
                // In a finally because of what the alternative leaves behind. A control that throws mid-replay
                // (W7's hole-and-a-message case) would otherwise leave `sealedSlots` pointing at the PREVIOUS
                // frame's length over a buffer this frame has already partly overwritten - a table JavaScript
                // would read as records it never wrote. Sealing here hands over a short, self-consistent table
                // holding exactly the controls that got as far as drawing.
                m_Results.Seal();
            }
        }

        /// <summary>
        /// One replay pass. Called inside <c>Now.StartUI</c>; called TWICE per frame when the frame asked for
        /// <c>exactLayout</c>, which is legal because this method has no side effects of its own (section 5.6).
        /// </summary>
        private void Replay()
        {
            ++passes;
            decodedOps = 0;
            decodedControls = 0;
            m_Depth = 0;
            m_Backstop.BeginFrame();
            m_Results.BeginPass();
            BeginControlsPass();
            if (trackControlIds) m_ControlIds.Clear();

            // A CONTROL THAT THROWS LEAVES A HOLE AND A MESSAGE, NOT A DEAD FRAME LOOP - W7's acceptance, landed
            // here because W6 could not be diagnosed without it, and because the alternative destroys the
            // evidence rather than merely failing.
            //
            // Without this the sequence is: a control throws; the bridge's scope stack is left open; the root
            // identity scope's `using` disposes on the way out and NowControls.PopIdScope throws "scopes must be
            // disposed in reverse order" (NowControls.cs:275); THAT exception replaces the original; it escapes
            // into Now.StartUI's own using, whose Dispose throws "cannot finish while a nested scope is active"
            // (Now.cs:1976) and replaces it again. What reaches the page is the third exception, which names a
            // symptom of a symptom, and Program.cs latches s_Failed and stops the loop. The real one is gone.
            //
            // Catching HERE - inside the pass, before any enclosing scope has been disposed - is the only point
            // at which the original is still the exception in flight. The scopes the pass left open are closed
            // in the finally, so NowUI's stacks are balanced by the time RunMeasured or Area sees the return,
            // and the frame completes with whatever drew before the throw.
            try
            {
                Decode();
            }
            catch (Exception e)
            {
                m_Faults++;

                // Once per session per message: a UI that throws throws on every frame, and sixty identical
                // stack traces a second bury the first one, which is the only useful one.
                if (m_Reported.Add(e.Message))
                    Report("NowUI bridge: the replay threw, and the frame was drawn up to that point.\n" + e);
            }
            finally
            {
                // Closing what the pass left open. Wrapped, because closing a scope out of order throws too and
                // the second exception would be the one that escaped.
                while (m_Depth > 0)
                {
                    try { CloseScope(); }
                    catch (Exception) { m_Depth = 0; }
                }
            }

            if (passCompleted != null) passCompleted(passes);
        }

        private void Decode()
        {
            int slot = m_Recorder.opStart;
            int end = m_Recorder.opEnd;

            while (slot < end)
            {
                int header = m_Recorder.Slot(slot);
                int opcode = header & 0xFFFF;
                int argSlots = (header >> 16) & 0xFFFF;
                int args = slot + 1;

                ++decodedOps;

                switch (opcode)
                {
                    case Abi.OpScopeClose:
                        CloseScope();
                        break;

                    case Abi.OpNop:
                        break;

                    default:
                        Dispatch(opcode, args);
                        break;
                }

                slot = args + argSlots;
            }

            // The validator already proved the stream balances (section 5.5 rule 5), so this can only fire if the
            // decode itself lost a scope. Closing them keeps NowUI's own stacks consistent for the next frame -
            // NowControls.ResetIdScopesForFrame would self-heal a leak, but it would also report one, and a
            // report the bridge caused is a report the author cannot act on.
            while (m_Depth > 0) CloseScope();
        }

        private void Dispatch(int opcode, int args)
        {
            OpSpec spec = Abi.Find(opcode);

            if (spec == null)
            {
                // Section 5.2: a decoder meeting an opcode it does not know skips argSlots and continues rather
                // than desynchronising. The surface hash makes this all but unreachable - the frame would have
                // been refused at the header - so it is a belt for the case where the hash somehow agrees and one
                // op does not.
                Report("NowUI bridge: unknown opcode " + opcode + " skipped. The surface hash matched, which means " +
                       "abi.js and Abi.cs agree on the manifest but disagree on this op.");
                return;
            }

            switch (spec.Name)
            {
                case "TEXT":
                    // A drawing, not a control: no key, no identity, no result (section 2.3). W6 moved the body
                    // to DrawLabel so that `textStyle` - which is how ui.heading, ui.subheading and ui.caption
                    // are expressed (section 2.3's three aliases) - and the sizing options reach it.
                    DrawLabel(m_Recorder.Text(m_Recorder.Slot(args)));
                    break;

                case "COLUMN":
                    OpenContainer(horizontal: false, rid: m_Recorder.Slot(args), segment: m_Recorder.Slot(args + 1));
                    break;

                case "ROW":
                    OpenContainer(horizontal: true, rid: m_Recorder.Slot(args), segment: m_Recorder.Slot(args + 1));
                    break;

                case "LIST_ITEM":
                    OpenListItem(
                        rid: m_Recorder.Slot(args),
                        listSegment: m_Recorder.Slot(args + 1),
                        itemSegment: m_Recorder.Slot(args + 2));
                    break;

                case "BUTTON":
                    DrawButton(
                        rid: m_Recorder.Slot(args),
                        segment: m_Recorder.Slot(args + 1),
                        label: m_Recorder.Text(m_Recorder.Slot(args + 2)));
                    break;

                case "TEXT_FIELD":
                    DrawTextField(
                        rid: m_Recorder.Slot(args),
                        segment: m_Recorder.Slot(args + 1),
                        value: m_Recorder.Text(m_Recorder.Slot(args + 2)),
                        placeholder: m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    break;

                default:
                    // W6's half of the switch: section 2's surface, in Replay.Controls.cs. It returns false for
                    // an op it does not own, which is what tells "handled" from "no case at all".
                    if (!DispatchControl(spec.Name, args))
                        Report("NowUI bridge: op " + spec.Name + " has an opcode but no case in the decoder.");
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------- the ops

        private void OpenContainer(bool horizontal, int rid, int segment)
        {
            BridgeOptions options = TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);

            // The identity scope first, then the container inside it.
            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            // THE CONTAINER TAKES THE RID, NOT THE IDENTITY SEGMENT. This is a correction to section 3.3, whose
            // table gives a layout container "the same value as its id scope".
            //
            // Section 3.3's reasoning is that a scope derives in NowIdDomain.Scope and a container in
            // NowIdDomain.Layout, so one segment cannot make the two alias. The domains are right. What the table
            // also implies - that a container resolves UNDER its id scope - holds only for a ROOT area
            // (NowLayoutContainers.cs:365, via NowControls.ResolveScopedId). A NESTED container resolves under the
            // enclosing LAYOUT GROUP:
            //
            //     NowLayout.cs:2489   groupId = parent.id.Derive(NowIdDomain.Layout, identity.authored);
            //
            // The bridge walks straight into that. A ui.list item opens an identity scope and NO container -
            // section 3.2's canonical path has no box for it - so three items each drawing one anonymous ui.row
            // give three rows all carrying segment -1 under one layout parent, deriving ONE group id between them
            // and sharing one entry in NowLayout's measurement cache.
            //
            // Measured, in LayoutIdentityTests: two sibling auto-sized groups with the same authored id, one 120
            // tall and one 40, come back on the next frame with each other's extents. Not "similar" - swapped.
            //
            // The rid fixes it exactly. It is the recorder's dense integer for this PATH (section 3.4) and is
            // unique across the frame by construction - check 1 throws on a second sighting of one - so no two
            // containers can ever derive one group id. It also serves section 3.3's stated GOAL better than the
            // segment did: "the layout cache is exactly as stable as the identity" is true of a value that is 1:1
            // with a path, and was not true of one shared by every anonymous sibling.
            //
            // The one cost: rids are recycled through a free list when a node has gone 600 frames untouched, so a
            // recycled rid could in principle meet a stale cache entry under the same layout parent. The node
            // being recycled is by definition one nothing has drawn for ten seconds, and NowLayout ages its own
            // cache on the same kind of schedule.
            frame.layout = horizontal
                ? NowLayout.Row().SetId(new NowId(rid)).Options(options.ToLayout(container: true)).Begin()
                : NowLayout.Column().SetId(new NowId(rid)).Options(options.ToLayout(container: true)).Begin();
            frame.kinds |= BridgeScopeFrame.HasLayout;
        }

        private void OpenListItem(int rid, int listSegment, int itemSegment)
        {
            // A list item takes no options - ui.list has no options object in the first release (section 2.2) -
            // but it must still CONSUME anything pending, or an option meant for a control that was not drawn
            // would leak onto the first control inside the item.
            TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);

            // Identity only. A list item is a path segment, not a box: whatever the author draws inside it lays
            // out as a sibling in the enclosing container, which is what section 3.2's canonical path shows
            // (".../team[grace]/#0/Remove" - the #0 is the author's own ui.row, not one the bridge added).
            frame.item = BridgeIdentity.Item(listSegment, itemSegment);
            frame.kinds |= BridgeScopeFrame.HasItemScope;
        }

        private void DrawButton(int rid, int segment, string label)
        {
            BridgeOptions options = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool clicked = NowLayout.Button(label)
                .SetId(new NowId(segment))
                .SetOptions(options.ToLayout())
                .SetStyle(options.RectStyle(NowRectangleStyle.Surface))
                .SetTextStyle(options.TextStyle(NowTextStyle.Button))
                .Draw();

            // WHAT A TIER-1 BUTTON CAN AND CANNOT TELL THE TABLE, stated rather than left to be discovered.
            //
            // Section 6.1 lists thirteen flags - clicked changed submitted focused hovered pressed held released
            // dragging dragStarted dragEnded cancelled hasRect - and says every one is computed synchronously
            // inside the replay. The first half is true: NowControls.Interact does compute all of them against the
            // current input snapshot, before the control draws. The second half is not reachable from here.
            // NowButton.Draw() returns ONE bool - `interaction.clicked || submitted`
            // (NowControlBuilders.cs:145) - and the NowInteraction struct that carried the rest is a local inside
            // Draw. The only public way to see hovered/held/focused on a button is Begin(), which reserves an
            // area, opens a mask and a row, and is the materially heavier call section 6.1 makes `{ rect: true }`
            // opt into.
            //
            // So this writes the one flag the consumer actually returns. Section 6.1's flag set is the table's
            // vocabulary, not a promise that every control fills it in; the controls whose consumers return a
            // result struct - the text field below is the first - fill in more.
            // `disabled` discards the interaction result, which is the whole of what section 2.7 says it can
            // do: the control is still hoverable, still focusable and still in the keyboard order, because
            // NowButton's fluent surface has no disabled concept to reach for.
            m_Results.WriteEvent(rid, options.Disable(clicked) ? BridgeFlags.Clicked : BridgeFlags.None);
        }

        private void DrawTextField(int rid, int segment, string value, string placeholder)
        {
            BridgeOptions options = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            // THE CALLER'S STRING IS AUTHORITATIVE, which is the whole reason section 6.4's rule is shaped the way
            // it is. NowTextField stores no text of its own: Draw(ref string) clamps the field's edit state to the
            // string it is handed (NowTextField.cs:1398, NowTextEdit.Clamp(ref state, text)) and edits it in
            // place. So `value` here is not a hint - it IS the field's content for this frame, and the recorder
            // resolving it BEFORE emitting is what keeps a keystroke from being reverted.
            string text = value;
            NowTextFieldResult result = NowLayout.TextField()
                .SetId(new NowId(segment))
                .SetOptions(options.ToLayout())
                .SetTextStyle(options.TextStyle(NowTextStyle.Body))
                .SetPlaceholder(placeholder)
                .Draw(ref text);

            BridgeFlags flags = BridgeFlags.None;
            if (result.changed) flags |= BridgeFlags.Changed;
            if (result.submitted) flags |= BridgeFlags.Submitted;

            m_Results.WriteString(rid, flags, text);

            // The rect is free here and costs a crossing only in slots: unlike bool Draw(), which discards it
            // (section 6.1), NowTextFieldResult hands one back. Four slots for the one geometric fact a
            // JavaScript author cannot otherwise obtain, and the record walk that reads it is the same one every
            // { rect: true } control will use in W6.
            m_Results.AppendRect(result.rect);
        }

        /// <summary>
        /// Resolves a control's identity, runs section 3.6's backstop against it, and records it when asked.
        /// Shared by every control so that a new op cannot quietly skip the duplicate check.
        /// </summary>
        private NowResolvedId Identify(int rid, int segment)
        {
            NowResolvedId id = BridgeIdentity.ControlId(segment);

            string duplicate = m_Backstop.Check(id, rid, null);
            if (duplicate != null) Report(duplicate);
            if (trackControlIds) m_ControlIds[rid] = id;

            return id;
        }

        // ------------------------------------------------------------------------------------------- scopes

        private ref BridgeScopeFrame Push(int rid)
        {
            if (m_Depth == m_Scopes.Length)
            {
                var grown = new BridgeScopeFrame[m_Scopes.Length * 2];
                Array.Copy(m_Scopes, grown, m_Scopes.Length);
                m_Scopes = grown;
            }

            ref BridgeScopeFrame frame = ref m_Scopes[m_Depth++];
            frame.kinds = 0;
            frame.rid = rid;
            return ref frame;
        }

        private void CloseScope()
        {
            if (m_Depth == 0)
            {
                Report("NowUI bridge: OP_SCOPE_CLOSE with no scope open reached the decoder. The validator should " +
                       "have refused this frame (section 5.5 rule 5).");
                return;
            }

            m_Scopes[--m_Depth].Close();
        }

        private void Report(string message)
        {
            if (m_Log != null) m_Log(message);
            else Debug.LogWarning(message);
        }
    }
}
