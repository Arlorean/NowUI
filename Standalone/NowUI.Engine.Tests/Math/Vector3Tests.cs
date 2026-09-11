// Tests for the UnityEngine.Vector3 (spec §2) and UnityEngine.Vector3Int (spec §12) shims against
// Docs/Standalone/UnityValueTypeSemantics.md. Every [verified] value in the spec is asserted bit-exactly; formula-only
// members are checked against values worked out by hand from the spec's expression (the arithmetic is in the comments).
// The only ulp slack used is for the native Slerp/RotateTowards paths, where spec §15.2 explicitly says native trig
// rounding (1-3 ulp) cannot be reproduced from the outside and golden tests should tolerate it.
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class Vector3Tests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void AssertBits(Vector3 actual, float x, float y, float z)
        {
            Assert.That(Bits(actual.x), Is.EqualTo(Bits(x)), () => $"x of {actual:R} != {x:R}");
            Assert.That(Bits(actual.y), Is.EqualTo(Bits(y)), () => $"y of {actual:R} != {y:R}");
            Assert.That(Bits(actual.z), Is.EqualTo(Bits(z)), () => $"z of {actual:R} != {z:R}");
        }

        private static void AssertExact(Vector3Int actual, int x, int y, int z)
        {
            Assert.That(actual.x, Is.EqualTo(x));
            Assert.That(actual.y, Is.EqualTo(y));
            Assert.That(actual.z, Is.EqualTo(z));
        }

        // Spec §15.2: native sin/cos rounding differs by 1-3 ulp; used only for the native Slerp/RotateTowards members.
        private static void AssertNativeTrig(Vector3 actual, float x, float y, float z)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(3).Ulps, () => $"x of {actual:R}");
            Assert.That(actual.y, Is.EqualTo(y).Within(3).Ulps, () => $"y of {actual:R}");
            Assert.That(actual.z, Is.EqualTo(z).Within(3).Ulps, () => $"z of {actual:R}");
        }

        // ================================================================== Vector3 (spec §2)

        // ------------------------------------------------------------------ §0: type shape

        [Test]
        public void Vector3_TypeShape()
        {
            Type t = typeof(Vector3);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Vector3>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);

            // Public float fields x, y, z in declared order and nothing else (arrays are reinterpreted as float*).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(3));
            Assert.That(fields[0].Name, Is.EqualTo("x"));
            Assert.That(fields[1].Name, Is.EqualTo("y"));
            Assert.That(fields[2].Name, Is.EqualTo("z"));
            foreach (FieldInfo f in fields)
            {
                Assert.That(f.FieldType, Is.EqualTo(typeof(float)));
                Assert.That(f.IsPublic, Is.True);
            }
            Assert.That(Marshal.SizeOf<Vector3>(), Is.EqualTo(12));
        }

        [Test]
        public void Vector3_Constants()
        {
            Assert.That(Bits(Vector3.kEpsilon), Is.EqualTo(Bits(0.00001F)));
            Assert.That(Bits(Vector3.kEpsilonNormalSqrt), Is.EqualTo(Bits(1e-15F)));
        }

        [Test]
        public void Vector3_HasNoComponentwiseMultiplyOrDivideOperators()
        {
            // Spec §2: "No componentwise * / operators" (only Vector2 has them).
            Type t = typeof(Vector3);
            Assert.That(t.GetMethod("op_Multiply", new[] { typeof(Vector3), typeof(Vector3) }), Is.Null);
            Assert.That(t.GetMethod("op_Division", new[] { typeof(Vector3), typeof(Vector3) }), Is.Null);
            Assert.That(t.GetMethod("op_Multiply", new[] { typeof(Vector3), typeof(float) }), Is.Not.Null);
            Assert.That(t.GetMethod("op_Multiply", new[] { typeof(float), typeof(Vector3) }), Is.Not.Null);
            Assert.That(t.GetMethod("op_Division", new[] { typeof(Vector3), typeof(float) }), Is.Not.Null);
        }

        // ------------------------------------------------------------------ constructors / indexer

        [Test]
        public void Vector3_Constructors()
        {
            AssertBits(new Vector3(1F, 2F, 3F), 1F, 2F, 3F);
            AssertBits(new Vector3(1F, 2F), 1F, 2F, 0F);
            AssertBits(default(Vector3), 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_Indexer_GetAndSet()
        {
            Vector3 v = new Vector3(1F, 2F, 3F);
            Assert.That(v[0], Is.EqualTo(1F));
            Assert.That(v[1], Is.EqualTo(2F));
            Assert.That(v[2], Is.EqualTo(3F));
            v[0] = 10F;
            v[1] = 20F;
            v[2] = 30F;
            AssertBits(v, 10F, 20F, 30F);
        }

        [TestCase(3)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void Vector3_Indexer_InvalidIndexThrows(int index)
        {
            Vector3 v = new Vector3(1F, 2F, 3F);
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { float _ = v[index]; });
            Assert.That(getEx.Message, Is.EqualTo("Invalid Vector3 index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { v[index] = 1F; });
            Assert.That(setEx.Message, Is.EqualTo("Invalid Vector3 index!"));
        }

        // ------------------------------------------------------------------ presets

        [Test]
        public void Vector3_Presets()
        {
            AssertBits(Vector3.zero, 0F, 0F, 0F);
            AssertBits(Vector3.one, 1F, 1F, 1F);
            AssertBits(Vector3.up, 0F, 1F, 0F);
            AssertBits(Vector3.down, 0F, -1F, 0F);
            AssertBits(Vector3.left, -1F, 0F, 0F);
            AssertBits(Vector3.right, 1F, 0F, 0F);
            AssertBits(Vector3.forward, 0F, 0F, 1F);
            AssertBits(Vector3.back, 0F, 0F, -1F);
            AssertBits(Vector3.positiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            AssertBits(Vector3.negativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        }

        [Test]
        public void Vector3_PresetsAreNotMutableThroughCopies()
        {
            Vector3 v = Vector3.zero;
            v.x = 5F;
            AssertBits(Vector3.zero, 0F, 0F, 0F);
        }

        // ------------------------------------------------------------------ instance members

        [Test]
        public void Vector3_Set()
        {
            Vector3 v = new Vector3(1F, 2F, 3F);
            v.Set(4F, 5F, 6F);
            AssertBits(v, 4F, 5F, 6F);
        }

        [Test]
        public void Vector3_ScaleInstance()
        {
            Vector3 v = new Vector3(1F, 2F, 3F);
            v.Scale(new Vector3(4F, 5F, 6F));
            // 1*4, 2*5, 3*6
            AssertBits(v, 4F, 10F, 18F);

            // Spec §0 in-twin, present on Vector2 and Vector4 too, so `in`-passing callers bind the same way on all three.
            Vector3 w = new Vector3(1F, 2F, 3F);
            Vector3 factor = new Vector3(4F, 5F, 6F);
            w.Scale(in factor);
            AssertBits(w, 4F, 10F, 18F);
            Assert.That(typeof(Vector3).GetMethod("Scale", new[] { typeof(Vector3).MakeByRefType() }), Is.Not.Null,
                "Vector3.Scale(in Vector3) must exist");
        }

        [Test]
        public void Vector3_NormalizeInstance()
        {
            // mag = sqrt(9 + 16) = 5; 3/5 and 4/5 round to the floats nearest 0.6 and 0.8.
            Vector3 v = new Vector3(3F, 4F, 0F);
            v.Normalize();
            AssertBits(v, 0.6F, 0.8F, 0F);

            // Power-of-two magnitude: exact.
            Vector3 w = new Vector3(0F, 0F, -0.25F);
            w.Normalize();
            AssertBits(w, 0F, 0F, -1F);
        }

        [Test]
        public void Vector3_NormalizeInstance_ZeroesBelowOrAtEpsilon()
        {
            // Threshold is strictly mag > kEpsilon. f32(sqrt(f32(1e-5f * 1e-5f))) rounds back to exactly 1e-5f
            // [0x3727C5AC], so a vector whose magnitude is exactly kEpsilon is zeroed.
            Vector3 v = new Vector3(Vector3.kEpsilon, 0F, 0F);
            Assert.That(Bits(v.magnitude), Is.EqualTo(Bits(Vector3.kEpsilon)));
            v.Normalize();
            AssertBits(v, 0F, 0F, 0F);

            Vector3 tiny = new Vector3(0F, 5e-6F, 0F);
            tiny.Normalize();
            AssertBits(tiny, 0F, 0F, 0F);

            Vector3 z = Vector3.zero;
            z.Normalize();
            AssertBits(z, 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_NormalizedProperty_EqualsStaticNormalize()
        {
            Vector3 v = new Vector3(3F, 0F, 4F);
            AssertBits(v.normalized, 0.6F, 0F, 0.8F);
            // The property does not mutate the instance.
            AssertBits(v, 3F, 0F, 4F);
            Assert.That(new Vector3(1e-6F, 0F, 0F).normalized, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Vector3_Magnitude_And_SqrMagnitude()
        {
            Vector3 v = new Vector3(1F, 2F, 3F);
            // 1 + 4 + 9 = 14 exactly; f32(Math.Sqrt(14)) = 3.7416575 [0x406F7751]
            Assert.That(Bits(v.sqrMagnitude), Is.EqualTo(Bits(14F)));
            Assert.That(Bits(v.magnitude), Is.EqualTo(0x406F7751));
            Assert.That(Bits(Vector3.Magnitude(v)), Is.EqualTo(0x406F7751));
            Assert.That(Bits(Vector3.SqrMagnitude(v)), Is.EqualTo(Bits(14F)));
            Assert.That(Bits(Vector3.Magnitude(in v)), Is.EqualTo(0x406F7751));
            Assert.That(Bits(Vector3.SqrMagnitude(in v)), Is.EqualTo(Bits(14F)));

            // 4 + 16 + 4 = 24? no: (2, 3, 6): 4 + 9 + 36 = 49 -> 7 exactly.
            Assert.That(new Vector3(2F, 3F, 6F).magnitude, Is.EqualTo(7F));
            Assert.That(new Vector3(-2F, -3F, -6F).magnitude, Is.EqualTo(7F));
            Assert.That(Vector3.zero.magnitude, Is.EqualTo(0F));
            Assert.That(float.IsNaN(new Vector3(float.NaN, 0F, 0F).magnitude), Is.True);
            Assert.That(Vector3.positiveInfinity.magnitude, Is.EqualTo(float.PositiveInfinity));
        }

        // ------------------------------------------------------------------ hash / equality

        [Test]
        public void Vector3_GetHashCode_VerifiedValue()
        {
            // Spec §2 [verified]: (1,2,3) -> 797966336.
            // 0x3F800000 ^ (0x40000000 << 2 = 0) ^ (0x40400000 >> 2 = 0x10100000) = 0x2F900000 = 797966336.
            Assert.That(new Vector3(1F, 2F, 3F).GetHashCode(), Is.EqualTo(797966336));
        }

        [Test]
        public void Vector3_GetHashCode_Formula()
        {
            // x ^ (y << 2) ^ (z >> 2) on the raw float bits:
            // 0.5 = 0x3F000000; -1 = 0xBF800000, << 2 -> 0xFE000000; 4 = 0x40800000, >> 2 -> 0x10200000
            // 0x3F000000 ^ 0xFE000000 = 0xC1000000; ^ 0x10200000 = 0xD1200000 = -786432000.
            Assert.That(new Vector3(0.5F, -1F, 4F).GetHashCode(), Is.EqualTo(-786432000));
            Assert.That(Vector3.zero.GetHashCode(), Is.EqualTo(0));
            // Hash of a single component in x is the raw bit pattern.
            Assert.That(new Vector3(1F, 0F, 0F).GetHashCode(), Is.EqualTo(0x3F800000));
            // Same components -> same hash.
            Assert.That(new Vector3(1.25F, -7F, 1e-3F).GetHashCode(), Is.EqualTo(new Vector3(1.25F, -7F, 1e-3F).GetHashCode()));
        }

        [Test]
        public void Vector3_Equals_IsExact()
        {
            Vector3 a = new Vector3(1F, 2F, 3F);
            Assert.That(a.Equals(new Vector3(1F, 2F, 3F)), Is.True);
            Assert.That(a.Equals(in a), Is.True);
            // One ulp above 3 is not equal (unlike ==).
            Vector3 b = new Vector3(1F, 2F, BitConverter.Int32BitsToSingle(Bits(3F) + 1));
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a.Equals(in b), Is.False);
            Assert.That(a == b, Is.True);
            // +0 and -0 are equal under C# ==.
            Assert.That(new Vector3(0F, 0F, 0F).Equals(new Vector3(-0F, -0F, -0F)), Is.True);
        }

        [Test]
        public void Vector3_Equals_NaNIsUnequalToItself()
        {
            // Spec §0 [verified for Vector2]: the Vector2/3/4 family uses C# == (NaN != NaN).
            Vector3 n = new Vector3(float.NaN, 0F, 0F);
            Assert.That(n.Equals(n), Is.False);
            Assert.That(n.Equals(in n), Is.False);
            Assert.That(n.Equals((object)n), Is.False);
            Assert.That(new Vector3(0F, float.NaN, 0F).Equals(new Vector3(0F, float.NaN, 0F)), Is.False);
            Assert.That(new Vector3(0F, 0F, float.NaN).Equals(new Vector3(0F, 0F, float.NaN)), Is.False);
        }

        [Test]
        public void Vector3_EqualsObject()
        {
            Vector3 a = new Vector3(1F, 2F, 3F);
            Assert.That(a.Equals((object)new Vector3(1F, 2F, 3F)), Is.True);
            Assert.That(a.Equals((object)new Vector3(1F, 2F, 4F)), Is.False);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals("(1.00, 2.00, 3.00)"), Is.False);
            Assert.That(a.Equals((object)new Vector2(1F, 2F)), Is.False);
            Assert.That(a.Equals((object)new Vector4(1F, 2F, 3F, 0F)), Is.False);
        }

        [Test]
        public void Vector3_OperatorEquals_UsesSquaredDistanceTolerance()
        {
            // Spec §1 [verified for Vector2]: zero == (1e-6,0) true; zero == (1e-5,0) false. Threshold kEps*kEps
            // = 9.9999994E-11 [0x2EDBE6FE].
            Assert.That(Vector3.zero == new Vector3(1e-6F, 0F, 0F), Is.True);
            Assert.That(Vector3.zero == new Vector3(0F, 0F, 1e-6F), Is.True);
            Assert.That(Vector3.zero == new Vector3(1e-5F, 0F, 0F), Is.False);
            Assert.That(Vector3.zero == new Vector3(0F, 0F, 1e-5F), Is.False);
            Assert.That(Vector3.zero != new Vector3(1e-6F, 0F, 0F), Is.False);
            Assert.That(Vector3.zero != new Vector3(1e-5F, 0F, 0F), Is.True);

            // Three terms add: (6e-6)^2 * 3 = 1.08e-10 > 1e-10 -> not equal, although each axis alone would be.
            Vector3 d = new Vector3(6e-6F, 6e-6F, 6e-6F);
            Assert.That(Vector3.zero == new Vector3(6e-6F, 0F, 0F), Is.True);
            Assert.That(Vector3.zero == d, Is.False);
            // (1e-6)^2 * 3 = 3e-12 < 1e-10 -> equal.
            Assert.That(Vector3.zero == new Vector3(1e-6F, 1e-6F, 1e-6F), Is.True);

            Assert.That(new Vector3(1F, 2F, 3F) == new Vector3(1F, 2F, 3F), Is.True);
            Assert.That(new Vector3(1F, 2F, 3F) != new Vector3(1F, 2F, 3F), Is.False);
        }

        [Test]
        public void Vector3_OperatorEquals_NaNAndInfinity()
        {
            Vector3 n = new Vector3(float.NaN, 0F, 0F);
            Assert.That(n == n, Is.False);
            Assert.That(n != n, Is.True);
            // inf - inf = NaN -> false.
            Assert.That(Vector3.positiveInfinity == Vector3.positiveInfinity, Is.False);
            Assert.That(Vector3.positiveInfinity != Vector3.positiveInfinity, Is.True);
        }

        // ------------------------------------------------------------------ ToString

        [Test]
        public void Vector3_ToString_Default()
        {
            // Spec §1 [verified]: new Vector3(1,2,3).ToString() = "(1.00, 2.00, 3.00)".
            Assert.That(new Vector3(1F, 2F, 3F).ToString(), Is.EqualTo("(1.00, 2.00, 3.00)"));
            // Spec §0 only fixes the template and the "F2" default; the digits come from float.ToString(format, provider),
            // so the assertion deliberately avoids an exact .5 midpoint (0.125f formats as "0.12" on .NET Core's
            // correctly-rounded formatter but "0.13" on Mono/.NET Framework, and Unity 6 runs Mono).
            Assert.That(new Vector3(-1.5F, 0.126F, 1234.5678F).ToString(), Is.EqualTo("(-1.50, 0.13, 1234.57)"));
            Assert.That(Vector3.zero.ToString(), Is.EqualTo("(0.00, 0.00, 0.00)"));
            Assert.That(new Vector3(-0.004F, 1e7F, -12.345F).ToString(), Is.EqualTo("(-0.00, 10000000.00, -12.35)"));
        }

        [Test]
        public void Vector3_ToString_FormatAndProvider()
        {
            Vector3 v = new Vector3(1.5F, 2F, 3F);
            Assert.That(v.ToString("F0"), Is.EqualTo("(2, 2, 3)"));
            Assert.That(v.ToString("F4"), Is.EqualTo("(1.5000, 2.0000, 3.0000)"));
            Assert.That(v.ToString("R"), Is.EqualTo("(1.5, 2, 3)"));
            // null / empty format -> default "F2".
            Assert.That(v.ToString(null), Is.EqualTo("(1.50, 2.00, 3.00)"));
            Assert.That(v.ToString(""), Is.EqualTo("(1.50, 2.00, 3.00)"));
            Assert.That(v.ToString(null, null), Is.EqualTo("(1.50, 2.00, 3.00)"));
            // Provider is honoured (de-DE uses a decimal comma); null provider -> invariant culture.
            CultureInfo de = new CultureInfo("de-DE");
            Assert.That(v.ToString("F2", de), Is.EqualTo("(1,50, 2,00, 3,00)"));
            Assert.That(v.ToString(null, de), Is.EqualTo("(1,50, 2,00, 3,00)"));
            Assert.That(v.ToString("F1", CultureInfo.InvariantCulture), Is.EqualTo("(1.5, 2.0, 3.0)"));
            // IFormattable dispatch.
            Assert.That(((IFormattable)v).ToString("F1", null), Is.EqualTo("(1.5, 2.0, 3.0)"));
            Assert.That(string.Format(CultureInfo.InvariantCulture, "{0:F1}", v), Is.EqualTo("(1.5, 2.0, 3.0)"));
        }

        [Test]
        public void Vector3_ToString_IgnoresCurrentCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(new Vector3(1.5F, 2F, 3F).ToString(), Is.EqualTo("(1.50, 2.00, 3.00)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ------------------------------------------------------------------ Lerp / LerpUnclamped

        [Test]
        public void Vector3_Lerp_Formula()
        {
            Vector3 a = new Vector3(1F, 2F, 3F);
            Vector3 b = new Vector3(3F, 6F, 9F);
            // a + (b - a) * t with t = 0.25: 1 + 2*0.25 = 1.5; 2 + 4*0.25 = 3; 3 + 6*0.25 = 4.5
            AssertBits(Vector3.Lerp(a, b, 0.25F), 1.5F, 3F, 4.5F);
            AssertBits(Vector3.Lerp(in a, in b, 0.25F), 1.5F, 3F, 4.5F);
            AssertBits(Vector3.Lerp(a, b, 0F), 1F, 2F, 3F);
            AssertBits(Vector3.Lerp(a, b, 1F), 3F, 6F, 9F);
        }

        [Test]
        public void Vector3_Lerp_ClampsT()
        {
            Vector3 a = new Vector3(1F, 2F, 3F);
            Vector3 b = new Vector3(3F, 6F, 9F);
            AssertBits(Vector3.Lerp(a, b, 2F), 3F, 6F, 9F);
            AssertBits(Vector3.Lerp(a, b, -1F), 1F, 2F, 3F);
            AssertBits(Vector3.Lerp(in a, in b, 2F), 3F, 6F, 9F);
            AssertBits(Vector3.Lerp(in a, in b, -1F), 1F, 2F, 3F);
            // NaN t passes through Clamp01 unchanged (spec §11) -> NaN result.
            Vector3 n = Vector3.Lerp(a, b, float.NaN);
            Assert.That(float.IsNaN(n.x) && float.IsNaN(n.y) && float.IsNaN(n.z), Is.True);
        }

        [Test]
        public void Vector3_Lerp_UsesAPlusDeltaForm()
        {
            // a + (b - a) * t, not a*(1-t) + b*t: with a = 1, b = 1e-8, (b - a) rounds to exactly -1f, so at t = 1 the
            // result is 1 + (-1) = 0 rather than b = 1e-8.
            Vector3 a = new Vector3(1F, 1F, 1F);
            Vector3 b = new Vector3(1e-8F, 1e-8F, 1e-8F);
            AssertBits(Vector3.Lerp(a, b, 1F), 0F, 0F, 0F);
            AssertBits(Vector3.LerpUnclamped(a, b, 1F), 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_LerpUnclamped_Formula()
        {
            Vector3 a = new Vector3(1F, 2F, 3F);
            Vector3 b = new Vector3(3F, 6F, 9F);
            // t = 2: 1 + 2*2 = 5; 2 + 4*2 = 10; 3 + 6*2 = 15
            AssertBits(Vector3.LerpUnclamped(a, b, 2F), 5F, 10F, 15F);
            AssertBits(Vector3.LerpUnclamped(in a, in b, 2F), 5F, 10F, 15F);
            // t = -0.5: 1 - 1 = 0; 2 - 2 = 0; 3 - 3 = 0
            AssertBits(Vector3.LerpUnclamped(a, b, -0.5F), 0F, 0F, 0F);
            AssertBits(Vector3.LerpUnclamped(in a, in b, -0.5F), 0F, 0F, 0F);
        }

        // ------------------------------------------------------------------ MoveTowards

        [Test]
        public void Vector3_MoveTowards_NegativeDeltaMovesAway()
        {
            // Spec §1 [verified for Vector2]: MoveTowards(0, (10,0), -3) = (-3, 0).
            // to = (10,0,0); sqDist = 100; maxDelta < 0 so no early return; dist = 10; 0 + 10/10 * -3 = -3.
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(10F, 0F, 0F), -3F), -3F, 0F, 0F);
            // `in` overload: Vector3.zero is a property, so it needs a local to be passed by reference.
            Vector3 origin = Vector3.zero;
            Vector3 target = new Vector3(10F, 0F, 0F);
            AssertBits(Vector3.MoveTowards(in origin, in target, -3F), -3F, 0F, 0F);
            // Even when the target is within |maxDelta| a negative delta does not snap: (1,0,0) with -3 -> (-3,0,0).
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(1F, 0F, 0F), -3F), -3F, 0F, 0F);
        }

        [Test]
        public void Vector3_MoveTowards_SnapsWhenWithinReach()
        {
            // sqDist = 3 <= 2*2 = 4 -> return target.
            AssertBits(Vector3.MoveTowards(Vector3.zero, Vector3.one, 2F), 1F, 1F, 1F);
            // Exact boundary: sqDist = 25 <= 25 -> target.
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(3F, 4F, 0F), 5F), 3F, 4F, 0F);
            // sqDist == 0 -> target regardless of delta sign.
            Vector3 p = new Vector3(1F, 2F, 3F);
            AssertBits(Vector3.MoveTowards(p, p, -3F), 1F, 2F, 3F);
            AssertBits(Vector3.MoveTowards(p, p, 0F), 1F, 2F, 3F);
        }

        [Test]
        public void Vector3_MoveTowards_StepsAlongDirection()
        {
            // to = (3,4,0); sqDist = 25 > 1; dist = 5; 0 + 3/5*1 = 0.6; 0 + 4/5*1 = 0.8
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(3F, 4F, 0F), 1F), 0.6F, 0.8F, 0F);
            // z axis: 0 + 10/10*4 = 4
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(0F, 0F, 10F), 4F), 0F, 0F, 4F);
            // Offset start: current (1,1,1), target (1,1,11): to = (0,0,10); 1 + 10/10*4 = 5
            AssertBits(Vector3.MoveTowards(new Vector3(1F, 1F, 1F), new Vector3(1F, 1F, 11F), 4F), 1F, 1F, 5F);
            // maxDelta = 0 with a non-zero distance: 0 + to/dist*0 = current.
            AssertBits(Vector3.MoveTowards(Vector3.zero, new Vector3(3F, 4F, 0F), 0F), 0F, 0F, 0F);
        }

        // ------------------------------------------------------------------ Slerp / SlerpUnclamped (native)

        [Test]
        public void Vector3_Slerp_VerifiedMidpoint()
        {
            // Spec §2 [verified]: Slerp(right*2, up*4, 0.5) = (2.1213203, 2.1213203, 0) (magnitude 3).
            Vector3 r = Vector3.Slerp(Vector3.right * 2F, Vector3.up * 4F, 0.5F);
            AssertNativeTrig(r, 2.1213203F, 2.1213203F, 0F);
            Assert.That(r.magnitude, Is.EqualTo(3F).Within(3).Ulps);
            Vector3 a = Vector3.right * 2F, b = Vector3.up * 4F;
            AssertNativeTrig(Vector3.Slerp(in a, in b, 0.5F), 2.1213203F, 2.1213203F, 0F);
        }

        [Test]
        public void Vector3_Slerp_MagnitudeIsLinear()
        {
            // |a| = 2, |b| = 4: at t = 0.25 the magnitude is 2 + (4 - 2) * 0.25 = 2.5.
            Vector3 r = Vector3.Slerp(Vector3.right * 2F, Vector3.up * 4F, 0.25F);
            Assert.That(r.magnitude, Is.EqualTo(2.5F).Within(3).Ulps);
            // Direction at t = 0.25 is 22.5 degrees from +x: (cos 22.5, sin 22.5, 0) * 2.5
            Assert.That(r.x, Is.EqualTo(2.5F * 0.9238795F).Within(3).Ulps);
            Assert.That(r.y, Is.EqualTo(2.5F * 0.38268343F).Within(3).Ulps);
            Assert.That(r.z, Is.EqualTo(0F));
        }

        [Test]
        public void Vector3_Slerp_ZeroInputFallsBackToLerp()
        {
            // Spec §2 [verified]: Slerp(zero, up, 0.5) = (0, 0.5, 0).
            AssertBits(Vector3.Slerp(Vector3.zero, Vector3.up, 0.5F), 0F, 0.5F, 0F);
            AssertBits(Vector3.Slerp(Vector3.up, Vector3.zero, 0.25F), 0F, 0.75F, 0F);
        }

        [Test]
        public void Vector3_Slerp_ParallelInputFallsBackToLerp()
        {
            // Spec §2 [verified]: Slerp(right, right*3, 0.5) = (2, 0, 0).
            AssertBits(Vector3.Slerp(Vector3.right, Vector3.right * 3F, 0.5F), 2F, 0F, 0F);
        }

        [Test]
        public void Vector3_Slerp_AntiparallelRotatesAboutPerpendicularAxis()
        {
            // Spec §2 [verified]: Slerp(right, left, 0.5) = (~0, 0, -1); Slerp(up, down, 0.5) = (0, ~0, -1).
            Vector3 a = Vector3.Slerp(Vector3.right, Vector3.left, 0.5F);
            Assert.That(a.x, Is.EqualTo(0F).Within(1e-6F));
            Assert.That(a.y, Is.EqualTo(0F).Within(1e-6F));
            Assert.That(a.z, Is.EqualTo(-1F).Within(3).Ulps);

            Vector3 b = Vector3.Slerp(Vector3.up, Vector3.down, 0.5F);
            Assert.That(b.x, Is.EqualTo(0F).Within(1e-6F));
            Assert.That(b.y, Is.EqualTo(0F).Within(1e-6F));
            Assert.That(b.z, Is.EqualTo(-1F).Within(3).Ulps);
        }

        [Test]
        public void Vector3_Slerp_ClampsT()
        {
            AssertNativeTrig(Vector3.Slerp(Vector3.right, Vector3.up, 1.5F), 0F, 1F, 0F);
            AssertNativeTrig(Vector3.Slerp(Vector3.right, Vector3.up, -0.5F), 1F, 0F, 0F);
            AssertNativeTrig(Vector3.Slerp(Vector3.right, Vector3.up, 0F), 1F, 0F, 0F);
            AssertNativeTrig(Vector3.Slerp(Vector3.right, Vector3.up, 1F), 0F, 1F, 0F);
        }

        [Test]
        public void Vector3_SlerpUnclamped_Extrapolates()
        {
            // Spec §2 [verified]: SlerpUnclamped(right, up, 1.5) = (-0.70710677, 0.70710677, 0);
            // (..., -0.5) = (0.70710677, -0.70710677, 0).
            AssertNativeTrig(Vector3.SlerpUnclamped(Vector3.right, Vector3.up, 1.5F), -0.70710677F, 0.70710677F, 0F);
            AssertNativeTrig(Vector3.SlerpUnclamped(Vector3.right, Vector3.up, -0.5F), 0.70710677F, -0.70710677F, 0F);
            Vector3 r = Vector3.right, u = Vector3.up;
            AssertNativeTrig(Vector3.SlerpUnclamped(in r, in u, 1.5F), -0.70710677F, 0.70710677F, 0F);
            // Within [0,1] SlerpUnclamped == Slerp.
            Vector3 s = Vector3.Slerp(Vector3.right * 2F, Vector3.up * 4F, 0.5F);
            Vector3 su = Vector3.SlerpUnclamped(Vector3.right * 2F, Vector3.up * 4F, 0.5F);
            AssertBits(su, s.x, s.y, s.z);
        }

        // ------------------------------------------------------------------ RotateTowards (native)

        [Test]
        public void Vector3_RotateTowards_VerifiedValues()
        {
            // Spec §2 [verified]: RotateTowards(right, up, 0.5, 0) = (0.87758255, 0.47942555, 0) = (cos 0.5, sin 0.5, 0).
            // BIT-EXACT: the in-plane rotation is evaluated as dirC*cos(step) + perp*sin(step). The algebraically equal
            // sine-weight form sin(angle - step)/sin(angle) lands on 0x3F60A941, one ulp above the verified 0x3F60A940.
            AssertBits(Vector3.RotateTowards(Vector3.right, Vector3.up, 0.5F, 0F), 0.87758255F, 0.47942555F, 0F);
            // (right, up*3, 0.5, 1) = (1.7551651, 0.9588511, 0) (magnitude 2 = 1 moved by 1 toward 3).
            AssertBits(Vector3.RotateTowards(Vector3.right, Vector3.up * 3F, 0.5F, 1F), 1.7551651F, 0.9588511F, 0F);
            // (right, left, 0.5, 0) = (0.87758255, 0, -0.47942555) (antiparallel fallback rotates toward -z).
            AssertBits(Vector3.RotateTowards(Vector3.right, Vector3.left, 0.5F, 0F), 0.87758255F, 0F, -0.47942555F);
            Vector3 r = Vector3.right, u = Vector3.up;
            AssertBits(Vector3.RotateTowards(in r, in u, 0.5F, 0F), 0.87758255F, 0.47942555F, 0F);
            // A partial rotation out of the coordinate planes still keeps the input magnitude and the requested angle.
            Vector3 from = new Vector3(1F, 2F, 3F), to = new Vector3(3F, -1F, 2F);
            Vector3 partial = Vector3.RotateTowards(from, to, 0.2F, 0F);
            Assert.That(partial.magnitude, Is.EqualTo(from.magnitude).Within(4).Ulps);
            Assert.That(Vector3.Angle(from, partial) * Mathf.Deg2Rad, Is.EqualTo(0.2F).Within(1e-5F));
        }

        [Test]
        public void Vector3_RotateTowards_ReachesTargetWhenDeltaExceedsAngle()
        {
            // Angle right->up is pi/2 < 2 radians, magnitudes equal -> target direction, magnitude 1.
            AssertNativeTrig(Vector3.RotateTowards(Vector3.right, Vector3.up, 2F, 0F), 0F, 1F, 0F);
            // Magnitude delta 10 covers 1 -> 3, so the result is exactly the target.
            AssertNativeTrig(Vector3.RotateTowards(Vector3.right, Vector3.up * 3F, 2F, 10F), 0F, 3F, 0F);
            // Zero deltas: unchanged.
            AssertNativeTrig(Vector3.RotateTowards(Vector3.right, Vector3.up * 3F, 0F, 0F), 1F, 0F, 0F);
            // Same direction, only magnitude moves: (right, right*3, 1, 0.5) -> (1.5, 0, 0).
            AssertNativeTrig(Vector3.RotateTowards(Vector3.right, Vector3.right * 3F, 1F, 0.5F), 1.5F, 0F, 0F);
        }

        // ------------------------------------------------------------------ OrthoNormalize (native)

        [Test]
        public void Vector3_OrthoNormalize_TwoVectors()
        {
            // Spec §2 [verified]: ((2,0,0),(1,1,0)) -> (1,0,0),(0,1,0).
            Vector3 n = new Vector3(2F, 0F, 0F);
            Vector3 t = new Vector3(1F, 1F, 0F);
            Vector3.OrthoNormalize(ref n, ref t);
            AssertBits(n, 1F, 0F, 0F);
            AssertBits(t, 0F, 1F, 0F);
        }

        [Test]
        public void Vector3_OrthoNormalize_ThreeVectors()
        {
            // Spec §2 [verified]: three-vector form with (1,1,1) -> (0,0,1).
            Vector3 n = new Vector3(2F, 0F, 0F);
            Vector3 t = new Vector3(1F, 1F, 0F);
            Vector3 b = new Vector3(1F, 1F, 1F);
            Vector3.OrthoNormalize(ref n, ref t, ref b);
            AssertBits(n, 1F, 0F, 0F);
            AssertBits(t, 0F, 1F, 0F);
            AssertBits(b, 0F, 0F, 1F);
        }

        // ------------------------------------------------------------------ algebra

        [Test]
        public void Vector3_ScaleStatic()
        {
            Vector3 a = new Vector3(1F, 2F, 3F), b = new Vector3(4F, 5F, 6F);
            AssertBits(Vector3.Scale(a, b), 4F, 10F, 18F);
            AssertBits(Vector3.Scale(in a, in b), 4F, 10F, 18F);
            AssertBits(Vector3.Scale(a, new Vector3(-1F, 0F, 0.5F)), -1F, 0F, 1.5F);
        }

        [Test]
        public void Vector3_Cross()
        {
            AssertBits(Vector3.Cross(Vector3.right, Vector3.up), 0F, 0F, 1F);
            AssertBits(Vector3.Cross(Vector3.up, Vector3.right), 0F, 0F, -1F);
            AssertBits(Vector3.Cross(Vector3.up, Vector3.forward), 1F, 0F, 0F);
            AssertBits(Vector3.Cross(Vector3.forward, Vector3.right), 0F, 1F, 0F);
            // (1,2,3) x (4,5,6) = (2*6 - 3*5, 3*4 - 1*6, 1*5 - 2*4) = (12-15, 12-6, 5-8) = (-3, 6, -3)
            Vector3 a = new Vector3(1F, 2F, 3F), b = new Vector3(4F, 5F, 6F);
            AssertBits(Vector3.Cross(a, b), -3F, 6F, -3F);
            AssertBits(Vector3.Cross(in a, in b), -3F, 6F, -3F);
            AssertBits(Vector3.Cross(a, a), 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_Reflect()
        {
            // factor = -2 * Dot(n, d) = -2 * (-1) = 2; result = (2*0 + 1, 2*1 - 1, 2*0 + 0) = (1, 1, 0)
            Vector3 d = new Vector3(1F, -1F, 0F);
            AssertBits(Vector3.Reflect(d, Vector3.up), 1F, 1F, 0F);
            Vector3 up = Vector3.up;
            AssertBits(Vector3.Reflect(in d, in up), 1F, 1F, 0F);
            // Normal is not normalised: n = (0,2,0): factor = -2 * (-2) = 4; result = (1, 4*2 - 1, 0) = (1, 7, 0)
            AssertBits(Vector3.Reflect(d, new Vector3(0F, 2F, 0F)), 1F, 7F, 0F);
            // 3D: d = (1,2,3), n = (0,0,1): factor = -6; result = (1, 2, -6 + 3) = (1, 2, -3)
            AssertBits(Vector3.Reflect(new Vector3(1F, 2F, 3F), Vector3.forward), 1F, 2F, -3F);
        }

        [Test]
        public void Vector3_NormalizeStatic()
        {
            Vector3 v = new Vector3(0F, 3F, 4F);
            AssertBits(Vector3.Normalize(v), 0F, 0.6F, 0.8F);
            AssertBits(Vector3.Normalize(in v), 0F, 0.6F, 0.8F);
            AssertBits(Vector3.Normalize(new Vector3(0.25F, 0F, 0F)), 1F, 0F, 0F);
            // mag = 1e-6 <= kEpsilon -> zero; mag == kEpsilon exactly -> zero (strict >).
            AssertBits(Vector3.Normalize(new Vector3(1e-6F, 0F, 0F)), 0F, 0F, 0F);
            AssertBits(Vector3.Normalize(new Vector3(0F, 0F, Vector3.kEpsilon)), 0F, 0F, 0F);
            Vector3 tiny = new Vector3(0F, 0F, Vector3.kEpsilon);
            AssertBits(Vector3.Normalize(in tiny), 0F, 0F, 0F);
            AssertBits(Vector3.Normalize(Vector3.zero), 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_Dot()
        {
            Vector3 a = new Vector3(1F, 2F, 3F), b = new Vector3(4F, 5F, 6F);
            // 4 + 10 + 18 = 32
            Assert.That(Bits(Vector3.Dot(a, b)), Is.EqualTo(Bits(32F)));
            Assert.That(Bits(Vector3.Dot(in a, in b)), Is.EqualTo(Bits(32F)));
            Assert.That(Vector3.Dot(Vector3.right, Vector3.up), Is.EqualTo(0F));
            Assert.That(Vector3.Dot(Vector3.forward, Vector3.back), Is.EqualTo(-1F));
        }

        [Test]
        public void Vector3_Project()
        {
            // sqrMag = 4; k = Dot((1,2,3),(0,0,2)) / 4 = 6 / 4 = 1.5; result = (0, 0, 2 * 1.5) = (0, 0, 3)
            Vector3 v = new Vector3(1F, 2F, 3F);
            Vector3 n = new Vector3(0F, 0F, 2F);
            AssertBits(Vector3.Project(v, n), 0F, 0F, 3F);
            AssertBits(Vector3.Project(in v, in n), 0F, 0F, 3F);
            // Non-axis normal: n = (1,1,0): sqrMag = 2; k = 3/2 = 1.5; result = (1.5, 1.5, 0)
            AssertBits(Vector3.Project(v, new Vector3(1F, 1F, 0F)), 1.5F, 1.5F, 0F);
        }

        [Test]
        public void Vector3_Project_ZeroNormalReturnsZero()
        {
            // Guard: sqrMag < Mathf.Epsilon (float.Epsilon on desktop) -> zero.
            AssertBits(Vector3.Project(new Vector3(1F, 2F, 3F), Vector3.zero), 0F, 0F, 0F);
            // (1e-23)^2 underflows to exactly 0 -> zero.
            AssertBits(Vector3.Project(new Vector3(1F, 2F, 3F), new Vector3(1e-23F, 0F, 0F)), 0F, 0F, 0F);
            // (1e-22)^2 = 1e-44 is a denormal above float.Epsilon, so the guard does not fire: the projection of
            // (1,2,3) onto +x is roughly (1,0,0) (denormal precision is coarse, so only a loose check).
            Vector3 p = Vector3.Project(new Vector3(1F, 2F, 3F), new Vector3(1e-22F, 0F, 0F));
            Assert.That(p.x, Is.Not.EqualTo(0F));
            Assert.That(p.x, Is.EqualTo(1F).Within(0.2F));
            Assert.That(p.y, Is.EqualTo(0F));
            Assert.That(p.z, Is.EqualTo(0F));
        }

        [Test]
        public void Vector3_ProjectOnPlane()
        {
            // k = 6/4 = 1.5; result = (1 - 0, 2 - 0, 3 - 2 * 1.5) = (1, 2, 0)
            Vector3 v = new Vector3(1F, 2F, 3F);
            Vector3 n = new Vector3(0F, 0F, 2F);
            AssertBits(Vector3.ProjectOnPlane(v, n), 1F, 2F, 0F);
            AssertBits(Vector3.ProjectOnPlane(in v, in n), 1F, 2F, 0F);
            // Degenerate normal returns the input unchanged.
            AssertBits(Vector3.ProjectOnPlane(v, Vector3.zero), 1F, 2F, 3F);
            AssertBits(Vector3.ProjectOnPlane(v, new Vector3(0F, 1e-23F, 0F)), 1F, 2F, 3F);
        }

        [Test]
        public void Vector3_Angle_RightAngles()
        {
            // f32(acos(0)) * Rad2Deg = 90 exactly [0x42B40000]; acos(-1) * Rad2Deg = 180 [0x43340000].
            Assert.That(Bits(Vector3.Angle(Vector3.right, Vector3.up)), Is.EqualTo(Bits(90F)));
            Assert.That(Bits(Vector3.Angle(Vector3.right, Vector3.left)), Is.EqualTo(Bits(180F)));
            Assert.That(Bits(Vector3.Angle(Vector3.right, Vector3.right)), Is.EqualTo(Bits(0F)));
            Assert.That(Bits(Vector3.Angle(Vector3.right * 5F, Vector3.forward * 0.25F)), Is.EqualTo(Bits(90F)));
            Vector3 r = Vector3.right, u = Vector3.up;
            Assert.That(Bits(Vector3.Angle(in r, in u)), Is.EqualTo(Bits(90F)));
        }

        [Test]
        public void Vector3_Angle_45Degrees()
        {
            // denominator = f32(sqrt(1 * 2)) = 1.4142135 [0x3FB504F3]; dot = 1 / 1.4142135 = 0.70710677 [0x3F3504F3];
            // f32(acos(0.70710677)) * Rad2Deg = 45 exactly [0x42340000].
            Assert.That(Bits(Vector3.Angle(Vector3.right, new Vector3(1F, 1F, 0F))), Is.EqualTo(Bits(45F)));
        }

        [Test]
        public void Vector3_Angle_ZeroVectorReturnsZero()
        {
            Assert.That(Vector3.Angle(Vector3.zero, Vector3.up), Is.EqualTo(0F));
            Assert.That(Vector3.Angle(Vector3.up, Vector3.zero), Is.EqualTo(0F));
            Assert.That(Vector3.Angle(Vector3.zero, Vector3.zero), Is.EqualTo(0F));
        }

        [Test]
        public void Vector3_Angle_UnderflowThreshold()
        {
            // Spec §2/§15.16: Vector3 tests f32(sqrt(sqrMag(a) * sqrMag(b))) < 1e-15.
            // (1e-8)^2 = 1e-16 each; product 1e-32; sqrt = 1e-16 < 1e-15 -> 0 even though the vectors are perpendicular.
            Assert.That(Vector3.Angle(new Vector3(1e-8F, 0F, 0F), new Vector3(0F, 1e-8F, 0F)), Is.EqualTo(0F));
            // (1e-7)^2 = 1e-14 each; product 1e-28; sqrt = 1e-14 >= 1e-15 -> normal path -> 90.
            Assert.That(Bits(Vector3.Angle(new Vector3(1e-7F, 0F, 0F), new Vector3(0F, 1e-7F, 0F))), Is.EqualTo(Bits(90F)));
        }

        [Test]
        public void Vector3_SignedAngle()
        {
            // Cross(right, up) = forward; Dot(forward, forward) = 1 -> Sign = +1 -> +90.
            Assert.That(Bits(Vector3.SignedAngle(Vector3.right, Vector3.up, Vector3.forward)), Is.EqualTo(Bits(90F)));
            // Cross(up, right) = back; Dot(forward, back) = -1 -> -90.
            Assert.That(Bits(Vector3.SignedAngle(Vector3.up, Vector3.right, Vector3.forward)), Is.EqualTo(Bits(-90F)));
            // Flipping the axis flips the sign.
            Assert.That(Bits(Vector3.SignedAngle(Vector3.right, Vector3.up, Vector3.back)), Is.EqualTo(Bits(-90F)));
            Vector3 r = Vector3.right, u = Vector3.up, f = Vector3.forward;
            Assert.That(Bits(Vector3.SignedAngle(in r, in u, in f)), Is.EqualTo(Bits(90F)));
        }

        [Test]
        public void Vector3_SignedAngle_CollinearUsesSignOfZeroAsPositive()
        {
            // Cross(right, left) = 0 -> Dot = 0 -> Mathf.Sign(0) = 1 -> +180 (not -180).
            Assert.That(Bits(Vector3.SignedAngle(Vector3.right, Vector3.left, Vector3.up)), Is.EqualTo(Bits(180F)));
            // Axis perpendicular to the rotation plane's normal: Dot(up, forward) = 0 -> Sign(0) = 1 -> +90.
            Assert.That(Bits(Vector3.SignedAngle(Vector3.right, Vector3.up, Vector3.up)), Is.EqualTo(Bits(90F)));
        }

        [Test]
        public void Vector3_Distance()
        {
            // dx = -3, dy = -4, dz = 0: sqrt(9 + 16 + 0) = 5
            Vector3 a = new Vector3(1F, 2F, 3F), b = new Vector3(4F, 6F, 3F);
            Assert.That(Bits(Vector3.Distance(a, b)), Is.EqualTo(Bits(5F)));
            Assert.That(Bits(Vector3.Distance(b, a)), Is.EqualTo(Bits(5F)));
            Assert.That(Bits(Vector3.Distance(in a, in b)), Is.EqualTo(Bits(5F)));
            // (0,0,0) -> (1,2,3): f32(sqrt(14)) = 3.7416575 [0x406F7751]
            Assert.That(Bits(Vector3.Distance(Vector3.zero, new Vector3(1F, 2F, 3F))), Is.EqualTo(0x406F7751));
            Assert.That(Vector3.Distance(a, a), Is.EqualTo(0F));
        }

        [Test]
        public void Vector3_ClampMagnitude()
        {
            // sqr = 25 > 1: mag = 5; nx = 0.6, ny = 0.8; * 1 -> (0.6, 0.8, 0)
            Vector3 v = new Vector3(3F, 4F, 0F);
            AssertBits(Vector3.ClampMagnitude(v, 1F), 0.6F, 0.8F, 0F);
            AssertBits(Vector3.ClampMagnitude(in v, 1F), 0.6F, 0.8F, 0F);
            // Spec §1 [verified for Vector2]: a negative maxLength flips: ClampMagnitude((3,4), -1) = (-0.6, -0.8).
            // The z term follows the same expression: nz = 0/5 = +0, and +0 * -1 = -0, so z carries the sign bit.
            AssertBits(Vector3.ClampMagnitude(v, -1F), -0.6F, -0.8F, -0F);
            Assert.That(Bits(Vector3.ClampMagnitude(v, -1F).z), Is.EqualTo(unchecked((int)0x80000000)));
            // 3D: (0, 3, 4) with maxLength 2.5 -> (0, 0.6*2.5, 0.8*2.5) = (0, 1.5, 2)
            AssertBits(Vector3.ClampMagnitude(new Vector3(0F, 3F, 4F), 2.5F), 0F, 1.5F, 2F);
        }

        [Test]
        public void Vector3_ClampMagnitude_ReturnsInputWhenNotLonger()
        {
            Vector3 v = new Vector3(3F, 4F, 0F);
            AssertBits(Vector3.ClampMagnitude(v, 10F), 3F, 4F, 0F);
            // Exact boundary: sqr = 25 > 25 is false -> unchanged.
            AssertBits(Vector3.ClampMagnitude(v, 5F), 3F, 4F, 0F);
            // -5: 25 > 25 false -> unchanged (no flip).
            AssertBits(Vector3.ClampMagnitude(v, -5F), 3F, 4F, 0F);
            AssertBits(Vector3.ClampMagnitude(Vector3.zero, 0F), 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_MinMax_Componentwise()
        {
            Vector3 a = new Vector3(1F, 5F, -3F), b = new Vector3(4F, 2F, -3F);
            AssertBits(Vector3.Min(a, b), 1F, 2F, -3F);
            AssertBits(Vector3.Max(a, b), 4F, 5F, -3F);
            AssertBits(Vector3.Min(in a, in b), 1F, 2F, -3F);
            AssertBits(Vector3.Max(in a, in b), 4F, 5F, -3F);
        }

        [Test]
        public void Vector3_MinMax_NaNOrdering()
        {
            // Spec §11 [verified]: Min(NaN,1) = 1, Min(1,NaN) = NaN, Max(NaN,1) = 1, Max(1,NaN) = NaN.
            Vector3 a = new Vector3(float.NaN, 1F, float.NaN);
            Vector3 b = new Vector3(1F, float.NaN, 2F);
            Vector3 mn = Vector3.Min(a, b);
            Assert.That(mn.x, Is.EqualTo(1F));
            Assert.That(float.IsNaN(mn.y), Is.True);
            Assert.That(mn.z, Is.EqualTo(2F));
            Vector3 mx = Vector3.Max(a, b);
            Assert.That(mx.x, Is.EqualTo(1F));
            Assert.That(float.IsNaN(mx.y), Is.True);
            Assert.That(mx.z, Is.EqualTo(2F));
        }

        // ------------------------------------------------------------------ SmoothDamp

        [Test]
        public void Vector3_SmoothDamp_VerifiedValues()
        {
            // Spec §1 [verified for Vector2]: SmoothDamp(0, (10,0), ref 0, 0.3, inf, 0.016) = (0.05165842, 0),
            // velocity (6.392509, 0). The three-component algorithm is identical for a single-axis change.
            Vector3 vel = Vector3.zero;
            Vector3 o = Vector3.SmoothDamp(Vector3.zero, new Vector3(10F, 0F, 0F), ref vel, 0.3F, float.PositiveInfinity, 0.016F);
            AssertBits(o, 0.05165842F, 0F, 0F);
            AssertBits(vel, 6.392509F, 0F, 0F);

            // Same numbers on the z axis.
            vel = Vector3.zero;
            o = Vector3.SmoothDamp(Vector3.zero, new Vector3(0F, 0F, 10F), ref vel, 0.3F, float.PositiveInfinity, 0.016F);
            AssertBits(o, 0F, 0F, 0.05165842F);
            AssertBits(vel, 0F, 0F, 6.392509F);
        }

        [Test]
        public void Vector3_SmoothDamp_MaxSpeedClampsChange()
        {
            // Spec §11 [verified scalar]: maxSpeed = 5 -> 0.007748774, vel 0.95887625. For a single-axis change the
            // vector clamp (change / mag * maxChange = -10 / 10 * 1.5 = -1.5) equals the scalar Clamp(-10, -1.5, 1.5).
            Vector3 vel = Vector3.zero;
            Vector3 o = Vector3.SmoothDamp(Vector3.zero, new Vector3(10F, 0F, 0F), ref vel, 0.3F, 5F, 0.016F);
            AssertBits(o, 0.007748774F, 0F, 0F);
            AssertBits(vel, 0.95887625F, 0F, 0F);
        }

        [Test]
        public void Vector3_SmoothDamp_Overshoot()
        {
            // Spec §1 [verified for Vector2]: current 0, target (1,0), vel (100,0), 0.3, inf, 0.5 -> (1,0), vel (0,0).
            Vector3 vel = new Vector3(100F, 0F, 0F);
            Vector3 o = Vector3.SmoothDamp(Vector3.zero, new Vector3(1F, 0F, 0F), ref vel, 0.3F, float.PositiveInfinity, 0.5F);
            AssertBits(o, 1F, 0F, 0F);
            AssertBits(vel, 0F, 0F, 0F);

            // Diagonal overshoot: the 3-component dot product drives the guard.
            vel = new Vector3(100F, 100F, 100F);
            o = Vector3.SmoothDamp(Vector3.zero, Vector3.one, ref vel, 0.3F, float.PositiveInfinity, 0.5F);
            AssertBits(o, 1F, 1F, 1F);
            AssertBits(vel, 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_SmoothDamp_AlreadyAtTargetWithZeroVelocity()
        {
            // change = 0, temp = 0 -> output = target, velocity stays 0; dot = 0 so the overshoot branch is not taken.
            Vector3 vel = Vector3.zero;
            Vector3 o = Vector3.SmoothDamp(Vector3.one, Vector3.one, ref vel, 0.3F, float.PositiveInfinity, 0.016F);
            AssertBits(o, 1F, 1F, 1F);
            AssertBits(vel, 0F, 0F, 0F);
        }

        [Test]
        public void Vector3_SmoothDamp_SmoothTimeFloor()
        {
            // smoothTime = Max(0.0001, smoothTime): 0 and 0.0001 behave identically.
            Vector3 v1 = Vector3.zero, v2 = Vector3.zero;
            Vector3 o1 = Vector3.SmoothDamp(Vector3.zero, new Vector3(10F, 0F, 0F), ref v1, 0F, float.PositiveInfinity, 0.016F);
            Vector3 o2 = Vector3.SmoothDamp(Vector3.zero, new Vector3(10F, 0F, 0F), ref v2, 0.0001F, float.PositiveInfinity, 0.016F);
            AssertBits(o1, o2.x, o2.y, o2.z);
            AssertBits(v1, v2.x, v2.y, v2.z);
        }

        [Test]
        public void Vector3_SmoothDamp_OverloadsUseEngineClockAndInfinity()
        {
            float saved = EngineClock.deltaTime;
            try
            {
                EngineClock.deltaTime = 0.016F;
                Vector3 target = new Vector3(10F, 0F, 0F);
                Vector3 vA = Vector3.zero, vB = Vector3.zero, vC = Vector3.zero;
                Vector3 a = Vector3.SmoothDamp(Vector3.zero, target, ref vA, 0.3F, float.PositiveInfinity, 0.016F);
                Vector3 b = Vector3.SmoothDamp(Vector3.zero, target, ref vB, 0.3F, float.PositiveInfinity);
                Vector3 c = Vector3.SmoothDamp(Vector3.zero, target, ref vC, 0.3F);
                AssertBits(b, a.x, a.y, a.z);
                AssertBits(vB, vA.x, vA.y, vA.z);
                AssertBits(c, a.x, a.y, a.z);
                AssertBits(vC, vA.x, vA.y, vA.z);

                // maxSpeed matters, so the 5-arg overload must honour it and the 4-arg one must be using Infinity.
                Vector3 vD = Vector3.zero;
                Vector3 d = Vector3.SmoothDamp(Vector3.zero, target, ref vD, 0.3F, 5F);
                AssertBits(d, 0.007748774F, 0F, 0F);

                EngineClock.deltaTime = 0.5F;
                Vector3 vE = new Vector3(100F, 0F, 0F);
                Vector3 e = Vector3.SmoothDamp(Vector3.zero, new Vector3(1F, 0F, 0F), ref vE, 0.3F);
                AssertBits(e, 1F, 0F, 0F);
                AssertBits(vE, 0F, 0F, 0F);
            }
            finally
            {
                EngineClock.deltaTime = saved;
            }
        }

        // ------------------------------------------------------------------ operators

        [Test]
        public void Vector3_ArithmeticOperators()
        {
            Vector3 a = new Vector3(1F, 2F, 3F), b = new Vector3(4F, 5F, 6F);
            AssertBits(a + b, 5F, 7F, 9F);
            AssertBits(a - b, -3F, -3F, -3F);
            AssertBits(-a, -1F, -2F, -3F);
            AssertBits(a * 2F, 2F, 4F, 6F);
            AssertBits(2F * a, 2F, 4F, 6F);
            AssertBits(a / 2F, 0.5F, 1F, 1.5F);
            AssertBits(a * 0.5F, 0.5F, 1F, 1.5F);
            // Unary minus of zero produces negative zero bits.
            AssertBits(-Vector3.zero, -0F, -0F, -0F);
            Assert.That(Bits((-Vector3.zero).x), Is.EqualTo(unchecked((int)0x80000000)));
            // Division by zero: +inf / NaN, no guard.
            Vector3 d = a / 0F;
            Assert.That(d.x, Is.EqualTo(float.PositiveInfinity));
            Vector3 z = Vector3.zero / 0F;
            Assert.That(float.IsNaN(z.x), Is.True);
        }

        [Test]
        public void Vector3_ScalarMultiplyIsSameEitherSide()
        {
            Vector3 a = new Vector3(1.1F, -2.2F, 3.3F);
            Vector3 l = a * 0.7F, r = 0.7F * a;
            AssertBits(l, r.x, r.y, r.z);
        }

        // ------------------------------------------------------------------ conversions (declared on Vector2/Vector4/Vector3Int)

        [Test]
        public void Vector3_Conversions()
        {
            Vector3 v3 = new Vector3(1F, 2F, 3F);
            Vector2 v2 = v3;
            Assert.That(Bits(v2.x), Is.EqualTo(Bits(1F)));
            Assert.That(Bits(v2.y), Is.EqualTo(Bits(2F)));

            Vector3 back = new Vector2(4F, 5F);
            AssertBits(back, 4F, 5F, 0F);

            Vector4 v4 = v3;
            Assert.That(Bits(v4.x), Is.EqualTo(Bits(1F)));
            Assert.That(Bits(v4.y), Is.EqualTo(Bits(2F)));
            Assert.That(Bits(v4.z), Is.EqualTo(Bits(3F)));
            Assert.That(Bits(v4.w), Is.EqualTo(Bits(0F)));

            Vector3 from4 = new Vector4(6F, 7F, 8F, 9F);
            AssertBits(from4, 6F, 7F, 8F);

            Vector3 fromInt = new Vector3Int(-1, 2, 3);
            AssertBits(fromInt, -1F, 2F, 3F);
        }

        // ------------------------------------------------------------------ obsolete members

        [Test]
        public void Vector3_ObsoleteMembers()
        {
#pragma warning disable CS0618
            AssertBits(Vector3.fwd, 0F, 0F, 1F);
            // AngleBetween is in radians: f32(acos(clamp(dot(right, up), -1, 1))) = f32(acos(0)) = 1.5707964 [0x3FC90FDB]
            Assert.That(Bits(Vector3.AngleBetween(Vector3.right, Vector3.up)), Is.EqualTo(0x3FC90FDB));
            Assert.That(Vector3.AngleBetween(Vector3.right * 3F, Vector3.right), Is.EqualTo(0F));
            // Exclude(excludeThis, fromThat) = ProjectOnPlane(fromThat, excludeThis): remove z from (1,2,3) -> (1,2,0)
            AssertBits(Vector3.Exclude(Vector3.forward, new Vector3(1F, 2F, 3F)), 1F, 2F, 0F);
#pragma warning restore CS0618
            Assert.That(typeof(Vector3).GetProperty("fwd").GetCustomAttribute<ObsoleteAttribute>(), Is.Not.Null);
            Assert.That(typeof(Vector3).GetMethod("AngleBetween").GetCustomAttribute<ObsoleteAttribute>(), Is.Not.Null);
            Assert.That(typeof(Vector3).GetMethod("Exclude").GetCustomAttribute<ObsoleteAttribute>(), Is.Not.Null);
        }

        // ================================================================== Vector3Int (spec §12)

        [Test]
        public void Vector3Int_TypeShape()
        {
            Type t = typeof(Vector3Int);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Vector3Int>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            // Private int fields behind x/y/z properties; nothing public.
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(3));
            foreach (FieldInfo f in fields)
                Assert.That(f.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(Marshal.SizeOf<Vector3Int>(), Is.EqualTo(12));
            Assert.That(t.GetProperty("x").PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(t.GetProperty("y").PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(t.GetProperty("z").PropertyType, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void Vector3Int_Constructors_And_Properties()
        {
            AssertExact(new Vector3Int(1, 2, 3), 1, 2, 3);
            AssertExact(new Vector3Int(1, 2), 1, 2, 0);
            AssertExact(default(Vector3Int), 0, 0, 0);
            Vector3Int v = new Vector3Int(1, 2, 3);
            v.x = -10;
            v.y = 20;
            v.z = int.MinValue;
            AssertExact(v, -10, 20, int.MinValue);
        }

        [Test]
        public void Vector3Int_Indexer_GetAndSet()
        {
            Vector3Int v = new Vector3Int(1, 2, 3);
            Assert.That(v[0], Is.EqualTo(1));
            Assert.That(v[1], Is.EqualTo(2));
            Assert.That(v[2], Is.EqualTo(3));
            v[0] = 7;
            v[1] = 8;
            v[2] = 9;
            AssertExact(v, 7, 8, 9);
        }

        [TestCase(3, "Invalid Vector3Int index addressed: 3!")]
        [TestCase(-1, "Invalid Vector3Int index addressed: -1!")]
        [TestCase(42, "Invalid Vector3Int index addressed: 42!")]
        public void Vector3Int_Indexer_InvalidIndexThrows(int index, string message)
        {
            Vector3Int v = new Vector3Int(1, 2, 3);
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { int _ = v[index]; });
            Assert.That(getEx.Message, Is.EqualTo(message));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { v[index] = 1; });
            Assert.That(setEx.Message, Is.EqualTo(message));
        }

        [Test]
        public void Vector3Int_Presets()
        {
            AssertExact(Vector3Int.zero, 0, 0, 0);
            AssertExact(Vector3Int.one, 1, 1, 1);
            AssertExact(Vector3Int.up, 0, 1, 0);
            AssertExact(Vector3Int.down, 0, -1, 0);
            AssertExact(Vector3Int.left, -1, 0, 0);
            AssertExact(Vector3Int.right, 1, 0, 0);
            AssertExact(Vector3Int.forward, 0, 0, 1);
            AssertExact(Vector3Int.back, 0, 0, -1);
        }

        [Test]
        public void Vector3Int_Set_Scale_Clamp()
        {
            Vector3Int v = new Vector3Int(1, 2, 3);
            v.Set(4, 5, 6);
            AssertExact(v, 4, 5, 6);
            v.Scale(new Vector3Int(2, -1, 0));
            AssertExact(v, 8, -5, 0);

            Vector3Int c = new Vector3Int(5, -5, 2);
            c.Clamp(new Vector3Int(0, 0, 0), new Vector3Int(3, 3, 3));
            AssertExact(c, 3, 0, 2);
        }

        [Test]
        public void Vector3Int_Magnitude_And_SqrMagnitude()
        {
            // 1 + 4 + 4 = 9 -> 3
            Assert.That(new Vector3Int(1, 2, 2).sqrMagnitude, Is.EqualTo(9));
            Assert.That(Bits(new Vector3Int(1, 2, 2).magnitude), Is.EqualTo(Bits(3F)));
            // 1 + 4 + 9 = 14 -> f32(sqrt(14)) = 3.7416575 [0x406F7751]
            Assert.That(new Vector3Int(1, 2, 3).sqrMagnitude, Is.EqualTo(14));
            Assert.That(Bits(new Vector3Int(1, 2, 3).magnitude), Is.EqualTo(0x406F7751));
            Assert.That(new Vector3Int(-1, -2, -3).sqrMagnitude, Is.EqualTo(14));
            Assert.That(Vector3Int.zero.magnitude, Is.EqualTo(0F));
        }

        [Test]
        public void Vector3Int_SqrMagnitude_UsesIntArithmeticAndWraps()
        {
            // Spec §12: int arithmetic inside, may overflow. 65536 * 65536 = 2^32 wraps to 0.
            Vector3Int v = new Vector3Int(65536, 0, 0);
            Assert.That(v.sqrMagnitude, Is.EqualTo(0));
            Assert.That(v.magnitude, Is.EqualTo(0F));
        }

        [Test]
        public void Vector3Int_GetHashCode_VerifiedValue()
        {
            // Spec §12 [verified]: (1,2,3) -> 805306401.
            // 1 ^ (2 << 4 = 32) ^ (2 >> 28 = 0) ^ (3 >> 4 = 0) ^ (3 << 28 = 0x30000000 = 805306368) = 805306401.
            Assert.That(new Vector3Int(1, 2, 3).GetHashCode(), Is.EqualTo(805306401));
        }

        [Test]
        public void Vector3Int_GetHashCode_Formula()
        {
            // (-1,-1,-1): 0xFFFFFFFF ^ 0xFFFFFFF0 ^ 0xFFFFFFFF (arithmetic >> 28) ^ 0xFFFFFFFF (>> 4) ^ 0xF0000000
            // = 0x0000000F ^ 0xFFFFFFFF ^ 0xFFFFFFFF ^ 0xF0000000 = 0xF000000F = -268435441.
            Assert.That(new Vector3Int(-1, -1, -1).GetHashCode(), Is.EqualTo(-268435441));
            Assert.That(Vector3Int.zero.GetHashCode(), Is.EqualTo(0));
            // x only: the hash is x itself.
            Assert.That(new Vector3Int(12345, 0, 0).GetHashCode(), Is.EqualTo(12345));
            // y only: (y << 4) ^ (y >> 28): 5 << 4 = 80, 5 >> 28 = 0 -> 80.
            Assert.That(new Vector3Int(0, 5, 0).GetHashCode(), Is.EqualTo(80));
            // z only: (z >> 4) ^ (z << 28): 16 >> 4 = 1, 16 << 28 = 0 (wraps) -> 1.
            Assert.That(new Vector3Int(0, 0, 16).GetHashCode(), Is.EqualTo(1));
        }

        [Test]
        public void Vector3Int_Equality()
        {
            Vector3Int a = new Vector3Int(1, 2, 3);
            Assert.That(a == new Vector3Int(1, 2, 3), Is.True);
            Assert.That(a != new Vector3Int(1, 2, 3), Is.False);
            Assert.That(a == new Vector3Int(1, 2, 4), Is.False);
            Assert.That(a != new Vector3Int(1, 2, 4), Is.True);
            Assert.That(a == new Vector3Int(0, 2, 3), Is.False);
            Assert.That(a == new Vector3Int(1, 0, 3), Is.False);
            Assert.That(a.Equals(new Vector3Int(1, 2, 3)), Is.True);
            Assert.That(a.Equals(new Vector3Int(3, 2, 1)), Is.False);
            Assert.That(a.Equals((object)new Vector3Int(1, 2, 3)), Is.True);
            Assert.That(a.Equals((object)new Vector3(1F, 2F, 3F)), Is.False);
            Assert.That(a.Equals((object)new Vector2Int(1, 2)), Is.False);
            Assert.That(a.Equals(null), Is.False);
        }

        [Test]
        public void Vector3Int_ToString()
        {
            // No default format: ints print as "G".
            Assert.That(new Vector3Int(1, 2, 3).ToString(), Is.EqualTo("(1, 2, 3)"));
            Assert.That(new Vector3Int(-1, 0, 1000000).ToString(), Is.EqualTo("(-1, 0, 1000000)"));
            Assert.That(new Vector3Int(1, 2, 3).ToString(null), Is.EqualTo("(1, 2, 3)"));
            Assert.That(new Vector3Int(1, 2, 3).ToString(null, null), Is.EqualTo("(1, 2, 3)"));
            Assert.That(new Vector3Int(1, 2, 3).ToString("D3"), Is.EqualTo("(001, 002, 003)"));
            Assert.That(new Vector3Int(1, 2, 3).ToString("F1"), Is.EqualTo("(1.0, 2.0, 3.0)"));
            Assert.That(new Vector3Int(1, 2, 3).ToString("X"), Is.EqualTo("(1, 2, 3)"));
            Assert.That(new Vector3Int(255, 0, 16).ToString("X"), Is.EqualTo("(FF, 0, 10)"));
            CultureInfo de = new CultureInfo("de-DE");
            Assert.That(new Vector3Int(1, 2, 3).ToString("F1", de), Is.EqualTo("(1,0, 2,0, 3,0)"));
            Assert.That(new Vector3Int(1000, 2, 3).ToString("N0", de), Is.EqualTo("(1.000, 2, 3)"));
            Assert.That(((IFormattable)new Vector3Int(1, 2, 3)).ToString("D2", null), Is.EqualTo("(01, 02, 03)"));
        }

        [Test]
        public void Vector3Int_ToString_IgnoresCurrentCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(new Vector3Int(1000, 2, 3).ToString("N0"), Is.EqualTo("(1,000, 2, 3)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void Vector3Int_Distance()
        {
            // diffs (-3, -4, 0) as floats: sqrt(9 + 16) = 5
            Assert.That(Bits(Vector3Int.Distance(new Vector3Int(1, 2, 3), new Vector3Int(4, 6, 3))), Is.EqualTo(Bits(5F)));
            Assert.That(Bits(Vector3Int.Distance(Vector3Int.zero, new Vector3Int(1, 2, 3))), Is.EqualTo(0x406F7751));
            Assert.That(Vector3Int.Distance(Vector3Int.one, Vector3Int.one), Is.EqualTo(0F));
        }

        [Test]
        public void Vector3Int_MinMax_ScaleStatic()
        {
            Vector3Int a = new Vector3Int(1, 5, 3), b = new Vector3Int(4, 2, 3);
            AssertExact(Vector3Int.Min(a, b), 1, 2, 3);
            AssertExact(Vector3Int.Max(a, b), 4, 5, 3);
            AssertExact(Vector3Int.Min(new Vector3Int(-1, -2, -3), Vector3Int.zero), -1, -2, -3);
            AssertExact(Vector3Int.Scale(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6)), 4, 10, 18);
        }

        [Test]
        public void Vector3Int_FloorCeilRoundToInt()
        {
            Vector3 v = new Vector3(1.5F, -1.5F, 2.5F);
            AssertExact(Vector3Int.FloorToInt(v), 1, -2, 2);
            AssertExact(Vector3Int.CeilToInt(v), 2, -1, 3);
            // Banker's rounding (spec §11 [verified]): 1.5 -> 2, -1.5 -> -2, 2.5 -> 2.
            AssertExact(Vector3Int.RoundToInt(v), 2, -2, 2);
            // 0.5 -> 0, -0.5 -> 0 (RoundToInt(0.5) = 0 [verified]).
            AssertExact(Vector3Int.RoundToInt(new Vector3(0.5F, -0.5F, 3.5F)), 0, 0, 4);
            AssertExact(Vector3Int.FloorToInt(new Vector3(-0.1F, 0.9F, 0F)), -1, 0, 0);
            AssertExact(Vector3Int.CeilToInt(new Vector3(-0.1F, 0.9F, 0F)), 0, 1, 0);
        }

        [Test]
        public void Vector3Int_Conversions()
        {
            Vector3 f = new Vector3Int(1, -2, 3);
            AssertBits(f, 1F, -2F, 3F);
            Vector2Int v2 = (Vector2Int)new Vector3Int(1, 2, 3);
            Assert.That(v2.x, Is.EqualTo(1));
            Assert.That(v2.y, Is.EqualTo(2));
            Vector3Int v3 = (Vector3Int)new Vector2Int(4, 5);
            AssertExact(v3, 4, 5, 0);
            // Large ints round to float.
            Vector3 big = new Vector3Int(16777217, 0, 0);
            Assert.That(Bits(big.x), Is.EqualTo(Bits(16777216F)));
        }

        [Test]
        public void Vector3Int_ArithmeticOperators()
        {
            Vector3Int a = new Vector3Int(1, 2, 3), b = new Vector3Int(4, 5, 6);
            AssertExact(a + b, 5, 7, 9);
            AssertExact(a - b, -3, -3, -3);
            AssertExact(a * b, 4, 10, 18);
            AssertExact(-a, -1, -2, -3);
            AssertExact(a * 2, 2, 4, 6);
            AssertExact(2 * a, 2, 4, 6);
            AssertExact(a * -1, -1, -2, -3);
        }

        [Test]
        public void Vector3Int_Division_TruncatesTowardZero()
        {
            AssertExact(new Vector3Int(7, -7, 5) / 2, 3, -3, 2);
            AssertExact(new Vector3Int(7, -7, 5) / -2, -3, 3, -2);
            AssertExact(new Vector3Int(8, -8, 0) / 4, 2, -2, 0);
            Assert.Throws<DivideByZeroException>(() => { Vector3Int _ = new Vector3Int(1, 2, 3) / 0; });
        }

        [Test]
        public void Vector3Int_ArithmeticWrapsUnchecked()
        {
            AssertExact(new Vector3Int(int.MaxValue, 0, 0) + new Vector3Int(1, 0, 0), int.MinValue, 0, 0);
            AssertExact(new Vector3Int(int.MinValue, 0, 0) * -1, int.MinValue, 0, 0);
            AssertExact(-new Vector3Int(int.MinValue, 0, 0), int.MinValue, 0, 0);
        }
    }
}
