// Tests for the UnityEngine.Vector2 (§1) and UnityEngine.Vector2Int (§12) shims against
// Docs/Standalone/UnityValueTypeSemantics.md. Every [verified] value in the spec is asserted bit-exactly; formula-only
// members are checked with values worked out by hand from the spec's expression (arithmetic in the comments).
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class Vector2Tests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        private static void AssertBits(Vector2 v, int xBits, int yBits)
        {
            Assert.That(Bits(v.x), Is.EqualTo(xBits), "x bits");
            Assert.That(Bits(v.y), Is.EqualTo(yBits), "y bits");
        }

        private static void AssertExact(Vector2 v, float x, float y)
        {
            // float.Equals-style exactness (NaN-aware) rather than the tolerant Vector2.==.
            Assert.That(v.x, Is.EqualTo(x), "x");
            Assert.That(v.y, Is.EqualTo(y), "y");
        }

        /// <summary>A culture whose separators would corrupt the output if ToString ever used CurrentCulture.</summary>
        private static CultureInfo WeirdCulture()
        {
            var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            ci.NumberFormat.NumberDecimalSeparator = ",";
            ci.NumberFormat.NegativeSign = "~";
            ci.NumberFormat.NumberGroupSeparator = "'";
            return ci;
        }

        private static void WithCurrentCulture(CultureInfo ci, Action body)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = ci;
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ================================================================== Vector2 (§1)

        // ------------------------------------------------------------------ §0: type shape

        [Test]
        public void Vector2_TypeShape()
        {
            Type t = typeof(Vector2);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True, "[Serializable]");
            Assert.That(t.IsLayoutSequential, Is.True, "LayoutKind.Sequential");
            Assert.That(typeof(IEquatable<Vector2>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);

            // Exactly two public float instance fields, x then y, and nothing else (native code reinterprets arrays as float*).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(2));
            Assert.That(Marshal.SizeOf<Vector2>(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<Vector2>("x").ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<Vector2>("y").ToInt32(), Is.EqualTo(4));
            Assert.That(t.GetField("x").FieldType, Is.EqualTo(typeof(float)));
            Assert.That(t.GetField("y").FieldType, Is.EqualTo(typeof(float)));
        }

        [Test]
        public unsafe void Vector2_ReinterpretsAsFloatPair()
        {
            Vector2 v = new Vector2(1.5F, -2.5F);
            float* p = (float*)&v;
            Assert.That(p[0], Is.EqualTo(1.5F));
            Assert.That(p[1], Is.EqualTo(-2.5F));
        }

        [Test]
        public void Vector2_Constants()
        {
            Assert.That(Vector2.kEpsilon, Is.EqualTo(0.00001F));
            Assert.That(Bits(Vector2.kEpsilon), Is.EqualTo(0x3727C5AC));
            Assert.That(Vector2.kEpsilonNormalSqrt, Is.EqualTo(1e-15F));
            Assert.That(Bits(Vector2.kEpsilonNormalSqrt), Is.EqualTo(0x26901D7D));
            // kEpsilon * kEpsilon folded in float = 9.9999994e-11 [0x2EDBE6FE]; kEpsilonNormalSqrt² = 1e-30 [0x0DA24260].
            Assert.That(Bits(Vector2.kEpsilon * Vector2.kEpsilon), Is.EqualTo(0x2EDBE6FE));
            Assert.That(Bits(Vector2.kEpsilonNormalSqrt * Vector2.kEpsilonNormalSqrt), Is.EqualTo(0x0DA24260));
        }

        [Test]
        public void Vector2_Constructor_AndSet()
        {
            var v = new Vector2(1F, 2F);
            AssertExact(v, 1F, 2F);
            v.Set(-3.5F, 7.25F);
            AssertExact(v, -3.5F, 7.25F);
            Assert.That(default(Vector2), Is.EqualTo(new Vector2(0F, 0F)));
        }

        // ------------------------------------------------------------------ presets

        [Test]
        public void Vector2_Presets()
        {
            AssertExact(Vector2.zero, 0F, 0F);
            AssertExact(Vector2.one, 1F, 1F);
            AssertExact(Vector2.up, 0F, 1F);
            AssertExact(Vector2.down, 0F, -1F);
            AssertExact(Vector2.left, -1F, 0F);
            AssertExact(Vector2.right, 1F, 0F);
            AssertExact(Vector2.positiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            AssertExact(Vector2.negativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            // Positive zero, not negative zero.
            AssertBits(Vector2.zero, 0, 0);
        }

        [Test]
        public void Vector2_Presets_AreStaticProperties_ReturningCopies()
        {
            PropertyInfo p = typeof(Vector2).GetProperty("zero", BindingFlags.Static | BindingFlags.Public);
            Assert.That(p, Is.Not.Null);
            Assert.That(p.CanWrite, Is.False);
            // Mutating a returned copy must not corrupt the preset.
            Vector2 z = Vector2.zero;
            z.x = 5F;
            AssertExact(Vector2.zero, 0F, 0F);
        }

        // ------------------------------------------------------------------ indexer

        [Test]
        public void Vector2_Indexer_GetSet()
        {
            var v = new Vector2(3F, 4F);
            Assert.That(v[0], Is.EqualTo(3F));
            Assert.That(v[1], Is.EqualTo(4F));
            v[0] = 10F;
            v[1] = 20F;
            AssertExact(v, 10F, 20F);
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void Vector2_Indexer_InvalidIndex_Throws(int index)
        {
            var v = new Vector2(1F, 2F);
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { float f = v[index]; });
            Assert.That(getEx.Message, Is.EqualTo("Invalid Vector2 index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { v[index] = 1F; });
            Assert.That(setEx.Message, Is.EqualTo("Invalid Vector2 index!"));
            // A failed set leaves the vector untouched.
            AssertExact(v, 1F, 2F);
        }

        // ------------------------------------------------------------------ instance members

        [Test]
        public void Vector2_Scale_Instance_And_Static()
        {
            var v = new Vector2(2F, -3F);
            v.Scale(new Vector2(4F, 0.5F));
            AssertExact(v, 8F, -1.5F);

            var w = new Vector2(2F, -3F);
            Vector2 s = new Vector2(4F, 0.5F);
            w.Scale(in s);
            AssertExact(w, 8F, -1.5F);

            AssertExact(Vector2.Scale(new Vector2(2F, -3F), new Vector2(4F, 0.5F)), 8F, -1.5F);
            Vector2 a = new Vector2(2F, -3F);
            AssertExact(Vector2.Scale(in a, in s), 8F, -1.5F);
        }

        [Test]
        public void Vector2_Magnitude_And_SqrMagnitude()
        {
            var v = new Vector2(3F, 4F);
            Assert.That(v.magnitude, Is.EqualTo(5F));
            Assert.That(Bits(v.magnitude), Is.EqualTo(0x40A00000));
            Assert.That(v.sqrMagnitude, Is.EqualTo(25F));
            Assert.That(v.SqrMagnitude(), Is.EqualTo(25F));
            Assert.That(Vector2.SqrMagnitude(v), Is.EqualTo(25F));
            Assert.That(Vector2.SqrMagnitude(in v), Is.EqualTo(25F));

            // sqrt(1*1 + 1*1) = sqrt(2) -> 1.4142135 [0x3FB504F3].
            Assert.That(Bits(Vector2.one.magnitude), Is.EqualTo(0x3FB504F3));
            Assert.That(Vector2.zero.magnitude, Is.EqualTo(0F));
            Assert.That(new Vector2(-3F, -4F).magnitude, Is.EqualTo(5F));

            Assert.That(new Vector2(float.NaN, 0F).magnitude, Is.NaN);
            Assert.That(Vector2.positiveInfinity.magnitude, Is.EqualTo(float.PositiveInfinity));
            Assert.That(Vector2.negativeInfinity.sqrMagnitude, Is.EqualTo(float.PositiveInfinity));
            // Squares overflow to +inf in float: (3e19)² = 9e38 > float.MaxValue.
            Assert.That(new Vector2(3e19F, 0F).sqrMagnitude, Is.EqualTo(float.PositiveInfinity));
            Assert.That(new Vector2(3e19F, 0F).magnitude, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Vector2_Normalize_Instance()
        {
            var v = new Vector2(3F, 4F);
            v.Normalize();
            // 3/5 = 0.6 [0x3F19999A], 4/5 = 0.8 [0x3F4CCCCD].
            AssertBits(v, 0x3F19999A, 0x3F4CCCCD);

            // magnitude 1e-6 < kEpsilon -> zero.
            var tiny = new Vector2(1e-6F, 0F);
            tiny.Normalize();
            AssertBits(tiny, 0, 0);

            // magnitude exactly kEpsilon is NOT > kEpsilon -> zero.
            var atEps = new Vector2(Vector2.kEpsilon, 0F);
            atEps.Normalize();
            AssertBits(atEps, 0, 0);

            // magnitude 2e-5 > kEpsilon -> normalised.
            var justAbove = new Vector2(2e-5F, 0F);
            justAbove.Normalize();
            AssertExact(justAbove, 1F, 0F);

            var zero = Vector2.zero;
            zero.Normalize();
            AssertBits(zero, 0, 0);

            // Negative components keep their sign: (-3,-4) -> (-0.6,-0.8).
            var neg = new Vector2(-3F, -4F);
            neg.Normalize();
            AssertBits(neg, unchecked((int)0xBF19999A), unchecked((int)0xBF4CCCCD));
        }

        [Test]
        public void Vector2_Normalized_And_StaticNormalize()
        {
            var v = new Vector2(3F, 4F);
            AssertBits(v.normalized, 0x3F19999A, 0x3F4CCCCD);
            AssertExact(v, 3F, 4F); // property does not mutate
            AssertBits(Vector2.Normalize(v), 0x3F19999A, 0x3F4CCCCD);
            AssertBits(Vector2.Normalize(in v), 0x3F19999A, 0x3F4CCCCD);

            AssertBits(new Vector2(1e-6F, 0F).normalized, 0, 0);
            AssertBits(Vector2.Normalize(new Vector2(Vector2.kEpsilon, 0F)), 0, 0);
            AssertExact(Vector2.Normalize(new Vector2(0F, -2e-5F)), 0F, -1F);
            // (1,1)/sqrt(2) = 0.70710677 [0x3F3504F3].
            AssertBits(Vector2.one.normalized, 0x3F3504F3, 0x3F3504F3);
            // NaN magnitude: NaN > kEpsilon is false -> zero.
            AssertBits(new Vector2(float.NaN, 1F).normalized, 0, 0);
        }

        // ------------------------------------------------------------------ hash / equality

        [Test]
        public void Vector2_GetHashCode_VerifiedValue()
        {
            // Spec §0 [verified]: new Vector2(1,2).GetHashCode() == 1065353216 == 0x3F800000.
            // 1f = 0x3F800000; 2f = 0x40000000, << 2 = 0x00000000 (top bits shifted out); xor = 0x3F800000.
            Assert.That(new Vector2(1F, 2F).GetHashCode(), Is.EqualTo(1065353216));
        }

        [Test]
        public void Vector2_GetHashCode_Formula()
        {
            // (1,1): 0x3F800000 ^ (0x3F800000 << 2 = 0xFE000000) = 0xC1800000 = -1048576000.
            Assert.That(new Vector2(1F, 1F).GetHashCode(), Is.EqualTo(-1048576000));
            // (3,4): 3f = 0x40400000; 4f = 0x40800000 << 2 = 0x02000000; xor = 0x42400000 = 1111490560.
            Assert.That(new Vector2(3F, 4F).GetHashCode(), Is.EqualTo(1111490560));
            Assert.That(Vector2.zero.GetHashCode(), Is.EqualTo(0));
            // Both Mono and .NET normalise -0 to the +0 hash, so (-0, 0) hashes like zero.
            Assert.That(new Vector2(-0F, 0F).GetHashCode(), Is.EqualTo(0));

            var v = new Vector2(-12.75F, 0.125F);
            Assert.That(v.GetHashCode(), Is.EqualTo(v.x.GetHashCode() ^ (v.y.GetHashCode() << 2)));
            // x and y are not interchangeable.
            Assert.That(new Vector2(1F, 2F).GetHashCode(), Is.Not.EqualTo(new Vector2(2F, 1F).GetHashCode()));
        }

        [Test]
        public void Vector2_Equals_IsExact()
        {
            var a = new Vector2(1F, 2F);
            Assert.That(a.Equals(new Vector2(1F, 2F)), Is.True);
            // 2.0000001f is NOT a distinct float: the ulp at 2 is 2^-22 = 2.38e-7, so 2.0000001 is within half an ulp
            // of 2 and the literal rounds to exactly 2f. Use the next representable float above 2 instead:
            // 0x40000001 = 2.0000002384185791.
            Assert.That(a.Equals(new Vector2(1F, BitConverter.Int32BitsToSingle(0x40000001))), Is.False);
            Assert.That(a.Equals(new Vector2(1F, 2.0000001F)), Is.True, "2.0000001f rounds to exactly 2f");
            Assert.That(a.Equals(new Vector2(1F + 1e-7F, 2F)), Is.False);
            // Well within the == tolerance but not exact.
            Assert.That(a.Equals(new Vector2(1F, 2F + 1e-6F)), Is.False);
            Assert.That(a == new Vector2(1F, 2F + 1e-6F), Is.True);

            Vector2 same = new Vector2(1F, 2F);
            Assert.That(a.Equals(in same), Is.True);
            Vector2 other = new Vector2(1F, 2.5F);
            Assert.That(a.Equals(in other), Is.False);
            // +0 and -0 are == in C#.
            Assert.That(new Vector2(0F, -0F).Equals(new Vector2(-0F, 0F)), Is.True);
        }

        [Test]
        public void Vector2_Equals_NaN_IsFalse()
        {
            // Spec §0 [verified]: new Vector2(NaN,0).Equals(itself) = false (C# == exactness, not float.Equals).
            var n = new Vector2(float.NaN, 0F);
            Assert.That(n.Equals(n), Is.False);
            Assert.That(n.Equals((object)n), Is.False);
            Assert.That(n.Equals(in n), Is.False);
            Assert.That(n == n, Is.False);
            Assert.That(n != n, Is.True);
        }

        [Test]
        public void Vector2_Equals_Object()
        {
            var a = new Vector2(1F, 2F);
            Assert.That(a.Equals((object)new Vector2(1F, 2F)), Is.True);
            Assert.That(a.Equals((object)new Vector2(2F, 1F)), Is.False);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals("(1.00, 2.00)"), Is.False);
            Assert.That(a.Equals((object)new Vector3(1F, 2F, 0F)), Is.False);
            Assert.That(a.Equals((object)new Vector2Int(1, 2)), Is.False);
        }

        [Test]
        public void Vector2_OperatorEquals_Tolerance_VerifiedBoundary()
        {
            // Spec §1 [verified]: zero == (1e-6,0) true; zero == (1e-5,0) false.
            Assert.That(Vector2.zero == new Vector2(1e-6F, 0F), Is.True);
            Assert.That(Vector2.zero == new Vector2(1e-5F, 0F), Is.False);
            Assert.That(Vector2.zero != new Vector2(1e-6F, 0F), Is.False);
            Assert.That(Vector2.zero != new Vector2(1e-5F, 0F), Is.True);
            // (1e-5)² == kEpsilon*kEpsilon exactly, and the comparison is strict '<' -> false on either side too.
            Assert.That(new Vector2(0F, 1e-5F) == Vector2.zero, Is.False);
            Assert.That(new Vector2(0F, -1e-5F) == Vector2.zero, Is.False);
        }

        [Test]
        public void Vector2_OperatorEquals_Tolerance_IsSquaredDistance()
        {
            // 2 * (7e-6)² = 9.8000004e-11 < 9.9999994e-11 -> equal; 2 * (7.1e-6)² = 1.0082e-10 -> not equal.
            Assert.That(Vector2.zero == new Vector2(7e-6F, 7e-6F), Is.True);
            Assert.That(Vector2.zero == new Vector2(7.1e-6F, 7.1e-6F), Is.False);
            // Tolerance is absolute, so large vectors compare equal through float rounding of the difference.
            Assert.That(new Vector2(1000F, 1000F) == new Vector2(1000F, 1000.00001F), Is.True);
            Assert.That(new Vector2(1F, 2F) == new Vector2(1F, 2F), Is.True);
            Assert.That(new Vector2(1F, 2F) == new Vector2(1F, 2.001F), Is.False);
        }

        [Test]
        public void Vector2_OperatorEquals_NaN_And_Infinity()
        {
            Assert.That(new Vector2(float.NaN, 0F) == new Vector2(float.NaN, 0F), Is.False);
            Assert.That(new Vector2(0F, float.NaN) != new Vector2(0F, float.NaN), Is.True);
            Assert.That(new Vector2(float.NaN, 0F) == Vector2.zero, Is.False);
            // inf - inf = NaN in the difference, so identical infinite vectors are NOT == (formula consequence).
            Assert.That(Vector2.positiveInfinity == Vector2.positiveInfinity, Is.False);
            Assert.That(Vector2.positiveInfinity != Vector2.positiveInfinity, Is.True);
            // ...but Equals is exact and inf == inf in C#.
            Assert.That(Vector2.positiveInfinity.Equals(Vector2.positiveInfinity), Is.True);
        }

        // ------------------------------------------------------------------ ToString

        [Test]
        public void Vector2_ToString_Default_F2()
        {
            Assert.That(new Vector2(1F, 2F).ToString(), Is.EqualTo("(1.00, 2.00)"));
            // 1.234f -> "1.23", -5.678f -> "-5.68".
            Assert.That(new Vector2(1.234F, -5.678F).ToString(), Is.EqualTo("(1.23, -5.68)"));
            Assert.That(Vector2.zero.ToString(), Is.EqualTo("(0.00, 0.00)"));
            Assert.That(new Vector2(-0F, 0F).ToString(), Is.EqualTo("(-0.00, 0.00)"));
        }

        [Test]
        public void Vector2_ToString_NullOrEmptyFormat_FallsBackToF2()
        {
            var v = new Vector2(1.234F, -5.678F);
            Assert.That(v.ToString(null), Is.EqualTo("(1.23, -5.68)"));
            Assert.That(v.ToString(""), Is.EqualTo("(1.23, -5.68)"));
            Assert.That(v.ToString(null, null), Is.EqualTo("(1.23, -5.68)"));
            Assert.That(v.ToString("", null), Is.EqualTo("(1.23, -5.68)"));
        }

        [Test]
        public void Vector2_ToString_ExplicitFormat()
        {
            var v = new Vector2(1.234F, -5.678F);
            Assert.That(v.ToString("F0"), Is.EqualTo("(1, -6)"));
            Assert.That(v.ToString("F4"), Is.EqualTo("(1.2340, -5.6780)"));
            Assert.That(v.ToString("G"), Is.EqualTo("(1.234, -5.678)"));
            Assert.That(v.ToString("E1"), Is.EqualTo("(1.2E+000, -5.7E+000)"));
            Assert.That(((IFormattable)v).ToString("F1", null), Is.EqualTo("(1.2, -5.7)"));
        }

        [Test]
        public void Vector2_ToString_Provider()
        {
            var v = new Vector2(1.234F, -5.678F);
            CultureInfo weird = WeirdCulture();
            Assert.That(v.ToString("F2", weird.NumberFormat), Is.EqualTo("(1,23, ~5,68)"));
            Assert.That(v.ToString(null, weird), Is.EqualTo("(1,23, ~5,68)"));
            Assert.That(v.ToString("F1", CultureInfo.InvariantCulture), Is.EqualTo("(1.2, -5.7)"));
        }

        [Test]
        public void Vector2_ToString_NullProvider_IsInvariant_NotCurrentCulture()
        {
            var v = new Vector2(1.234F, -5.678F);
            WithCurrentCulture(WeirdCulture(), () =>
            {
                Assert.That(v.ToString(), Is.EqualTo("(1.23, -5.68)"));
                Assert.That(v.ToString("F1"), Is.EqualTo("(1.2, -5.7)"));
                Assert.That(v.ToString("F1", null), Is.EqualTo("(1.2, -5.7)"));
            });
        }

        [Test]
        public void Vector2_ToString_SpecialValues()
        {
            Assert.That(new Vector2(float.NaN, float.PositiveInfinity).ToString(), Is.EqualTo("(NaN, Infinity)"));
            Assert.That(new Vector2(float.NegativeInfinity, 0F).ToString(), Is.EqualTo("(-Infinity, 0.00)"));
        }

        // ------------------------------------------------------------------ Lerp / LerpUnclamped

        [Test]
        public void Vector2_Lerp_Formula()
        {
            // (0,0) + ((10,20) - (0,0)) * 0.25 = (2.5, 5).
            AssertExact(Vector2.Lerp(Vector2.zero, new Vector2(10F, 20F), 0.25F), 2.5F, 5F);
            AssertExact(Vector2.Lerp(new Vector2(1F, 2F), new Vector2(3F, 6F), 0.5F), 2F, 4F);
            AssertExact(Vector2.Lerp(new Vector2(1F, 2F), new Vector2(3F, 6F), 0F), 1F, 2F);
            AssertExact(Vector2.Lerp(new Vector2(1F, 2F), new Vector2(3F, 6F), 1F), 3F, 6F);
        }

        [Test]
        public void Vector2_Lerp_UsesAPlusBMinusATimesT_NotWeightedSum()
        {
            // a = 0.8, b = 6.6, t = 0.4: a + (b-a)*t = 3.12 [0x4047AE14] whereas a*(1-t) + b*t = 3.1200001 [0x4047AE15].
            // a = 0.3, b = 9.9, t = 0.7: a + (b-a)*t = 7.0199995 [0x40E0A3D6] whereas a*(1-t) + b*t = 7.02 [0x40E0A3D7].
            Vector2 r = Vector2.Lerp(new Vector2(0.8F, 0.3F), new Vector2(6.6F, 9.9F), 0.4F);
            Assert.That(Bits(r.x), Is.EqualTo(0x4047AE14));
            Vector2 r2 = Vector2.LerpUnclamped(new Vector2(0.3F, 0.8F), new Vector2(9.9F, 6.6F), 0.7F);
            Assert.That(Bits(r2.x), Is.EqualTo(0x40E0A3D6));
        }

        [Test]
        public void Vector2_Lerp_ClampsT()
        {
            var a = new Vector2(0F, 0F);
            var b = new Vector2(10F, 20F);
            AssertExact(Vector2.Lerp(a, b, 2F), 10F, 20F);
            AssertExact(Vector2.Lerp(a, b, -1F), 0F, 0F);
            AssertExact(Vector2.Lerp(a, b, float.PositiveInfinity), 10F, 20F);
            AssertExact(Vector2.Lerp(a, b, float.NegativeInfinity), 0F, 0F);
            // Clamp01(NaN) = NaN passes through -> NaN components.
            Vector2 n = Vector2.Lerp(a, b, float.NaN);
            Assert.That(n.x, Is.NaN);
            Assert.That(n.y, Is.NaN);
        }

        [Test]
        public void Vector2_LerpUnclamped_DoesNotClamp()
        {
            var a = new Vector2(0F, 0F);
            var b = new Vector2(10F, 20F);
            AssertExact(Vector2.LerpUnclamped(a, b, 2F), 20F, 40F);
            AssertExact(Vector2.LerpUnclamped(a, b, -1F), -10F, -20F);
            AssertExact(Vector2.LerpUnclamped(a, b, 0.25F), 2.5F, 5F);
        }

        [Test]
        public void Vector2_Lerp_InTwins()
        {
            Vector2 a = new Vector2(0F, 0F), b = new Vector2(10F, 20F);
            AssertExact(Vector2.Lerp(in a, in b, 0.25F), 2.5F, 5F);
            AssertExact(Vector2.Lerp(in a, in b, 3F), 10F, 20F);
            AssertExact(Vector2.LerpUnclamped(in a, in b, 2F), 20F, 40F);
        }

        // ------------------------------------------------------------------ MoveTowards

        [Test]
        public void Vector2_MoveTowards_VerifiedNegativeDeltaMovesAway()
        {
            // Spec §1 [verified]: MoveTowards(0, (10,0), -3) = (-3,0).
            // to = (10,0); sqDist = 100; maxDistanceDelta < 0 so the early-out is skipped; dist = 10;
            // x = 0 + 10/10 * -3 = -3.
            Vector2 r = Vector2.MoveTowards(Vector2.zero, new Vector2(10F, 0F), -3F);
            AssertExact(r, -3F, 0F);
            AssertBits(r, unchecked((int)0xC0400000), 0);
        }

        [Test]
        public void Vector2_MoveTowards_PartialStep()
        {
            // to = (3,4), sqDist = 25 > 1; dist = 5; (0 + 3/5*1, 0 + 4/5*1) = (0.6, 0.8).
            AssertBits(Vector2.MoveTowards(Vector2.zero, new Vector2(3F, 4F), 1F), 0x3F19999A, 0x3F4CCCCD);
            // From (1,1) to (4,5) with 2.5: dist 5 -> (1 + 3/5*2.5, 1 + 4/5*2.5) = (2.5, 3).
            AssertExact(Vector2.MoveTowards(new Vector2(1F, 1F), new Vector2(4F, 5F), 2.5F), 2.5F, 3F);
        }

        [Test]
        public void Vector2_MoveTowards_ReturnsTargetWhenWithinReach()
        {
            var target = new Vector2(3F, 4F);
            AssertExact(Vector2.MoveTowards(Vector2.zero, target, 100F), 3F, 4F);
            // sqDist (25) <= maxDistanceDelta² (25) -> target exactly.
            AssertExact(Vector2.MoveTowards(Vector2.zero, target, 5F), 3F, 4F);
            // Just under reach -> not the target.
            Vector2 r = Vector2.MoveTowards(Vector2.zero, target, 4.999F);
            Assert.That(r.Equals(target), Is.False);
        }

        [Test]
        public void Vector2_MoveTowards_ZeroDistance_ReturnsTarget_EvenWithNegativeOrNaNDelta()
        {
            var p = new Vector2(2F, -7F);
            AssertExact(Vector2.MoveTowards(p, p, -3F), 2F, -7F);
            AssertExact(Vector2.MoveTowards(p, p, float.NaN), 2F, -7F);
            AssertExact(Vector2.MoveTowards(p, p, 0F), 2F, -7F);
        }

        [Test]
        public void Vector2_MoveTowards_ZeroDelta_StaysPut()
        {
            // sqDist 25 <= 0 is false -> dist path: 0 + 3/5*0 = 0.
            AssertExact(Vector2.MoveTowards(Vector2.zero, new Vector2(3F, 4F), 0F), 0F, 0F);
        }

        [Test]
        public void Vector2_MoveTowards_NaNDelta_PropagatesWhenNotAtTarget()
        {
            // maxDistanceDelta >= 0 is false for NaN -> takes the dist path -> NaN.
            Vector2 r = Vector2.MoveTowards(Vector2.zero, new Vector2(3F, 4F), float.NaN);
            Assert.That(r.x, Is.NaN);
            Assert.That(r.y, Is.NaN);
        }

        [Test]
        public void Vector2_MoveTowards_InTwin()
        {
            Vector2 c = Vector2.zero, t = new Vector2(10F, 0F);
            AssertExact(Vector2.MoveTowards(in c, in t, -3F), -3F, 0F);
            AssertExact(Vector2.MoveTowards(in c, in t, 50F), 10F, 0F);
        }

        // ------------------------------------------------------------------ Reflect / Perpendicular / Dot

        [Test]
        public void Vector2_Reflect_Formula()
        {
            // d = (1,-1), n = (0,1): Dot(n,d) = -1; factor = 2; result = (2*0 + 1, 2*1 + -1) = (1, 1).
            AssertExact(Vector2.Reflect(new Vector2(1F, -1F), new Vector2(0F, 1F)), 1F, 1F);
            // Normal is NOT normalised: d = (0,-1), n = (0,2): Dot = -2; factor = 4; result = (0, 4*2 + -1) = (0, 7).
            AssertExact(Vector2.Reflect(new Vector2(0F, -1F), new Vector2(0F, 2F)), 0F, 7F);
            // Parallel-to-surface direction is unchanged: d = (1,0), n = (0,1): Dot = 0; factor = -0; (-0*0 + 1, -0*1 + 0) = (1, 0).
            AssertExact(Vector2.Reflect(new Vector2(1F, 0F), new Vector2(0F, 1F)), 1F, 0F);

            Vector2 d = new Vector2(1F, -1F), n = new Vector2(0F, 1F);
            AssertExact(Vector2.Reflect(in d, in n), 1F, 1F);
        }

        [Test]
        public void Vector2_Perpendicular_Is90DegreesCounterClockwise()
        {
            AssertExact(Vector2.Perpendicular(new Vector2(1F, 0F)), 0F, 1F);
            AssertExact(Vector2.Perpendicular(new Vector2(0F, 1F)), -1F, 0F);
            AssertExact(Vector2.Perpendicular(new Vector2(3F, 4F)), -4F, 3F);
            // (-y, x) of zero gives (-0, 0) bitwise.
            AssertBits(Vector2.Perpendicular(Vector2.zero), NegativeZeroBits, 0);
            Vector2 v = new Vector2(3F, 4F);
            AssertExact(Vector2.Perpendicular(in v), -4F, 3F);
        }

        [Test]
        public void Vector2_Dot()
        {
            // 1*3 + 2*4 = 11.
            Assert.That(Vector2.Dot(new Vector2(1F, 2F), new Vector2(3F, 4F)), Is.EqualTo(11F));
            Assert.That(Vector2.Dot(Vector2.right, Vector2.up), Is.EqualTo(0F));
            Assert.That(Vector2.Dot(Vector2.right, Vector2.left), Is.EqualTo(-1F));
            Vector2 a = new Vector2(1F, 2F), b = new Vector2(3F, 4F);
            Assert.That(Vector2.Dot(in a, in b), Is.EqualTo(11F));
            Assert.That(Vector2.Dot(new Vector2(float.NaN, 0F), Vector2.zero), Is.NaN);
        }

        // ------------------------------------------------------------------ Angle / SignedAngle

        [Test]
        public void Vector2_Angle_RightAngles_Exact()
        {
            // (1,0),(0,1): denominator = 1*1 = 1 -> sqrt 1; dot = 0; acos(0) = pi/2 [0x3FC90FDB]; * Rad2Deg = 90 [0x42B40000].
            Assert.That(Bits(Vector2.Angle(Vector2.right, Vector2.up)), Is.EqualTo(0x42B40000));
            // acos(-1) = pi [0x40490FDB]; * Rad2Deg = 180 [0x43340000].
            Assert.That(Bits(Vector2.Angle(Vector2.right, Vector2.left)), Is.EqualTo(0x43340000));
            // acos(1) = 0.
            Assert.That(Vector2.Angle(Vector2.right, Vector2.right), Is.EqualTo(0F));
            Assert.That(Vector2.Angle(Vector2.right, new Vector2(5F, 0F)), Is.EqualTo(0F));
            // (1,0),(1,1): denominator = 1*2 = 2 -> sqrt(2) = 1.4142135; dot = 1/1.4142135 = 0.70710677; acos -> pi/4; * Rad2Deg = 45.
            Assert.That(Vector2.Angle(Vector2.right, Vector2.one), Is.EqualTo(45F).Within(1e-4F));
            Assert.That(Vector2.Angle(Vector2.up, Vector2.right), Is.EqualTo(90F));
            Vector2 r = Vector2.right, u = Vector2.up;
            Assert.That(Vector2.Angle(in r, in u), Is.EqualTo(90F));
        }

        [Test]
        public void Vector2_Angle_ClampsDotBeforeAcos()
        {
            // (0.1,0.2) vs (-0.1,-0.2): sqrMagnitudes 0.050000004 each; product 0.0025000004; sqrt 0.050000004;
            // Dot = -0.050000004; ratio = -1 exactly here, but Clamp guards |ratio| > 1 -> never NaN.
            float a = Vector2.Angle(new Vector2(0.1F, 0.2F), new Vector2(-0.1F, -0.2F));
            Assert.That(a, Is.Not.NaN);
            Assert.That(a, Is.EqualTo(180F).Within(0.01F));
            float b = Vector2.Angle(new Vector2(0.3F, 0.7F), new Vector2(0.3F, 0.7F));
            Assert.That(b, Is.Not.NaN);
            Assert.That(b, Is.EqualTo(0F).Within(0.05F));
        }

        [Test]
        public void Vector2_Angle_TinyVectors_UnderflowThreshold()
        {
            // (1e-8,0),(0,1e-8): sqrMagnitude 1e-16 each; product 1e-32 < 1e-30 -> 0 (not 90).
            Assert.That(Vector2.Angle(new Vector2(1e-8F, 0F), new Vector2(0F, 1e-8F)), Is.EqualTo(0F));
            // (1e-7,0),(0,1e-7): product 1e-28 >= 1e-30 -> proceeds; sqrt = 1e-14; dot 0 -> 90.
            Assert.That(Vector2.Angle(new Vector2(1e-7F, 0F), new Vector2(0F, 1e-7F)), Is.EqualTo(90F));
            // One tiny and one unit vector: 1e-16 * 1 = 1e-16 >= 1e-30 -> 90.
            Assert.That(Vector2.Angle(new Vector2(1e-8F, 0F), Vector2.up), Is.EqualTo(90F));
            // Zero vectors -> 0 (no NaN).
            Assert.That(Vector2.Angle(Vector2.zero, Vector2.up), Is.EqualTo(0F));
            Assert.That(Vector2.Angle(Vector2.zero, Vector2.zero), Is.EqualTo(0F));
        }

        [Test]
        public void Vector2_SignedAngle_SignFromCross()
        {
            // cross = from.x*to.y - from.y*to.x: (1,0)->(0,1): 1*1 - 0*0 = 1 -> +90.
            Assert.That(Vector2.SignedAngle(Vector2.right, Vector2.up), Is.EqualTo(90F));
            // (0,1)->(1,0): 0*0 - 1*1 = -1 -> -90.
            Assert.That(Vector2.SignedAngle(Vector2.up, Vector2.right), Is.EqualTo(-90F));
            Assert.That(Vector2.SignedAngle(Vector2.right, new Vector2(1F, -1F)), Is.EqualTo(-45F).Within(1e-4F));
            Vector2 r = Vector2.right, u = Vector2.up;
            Assert.That(Vector2.SignedAngle(in r, in u), Is.EqualTo(90F));
            Assert.That(Vector2.SignedAngle(in u, in r), Is.EqualTo(-90F));
        }

        [Test]
        public void Vector2_SignedAngle_Collinear_UsesSignZeroIsPlusOne()
        {
            // Opposite vectors: cross = 1*0 - 0*(-1) = 0; Mathf.Sign(0) = 1 -> +180 (never -180).
            Assert.That(Vector2.SignedAngle(Vector2.right, Vector2.left), Is.EqualTo(180F));
            Assert.That(Vector2.SignedAngle(Vector2.up, Vector2.down), Is.EqualTo(180F));
            // Same direction: angle 0 * +1 = +0 (positive zero).
            Assert.That(Bits(Vector2.SignedAngle(Vector2.right, new Vector2(2F, 0F))), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ Distance

        [Test]
        public void Vector2_Distance()
        {
            // dx = -3, dy = -4 -> sqrt(9 + 16) = 5.
            Assert.That(Vector2.Distance(new Vector2(1F, 1F), new Vector2(4F, 5F)), Is.EqualTo(5F));
            Assert.That(Vector2.Distance(new Vector2(4F, 5F), new Vector2(1F, 1F)), Is.EqualTo(5F));
            Assert.That(Vector2.Distance(Vector2.zero, Vector2.zero), Is.EqualTo(0F));
            Assert.That(Bits(Vector2.Distance(Vector2.zero, Vector2.one)), Is.EqualTo(0x3FB504F3));
            Vector2 a = new Vector2(1F, 1F), b = new Vector2(4F, 5F);
            Assert.That(Vector2.Distance(in a, in b), Is.EqualTo(5F));
            Assert.That(Vector2.Distance(Vector2.positiveInfinity, Vector2.positiveInfinity), Is.NaN);
        }

        // ------------------------------------------------------------------ ClampMagnitude

        [Test]
        public void Vector2_ClampMagnitude_VerifiedNegativeMaxLengthFlips()
        {
            // Spec §1 [verified]: ClampMagnitude((3,4), -1) = (-0.6, -0.8).
            // sqr = 25 > (-1)² = 1; mag = 5; nx = 0.6, ny = 0.8; * -1 -> (-0.6 [0xBF19999A], -0.8 [0xBF4CCCCD]).
            Vector2 r = Vector2.ClampMagnitude(new Vector2(3F, 4F), -1F);
            AssertBits(r, unchecked((int)0xBF19999A), unchecked((int)0xBF4CCCCD));
        }

        [Test]
        public void Vector2_ClampMagnitude_Formula()
        {
            AssertBits(Vector2.ClampMagnitude(new Vector2(3F, 4F), 1F), 0x3F19999A, 0x3F4CCCCD);
            // nx*maxLength: 0.6 * 2.5 = 1.5, 0.8 * 2.5 = 2 (exact in float).
            AssertExact(Vector2.ClampMagnitude(new Vector2(3F, 4F), 2.5F), 1.5F, 2F);
            // Not exceeding -> unchanged (same bits, including exactly-at-limit since 25 > 25 is false).
            AssertExact(Vector2.ClampMagnitude(new Vector2(3F, 4F), 10F), 3F, 4F);
            AssertExact(Vector2.ClampMagnitude(new Vector2(3F, 4F), 5F), 3F, 4F);
            // maxLength 0: 25 > 0 -> (0.6*0, 0.8*0) = (0, 0).
            AssertBits(Vector2.ClampMagnitude(new Vector2(3F, 4F), 0F), 0, 0);
            // Zero vector: 0 > maxLength² false -> zero returned as is.
            AssertBits(Vector2.ClampMagnitude(Vector2.zero, 1F), 0, 0);
            Vector2 v = new Vector2(3F, 4F);
            AssertBits(Vector2.ClampMagnitude(in v, -1F), unchecked((int)0xBF19999A), unchecked((int)0xBF4CCCCD));
        }

        // ------------------------------------------------------------------ Min / Max

        [Test]
        public void Vector2_Min_Max_Componentwise()
        {
            var a = new Vector2(1F, 5F);
            var b = new Vector2(3F, 2F);
            AssertExact(Vector2.Min(a, b), 1F, 2F);
            AssertExact(Vector2.Max(a, b), 3F, 5F);
            AssertExact(Vector2.Min(in a, in b), 1F, 2F);
            AssertExact(Vector2.Max(in a, in b), 3F, 5F);
            AssertExact(Vector2.Min(Vector2.negativeInfinity, Vector2.zero), float.NegativeInfinity, float.NegativeInfinity);
            AssertExact(Vector2.Max(Vector2.positiveInfinity, Vector2.zero), float.PositiveInfinity, float.PositiveInfinity);
        }

        [Test]
        public void Vector2_Min_Max_NaNOrdering_FollowsMathf()
        {
            // Mathf.Min(a,b) = a < b ? a : b -> Min(NaN,1) = 1, Min(1,NaN) = NaN; Max(a,b) = a > b ? a : b -> same shape.
            Vector2 mn = Vector2.Min(new Vector2(float.NaN, 1F), new Vector2(1F, float.NaN));
            Assert.That(mn.x, Is.EqualTo(1F));
            Assert.That(mn.y, Is.NaN);
            Vector2 mx = Vector2.Max(new Vector2(float.NaN, 1F), new Vector2(1F, float.NaN));
            Assert.That(mx.x, Is.EqualTo(1F));
            Assert.That(mx.y, Is.NaN);
        }

        // ------------------------------------------------------------------ SmoothDamp

        [Test]
        public void Vector2_SmoothDamp_VerifiedValues()
        {
            // Spec §1 [verified]: SmoothDamp(0, (10,0), ref 0, 0.3, ∞, 0.016) = (0.05165842, 0), velocity (6.392509, 0).
            Vector2 vel = Vector2.zero;
            Vector2 o = Vector2.SmoothDamp(Vector2.zero, new Vector2(10F, 0F), ref vel, 0.3F, float.PositiveInfinity, 0.016F);
            Assert.That(o.x, Is.EqualTo(0.05165842F), "output.x");
            Assert.That(Bits(o.x), Is.EqualTo(0x3D5397C8), "output.x bits");
            Assert.That(o.y, Is.EqualTo(0F));
            Assert.That(vel.x, Is.EqualTo(6.392509F), "velocity.x");
            Assert.That(Bits(vel.x), Is.EqualTo(0x40CC8F6F), "velocity.x bits");
            Assert.That(vel.y, Is.EqualTo(0F));
        }

        [Test]
        public void Vector2_SmoothDamp_VerifiedOvershoot()
        {
            // Spec §1 [verified]: current 0, target (1,0), vel (100,0), 0.3, ∞, 0.5 -> (1,0), vel (0,0).
            // Pre-guard output ≈ 3.486 so (target-current)·(output-target) = 1 * 2.486 > 0 -> clamp.
            Vector2 vel = new Vector2(100F, 0F);
            Vector2 o = Vector2.SmoothDamp(Vector2.zero, new Vector2(1F, 0F), ref vel, 0.3F, float.PositiveInfinity, 0.5F);
            AssertExact(o, 1F, 0F);
            AssertExact(vel, 0F, 0F);
        }

        [Test]
        public void Vector2_SmoothDamp_OvershootGuard_UsesDotProduct_2D()
        {
            // Same overshoot along the diagonal: each component behaves like the scalar case, dot = 2 * 2.486 > 0.
            Vector2 vel = new Vector2(100F, 100F);
            Vector2 o = Vector2.SmoothDamp(Vector2.zero, new Vector2(1F, 1F), ref vel, 0.3F, float.PositiveInfinity, 0.5F);
            AssertExact(o, 1F, 1F);
            AssertExact(vel, 0F, 0F);

            // Moving away from the target (negative velocity) does not trigger the guard: output ends up below current.
            Vector2 vel2 = new Vector2(-100F, 0F);
            Vector2 o2 = Vector2.SmoothDamp(Vector2.zero, new Vector2(1F, 0F), ref vel2, 0.3F, float.PositiveInfinity, 0.5F);
            Assert.That(o2.x, Is.LessThan(0F));
            Assert.That(vel2.x, Is.Not.EqualTo(0F));
        }

        [Test]
        public void Vector2_SmoothDamp_MaxSpeed_ClampsChangeByMagnitude()
        {
            // 1-D input: change = -10, maxChange = 5 * 0.3 = 1.5, sqDist 100 > 2.25 -> change = -10/10*1.5 = -1.5, which is exactly
            // the scalar Clamp result, so the output must equal the spec §11 [verified] Mathf.SmoothDamp oracle
            // (0.007748774, vel 0.95887625).
            Vector2 vel = Vector2.zero;
            Vector2 o = Vector2.SmoothDamp(Vector2.zero, new Vector2(10F, 0F), ref vel, 0.3F, 5F, 0.016F);
            Assert.That(o.x, Is.EqualTo(0.007748774F));
            Assert.That(vel.x, Is.EqualTo(0.95887625F));
            Assert.That(o.y, Is.EqualTo(0F));
            Assert.That(vel.y, Is.EqualTo(0F));

            // Diagonal: change = (-3,-4), maxChange = 1*1 = 1, sqDist 25 > 1 -> change = (-0.6,-0.8); target' = (0.6,0.8).
            // Compare against the scalar algorithm fed with the pre-clamped change (maxSpeed large enough not to clamp again).
            Vector2 velD = Vector2.zero;
            Vector2 oD = Vector2.SmoothDamp(Vector2.zero, new Vector2(3F, 4F), ref velD, 1F, 1F, 0.016F);
            float vx = 0F, vy = 0F;
            float ex = Mathf.SmoothDamp(0F, 0.6F, ref vx, 1F, float.PositiveInfinity, 0.016F);
            float ey = Mathf.SmoothDamp(0F, 0.8F, ref vy, 1F, float.PositiveInfinity, 0.016F);
            Assert.That(oD.x, Is.EqualTo(ex));
            Assert.That(oD.y, Is.EqualTo(ey));
            Assert.That(velD.x, Is.EqualTo(vx));
            Assert.That(velD.y, Is.EqualTo(vy));
        }

        [Test]
        public void Vector2_SmoothDamp_1D_MatchesScalarMathfSmoothDamp()
        {
            // Identical algorithm per component when no clamping/overshoot is involved (spec §1 vs §11).
            float[][] cases =
            {
                new[] { 0F, 10F, 0F, 0.3F, 0.016F },
                new[] { 5F, -2F, 3F, 0.1F, 0.033F },
                new[] { -1F, 1F, 0F, 1F, 0.1F },
                new[] { 100F, 0F, -20F, 0.5F, 0.02F },
                new[] { 0F, 10F, 0F, 0F, 0.016F }, // smoothTime floors to 0.0001
            };
            foreach (float[] c in cases)
            {
                Vector2 vel = new Vector2(c[2], 0F);
                Vector2 o = Vector2.SmoothDamp(new Vector2(c[0], 0F), new Vector2(c[1], 0F), ref vel, c[3], float.PositiveInfinity, c[4]);
                float sv = c[2];
                float so = Mathf.SmoothDamp(c[0], c[1], ref sv, c[3], float.PositiveInfinity, c[4]);
                Assert.That(Bits(o.x), Is.EqualTo(Bits(so)), $"output for case [{string.Join(",", c)}]");
                Assert.That(Bits(vel.x), Is.EqualTo(Bits(sv)), $"velocity for case [{string.Join(",", c)}]");
                Assert.That(o.y, Is.EqualTo(0F));
                Assert.That(vel.y, Is.EqualTo(0F));
            }
        }

        [Test]
        public void Vector2_SmoothDamp_SmoothTimeFloor()
        {
            Vector2 v1 = Vector2.zero, v2 = Vector2.zero;
            Vector2 o1 = Vector2.SmoothDamp(Vector2.zero, new Vector2(10F, 0F), ref v1, 0F, float.PositiveInfinity, 0.016F);
            Vector2 o2 = Vector2.SmoothDamp(Vector2.zero, new Vector2(10F, 0F), ref v2, 0.0001F, float.PositiveInfinity, 0.016F);
            Assert.That(Bits(o1.x), Is.EqualTo(Bits(o2.x)));
            Assert.That(Bits(v1.x), Is.EqualTo(Bits(v2.x)));
            Vector2 v3 = Vector2.zero;
            Vector2 o3 = Vector2.SmoothDamp(Vector2.zero, new Vector2(10F, 0F), ref v3, -5F, float.PositiveInfinity, 0.016F);
            Assert.That(Bits(o3.x), Is.EqualTo(Bits(o2.x)));
        }

        [Test]
        public void Vector2_SmoothDamp_AtTarget_StaysAtTarget()
        {
            // change = 0 -> temp = vel*dt, output = current + temp*exp; with zero velocity output == target and no NaN.
            Vector2 vel = Vector2.zero;
            Vector2 o = Vector2.SmoothDamp(new Vector2(2F, 3F), new Vector2(2F, 3F), ref vel, 0.3F, float.PositiveInfinity, 0.016F);
            AssertExact(o, 2F, 3F);
            AssertExact(vel, 0F, 0F);
        }

        [Test]
        public void Vector2_SmoothDamp_OverloadsUseEngineClockAndInfinity()
        {
            float saved = EngineClock.deltaTime;
            try
            {
                EngineClock.deltaTime = 0.016F;
                Vector2 target = new Vector2(10F, -4F);
                Vector2 vA = Vector2.zero, vB = Vector2.zero, vC = Vector2.zero;
                Vector2 a = Vector2.SmoothDamp(Vector2.zero, target, ref vA, 0.3F, float.PositiveInfinity, 0.016F);
                Vector2 b = Vector2.SmoothDamp(Vector2.zero, target, ref vB, 0.3F, float.PositiveInfinity);
                Vector2 c = Vector2.SmoothDamp(Vector2.zero, target, ref vC, 0.3F);
                Assert.That(b.Equals(a), Is.True);
                Assert.That(c.Equals(a), Is.True);
                Assert.That(vB.Equals(vA), Is.True);
                Assert.That(vC.Equals(vA), Is.True);

                Vector2 vD = Vector2.zero, vE = Vector2.zero;
                Vector2 d = Vector2.SmoothDamp(Vector2.zero, target, ref vD, 0.3F, 5F);
                Vector2 e = Vector2.SmoothDamp(Vector2.zero, target, ref vE, 0.3F, 5F, 0.016F);
                Assert.That(d.Equals(e), Is.True);
                Assert.That(vD.Equals(vE), Is.True);
                Assert.That(d.Equals(a), Is.False, "maxSpeed 5 must clamp");

                // Changing the clock changes the result of the clock-driven overloads.
                EngineClock.deltaTime = 0.1F;
                Vector2 vF = Vector2.zero;
                Vector2 f = Vector2.SmoothDamp(Vector2.zero, target, ref vF, 0.3F);
                Assert.That(f.Equals(a), Is.False);
            }
            finally
            {
                EngineClock.deltaTime = saved;
            }
        }

        // ------------------------------------------------------------------ operators

        [Test]
        public void Vector2_Operators_AddSubNegate()
        {
            AssertExact(new Vector2(1F, 2F) + new Vector2(3F, -5F), 4F, -3F);
            AssertExact(new Vector2(1F, 2F) - new Vector2(3F, -5F), -2F, 7F);
            AssertExact(-new Vector2(1F, -2F), -1F, 2F);
            // Negating zero produces negative zero bitwise.
            AssertBits(-Vector2.zero, NegativeZeroBits, NegativeZeroBits);
            AssertExact(Vector2.positiveInfinity + Vector2.negativeInfinity, float.NaN, float.NaN);
        }

        [Test]
        public void Vector2_Operators_ComponentwiseMultiplyDivide()
        {
            // Unlike Vector3/Vector4, Vector2 has vector*vector and vector/vector.
            AssertExact(new Vector2(2F, 3F) * new Vector2(4F, 5F), 8F, 15F);
            AssertExact(new Vector2(8F, 15F) / new Vector2(4F, 5F), 2F, 3F);
            AssertExact(new Vector2(1F, -1F) / Vector2.zero, float.PositiveInfinity, float.NegativeInfinity);
            Assert.That((Vector2.zero / Vector2.zero).x, Is.NaN);
            // 0.1 * 0.2 in float = 0.020000001 [0x3CA3D70B].
            Assert.That(Bits((new Vector2(0.1F, 0F) * new Vector2(0.2F, 0F)).x), Is.EqualTo(0x3CA3D70B));
        }

        [Test]
        public void Vector2_Operators_Scalar()
        {
            AssertExact(new Vector2(2F, -3F) * 2.5F, 5F, -7.5F);
            AssertExact(2.5F * new Vector2(2F, -3F), 5F, -7.5F);
            AssertExact(new Vector2(5F, -7.5F) / 2.5F, 2F, -3F);
            AssertExact(new Vector2(1F, -1F) / 0F, float.PositiveInfinity, float.NegativeInfinity);
            // (float, Vector2) and (Vector2, float) commute bit-for-bit.
            Vector2 v = new Vector2(0.1F, 0.7F);
            Vector2 l = 0.3F * v, r = v * 0.3F;
            AssertBits(l, Bits(r.x), Bits(r.y));
            // x / d is a true division, not x * (1/d): 1/3 vs 1*(1/3) differ for some inputs; 7/7 = 1 exactly.
            AssertExact(new Vector2(7F, 21F) / 7F, 1F, 3F);
        }

        [Test]
        public void Vector2_Operators_NoImplicitIntDivision()
        {
            // 3/2 must be 1.5 (float), never integer division.
            AssertExact(new Vector2(3F, 5F) / 2, 1.5F, 2.5F);
        }

        // ------------------------------------------------------------------ conversions

        [Test]
        public void Vector2_ImplicitConversions_Vector3()
        {
            Vector2 fromV3 = new Vector3(1F, 2F, 3F);
            AssertExact(fromV3, 1F, 2F);
            Vector3 toV3 = new Vector2(4F, 5F);
            Assert.That(toV3.x, Is.EqualTo(4F));
            Assert.That(toV3.y, Is.EqualTo(5F));
            Assert.That(toV3.z, Is.EqualTo(0F));
            Assert.That(Bits(toV3.z), Is.EqualTo(0));

            // Declared on Vector2 (spec §1).
            MethodInfo[] ops = typeof(Vector2).GetMethods(BindingFlags.Static | BindingFlags.Public);
            bool hasV3ToV2 = false, hasV2ToV3 = false;
            foreach (MethodInfo m in ops)
            {
                if (m.Name != "op_Implicit") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 1) continue;
                if (ps[0].ParameterType == typeof(Vector3) && m.ReturnType == typeof(Vector2)) hasV3ToV2 = true;
                if (ps[0].ParameterType == typeof(Vector2) && m.ReturnType == typeof(Vector3)) hasV2ToV3 = true;
            }
            Assert.That(hasV3ToV2, Is.True, "implicit Vector2(Vector3) declared on Vector2");
            Assert.That(hasV2ToV3, Is.True, "implicit Vector3(Vector2) declared on Vector2");
        }

        [Test]
        public void Vector2_ConversionRoundTrip_ThroughVector3_DropsZ()
        {
            Vector3 v3 = new Vector3(1F, 2F, 3F);
            Vector2 v2 = v3;
            Vector3 back = v2;
            Assert.That(back.z, Is.EqualTo(0F));
            Assert.That(back.Equals(new Vector3(1F, 2F, 0F)), Is.True);
        }

        // ------------------------------------------------------------------ readonly / in surface

        [Test]
        public void Vector2_InTwins_Exist_ForListedStatics()
        {
            string[] names = { "Lerp", "LerpUnclamped", "MoveTowards", "Scale", "Normalize", "Reflect", "Perpendicular", "Dot", "Angle", "SignedAngle", "Distance", "ClampMagnitude", "SqrMagnitude", "Min", "Max" };
            foreach (string name in names)
            {
                bool hasIn = false, hasByValue = false;
                foreach (MethodInfo m in typeof(Vector2).GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (m.Name != name) continue;
                    ParameterInfo p0 = m.GetParameters()[0];
                    if (p0.ParameterType.IsByRef && p0.IsIn) hasIn = true;
                    else if (!p0.ParameterType.IsByRef) hasByValue = true;
                }
                Assert.That(hasIn, Is.True, $"{name} has an 'in' twin");
                Assert.That(hasByValue, Is.True, $"{name} has a by-value form");
            }
        }

        [Test]
        public void Vector2_PureMembers_AreReadonly()
        {
            // 'readonly' instance members carry IsReadOnlyAttribute on the method.
            static bool IsReadOnly(MethodInfo m)
            {
                foreach (CustomAttributeData a in m.CustomAttributes)
                    if (a.AttributeType.Name == "IsReadOnlyAttribute") return true;
                return false;
            }
            Type t = typeof(Vector2);
            Assert.That(IsReadOnly(t.GetProperty("magnitude").GetMethod), Is.True, "magnitude");
            Assert.That(IsReadOnly(t.GetProperty("sqrMagnitude").GetMethod), Is.True, "sqrMagnitude");
            Assert.That(IsReadOnly(t.GetProperty("normalized").GetMethod), Is.True, "normalized");
            Assert.That(IsReadOnly(t.GetMethod("GetHashCode")), Is.True, "GetHashCode");
            Assert.That(IsReadOnly(t.GetMethod("Equals", new[] { typeof(Vector2) })), Is.True, "Equals(Vector2)");
            Assert.That(IsReadOnly(t.GetMethod("ToString", Type.EmptyTypes)), Is.True, "ToString()");
            // Mutators are not readonly.
            Assert.That(IsReadOnly(t.GetMethod("Normalize", Type.EmptyTypes)), Is.False, "Normalize()");
            Assert.That(IsReadOnly(t.GetMethod("Set")), Is.False, "Set");
            Assert.That(IsReadOnly(t.GetMethod("Scale", new[] { typeof(Vector2) })), Is.False, "Scale(Vector2)");
        }

        // ================================================================== Vector2Int (§12)

        [Test]
        public void Vector2Int_TypeShape()
        {
            Type t = typeof(Vector2Int);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True, "[Serializable]");
            Assert.That(t.IsLayoutSequential, Is.True, "LayoutKind.Sequential");
            Assert.That(typeof(IEquatable<Vector2Int>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            Assert.That(Marshal.SizeOf<Vector2Int>(), Is.EqualTo(8));

            // Private m_X / m_Y behind x / y properties (spec §0, §12).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(2));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty, "no public fields");
            Assert.That(t.GetField("m_X", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(t.GetField("m_Y", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(t.GetField("m_X", BindingFlags.Instance | BindingFlags.NonPublic).FieldType, Is.EqualTo(typeof(int)));
            Assert.That(Marshal.OffsetOf<Vector2Int>("m_X").ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<Vector2Int>("m_Y").ToInt32(), Is.EqualTo(4));
            Assert.That(t.GetProperty("x").PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(t.GetProperty("y").CanWrite, Is.True);
        }

        [Test]
        public void Vector2Int_Constructor_Properties_Set()
        {
            var v = new Vector2Int(1, 2);
            Assert.That(v.x, Is.EqualTo(1));
            Assert.That(v.y, Is.EqualTo(2));
            v.x = -7;
            v.y = 9;
            Assert.That(v, Is.EqualTo(new Vector2Int(-7, 9)));
            v.Set(int.MaxValue, int.MinValue);
            Assert.That(v.x, Is.EqualTo(int.MaxValue));
            Assert.That(v.y, Is.EqualTo(int.MinValue));
            Assert.That(default(Vector2Int), Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void Vector2Int_Presets()
        {
            Assert.That(Vector2Int.zero, Is.EqualTo(new Vector2Int(0, 0)));
            Assert.That(Vector2Int.one, Is.EqualTo(new Vector2Int(1, 1)));
            Assert.That(Vector2Int.up, Is.EqualTo(new Vector2Int(0, 1)));
            Assert.That(Vector2Int.down, Is.EqualTo(new Vector2Int(0, -1)));
            Assert.That(Vector2Int.left, Is.EqualTo(new Vector2Int(-1, 0)));
            Assert.That(Vector2Int.right, Is.EqualTo(new Vector2Int(1, 0)));
            PropertyInfo p = typeof(Vector2Int).GetProperty("zero", BindingFlags.Static | BindingFlags.Public);
            Assert.That(p, Is.Not.Null);
            Assert.That(p.CanWrite, Is.False);
            Vector2Int z = Vector2Int.zero;
            z.x = 3;
            Assert.That(Vector2Int.zero.x, Is.EqualTo(0));
        }

        [Test]
        public void Vector2Int_Indexer_GetSet()
        {
            var v = new Vector2Int(3, 4);
            Assert.That(v[0], Is.EqualTo(3));
            Assert.That(v[1], Is.EqualTo(4));
            v[0] = 10;
            v[1] = 20;
            Assert.That(v, Is.EqualTo(new Vector2Int(10, 20)));
        }

        [TestCase(2, "Invalid Vector2Int index addressed: 2!")]
        [TestCase(-1, "Invalid Vector2Int index addressed: -1!")]
        [TestCase(100, "Invalid Vector2Int index addressed: 100!")]
        public void Vector2Int_Indexer_InvalidIndex_Throws(int index, string message)
        {
            var v = new Vector2Int(1, 2);
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { int i = v[index]; });
            Assert.That(getEx.Message, Is.EqualTo(message));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { v[index] = 1; });
            Assert.That(setEx.Message, Is.EqualTo(message));
            Assert.That(v, Is.EqualTo(new Vector2Int(1, 2)));
        }

        [Test]
        public void Vector2Int_Indexer_Message_IsCultureInvariantForNegativeIndex()
        {
            // string.Format(null provider) uses the current culture; Unity's message with index -1 prints "-1" under
            // invariant/most cultures. Assert the invariant rendering with a neutral culture active.
            WithCurrentCulture(CultureInfo.InvariantCulture, () =>
            {
                var v = new Vector2Int(1, 2);
                var ex = Assert.Throws<IndexOutOfRangeException>(() => { int i = v[-1]; });
                Assert.That(ex.Message, Is.EqualTo("Invalid Vector2Int index addressed: -1!"));
            });
        }

        [Test]
        public void Vector2Int_Magnitude_And_SqrMagnitude()
        {
            var v = new Vector2Int(3, 4);
            Assert.That(v.magnitude, Is.EqualTo(5F));
            Assert.That(v.sqrMagnitude, Is.EqualTo(25));
            Assert.That(new Vector2Int(-3, -4).magnitude, Is.EqualTo(5F));
            Assert.That(Vector2Int.zero.magnitude, Is.EqualTo(0F));
            Assert.That(Bits(Vector2Int.one.magnitude), Is.EqualTo(0x3FB504F3)); // sqrt(2)
            Assert.That(Vector2Int.one.sqrMagnitude, Is.EqualTo(2));
        }

        [Test]
        public void Vector2Int_Magnitude_IntArithmeticOverflows()
        {
            // 65536² = 2^32 wraps to 0 in int -> sqrMagnitude 0, magnitude 0.
            Assert.That(new Vector2Int(65536, 0).sqrMagnitude, Is.EqualTo(0));
            Assert.That(new Vector2Int(65536, 0).magnitude, Is.EqualTo(0F));
            // 46341² = 2147488281 wraps to -2147479015 -> sqrt of a negative -> NaN.
            Assert.That(new Vector2Int(46341, 0).sqrMagnitude, Is.EqualTo(-2147479015));
            Assert.That(new Vector2Int(46341, 0).magnitude, Is.NaN);
            // 46340² = 2147395600 fits -> 46340.
            Assert.That(new Vector2Int(46340, 0).magnitude, Is.EqualTo(46340F));
        }

        [Test]
        public void Vector2Int_Distance_UsesFloatDifferences()
        {
            Assert.That(Vector2Int.Distance(Vector2Int.zero, new Vector2Int(3, 4)), Is.EqualTo(5F));
            Assert.That(Vector2Int.Distance(new Vector2Int(3, 4), Vector2Int.zero), Is.EqualTo(5F));
            Assert.That(Vector2Int.Distance(new Vector2Int(1, 1), new Vector2Int(4, 5)), Is.EqualTo(5F));
            // Differences are floats before squaring, so no int overflow: 65536² = 4294967296 as float -> sqrt = 65536.
            Assert.That(Vector2Int.Distance(new Vector2Int(65536, 0), Vector2Int.zero), Is.EqualTo(65536F));
            Assert.That(Vector2Int.Distance(new Vector2Int(46341, 0), Vector2Int.zero), Is.EqualTo(46341F));
            Assert.That(Bits(Vector2Int.Distance(Vector2Int.zero, Vector2Int.one)), Is.EqualTo(0x3FB504F3));
        }

        [Test]
        public void Vector2Int_Min_Max()
        {
            var a = new Vector2Int(1, 5);
            var b = new Vector2Int(3, 2);
            Assert.That(Vector2Int.Min(a, b), Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(Vector2Int.Max(a, b), Is.EqualTo(new Vector2Int(3, 5)));
            Assert.That(Vector2Int.Min(new Vector2Int(int.MinValue, 0), new Vector2Int(0, int.MaxValue)), Is.EqualTo(new Vector2Int(int.MinValue, 0)));
            Assert.That(Vector2Int.Max(new Vector2Int(int.MinValue, 0), new Vector2Int(0, int.MaxValue)), Is.EqualTo(new Vector2Int(0, int.MaxValue)));
        }

        [Test]
        public void Vector2Int_Scale_Static_And_Instance()
        {
            Assert.That(Vector2Int.Scale(new Vector2Int(2, -3), new Vector2Int(4, 5)), Is.EqualTo(new Vector2Int(8, -15)));
            var v = new Vector2Int(2, -3);
            v.Scale(new Vector2Int(4, 5));
            Assert.That(v, Is.EqualTo(new Vector2Int(8, -15)));
            // Unchecked overflow wraps.
            var big = new Vector2Int(65536, 1);
            big.Scale(new Vector2Int(65536, 1));
            Assert.That(big, Is.EqualTo(new Vector2Int(0, 1)));
        }

        [Test]
        public void Vector2Int_Clamp_PerComponentMathfClamp()
        {
            var v = new Vector2Int(5, -5);
            v.Clamp(new Vector2Int(0, 0), new Vector2Int(3, 3));
            Assert.That(v, Is.EqualTo(new Vector2Int(3, 0)));

            var inside = new Vector2Int(1, 2);
            inside.Clamp(new Vector2Int(0, 0), new Vector2Int(3, 3));
            Assert.That(inside, Is.EqualTo(new Vector2Int(1, 2)));

            // Mathf.Clamp: value < min ? min : (value > max ? max : value) -> with min > max the min test wins.
            var inverted = new Vector2Int(5, 5);
            inverted.Clamp(new Vector2Int(10, 10), new Vector2Int(0, 0));
            Assert.That(inverted, Is.EqualTo(new Vector2Int(10, 10)));
            var inverted2 = new Vector2Int(20, 20);
            inverted2.Clamp(new Vector2Int(10, 10), new Vector2Int(0, 0));
            Assert.That(inverted2, Is.EqualTo(new Vector2Int(0, 0)));
        }

        [Test]
        public void Vector2Int_FloorToInt_CeilToInt()
        {
            Assert.That(Vector2Int.FloorToInt(new Vector2(-0.5F, 1.7F)), Is.EqualTo(new Vector2Int(-1, 1)));
            Assert.That(Vector2Int.FloorToInt(new Vector2(2F, -2F)), Is.EqualTo(new Vector2Int(2, -2)));
            Assert.That(Vector2Int.FloorToInt(new Vector2(-0.0001F, 0.9999F)), Is.EqualTo(new Vector2Int(-1, 0)));
            Assert.That(Vector2Int.CeilToInt(new Vector2(-0.5F, 1.2F)), Is.EqualTo(new Vector2Int(0, 2)));
            Assert.That(Vector2Int.CeilToInt(new Vector2(2F, -2F)), Is.EqualTo(new Vector2Int(2, -2)));
            Assert.That(Vector2Int.CeilToInt(new Vector2(-1.9F, 0.0001F)), Is.EqualTo(new Vector2Int(-1, 1)));
        }

        [Test]
        public void Vector2Int_RoundToInt_IsBankersRounding()
        {
            // Spec §11 [verified]: Math.Round is to-even: 0.5 -> 0, 1.5 -> 2, 2.5 -> 2, -0.5 -> 0, -1.5 -> -2, -2.5 -> -2.
            Assert.That(Vector2Int.RoundToInt(new Vector2(0.5F, 1.5F)), Is.EqualTo(new Vector2Int(0, 2)));
            Assert.That(Vector2Int.RoundToInt(new Vector2(2.5F, -0.5F)), Is.EqualTo(new Vector2Int(2, 0)));
            Assert.That(Vector2Int.RoundToInt(new Vector2(-1.5F, -2.5F)), Is.EqualTo(new Vector2Int(-2, -2)));
            Assert.That(Vector2Int.RoundToInt(new Vector2(3.5F, 0.49F)), Is.EqualTo(new Vector2Int(4, 0)));
            Assert.That(Vector2Int.RoundToInt(new Vector2(1.2F, -1.7F)), Is.EqualTo(new Vector2Int(1, -2)));
        }

        [Test]
        public void Vector2Int_Operators_Arithmetic()
        {
            Assert.That(-new Vector2Int(1, -2), Is.EqualTo(new Vector2Int(-1, 2)));
            Assert.That(new Vector2Int(1, 2) + new Vector2Int(3, -5), Is.EqualTo(new Vector2Int(4, -3)));
            Assert.That(new Vector2Int(1, 2) - new Vector2Int(3, -5), Is.EqualTo(new Vector2Int(-2, 7)));
            Assert.That(new Vector2Int(2, 3) * new Vector2Int(4, -5), Is.EqualTo(new Vector2Int(8, -15)));
            Assert.That(3 * new Vector2Int(2, -3), Is.EqualTo(new Vector2Int(6, -9)));
            Assert.That(new Vector2Int(2, -3) * 3, Is.EqualTo(new Vector2Int(6, -9)));
            // Unchecked wrap-around.
            Assert.That(new Vector2Int(int.MaxValue, 0) + new Vector2Int(1, 0), Is.EqualTo(new Vector2Int(int.MinValue, 0)));
            Assert.That(-new Vector2Int(int.MinValue, 0), Is.EqualTo(new Vector2Int(int.MinValue, 0)));
        }

        [Test]
        public void Vector2Int_Operators_IntegerDivisionTruncates()
        {
            Assert.That(new Vector2Int(7, -7) / 2, Is.EqualTo(new Vector2Int(3, -3)));
            Assert.That(new Vector2Int(9, 10) / 3, Is.EqualTo(new Vector2Int(3, 3)));
            Assert.That(new Vector2Int(1, -1) / -1, Is.EqualTo(new Vector2Int(-1, 1)));
            Assert.Throws<DivideByZeroException>(() => { var r = new Vector2Int(1, 2) / 0; });
        }

        [Test]
        public void Vector2Int_Equality_Exact()
        {
            var a = new Vector2Int(1, 2);
            Assert.That(a == new Vector2Int(1, 2), Is.True);
            Assert.That(a != new Vector2Int(1, 2), Is.False);
            Assert.That(a == new Vector2Int(2, 1), Is.False);
            Assert.That(a != new Vector2Int(1, 3), Is.True);
            Assert.That(a.Equals(new Vector2Int(1, 2)), Is.True);
            Assert.That(a.Equals(new Vector2Int(1, 3)), Is.False);
            Assert.That(a.Equals((object)new Vector2Int(1, 2)), Is.True);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals((object)new Vector2(1F, 2F)), Is.False);
            Assert.That(a.Equals("(1, 2)"), Is.False);
        }

        [Test]
        public void Vector2Int_GetHashCode_VerifiedValue()
        {
            // Spec §12 [verified]: (1,2) -> 227871539 = (1 * 73856093) ^ (2 * 83492791) = 73856093 ^ 166985582.
            Assert.That(new Vector2Int(1, 2).GetHashCode(), Is.EqualTo(227871539));
        }

        [Test]
        public void Vector2Int_GetHashCode_Formula_Unchecked()
        {
            Assert.That(Vector2Int.zero.GetHashCode(), Is.EqualTo(0));
            // (1,0) -> 73856093; (0,1) -> 83492791.
            Assert.That(new Vector2Int(1, 0).GetHashCode(), Is.EqualTo(73856093));
            Assert.That(new Vector2Int(0, 1).GetHashCode(), Is.EqualTo(83492791));
            // (3,-4) -> (221568279) ^ (-333971164) = -517153741.
            Assert.That(new Vector2Int(3, -4).GetHashCode(), Is.EqualTo(-517153741));
            // (int.MaxValue,1): the multiply overflows and must wrap (no OverflowException) -> 2137060372.
            Assert.That(new Vector2Int(int.MaxValue, 1).GetHashCode(), Is.EqualTo(2137060372));
            Assert.That(new Vector2Int(int.MinValue, int.MinValue).GetHashCode(), Is.EqualTo(unchecked((int.MinValue * 73856093) ^ (int.MinValue * 83492791))));
            Assert.That(new Vector2Int(1, 2).GetHashCode(), Is.Not.EqualTo(new Vector2Int(2, 1).GetHashCode()));
        }

        [Test]
        public void Vector2Int_Conversions()
        {
            Vector2 v2 = new Vector2Int(3, -4);
            AssertExact(v2, 3F, -4F);
            Vector3Int v3 = (Vector3Int)new Vector2Int(3, -4);
            Assert.That(v3.x, Is.EqualTo(3));
            Assert.That(v3.y, Is.EqualTo(-4));
            Assert.That(v3.z, Is.EqualTo(0));
            // Large ints lose precision through float, as Unity's implicit conversion does.
            Vector2 big = new Vector2Int(16777217, 0);
            Assert.That(big.x, Is.EqualTo(16777216F));

            // Vector2 conversion is implicit, Vector3Int conversion is explicit (spec §12).
            bool implicitToVector2 = false, explicitToVector3Int = false, implicitToVector3Int = false;
            foreach (MethodInfo m in typeof(Vector2Int).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (m.Name == "op_Implicit" && m.ReturnType == typeof(Vector2)) implicitToVector2 = true;
                if (m.Name == "op_Explicit" && m.ReturnType == typeof(Vector3Int)) explicitToVector3Int = true;
                if (m.Name == "op_Implicit" && m.ReturnType == typeof(Vector3Int)) implicitToVector3Int = true;
            }
            Assert.That(implicitToVector2, Is.True);
            Assert.That(explicitToVector3Int, Is.True);
            Assert.That(implicitToVector3Int, Is.False);
        }

        [Test]
        public void Vector2Int_ToString_NoDefaultFormat()
        {
            Assert.That(new Vector2Int(1, 2).ToString(), Is.EqualTo("(1, 2)"));
            Assert.That(new Vector2Int(-3, 0).ToString(), Is.EqualTo("(-3, 0)"));
            Assert.That(new Vector2Int(int.MinValue, int.MaxValue).ToString(), Is.EqualTo("(-2147483648, 2147483647)"));
            // null / empty format pass through to int.ToString -> "G".
            Assert.That(new Vector2Int(1, 2).ToString(null), Is.EqualTo("(1, 2)"));
            Assert.That(new Vector2Int(1, 2).ToString(""), Is.EqualTo("(1, 2)"));
            Assert.That(new Vector2Int(1, 2).ToString(null, null), Is.EqualTo("(1, 2)"));
        }

        [Test]
        public void Vector2Int_ToString_FormatPassesThroughToInt()
        {
            Assert.That(new Vector2Int(1, 2).ToString("D3"), Is.EqualTo("(001, 002)"));
            Assert.That(new Vector2Int(10, 11).ToString("X"), Is.EqualTo("(A, B)"));
            // No "F2" default is injected, but an explicit F2 is honoured by int.ToString.
            Assert.That(new Vector2Int(1, 2).ToString("F2"), Is.EqualTo("(1.00, 2.00)"));
            Assert.That(((IFormattable)new Vector2Int(5, 6)).ToString("D2", null), Is.EqualTo("(05, 06)"));
        }

        [Test]
        public void Vector2Int_ToString_Provider()
        {
            CultureInfo weird = WeirdCulture();
            Assert.That(new Vector2Int(-3, 0).ToString(null, weird.NumberFormat), Is.EqualTo("(~3, 0)"));
            Assert.That(new Vector2Int(-3, 0).ToString("F1", weird), Is.EqualTo("(~3,0, 0,0)"));
            Assert.That(new Vector2Int(-3, 0).ToString("D", CultureInfo.InvariantCulture), Is.EqualTo("(-3, 0)"));
        }

        [Test]
        public void Vector2Int_ToString_NullProvider_IsInvariant_NotCurrentCulture()
        {
            WithCurrentCulture(WeirdCulture(), () =>
            {
                Assert.That(new Vector2Int(-3, 0).ToString(), Is.EqualTo("(-3, 0)"));
                Assert.That(new Vector2Int(-3, 0).ToString("F1"), Is.EqualTo("(-3.0, 0.0)"));
                Assert.That(new Vector2Int(-3, 0).ToString("F1", null), Is.EqualTo("(-3.0, 0.0)"));
            });
        }

        [Test]
        public void Vector2Int_PureMembers_AreReadonly()
        {
            static bool IsReadOnly(MethodInfo m)
            {
                foreach (CustomAttributeData a in m.CustomAttributes)
                    if (a.AttributeType.Name == "IsReadOnlyAttribute") return true;
                return false;
            }
            Type t = typeof(Vector2Int);
            Assert.That(IsReadOnly(t.GetProperty("magnitude").GetMethod), Is.True, "magnitude");
            Assert.That(IsReadOnly(t.GetProperty("sqrMagnitude").GetMethod), Is.True, "sqrMagnitude");
            Assert.That(IsReadOnly(t.GetProperty("x").GetMethod), Is.True, "x getter");
            Assert.That(IsReadOnly(t.GetMethod("GetHashCode")), Is.True, "GetHashCode");
            Assert.That(IsReadOnly(t.GetMethod("Equals", new[] { typeof(Vector2Int) })), Is.True, "Equals(Vector2Int)");
            Assert.That(IsReadOnly(t.GetMethod("ToString", Type.EmptyTypes)), Is.True, "ToString()");
            Assert.That(IsReadOnly(t.GetMethod("Set")), Is.False, "Set");
            Assert.That(IsReadOnly(t.GetMethod("Clamp")), Is.False, "Clamp");
            Assert.That(IsReadOnly(t.GetMethod("Scale", new[] { typeof(Vector2Int) })), Is.False, "Scale(Vector2Int)");
        }
    }
}
