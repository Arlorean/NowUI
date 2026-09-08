// What a layout container's authored id actually resolves under, measured.
//
// Docs/Standalone/M3-Spec.md section 3.3's table gives a layout container "the same value as its id scope", and
// argues it is safe because a scope derives in NowIdDomain.Scope and a container in NowIdDomain.Layout. The domain
// argument is right. The implied claim that a container resolves UNDER its id scope is right only for a root area
// (NowLayoutContainers.cs:365, via NowControls.ResolveScopedId). A nested container resolves under the enclosing
// LAYOUT GROUP instead:
//
//     NowLayout.cs:2489   groupId = parent.id.Derive(NowIdDomain.Layout, identity.authored);
//
// The bridge walks straight into the consequence. A ui.list item opens an identity scope and no container -
// section 3.2's canonical path has no box for it - so three items each drawing one anonymous ui.row give three
// rows carrying segment -1 under a single layout parent, and therefore one group id between them.
//
// Three rows sharing one measurement-cache entry is exactly what happens, and the first test below is the
// measurement: two sibling auto-sized groups with the same authored id, one 120 tall and one 40, come back on the
// next frame holding each other's extents. Not approximately - swapped.
//
// So the replay gives a container the op's RID instead of its identity segment. A rid is the recorder's dense
// integer for a path (section 3.4), unique across the whole frame by construction, so no two containers can
// derive one group id. The second test is what that buys, and the first is kept as the reason - delete it and the
// next person to "simplify" the replay back to the segment gets a silently wrong layout instead of a red test.

using NowUI;
using NowUI.Bridge;
using NowUI.Engine;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class LayoutIdentityTests
    {
        private static readonly NowRect k_Screen = new NowRect(0f, 0f, 400f, 300f);

        /// <summary>
        /// Draws two sibling auto-sized columns under one root column, each given <paramref name="idA"/> /
        /// <paramref name="idB"/> and each containing a differently sized fixed space, and returns the HEIGHTS the
        /// second frame gives them.
        /// </summary>
        /// <remarks>
        /// Heights, not widths, and the second frame, not the first - both established by getting them wrong. A
        /// vertical group inside a vertical parent stretches across the cross axis, so both children's WIDTHS are
        /// the parent's 400 whatever their identity is, and a width comparison would pass for the wrong reason.
        /// And an auto group extent "resolves from the previous frame in a one-pass host"
        /// (NowLayout.cs:1260-1266), so frame one is zero for both and frame two is the one that can see a shared
        /// cache entry.
        /// </remarks>
        private static void TwoSiblingGroups(int idA, int idB, out float heightA, out float heightB)
        {
            heightA = 0f;
            heightB = 0f;

            for (int frame = 0; frame < 2; ++frame)
            {
                NowRuntime.BeginFrame();

                using (Now.StartUI(1f))
                using (BridgeIdentity.Root())
                using (NowLayout.Column(k_Screen).SetId(new NowId(BridgeIdentity.RootAreaSegment)).Begin())
                {
                    using (NowLayoutScope a = NowLayout.Column().SetId(new NowId(idA)).Begin())
                    {
                        NowLayout.Space(120f);
                        heightA = a.rect.height;
                    }

                    using (NowLayoutScope b = NowLayout.Column().SetId(new NowId(idB)).Begin())
                    {
                        NowLayout.Space(40f);
                        heightB = b.rect.height;
                    }
                }
            }
        }

        [Test]
        public void SiblingContainersSharingOneAuthoredIdSwapTheirExtents()
        {
            // The defect, in NowUI, with no bridge in the way. Two anonymous containers, both segment -1, under
            // one layout parent, in two different identity scopes - which is what a two-item ui.list looks like
            // from NowLayout's side if the replay passes the identity segment.
            //
            // One group id, one cache entry, two writers: the first group writes 120 into it, the second writes
            // 40 over that, and next frame each of them reads what the other left.
            float a;
            float b;
            TwoSiblingGroups(-1, -1, out a, out b);

            Assert.AreEqual(40f, a, 0.001f,
                "the 120-tall group did NOT come back holding the 40-tall group's extent, so nested containers no " +
                "longer share a group id when they share an authored id. If NowLayout.cs:2489 has changed to " +
                "derive from the id scope rather than the layout parent, section 3.3's table is now literally " +
                "correct and Replay.cs can pass the segment instead of the rid.");
            Assert.AreEqual(120f, b, 0.001f, "and the 40-tall group came back holding the 120-tall one's");
        }

        [Test]
        public void SiblingContainersWithDistinctIdsSizeIndependently()
        {
            // What the rid buys, and the control for the test above: with two ids nothing is shared, and each
            // group comes back with its own extent.
            float a;
            float b;
            TwoSiblingGroups(11, 12, out a, out b);

            Assert.AreEqual(120f, a, 0.001f, "the first group sized to its own content");
            Assert.AreEqual(40f, b, 0.001f, "and the second to its own");
        }
    }
}
