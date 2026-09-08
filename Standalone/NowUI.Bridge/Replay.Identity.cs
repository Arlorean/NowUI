// W3 - identity, managed half. Docs/Standalone/M3-Spec.md section 3.3, and gates G9 and G10 (section 7.4).
//
// The whole of what the wasm side does with identity is in this file, and it is deliberately small: the recorder
// has already decided WHAT every segment is (section 3.4's trie), so this side only has to hand each segment to the
// one NowUI entry point that accepts it without a call site. Five rows of section 3.3's table, five methods.
//
// Why an INTEGER segment and not a string. NowId admits a string or a 32-bit int and nothing else (NowId.cs:42,
// :60), and NowResolvedId's only value constructor is internal (NowResolvedId.cs:15) - so an integer handle is the
// only call-site-free segment the public API accepts that is also allocation-free. NowIdHash.Derive hashes a
// string's characters every time it is used (NowIdHash.cs:105-113) with no reference-identity fast path, so a
// string segment would re-hash every key on every frame; an AuthoredInt segment skips that entirely.
//
// Two traps this file avoids by construction, both named in M3-SurfaceScout:
//
//   * IdScope(NowId) silently pushes NOTHING on a default id (NowControls.cs:213-214). Never used here: every
//     segment arrives as an int, and IdScope(int) cannot no-op.
//   * IdScope(NowResolvedId) REPLACES the ambient path instead of nesting under it (NowControls.cs:126-138, via
//     RestoreIdScope). Never used here either.
//
// And one it avoids by asserting: gate G9. NowControls.GetControlId(new NowId(h)) under scope S must equal what
// builder.SetId(new NowId(h)) resolves to under S. Both should funnel through NowControls.cs:498-504 to
// CurrentIdentityParent().Derive(NowIdDomain.Control, id); "should" is not "does", and
// Standalone/NowUI.Bridge.Tests/IdentityTests.cs is where it stops being an assumption.

using System;
using System.Collections.Generic;
using NowUI;
using UnityEngine;

namespace NowUI.Bridge
{
    /// <summary>Section 3.3's plumbing: how a recorded segment becomes a NowUI identity.</summary>
    public static class BridgeIdentity
    {
        /// <summary>
        /// The one string segment in the design, hashed once per frame. Opened as the outermost scope inside
        /// <c>Now.StartUI</c>, so a JavaScript-authored UI can never collide with a C# UI drawn on the same
        /// surface in the same frame.
        /// </summary>
        public const string RootSegment = "nowui";

        /// <summary>
        /// The layout segment of the root container area. Not an intern handle and not an anonymous ordinal: the
        /// root area is the bridge's own, not the author's, and giving it a value neither namespace can produce
        /// makes that visible in a diagnostic rather than merely true.
        /// </summary>
        public const int RootAreaSegment = int.MinValue;

        /// <summary>
        /// The call site the bridge presents to NowUI when an API insists on one. It is never used to derive an
        /// identity - every bridge control carries an explicit id, and an explicit id is never occurrence-salted
        /// (NowControls.cs:457-462) - but the overload that takes it avoids re-interning a file/line pair on every
        /// control, which the <c>[CallerFilePath]</c> overload would do.
        /// </summary>
        private static readonly NowCallSiteId s_UnusedFallback = NowControls.SiteId("NowUI.Bridge/Replay.g.cs", 1);

        /// <summary>Opens the root identity scope. Dispose it at the end of the replay.</summary>
        public static ControlIdScope Root()
        {
            return NowControls.IdScope(RootSegment);
        }

        /// <summary>
        /// One scope segment. A keyed scope arrives as an intern handle (>= 0); an anonymous one as
        /// -(ordinal + 1) (&lt;= -1). The two integer namespaces cannot collide, which is why one method serves
        /// both and why no flag has to ride alongside the segment.
        /// </summary>
        public static ControlIdScope Scope(int segment)
        {
            return NowControls.IdScope(segment);
        }

        /// <summary>
        /// One item of a keyed list. The list namespace is explicit rather than call-site derived, which is what
        /// <see cref="NowControls.KeyedItemIn(NowId, NowId)"/> exists for (NowControls.cs:260) - the alternative,
        /// <c>KeyedItem</c>, would take the bridge's own file and line as the list namespace and put every list in
        /// the application under one.
        /// </summary>
        public static NowKeyedItemScope Item(int listSegment, int itemSegment)
        {
            return NowControls.KeyedItemIn(new NowId(listSegment), new NowId(itemSegment));
        }

        /// <summary>
        /// The identity a control with this segment resolves to under the current scope. The replay does not use
        /// this to draw - it hands the segment to the builder's <c>SetId</c>, which resolves it itself - but the
        /// duplicate backstop needs the value, and gate G9 is the assertion that the two are the same value.
        /// </summary>
        public static NowResolvedId ControlId(int segment)
        {
            return NowControls.GetControlId(new NowId(segment), s_UnusedFallback);
        }

        /// <summary>
        /// The same resolution a builder performs for <c>SetId(new NowId(segment))</c>: every control builder
        /// stores its id as a <see cref="NowControlIdentity"/> and resolves it with
        /// <c>_id.Resolve(_site)</c> (NowControlBuilders.cs:30). Exposed so gate G9 can assert the equality
        /// against the code path the builders actually run, rather than against a re-derivation of it.
        /// </summary>
        public static NowResolvedId BuilderId(int segment)
        {
            NowControlIdentity identity = new NowControlIdentity(new NowId(segment));
            return identity.Resolve(s_UnusedFallback);
        }
    }

    /// <summary>
    /// The replay's open-scope stack. One entry can hold an identity scope, a keyed-item scope and a layout
    /// container, because a bridge scope opens up to two of the three and they must close in the reverse order
    /// they opened.
    /// </summary>
    /// <remarks>
    /// A struct union rather than a <c>Stack&lt;IDisposable&gt;</c>: <c>ControlIdScope</c>,
    /// <c>NowKeyedItemScope</c> and <c>NowLayoutScope</c> are all structs, and boxing three of them per scope per
    /// frame would put the bridge's allocation profile above the C# it mirrors for no reason.
    /// </remarks>
    internal struct BridgeScopeFrame
    {
        internal const int HasIdScope = 1 << 0;
        internal const int HasItemScope = 1 << 1;
        internal const int HasLayout = 1 << 2;

        /// <summary>W6. <c>ui.scroll</c> opens a NowScrollScope where a container opens a NowLayoutScope.</summary>
        internal const int HasScroll = 1 << 3;

        // --- W9 ------------------------------------------------------------------------------------------------

        /// <summary>An ambient mask: <c>ui.canvas</c>'s own box, or <c>ui.mask</c>'s analytic shape.</summary>
        internal const int HasMask = 1 << 4;

        /// <summary>One pane of a split - a NowSplitPaneScope, which is itself a mask plus a layout area.</summary>
        internal const int HasPane = 1 << 5;

        /// <summary>A theme override.</summary>
        internal const int HasTheme = 1 << 6;

        /// <summary>
        /// This scope changed the drawing origin and must put it back. Carried as a kind rather than restored
        /// unconditionally so that the common scope pays one bit test rather than a Vector2 copy.
        /// </summary>
        internal const int HasOrigin = 1 << 7;

        /// <summary>
        /// A split view whose <see cref="split"/> the two PANE ops inside it read. Nothing to dispose - a
        /// NowSplitViewResult is a value - but the flag is what lets PANE say "my parent is not a split" rather
        /// than open a pane on a default-constructed result.
        /// </summary>
        internal const int HasSplit = 1 << 8;

        /// <summary>
        /// Nine kinds, so this cannot be the byte it was through W6. Widened rather than packed: the frames live
        /// in one array that is grown, never enumerated per op, and three bytes per open scope is not a cost
        /// anything can measure.
        /// </summary>
        public int kinds;

        public ControlIdScope id;
        public NowKeyedItemScope item;
        public NowLayoutScope layout;
        public NowScrollScope scroll;
        public NowMaskScope mask;
        public NowSplitPaneScope pane;
        public ThemeScope theme;
        public NowSplitViewResult split;

        /// <summary>The drawing origin this scope replaced, restored by <c>CloseScope</c> after <see cref="Close"/>.</summary>
        public Vector2 savedOrigin;

        /// <summary>The rid the op carried, so a diagnostic can name a path rather than a hash.</summary>
        public int rid;

        public void Close()
        {
            // REVERSE ORDER, and it is load-bearing rather than tidy: NowScopeGuard throws "scopes must be
            // disposed in reverse order" on a mistake here, which is a runtime failure in wasm with the worst
            // available stack trace. The order below is the exact reverse of the order the openers push in:
            //
            //   CANVAS   id, layout, mask          ->  mask, layout, id
            //   MASK     id, mask                  ->  mask, id
            //   PANE     pane                      ->  pane
            //   THEME    id, theme                 ->  theme, id
            //   SPLIT    id                        ->  id            (the result is a value, not a scope)
            //
            // A mask or a pane opened INSIDE a layout therefore closes before it.
            if ((kinds & HasMask) != 0) mask.Dispose();
            if ((kinds & HasPane) != 0) pane.Dispose();
            if ((kinds & HasTheme) != 0) theme.Dispose();
            if ((kinds & HasLayout) != 0) layout.Dispose();
            if ((kinds & HasScroll) != 0) scroll.Dispose();
            if ((kinds & HasItemScope) != 0) item.Dispose();
            if ((kinds & HasIdScope) != 0) id.Dispose();
            kinds = 0;
        }
    }

    /// <summary>
    /// Section 3.6's backstop. NowUI's own <c>CheckDuplicateControlId</c> is compiled out of a Release wasm build
    /// (<c>[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]</c>, NowControls.cs:606-608), so this
    /// side cannot rely on it - and checks 1 to 3 run in JavaScript, before a byte crosses, so it should never see
    /// anything.
    /// </summary>
    /// <remarks>
    /// That is exactly why it is kept: if this fires while the JavaScript checks are silent, the BRIDGE is wrong,
    /// not the author, and the message says so. It maps <see cref="NowResolvedId"/> back to the rid that produced
    /// it, so the report names a path rather than a hash.
    /// </remarks>
    internal sealed class DuplicateIdBackstop
    {
        private readonly Dictionary<NowResolvedId, int> m_Seen = new Dictionary<NowResolvedId, int>(128);
        private readonly HashSet<int> m_Reported = new HashSet<int>();

        /// <summary>Reported once per rid pair per session; a repeating frame must not repeat the message.</summary>
        public int reportCount { get; private set; }

        public void BeginFrame()
        {
            m_Seen.Clear();
        }

        /// <summary>
        /// Returns null when the identity is new this frame, and the message to log when it is not.
        /// </summary>
        public string Check(NowResolvedId id, int rid, Func<int, string> pathOf)
        {
            int firstRid;
            if (!m_Seen.TryGetValue(id, out firstRid))
            {
                m_Seen.Add(id, rid);
                return null;
            }

            if (firstRid == rid || !m_Reported.Add(rid))
                return null;

            reportCount++;
            return "NowUI bridge: two controls resolved to the same NowUI identity in one replay, and the " +
                   "record-time checks did not catch it. That is a BRIDGE bug, not an author error.\n" +
                   "  first: rid " + firstRid + "  " + (pathOf != null ? pathOf(firstRid) : "<no path>") + "\n" +
                   "  again: rid " + rid + "  " + (pathOf != null ? pathOf(rid) : "<no path>");
        }
    }
}
