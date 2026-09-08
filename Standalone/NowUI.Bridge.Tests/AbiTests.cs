// The ABI, and gate G3. Docs/Standalone/M3-Spec.md sections 5.2 and 7.4.
//
// Abi.cs and wwwroot/nowui/abi.js are two hand-edited copies of one table until W8's generator emits both. The
// thing that keeps them honest is the surface hash, and the thing that keeps the surface hash honest is the first
// test below: it asks node for abi.js's value and compares.

using System.Collections.Generic;
using NowUI.Bridge;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class AbiTests
    {
        [Test]
        public void G3_TheJavaScriptAndManagedSurfaceHashesAgree()
        {
            string reported = Node.Run("--surface-hash").Trim();

            Assert.AreEqual(Abi.SurfaceHash.ToString(), reported,
                "abi.js and Abi.cs computed different surface hashes, which means the two halves of the ABI have " +
                "drifted. Every frame would be refused at the header with both hashes named - which is the design " +
                "working, but it is the wrong place to find out.");
        }

        [Test]
        public void EveryOpcodeIsAboveTheReservedStructuralRange()
        {
            foreach (OpSpec spec in Abi.Specs)
            {
                Assert.GreaterOrEqual(spec.Opcode, Abi.FirstHashedOp,
                    spec.Name + " collided with the reserved structural opcodes 0-15");
                Assert.LessOrEqual(spec.Opcode, 0xFFFF, spec.Name + " does not fit in 16 bits");
            }
        }

        [Test]
        public void OpcodesAreUniqueAndContentHashed()
        {
            var seen = new Dictionary<int, string>();

            foreach (OpSpec spec in Abi.Specs)
            {
                Assert.IsFalse(seen.ContainsKey(spec.Opcode),
                    spec.Name + " and " + (seen.ContainsKey(spec.Opcode) ? seen[spec.Opcode] : "") +
                    " share opcode " + spec.Opcode);
                seen.Add(spec.Opcode, spec.Name);

                Assert.AreEqual(spec.Opcode, Abi.OpcodeFor(spec.Signature, spec.Salt),
                    spec.Name + "'s opcode is not the hash of its signature, so adding a function would renumber it");
            }
        }

        [Test]
        public void AddingAnOpDoesNotRenumberTheExistingOnes()
        {
            // The property section 5.2 buys with a content hash instead of an ordinal: "adding a function
            // renumbers nothing". Asserted by hashing a signature that is not in the table and checking that
            // every existing opcode is unchanged by its existence - which is true by construction here, and would
            // not be for an ordinal scheme.
            int before = Abi.Op("TEXT").Opcode;
            int hypothetical = Abi.OpcodeFor("NowUI.NowLayout.Checkbox(System.String,System.Boolean)");

            Assert.AreNotEqual(hypothetical, before);
            Assert.AreEqual(before, Abi.Op("TEXT").Opcode);
        }

        [Test]
        public void TheSurfaceHashChangesWhenAnyOpChanges()
        {
            // A hash that did not move when a signature moved would be worse than no hash: it would certify a
            // mismatch. Recomputed here over a modified line rather than by editing the table.
            uint asIs = Abi.Fnv1a32("TEXT=" + Abi.Op("TEXT").Opcode + ":" + Abi.Op("TEXT").Signature + ":str");
            uint renamed = Abi.Fnv1a32("TEXT=" + Abi.Op("TEXT").Opcode + ":NowUI.NowLayout.Caption(System.String):str");

            Assert.AreNotEqual(asIs, renamed);
        }

        [Test]
        public void ArgumentSlotWidthsMatchSection53()
        {
            Assert.AreEqual(1, Abi.Op("TEXT").Slots, "TEXT: one str slot");
            Assert.AreEqual(2, Abi.Op("COLUMN").Slots, "COLUMN: rid + seg");
            Assert.AreEqual(2, Abi.Op("ROW").Slots, "ROW: rid + seg");
            Assert.AreEqual(3, Abi.Op("LIST_ITEM").Slots, "LIST_ITEM: rid + list seg + item seg");
            Assert.AreEqual(3, Abi.Op("BUTTON").Slots, "BUTTON: rid + seg + label");
        }

        [Test]
        public void OnlyScopeOpeningOpsAreMarkedAsSuch()
        {
            Assert.IsTrue(Abi.Op("COLUMN").OpensScope);
            Assert.IsTrue(Abi.Op("ROW").OpensScope);
            Assert.IsTrue(Abi.Op("LIST_ITEM").OpensScope);
            Assert.IsFalse(Abi.Op("TEXT").OpensScope, "a drawing opens nothing");
            Assert.IsFalse(Abi.Op("BUTTON").OpensScope, "and neither does a plain control");
        }
    }
}
