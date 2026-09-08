// W3's acceptance (a), and gate G9. Docs/Standalone/M3-Spec.md sections 3.3, 3.6 and 7.4.
//
// G9 is the load-bearing one:
//
//   NowControls.GetControlId(new NowId(h)) under scope S must equal what builder.SetId(new NowId(h)) resolves to
//   under S, for nested scopes and for list items.
//
// Both SHOULD funnel through NowControls.cs:498-504 to CurrentIdentityParent().Derive(NowIdDomain.Control, id).
// "Should" is not "does", and the reason it has to be measured rather than reasoned is section 3.4: the bridge
// has no NowResolvedId to compare against, because NowResolvedId has no public value constructor
// (NowResolvedId.cs:15) - so if these two ever diverged, every rid, every diagnostic, every state lookup and the
// whole of ui.contextMenu would be wrong with nothing to say so.
//
// The comparison is made against the code path the BUILDERS actually run, not a re-derivation of it:
// BridgeIdentity.BuilderId resolves a NowControlIdentity exactly as NowButton.ResolveControlId does
// (NowControlBuilders.cs:30). And the last test in this file closes the loop behaviourally - it focuses an id
// derived one way and draws a control identified the other way, and the control comes up focused.

using NowUI;
using NowUI.Bridge;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class IdentityTests
    {
        // Two arbitrary intern handles and two anonymous ordinals, named so the tests read as paths.
        private const int Roster = 0;
        private const int Team = 1;
        private const int Grace = 2;
        private const int Remove = 3;
        private const int Anon0 = -1;      // -(0 + 1)
        private const int Anon1 = -2;      // -(1 + 1)

        // ------------------------------------------------------------------------------------- G9

        [Test]
        public void G9_AtTheRoot()
        {
            using (BridgeIdentity.Root())
            {
                Assert.AreEqual(BridgeIdentity.ControlId(Remove), BridgeIdentity.BuilderId(Remove));
            }
        }

        [Test]
        public void G9_UnderNestedScopes()
        {
            using (BridgeIdentity.Root())
            using (BridgeIdentity.Scope(Anon0))
            using (BridgeIdentity.Scope(Roster))
            using (BridgeIdentity.Scope(Anon1))
            {
                Assert.AreEqual(BridgeIdentity.ControlId(Remove), BridgeIdentity.BuilderId(Remove),
                    "GetControlId and SetId must resolve identically four scopes deep");
            }
        }

        [Test]
        public void G9_InsideAListItem()
        {
            using (BridgeIdentity.Root())
            using (BridgeIdentity.Scope(Roster))
            using (BridgeIdentity.Item(Team, Grace))
            {
                Assert.AreEqual(BridgeIdentity.ControlId(Remove), BridgeIdentity.BuilderId(Remove),
                    "and inside a keyed list item, which is the case section 3.2's canonical path is drawn from");
            }
        }

        [Test]
        public void G9_NegativeControl_ADifferentScopeIsADifferentControl()
        {
            NowResolvedId underAnon0;
            NowResolvedId underAnon1;

            using (BridgeIdentity.Root())
            {
                using (BridgeIdentity.Scope(Anon0)) underAnon0 = BridgeIdentity.BuilderId(Remove);
                using (BridgeIdentity.Scope(Anon1)) underAnon1 = BridgeIdentity.BuilderId(Remove);
            }

            Assert.AreNotEqual(underAnon0, underAnon1,
                "if these were equal, the whole identity model would be a no-op and every other test here would " +
                "pass vacuously");
        }

        // ------------------------------------------------------------------------------------- section 3.3

        [Test]
        public void IdScopeOfAnIntIsTotal_ItCanNeitherThrowNorNoOp()
        {
            // Section 3.3 point 2. The trap it avoids: IdScope(NowId) silently pushes NOTHING on a default id
            // (NowControls.cs:213-214), so a bridge built on that overload would put a whole subtree at the wrong
            // depth with no error. Every value an int can take is checked at the edges.
            using (BridgeIdentity.Root())
            {
                NowResolvedId atRoot = BridgeIdentity.BuilderId(Remove);

                foreach (int segment in new[] { 0, 1, -1, int.MaxValue, int.MinValue })
                {
                    using (BridgeIdentity.Scope(segment))
                    {
                        Assert.AreNotEqual(atRoot, BridgeIdentity.BuilderId(Remove),
                            "IdScope(" + segment + ") did not push a scope");
                    }
                }
            }
        }

        [Test]
        public void TheTwoIntegerNamespacesCannotCollide()
        {
            // Section 3.3 point 4: intern handles are >= 0, anonymous ordinals are <= -1, so a scope keyed by
            // handle 0 and the first anonymous scope are different scopes even though both are "the first one".
            NowResolvedId keyed;
            NowResolvedId anonymous;

            using (BridgeIdentity.Root())
            {
                using (BridgeIdentity.Scope(0)) keyed = BridgeIdentity.BuilderId(Remove);
                using (BridgeIdentity.Scope(-1)) anonymous = BridgeIdentity.BuilderId(Remove);
            }

            Assert.AreNotEqual(keyed, anonymous);
        }

        [Test]
        public void ScopeAndControlDomainsDoNotAlias()
        {
            // Section 3.3 point 5, and Identity.md:48-52. The same integer used as a scope segment and as a
            // control segment must not produce the same value, or a control could shadow the scope it sits in.
            using (BridgeIdentity.Root())
            {
                NowResolvedId asControl = BridgeIdentity.BuilderId(Roster);

                using (BridgeIdentity.Scope(Roster))
                {
                    // The scope's own resolved value is not public, so this compares the two things a caller can
                    // actually reach: a control keyed 'roster' at this level, and a control keyed 'roster' inside
                    // a scope keyed 'roster'.
                    Assert.AreNotEqual(asControl, BridgeIdentity.BuilderId(Roster));
                }
            }
        }

        [Test]
        public void KeyedItemInNestsUnderTheCurrentScopeRatherThanReplacingIt()
        {
            // KeyedItemIn is built on RestoreIdScope, which for IdScope(NowResolvedId) REPLACES the ambient path
            // (NowControls.cs:126-138). It is safe here because KeyedItemIn resolves the list under
            // CurrentIdentityParent() first (NowControls.cs:268-270) - but that is a fact about the source, and
            // the bridge's identity ancestry depends on it, so it is asserted.
            NowResolvedId underA;
            NowResolvedId underB;

            using (BridgeIdentity.Root())
            {
                using (BridgeIdentity.Scope(Anon0))
                using (BridgeIdentity.Item(Team, Grace))
                    underA = BridgeIdentity.BuilderId(Remove);

                using (BridgeIdentity.Scope(Anon1))
                using (BridgeIdentity.Item(Team, Grace))
                    underB = BridgeIdentity.BuilderId(Remove);
            }

            Assert.AreNotEqual(underA, underB,
                "the same list key and item key under two different parents must be two different items");
        }

        [Test]
        public void TwoItemsOfOneListAreDifferentAndSurviveReordering()
        {
            NowResolvedId grace;
            NowResolvedId ada;
            NowResolvedId graceAgain;

            using (BridgeIdentity.Root())
            {
                using (BridgeIdentity.Item(Team, Grace)) grace = BridgeIdentity.BuilderId(Remove);
                using (BridgeIdentity.Item(Team, 4)) ada = BridgeIdentity.BuilderId(Remove);

                // Drawn in the other order, which is what a reorder of the underlying array looks like.
                using (BridgeIdentity.Item(Team, 4)) { }
                using (BridgeIdentity.Item(Team, Grace)) graceAgain = BridgeIdentity.BuilderId(Remove);
            }

            Assert.AreNotEqual(grace, ada, "two items are two controls");
            Assert.AreEqual(grace, graceAgain, "and an item's identity follows its key, not its position");
        }

        [Test]
        public void ExplicitIdsAreNeverOccurrenceSalted()
        {
            // Section 3.3's consequence, and the reason section 5.6's idempotence holds: the measure pass and the
            // real pass resolve every bridge control to the same NowResolvedId, because salting applies only when
            // identity falls back to the call site (NowControls.cs:526-529).
            using (BridgeIdentity.Root())
            {
                NowResolvedId first = BridgeIdentity.BuilderId(Remove);
                NowResolvedId second = BridgeIdentity.BuilderId(Remove);
                NowResolvedId third = BridgeIdentity.BuilderId(Remove);

                Assert.AreEqual(first, second);
                Assert.AreEqual(first, third);
            }
        }

        [Test]
        public void TheRootScopeSeparatesABridgeUIFromACSharpOneOnTheSameSurface()
        {
            NowResolvedId inCSharp = NowControls.GetControlId(new NowId(Remove));

            NowResolvedId inBridge;
            using (BridgeIdentity.Root()) inBridge = BridgeIdentity.BuilderId(Remove);

            Assert.AreNotEqual(inCSharp, inBridge,
                "a C# control with the same explicit id, drawn in the same frame, must not share state with a " +
                "JavaScript-authored one");
        }

        // ------------------------------------------------------------------------------------- behavioural

        [Test]
        public void G9_Behavioural_FocusingTheGetControlIdValueFocusesTheSetIdControl()
        {
            // The loop closed. The id is derived one way (GetControlId, which is what the bridge's diagnostics and
            // its duplicate backstop use) and the control is identified the other way (SetId, which is what the
            // replay draws with). If G9 did not hold, the button would draw unfocused and nothing else would
            // change - which is exactly the class of silent failure this gate exists for.
            var screen = new NowRect(0f, 0f, 400f, 200f);

            using (Now.StartUI(1f))
            using (BridgeIdentity.Root())
            using (NowLayout.Column(screen).SetId(new NowId(BridgeIdentity.RootAreaSegment)).Begin())
            using (BridgeIdentity.Scope(Roster))
            {
                NowFocus.Focus(BridgeIdentity.ControlId(Remove));

                using (var button = NowLayout.Button().SetId(new NowId(Remove)).Begin())
                {
                    Assert.IsTrue(button.focused,
                        "the control the replay draws is the control GetControlId named");
                }

                using (var other = NowLayout.Button().SetId(new NowId(Team)).Begin())
                {
                    Assert.IsFalse(other.focused, "and only that one");
                }
            }
        }
    }
}
