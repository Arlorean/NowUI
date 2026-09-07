// Tests for unit U4 (value-type top-ups): the UnityEngine.BoundsInt and UnityEngine.LayerMask shims.
// BoundsInt: Docs/Standalone/StandaloneCoreDesign.md §3.1. LayerMask: Docs/Standalone/GradientCurveSemantics.md §7 —
// every [verified] value in that section (the layer table, the ""/null quirk, the GetMask results) is asserted here.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class BoundsIntLayerMaskTests
    {
        // =====================================================================================================
        // BoundsInt — shape
        // =====================================================================================================

        [Test]
        public void BoundsInt_IsSerializableSequentialStructOfTwoVector3Int()
        {
            Type t = typeof(BoundsInt);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<BoundsInt>).IsAssignableFrom(t), Is.True);
            Assert.That(Marshal.SizeOf<BoundsInt>(), Is.EqualTo(24));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty, "BoundsInt keeps its fields private");

            Assert.That((int)Marshal.OffsetOf<BoundsInt>("m_Position"), Is.EqualTo(0));
            Assert.That((int)Marshal.OffsetOf<BoundsInt>("m_Size"), Is.EqualTo(12));
        }

        [Test]
        public void BoundsInt_DefaultIsAllZero()
        {
            BoundsInt b = default;
            Assert.That(b.position, Is.EqualTo(new Vector3Int(0, 0, 0)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(0, 0, 0)));
        }

        // =====================================================================================================
        // BoundsInt — construction and raw accessors (the surface NowInspector uses: ctor, position, size)
        // =====================================================================================================

        [Test]
        public void BoundsInt_VectorConstructor_StoresPositionAndSizeRaw()
        {
            BoundsInt b = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(1, 2, 3)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(4, 5, 6)));
        }

        [Test]
        public void BoundsInt_ComponentConstructor_MatchesVectorConstructor()
        {
            Assert.That(new BoundsInt(1, 2, 3, 4, 5, 6), Is.EqualTo(new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6))));
        }

        [Test]
        public void BoundsInt_ComponentAccessors_ReadAndWriteRawStorage()
        {
            BoundsInt b = new BoundsInt(1, 2, 3, 4, 5, 6);
            Assert.That(b.x, Is.EqualTo(1));
            Assert.That(b.y, Is.EqualTo(2));
            Assert.That(b.z, Is.EqualTo(3));
            Assert.That(b.sizeX, Is.EqualTo(4));
            Assert.That(b.sizeY, Is.EqualTo(5));
            Assert.That(b.sizeZ, Is.EqualTo(6));

            b.x = 10;
            b.y = 20;
            b.z = 30;
            b.sizeX = 40;
            b.sizeY = 50;
            b.sizeZ = 60;
            Assert.That(b.position, Is.EqualTo(new Vector3Int(10, 20, 30)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(40, 50, 60)));
        }

        [Test]
        public void BoundsInt_PositionAndSizeSetters_ReplaceRawStorage()
        {
            BoundsInt b = new BoundsInt(1, 2, 3, 4, 5, 6);
            b.position = new Vector3Int(-1, -2, -3);
            b.size = new Vector3Int(7, 8, 9);
            Assert.That(b.position, Is.EqualTo(new Vector3Int(-1, -2, -3)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(7, 8, 9)));
        }

        // =====================================================================================================
        // BoundsInt — normalised extents
        // =====================================================================================================

        [Test]
        public void BoundsInt_Extents_ArePositionAndPositionPlusSize()
        {
            BoundsInt b = new BoundsInt(1, 2, 3, 4, 5, 6);
            Assert.That(b.xMin, Is.EqualTo(1));
            Assert.That(b.yMin, Is.EqualTo(2));
            Assert.That(b.zMin, Is.EqualTo(3));
            Assert.That(b.xMax, Is.EqualTo(5));
            Assert.That(b.yMax, Is.EqualTo(7));
            Assert.That(b.zMax, Is.EqualTo(9));
            Assert.That(b.min, Is.EqualTo(new Vector3Int(1, 2, 3)));
            Assert.That(b.max, Is.EqualTo(new Vector3Int(5, 7, 9)));
        }

        [Test]
        public void BoundsInt_Extents_NormaliseANegativeSize()
        {
            // Raw (10, 10, 10) + (-4, -4, -4): min/max swap so min <= max, while position/size stay raw.
            BoundsInt b = new BoundsInt(10, 10, 10, -4, -4, -4);
            Assert.That(b.xMin, Is.EqualTo(6));
            Assert.That(b.xMax, Is.EqualTo(10));
            Assert.That(b.min, Is.EqualTo(new Vector3Int(6, 6, 6)));
            Assert.That(b.max, Is.EqualTo(new Vector3Int(10, 10, 10)));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(10, 10, 10)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(-4, -4, -4)));
        }

        [Test]
        public void BoundsInt_MinSetter_KeepsMaxAndResizes()
        {
            BoundsInt b = new BoundsInt(0, 0, 0, 10, 10, 10);
            b.min = new Vector3Int(2, 3, 4);
            Assert.That(b.position, Is.EqualTo(new Vector3Int(2, 3, 4)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(8, 7, 6)));
            Assert.That(b.max, Is.EqualTo(new Vector3Int(10, 10, 10)), "assigning min must not move the far corner");
        }

        [Test]
        public void BoundsInt_MinSetter_OnAnInvertedBox_UsesTheNormalisedMax()
        {
            BoundsInt b = new BoundsInt(10, 10, 10, -4, -4, -4);   // min 6, max 10
            b.min = Vector3Int.zero;
            Assert.That(b.position, Is.EqualTo(new Vector3Int(0, 0, 0)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(10, 10, 10)));
        }

        [Test]
        public void BoundsInt_MaxSetter_KeepsPositionAndResizes()
        {
            BoundsInt b = new BoundsInt(1, 1, 1, 10, 10, 10);
            b.max = new Vector3Int(5, 6, 7);
            Assert.That(b.position, Is.EqualTo(new Vector3Int(1, 1, 1)), "assigning max must not move the near corner");
            Assert.That(b.size, Is.EqualTo(new Vector3Int(4, 5, 6)));
        }

        [Test]
        public void BoundsInt_Center_HalvesTheSizeInFloatSpace()
        {
            Assert.That(new BoundsInt(0, 0, 0, 3, 3, 3).center, Is.EqualTo(new Vector3(1.5f, 1.5f, 1.5f)));
            Assert.That(new BoundsInt(1, 2, 3, 4, 6, 8).center, Is.EqualTo(new Vector3(3f, 5f, 7f)));
            Assert.That(new BoundsInt(0, 0, 0, 1, 1, 1).center, Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));
            Assert.That(new BoundsInt(-4, -4, -4, -3, -3, -3).center, Is.EqualTo(new Vector3(-5.5f, -5.5f, -5.5f)));
        }

        // =====================================================================================================
        // BoundsInt — Contains, ClampToBounds, SetMinMax
        // =====================================================================================================

        [Test]
        public void BoundsInt_Contains_IsMinInclusiveAndMaxExclusive()
        {
            BoundsInt b = new BoundsInt(0, 0, 0, 2, 2, 2);
            Assert.That(b.Contains(new Vector3Int(0, 0, 0)), Is.True);
            Assert.That(b.Contains(new Vector3Int(1, 1, 1)), Is.True);
            Assert.That(b.Contains(new Vector3Int(2, 1, 1)), Is.False, "max is exclusive");
            Assert.That(b.Contains(new Vector3Int(1, 2, 1)), Is.False);
            Assert.That(b.Contains(new Vector3Int(1, 1, 2)), Is.False);
            Assert.That(b.Contains(new Vector3Int(-1, 0, 0)), Is.False);
        }

        [Test]
        public void BoundsInt_Contains_ZeroSizedBoxContainsNothing()
        {
            BoundsInt b = new BoundsInt(3, 3, 3, 0, 0, 0);
            Assert.That(b.Contains(new Vector3Int(3, 3, 3)), Is.False);
        }

        [Test]
        public void BoundsInt_Contains_UsesTheNormalisedExtentsOnAnInvertedBox()
        {
            BoundsInt b = new BoundsInt(5, 5, 5, -5, -5, -5);   // min 0, max 5
            Assert.That(b.Contains(new Vector3Int(0, 0, 0)), Is.True);
            Assert.That(b.Contains(new Vector3Int(4, 4, 4)), Is.True);
            Assert.That(b.Contains(new Vector3Int(5, 5, 5)), Is.False);
        }

        [Test]
        public void BoundsInt_ClampToBounds_ShrinksAnOverhangingBox()
        {
            // this: min 2, max 12. clamp: min 0, max 5. Position stays 2, size shrinks to 5 - 2 = 3.
            BoundsInt b = new BoundsInt(2, 2, 2, 10, 10, 10);
            b.ClampToBounds(new BoundsInt(0, 0, 0, 5, 5, 5));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(2, 2, 2)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(3, 3, 3)));
        }

        [Test]
        public void BoundsInt_ClampToBounds_LeavesAContainedBoxAlone()
        {
            BoundsInt b = new BoundsInt(1, 1, 1, 2, 2, 2);
            b.ClampToBounds(new BoundsInt(0, 0, 0, 10, 10, 10));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(1, 1, 1)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(2, 2, 2)));
        }

        [Test]
        public void BoundsInt_ClampToBounds_CollapsesABoxPastTheFarCorner()
        {
            // Position is clamped to bounds.max first, which leaves a zero size rather than a negative one.
            BoundsInt b = new BoundsInt(20, 20, 20, 5, 5, 5);
            b.ClampToBounds(new BoundsInt(0, 0, 0, 5, 5, 5));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(5, 5, 5)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(0, 0, 0)));
        }

        [Test]
        public void BoundsInt_ClampToBounds_PushesABoxBeforeTheNearCornerInwards()
        {
            // Quirk kept from Unity: the position moves to the near corner but the size is not reduced by the move,
            // so a box that started entirely before the clamping box comes out with its original size.
            BoundsInt b = new BoundsInt(-10, -10, -10, 3, 3, 3);
            b.ClampToBounds(new BoundsInt(0, 0, 0, 5, 5, 5));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(0, 0, 0)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(3, 3, 3)));
        }

        [Test]
        public void BoundsInt_SetMinMax_WritesPositionAndSizeDirectly()
        {
            BoundsInt b = new BoundsInt(9, 9, 9, 9, 9, 9);
            b.SetMinMax(new Vector3Int(1, 2, 3), new Vector3Int(4, 6, 8));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(1, 2, 3)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(3, 4, 5)));
        }

        [Test]
        public void BoundsInt_SetMinMax_WithASwappedPairStoresANegativeSize()
        {
            BoundsInt b = default;
            b.SetMinMax(new Vector3Int(5, 5, 5), new Vector3Int(1, 1, 1));
            Assert.That(b.position, Is.EqualTo(new Vector3Int(5, 5, 5)));
            Assert.That(b.size, Is.EqualTo(new Vector3Int(-4, -4, -4)));
            Assert.That(b.min, Is.EqualTo(new Vector3Int(1, 1, 1)), "the extents still normalise");
        }

        // =====================================================================================================
        // BoundsInt — equality, hashing, ToString
        // =====================================================================================================

        [Test]
        public void BoundsInt_Equality_ComparesRawStorage()
        {
            BoundsInt a = new BoundsInt(1, 2, 3, 4, 5, 6);
            BoundsInt b = new BoundsInt(1, 2, 3, 4, 5, 6);
            BoundsInt c = new BoundsInt(1, 2, 3, 4, 5, 7);

            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
            Assert.That(a == c, Is.False);
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals("not a bounds"), Is.False);
            Assert.That(a.Equals(null), Is.False);
        }

        [Test]
        public void BoundsInt_Equality_SameRegionDifferentRawStorageIsNotEqual()
        {
            // (0,0,0)+(5,5,5) and (5,5,5)+(-5,-5,-5) cover the same cells but are stored differently.
            BoundsInt a = new BoundsInt(0, 0, 0, 5, 5, 5);
            BoundsInt b = new BoundsInt(5, 5, 5, -5, -5, -5);
            Assert.That(a.min, Is.EqualTo(b.min));
            Assert.That(a.max, Is.EqualTo(b.max));
            Assert.That(a == b, Is.False);
        }

        [Test]
        public void BoundsInt_GetHashCode_IsStableAndMatchesEquality()
        {
            BoundsInt a = new BoundsInt(1, 2, 3, 4, 5, 6);
            BoundsInt b = new BoundsInt(1, 2, 3, 4, 5, 6);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.GetHashCode(), Is.EqualTo(a.GetHashCode()));

            // Position and size are mixed with a shift, so swapping them changes the hash.
            Assert.That(new BoundsInt(4, 5, 6, 1, 2, 3).GetHashCode(), Is.Not.EqualTo(a.GetHashCode()));
        }

        [Test]
        public void BoundsInt_ToString_IsPositionThenSize()
        {
            Assert.That(new BoundsInt(1, 2, 3, 4, 5, 6).ToString(), Is.EqualTo("Position: (1, 2, 3), Size: (4, 5, 6)"));
            Assert.That(new BoundsInt(-1, 0, 7, -4, 0, 2).ToString(), Is.EqualTo("Position: (-1, 0, 7), Size: (-4, 0, 2)"));
        }

        // =====================================================================================================
        // LayerMask — shape (GC §7)
        // =====================================================================================================

        [Test]
        public void LayerMask_IsANonSerializableSequentialStructOfOneInt()
        {
            Type t = typeof(LayerMask);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.False, "6000.4 has no [Serializable] on LayerMask (GC §7)");
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(Marshal.SizeOf<LayerMask>(), Is.EqualTo(4));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That((int)Marshal.OffsetOf<LayerMask>("m_Mask"), Is.EqualTo(0));
            Assert.That(typeof(IEquatable<LayerMask>).IsAssignableFrom(t), Is.False, "Unity does not implement IEquatable here");
        }

        [Test]
        public void LayerMask_HasNoToStringOverride()
        {
            // GC §7 [verified]: 6000.4 ships no ToString override, so the ValueType default shows through.
            MethodInfo toString = typeof(LayerMask).GetMethod("ToString", Type.EmptyTypes);
            Assert.That(toString.DeclaringType, Is.EqualTo(typeof(ValueType)));

            LayerMask mask = 5;
            Assert.That(mask.ToString(), Is.EqualTo("UnityEngine.LayerMask"));
        }

        [Test]
        public void LayerMask_DefaultValueIsZero()
        {
            Assert.That(default(LayerMask).value, Is.EqualTo(0));
        }

        [Test]
        public void LayerMask_ImplicitConversionsRoundTrip()
        {
            LayerMask mask = 33;                 // int -> LayerMask
            Assert.That(mask.value, Is.EqualTo(33));
            int back = mask;                     // LayerMask -> int
            Assert.That(back, Is.EqualTo(33));

            mask.value = -1;
            Assert.That((int)mask, Is.EqualTo(-1));
        }

        [Test]
        public void LayerMask_EqualsAndGetHashCode_RunOverTheMask()
        {
            LayerMask a = 5;
            LayerMask b = 5;
            LayerMask c = 6;

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals(c), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.GetHashCode(), Is.EqualTo(5.GetHashCode()));

            // A boxed int is not a LayerMask, so it is not equal — the same as Unity's ValueType.Equals.
            Assert.That(a.Equals((object)5), Is.False);
        }

        // =====================================================================================================
        // LayerMask — the default project table (GC §7 [verified])
        // =====================================================================================================

        [Test]
        public void LayerMask_DefaultTable_NamesTheSixBuiltInLayers()
        {
            Assert.That(LayerMask.LayerToName(0), Is.EqualTo("Default"));
            Assert.That(LayerMask.LayerToName(1), Is.EqualTo("TransparentFX"));
            Assert.That(LayerMask.LayerToName(2), Is.EqualTo("Ignore Raycast"));
            Assert.That(LayerMask.LayerToName(3), Is.EqualTo(""));
            Assert.That(LayerMask.LayerToName(4), Is.EqualTo("Water"));
            Assert.That(LayerMask.LayerToName(5), Is.EqualTo("UI"));
        }

        [Test]
        public void LayerMask_DefaultTable_LeavesLayersSixToThirtyOneEmpty()
        {
            for (int layer = 6; layer < 32; layer++)
                Assert.That(LayerMask.LayerToName(layer), Is.EqualTo(""), "layer " + layer);
        }

        [TestCase(-1)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(100)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void LayerMask_LayerToName_OutOfRangeReturnsEmptyAndNeverThrows(int layer)
        {
            Assert.That(LayerMask.LayerToName(layer), Is.EqualTo(""));
        }

        [TestCase("Default", 0)]
        [TestCase("TransparentFX", 1)]
        [TestCase("Ignore Raycast", 2)]
        [TestCase("Water", 4)]
        [TestCase("UI", 5)]
        public void LayerMask_NameToLayer_ResolvesTheBuiltInNames(string name, int expected)
        {
            Assert.That(LayerMask.NameToLayer(name), Is.EqualTo(expected));
        }

        [TestCase("default")]
        [TestCase("DEFAULT")]
        [TestCase(" Default")]
        [TestCase("Default ")]
        [TestCase("ui")]
        [TestCase("nope")]
        [TestCase("Ignore  Raycast")]
        public void LayerMask_NameToLayer_IsOrdinalCaseSensitiveAndUntrimmed(string name)
        {
            Assert.That(LayerMask.NameToLayer(name), Is.EqualTo(-1));
        }

        [Test]
        public void LayerMask_NameToLayer_EmptyAndNullHitTheFirstUnnamedLayer()
        {
            // GC §7 [verified] quirk: both return 3 with the default table, because layer 3 is the first empty name.
            Assert.That(LayerMask.NameToLayer(""), Is.EqualTo(3));
            Assert.That(LayerMask.NameToLayer(null), Is.EqualTo(3));
        }

        // =====================================================================================================
        // LayerMask — GetMask (GC §7 [verified])
        // =====================================================================================================

        [Test]
        public void LayerMask_GetMask_OrsTheResolvedLayers()
        {
            Assert.That(LayerMask.GetMask("Default", "UI"), Is.EqualTo(33));    // 1 << 0 | 1 << 5
            Assert.That(LayerMask.GetMask("Water"), Is.EqualTo(16));
            Assert.That(LayerMask.GetMask("Default", "Default"), Is.EqualTo(1), "a repeat ORs the same bit");
        }

        [Test]
        public void LayerMask_GetMask_SkipsUnknownNames()
        {
            Assert.That(LayerMask.GetMask("nope"), Is.EqualTo(0));
            Assert.That(LayerMask.GetMask(), Is.EqualTo(0));
            Assert.That(LayerMask.GetMask("nope", "UI"), Is.EqualTo(32));
        }

        [Test]
        public void LayerMask_GetMask_NullNameFollowsTheEmptyNameQuirk()
        {
            // NameToLayer(null) == 3, so the bit that comes out is 1 << 3 == 8.
            Assert.That(LayerMask.GetMask(new string[] { null }), Is.EqualTo(8));
            Assert.That(LayerMask.GetMask(new string[] { "" }), Is.EqualTo(8));
        }

        [Test]
        public void LayerMask_GetMask_NullArrayThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => LayerMask.GetMask((string[])null));
            Assert.That(ex.ParamName, Is.EqualTo("layerNames"));
        }

        // =====================================================================================================
        // LayerMask — the table comes from the host, not from the shim
        // =====================================================================================================

        [Test]
        public void LayerMask_ReadsTheTableFromTheRegisteredHost()
        {
            DefaultHostServices host = new DefaultHostServices();
            host.layerNames[6] = "Minimap";
            host.layerNames[0] = "Renamed";
            try
            {
                NowRuntime.Initialize(host, null);

                Assert.That(LayerMask.LayerToName(6), Is.EqualTo("Minimap"));
                Assert.That(LayerMask.NameToLayer("Minimap"), Is.EqualTo(6));
                Assert.That(LayerMask.GetMask("Minimap", "UI"), Is.EqualTo(96));   // 1 << 6 | 1 << 5

                Assert.That(LayerMask.LayerToName(0), Is.EqualTo("Renamed"));
                Assert.That(LayerMask.NameToLayer("Default"), Is.EqualTo(-1), "the built-in names are host data, not shim data");

                // Layer 3 is still the first empty slot, so the ""/null quirk is unchanged.
                Assert.That(LayerMask.NameToLayer(""), Is.EqualTo(3));
            }
            finally
            {
                // Passing null restores the stock DefaultHostServices, whose table is a fresh clone.
                NowRuntime.Initialize(null, null);
            }

            Assert.That(LayerMask.LayerToName(0), Is.EqualTo("Default"), "the default table must not have been mutated");
            Assert.That(LayerMask.LayerToName(6), Is.EqualTo(""));
        }
    }
}
