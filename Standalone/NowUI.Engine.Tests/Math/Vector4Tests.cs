// Tests for the UnityEngine.Vector4 (§3) and UnityEngine.Quaternion (§9) shims against
// Docs/Standalone/UnityValueTypeSemantics.md. Every [verified] value in the spec is asserted bit-exactly unless the spec
// itself allows ulp slack (native trig/pow members, §15 "Could not verify" item 2). Formula-only members are checked with
// values worked out by hand from the spec's expression (arithmetic in the comments; all in single precision).
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class Vector4Tests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        private static void AssertBits(Vector4 v, float x, float y, float z, float w)
        {
            Assert.That(Bits(v.x), Is.EqualTo(Bits(x)), "x");
            Assert.That(Bits(v.y), Is.EqualTo(Bits(y)), "y");
            Assert.That(Bits(v.z), Is.EqualTo(Bits(z)), "z");
            Assert.That(Bits(v.w), Is.EqualTo(Bits(w)), "w");
        }

        private static void AssertBits(Quaternion q, float x, float y, float z, float w)
        {
            Assert.That(Bits(q.x), Is.EqualTo(Bits(x)), "x");
            Assert.That(Bits(q.y), Is.EqualTo(Bits(y)), "y");
            Assert.That(Bits(q.z), Is.EqualTo(Bits(z)), "z");
            Assert.That(Bits(q.w), Is.EqualTo(Bits(w)), "w");
        }

        private static void AssertBits(Quaternion actual, Quaternion expected)
        {
            AssertBits(actual, expected.x, expected.y, expected.z, expected.w);
        }

        private static void AssertBits(Vector3 v, float x, float y, float z)
        {
            Assert.That(Bits(v.x), Is.EqualTo(Bits(x)), "x");
            Assert.That(Bits(v.y), Is.EqualTo(Bits(y)), "y");
            Assert.That(Bits(v.z), Is.EqualTo(Bits(z)), "z");
        }

        // Native members (spec §15 item 2): "exact reproduction is not achievable from the outside; golden tests should
        // tolerate these" — trig/pow last-ulp deviations of 1–3 ulp.
        private static void AssertUlps(Quaternion q, float x, float y, float z, float w, int ulps)
        {
            Assert.That(q.x, Is.EqualTo(x).Within(ulps).Ulps, "x");
            Assert.That(q.y, Is.EqualTo(y).Within(ulps).Ulps, "y");
            Assert.That(q.z, Is.EqualTo(z).Within(ulps).Ulps, "z");
            Assert.That(q.w, Is.EqualTo(w).Within(ulps).Ulps, "w");
        }

        private static void AssertUlps(Vector3 v, float x, float y, float z, int ulps)
        {
            Assert.That(v.x, Is.EqualTo(x).Within(ulps).Ulps, "x");
            Assert.That(v.y, Is.EqualTo(y).Within(ulps).Ulps, "y");
            Assert.That(v.z, Is.EqualTo(z).Within(ulps).Ulps, "z");
        }

        // ==================================================================================================== Vector4 (§3)

        // ------------------------------------------------------------------ §0/§3: shape

        [Test]
        public void Vector4_StructShape()
        {
            Type t = typeof(Vector4);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Vector4>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);

            // Exactly four public float instance fields in x, y, z, w order; no extra instance state (native float* reinterpretation).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(4));
            Assert.That(fields[0].Name, Is.EqualTo("x"));
            Assert.That(fields[1].Name, Is.EqualTo("y"));
            Assert.That(fields[2].Name, Is.EqualTo("z"));
            Assert.That(fields[3].Name, Is.EqualTo("w"));
            Assert.That(Array.TrueForAll(fields, f => f.FieldType == typeof(float) && f.IsPublic), Is.True);
            Assert.That(Marshal.SizeOf<Vector4>(), Is.EqualTo(16));
        }

        [Test]
        public void Vector4_kEpsilon_And_NoKEpsilonNormalSqrt()
        {
            Assert.That(Vector4.kEpsilon, Is.EqualTo(0.00001F));
            Assert.That(Bits(Vector4.kEpsilon), Is.EqualTo(Bits(0.00001F)));
            Assert.That(typeof(Vector4).GetField("kEpsilonNormalSqrt", BindingFlags.Public | BindingFlags.Static), Is.Null);
        }

        [Test]
        public void Vector4_HasNoVectorByVectorOperators()
        {
            // Spec §1: Vector3/Vector4 do NOT have vector×vector or vector÷vector operators.
            foreach (MethodInfo m in typeof(Vector4).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "op_Multiply" && m.Name != "op_Division") continue;
                ParameterInfo[] p = m.GetParameters();
                Assert.That(p[0].ParameterType == typeof(Vector4) && p[1].ParameterType == typeof(Vector4), Is.False, m.ToString());
            }
        }

        [Test]
        public void Vector4_HasOnlyTheFourSpecPresets()
        {
            // Spec §3: zero, one, positiveInfinity, negativeInfinity (no up/down/left/right/forward/back).
            foreach (string name in new[] { "up", "down", "left", "right", "forward", "back" })
                Assert.That(typeof(Vector4).GetProperty(name, BindingFlags.Public | BindingFlags.Static), Is.Null, name);
            foreach (string name in new[] { "zero", "one", "positiveInfinity", "negativeInfinity" })
                Assert.That(typeof(Vector4).GetProperty(name, BindingFlags.Public | BindingFlags.Static), Is.Not.Null, name);
        }

        // ------------------------------------------------------------------ constructors

        [Test]
        public void Vector4_Constructors()
        {
            AssertBits(new Vector4(1F, 2F, 3F, 4F), 1F, 2F, 3F, 4F);
            AssertBits(new Vector4(1F, 2F, 3F), 1F, 2F, 3F, 0F);
            AssertBits(new Vector4(1F, 2F), 1F, 2F, 0F, 0F);
            AssertBits(default(Vector4), 0F, 0F, 0F, 0F);
        }

        // ------------------------------------------------------------------ indexer

        [Test]
        public void Vector4_Indexer_Get()
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(v[0], Is.EqualTo(1F));
            Assert.That(v[1], Is.EqualTo(2F));
            Assert.That(v[2], Is.EqualTo(3F));
            Assert.That(v[3], Is.EqualTo(4F));
        }

        [Test]
        public void Vector4_Indexer_Set()
        {
            var v = new Vector4();
            v[0] = 5F; v[1] = 6F; v[2] = 7F; v[3] = 8F;
            AssertBits(v, 5F, 6F, 7F, 8F);
        }

        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void Vector4_Indexer_InvalidIndexThrows(int index)
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { float f = v[index]; });
            Assert.That(getEx.Message, Is.EqualTo("Invalid Vector4 index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { v[index] = 1F; });
            Assert.That(setEx.Message, Is.EqualTo("Invalid Vector4 index!"));
        }

        // ------------------------------------------------------------------ presets

        [Test]
        public void Vector4_Presets()
        {
            AssertBits(Vector4.zero, 0F, 0F, 0F, 0F);
            AssertBits(Vector4.one, 1F, 1F, 1F, 1F);
            AssertBits(Vector4.positiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            AssertBits(Vector4.negativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        }

        [Test]
        public void Vector4_PresetsAreCopies()
        {
            // Mutating the returned value must not affect the preset.
            Vector4 z = Vector4.zero;
            z.x = 42F;
            Assert.That(Vector4.zero.x, Is.EqualTo(0F));
        }

        // ------------------------------------------------------------------ instance members

        [Test]
        public void Vector4_Set()
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            v.Set(5F, 6F, 7F, 8F);
            AssertBits(v, 5F, 6F, 7F, 8F);
        }

        [Test]
        public void Vector4_Scale_Instance()
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            v.Scale(new Vector4(2F, 3F, 4F, 5F));
            AssertBits(v, 2F, 6F, 12F, 20F);
            var w = new Vector4(1F, 2F, 3F, 4F);
            Vector4 s = new Vector4(-1F, 0.5F, 0F, -2F);
            w.Scale(in s);
            AssertBits(w, -1F, 1F, 0F, -8F);
        }

        [Test]
        public void Vector4_Magnitude_IsFloatCastOfDoubleSqrtOfDot()
        {
            // Dot((1,2,3,4),(1,2,3,4)) = 1+4+9+16 = 30; (float)Math.Sqrt(30) = 5.477226 [0x40AF456F].
            var v = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(Bits(v.magnitude), Is.EqualTo(0x40AF456F));
            Assert.That(v.magnitude, Is.EqualTo((float)Math.Sqrt(30F)));
            Assert.That(v.sqrMagnitude, Is.EqualTo(30F));
            Assert.That(v.SqrMagnitude(), Is.EqualTo(30F));
            Assert.That(Vector4.Magnitude(v), Is.EqualTo(v.magnitude));
            Assert.That(Vector4.Magnitude(in v), Is.EqualTo(v.magnitude));
            Assert.That(Vector4.SqrMagnitude(v), Is.EqualTo(30F));
            Assert.That(Vector4.SqrMagnitude(in v), Is.EqualTo(30F));
            // (3,4,0,0): sqrt(25) = 5 exactly.
            Assert.That(new Vector4(3F, 4F).magnitude, Is.EqualTo(5F));
        }

        [Test]
        public void Vector4_Normalize_Instance_AboveEpsilon()
        {
            // mag = 5.477226; 1/mag = 0.18257418 [0x3E3AF4BA], 2/mag = 0.36514837 [0x3EBAF4BA],
            // 3/mag = 0.5477225 [0x3F0C378B], 4/mag = 0.73029673 [0x3F3AF4BA].
            var v = new Vector4(1F, 2F, 3F, 4F);
            v.Normalize();
            Assert.That(Bits(v.x), Is.EqualTo(0x3E3AF4BA));
            Assert.That(Bits(v.y), Is.EqualTo(0x3EBAF4BA));
            Assert.That(Bits(v.z), Is.EqualTo(0x3F0C378B));
            Assert.That(Bits(v.w), Is.EqualTo(0x3F3AF4BA));

            var u = new Vector4(3F, 4F, 0F, 0F);
            u.Normalize();
            AssertBits(u, 0.6F, 0.8F, 0F, 0F);
        }

        [Test]
        public void Vector4_Normalize_AtOrBelowEpsilonGivesZero()
        {
            // mag must be strictly > kEpsilon: sqrt(1e-5 * 1e-5) = 1e-5 exactly [0x3727C5AC] → not greater → zero.
            var v = new Vector4(0.00001F, 0F, 0F, 0F);
            v.Normalize();
            AssertBits(v, 0F, 0F, 0F, 0F);
            var z = Vector4.zero;
            z.Normalize();
            AssertBits(z, 0F, 0F, 0F, 0F);
            AssertBits(Vector4.Normalize(new Vector4(0F, 0F, 0F, 0.00001F)), 0F, 0F, 0F, 0F);
            AssertBits(new Vector4(0F, 0.000005F, 0F, 0F).normalized, 0F, 0F, 0F, 0F);
        }

        [Test]
        public void Vector4_Normalize_JustAboveEpsilon()
        {
            // sqrt(2e-5 * 2e-5) = 2e-5 > 1e-5 → divide: (0,0,2e-5,0)/2e-5 = (0,0,1,0).
            AssertBits(Vector4.Normalize(new Vector4(0F, 0F, 0.00002F, 0F)), 0F, 0F, 1F, 0F);
        }

        [Test]
        public void Vector4_normalized_And_StaticNormalize_AgreeWithInstance()
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            Vector4 copy = v;
            copy.Normalize();
            Assert.That(v.normalized.Equals(copy), Is.True);
            Assert.That(Vector4.Normalize(v).Equals(copy), Is.True);
            Assert.That(Vector4.Normalize(in v).Equals(copy), Is.True);
            // Original untouched.
            AssertBits(v, 1F, 2F, 3F, 4F);
        }

        [Test]
        public void Vector4_GetHashCode_VerifiedValue()
        {
            // Spec §3 [verified]: (1,2,3,4) → 265289728.
            Assert.That(new Vector4(1F, 2F, 3F, 4F).GetHashCode(), Is.EqualTo(265289728));
        }

        [Test]
        public void Vector4_GetHashCode_Formula()
        {
            // x ^ (y << 2) ^ (z >> 2) ^ (w >> 1) of float.GetHashCode: for (5,6,7,8) = 1930952704 (computed with the formula).
            Assert.That(new Vector4(5F, 6F, 7F, 8F).GetHashCode(), Is.EqualTo(1930952704));
            float x = -1.5F, y = 0.25F, z = 1e-3F, w = -7F;
            int expected = x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
            Assert.That(new Vector4(x, y, z, w).GetHashCode(), Is.EqualTo(expected));
            // (1,0,0,0) hashes to the raw bits of 1f.
            Assert.That(new Vector4(1F, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0x3F800000));
        }

        [Test]
        public void Vector4_GetHashCode_MatchesColorForSameComponents()
        {
            // Spec §4: Color(1,2,3,4) hashes identically to Vector4(1,2,3,4).
            Assert.That(new Color(1F, 2F, 3F, 4F).GetHashCode(), Is.EqualTo(new Vector4(1F, 2F, 3F, 4F).GetHashCode()));
        }

        [Test]
        public void Vector4_Equals_ExactAndNaNUnequal()
        {
            var a = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(a.Equals(new Vector4(1F, 2F, 3F, 4F)), Is.True);
            Assert.That(a.Equals(new Vector4(1F, 2F, 3F, 4.0000005F)), Is.False);
            Assert.That(a.Equals((object)new Vector4(1F, 2F, 3F, 4F)), Is.True);
            Assert.That(a.Equals((object)new Vector3(1F, 2F, 3F)), Is.False);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals("(1, 2, 3, 4)"), Is.False);

            // Spec §0: Vector types use C# == (NaN ≠ NaN), unlike Color/Rect/Quaternion.
            var nan = new Vector4(float.NaN, 0F, 0F, 0F);
            Assert.That(nan.Equals(nan), Is.False);
            Assert.That(nan.Equals(in nan), Is.False);
            Assert.That(nan.Equals((object)nan), Is.False);
            var n2 = new Vector4(0F, 0F, 0F, float.NaN);
            Assert.That(n2.Equals(n2), Is.False);

            // -0 == +0 under C# ==.
            Assert.That(new Vector4(-0F, 0F, 0F, 0F).Equals(new Vector4(0F, 0F, 0F, 0F)), Is.True);
            Vector4 b = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(a.Equals(in b), Is.True);
        }

        [Test]
        public void Vector4_ToString_Default()
        {
            Assert.That(new Vector4(1F, 2F, 3F, 4F).ToString(), Is.EqualTo("(1.00, 2.00, 3.00, 4.00)"));
            Assert.That(new Vector4(-0.5F, 1234.5678F, 0F, -1F).ToString(), Is.EqualTo("(-0.50, 1234.57, 0.00, -1.00)"));
            Assert.That(Vector4.zero.ToString(), Is.EqualTo("(0.00, 0.00, 0.00, 0.00)"));
            Assert.That(new Vector4(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0F).ToString(), Is.EqualTo("(NaN, Infinity, -Infinity, 0.00)"));
        }

        [Test]
        public void Vector4_ToString_NullOrEmptyFormatUsesF2()
        {
            var v = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(v.ToString(null), Is.EqualTo("(1.00, 2.00, 3.00, 4.00)"));
            Assert.That(v.ToString(""), Is.EqualTo("(1.00, 2.00, 3.00, 4.00)"));
            Assert.That(v.ToString(null, null), Is.EqualTo("(1.00, 2.00, 3.00, 4.00)"));
            Assert.That(v.ToString("", CultureInfo.InvariantCulture), Is.EqualTo("(1.00, 2.00, 3.00, 4.00)"));
        }

        [Test]
        public void Vector4_ToString_FormatAndProvider()
        {
            var v = new Vector4(1.5F, 2F, 3F, 4F);
            Assert.That(v.ToString("F0"), Is.EqualTo("(2, 2, 3, 4)"));
            Assert.That(v.ToString("F3"), Is.EqualTo("(1.500, 2.000, 3.000, 4.000)"));
            Assert.That(v.ToString("0.0"), Is.EqualTo("(1.5, 2.0, 3.0, 4.0)"));
            // Provider is passed through to the component formatting: de-DE uses a decimal comma.
            var de = new CultureInfo("de-DE");
            Assert.That(v.ToString("F2", de), Is.EqualTo("(1,50, 2,00, 3,00, 4,00)"));
            Assert.That(v.ToString(null, de), Is.EqualTo("(1,50, 2,00, 3,00, 4,00)"));
            // Null provider → InvariantCulture.NumberFormat even under a comma-decimal current culture.
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = de;
                Assert.That(v.ToString(), Is.EqualTo("(1.50, 2.00, 3.00, 4.00)"));
                Assert.That(v.ToString("F1", null), Is.EqualTo("(1.5, 2.0, 3.0, 4.0)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
            // IFormattable dispatch.
            Assert.That(((IFormattable)v).ToString("F1", CultureInfo.InvariantCulture), Is.EqualTo("(1.5, 2.0, 3.0, 4.0)"));
        }

        // ------------------------------------------------------------------ static members

        [Test]
        public void Vector4_Lerp_Clamped()
        {
            var a = new Vector4(0F, 10F, -10F, 1F);
            var b = new Vector4(10F, 20F, 10F, 3F);
            // a + (b - a) * 0.25: (0+10*0.25, 10+10*0.25, -10+20*0.25, 1+2*0.25) = (2.5, 12.5, -5, 1.5).
            AssertBits(Vector4.Lerp(a, b, 0.25F), 2.5F, 12.5F, -5F, 1.5F);
            AssertBits(Vector4.Lerp(in a, in b, 0.25F), 2.5F, 12.5F, -5F, 1.5F);
            AssertBits(Vector4.Lerp(a, b, 0F), 0F, 10F, -10F, 1F);
            AssertBits(Vector4.Lerp(a, b, 1F), 10F, 20F, 10F, 3F);
            // Clamp01: t < 0 → a, t > 1 → b.
            AssertBits(Vector4.Lerp(a, b, -1F), 0F, 10F, -10F, 1F);
            AssertBits(Vector4.Lerp(a, b, 2F), 10F, 20F, 10F, 3F);
            AssertBits(Vector4.Lerp(in a, in b, 2F), 10F, 20F, 10F, 3F);
        }

        [Test]
        public void Vector4_Lerp_UsesAPlusDeltaTimesTForm()
        {
            // a + (b - a) * t with a = 1e8, b = -1e8, t = 0.5: (b - a) = -2e8, * 0.5 = -1e8, + 1e8 = 0 exactly.
            AssertBits(Vector4.Lerp(new Vector4(1e8F, 0F, 0F, 0F), new Vector4(-1e8F, 0F, 0F, 0F), 0.5F), 0F, 0F, 0F, 0F);
            // NaN t passes through Clamp01 unchanged (spec §11) → NaN components.
            Vector4 n = Vector4.Lerp(Vector4.zero, Vector4.one, float.NaN);
            Assert.That(float.IsNaN(n.x) && float.IsNaN(n.y) && float.IsNaN(n.z) && float.IsNaN(n.w), Is.True);
        }

        [Test]
        public void Vector4_LerpUnclamped()
        {
            var a = new Vector4(0F, 10F, -10F, 1F);
            var b = new Vector4(10F, 20F, 10F, 3F);
            AssertBits(Vector4.LerpUnclamped(a, b, 0.25F), 2.5F, 12.5F, -5F, 1.5F);
            // t = 2: (0+10*2, 10+10*2, -10+20*2, 1+2*2) = (20, 30, 30, 5); t = -1: (-10, 0, -30, -1).
            AssertBits(Vector4.LerpUnclamped(a, b, 2F), 20F, 30F, 30F, 5F);
            AssertBits(Vector4.LerpUnclamped(a, b, -1F), -10F, 0F, -30F, -1F);
            AssertBits(Vector4.LerpUnclamped(in a, in b, -1F), -10F, 0F, -30F, -1F);
        }

        [Test]
        public void Vector4_MoveTowards_ReturnsTargetWhenReachable()
        {
            var current = new Vector4(1F, 1F, 1F, 1F);
            var target = new Vector4(2F, 3F, 4F, 5F);
            // sqDist = 1+4+9+16 = 30; maxDelta = 6 → 36 >= 30 → target.
            AssertBits(Vector4.MoveTowards(current, target, 6F), 2F, 3F, 4F, 5F);
            AssertBits(Vector4.MoveTowards(in current, in target, 6F), 2F, 3F, 4F, 5F);
            // sqDist == 0 → target regardless of maxDelta (even negative).
            AssertBits(Vector4.MoveTowards(target, target, -5F), 2F, 3F, 4F, 5F);
            AssertBits(Vector4.MoveTowards(target, target, 0F), 2F, 3F, 4F, 5F);
            // maxDelta² exactly equal to sqDist: (3,4,0,0) from zero, delta 5 → 25 <= 25 → target.
            AssertBits(Vector4.MoveTowards(Vector4.zero, new Vector4(3F, 4F), 5F), 3F, 4F, 0F, 0F);
        }

        [Test]
        public void Vector4_MoveTowards_PartialStep()
        {
            // to = (3,4,0,0); sqDist = 25; dist = 5; current + to/dist*1 = (0.6, 0.8, 0, 0).
            AssertBits(Vector4.MoveTowards(Vector4.zero, new Vector4(3F, 4F), 1F), 0.6F, 0.8F, 0F, 0F);
            // to = (1,2,3,4); dist = 5.477226 [0x40AF456F]; 1 + i/dist*0.5:
            // 1.0912871 [0x3F8BAF4C], 1.1825742 [0x3F975E97], 1.2738613 [0x3FA30DE3], 1.3651483 [0x3FAEBD2E].
            Vector4 r = Vector4.MoveTowards(new Vector4(1F, 1F, 1F, 1F), new Vector4(2F, 3F, 4F, 5F), 0.5F);
            Assert.That(Bits(r.x), Is.EqualTo(0x3F8BAF4C));
            Assert.That(Bits(r.y), Is.EqualTo(0x3F975E97));
            Assert.That(Bits(r.z), Is.EqualTo(0x3FA30DE3));
            Assert.That(Bits(r.w), Is.EqualTo(0x3FAEBD2E));
        }

        [Test]
        public void Vector4_MoveTowards_NegativeDeltaMovesAway()
        {
            // Spec §1 [verified for Vector2]: MoveTowards(0,(10,0),-3) = (-3,0); 4-component version of the same algorithm.
            // maxDistanceDelta < 0 skips the "reachable" test: dist = 10; 0 + 10/10*-3 = -3.
            AssertBits(Vector4.MoveTowards(Vector4.zero, new Vector4(10F, 0F, 0F, 0F), -3F), -3F, 0F, 0F, 0F);
            AssertBits(Vector4.MoveTowards(Vector4.zero, new Vector4(0F, 0F, 0F, 10F), -3F), 0F, 0F, 0F, -3F);
        }

        [Test]
        public void Vector4_Scale_Static()
        {
            var a = new Vector4(1F, 2F, 3F, 4F);
            var b = new Vector4(2F, -3F, 0.5F, 0F);
            AssertBits(Vector4.Scale(a, b), 2F, -6F, 1.5F, 0F);
            AssertBits(Vector4.Scale(in a, in b), 2F, -6F, 1.5F, 0F);
        }

        [Test]
        public void Vector4_Dot()
        {
            var a = new Vector4(1F, 2F, 3F, 4F);
            var b = new Vector4(5F, 6F, 7F, 8F);
            // 5 + 12 + 21 + 32 = 70.
            Assert.That(Vector4.Dot(a, b), Is.EqualTo(70F));
            Assert.That(Vector4.Dot(in a, in b), Is.EqualTo(70F));
            Assert.That(Vector4.Dot(a, Vector4.zero), Is.EqualTo(0F));
            Assert.That(Vector4.Dot(new Vector4(1F, 0F, 0F, 0F), new Vector4(0F, 0F, 0F, 1F)), Is.EqualTo(0F));
            // Left-to-right float sum order: ((x + y) + z) + w.
            var c = new Vector4(1e8F, 1F, 1F, -1e8F);
            var one = Vector4.one;
            // 1e8 + 1 = 1e8 (absorbed), + 1 = 1e8, + -1e8 = 0.
            Assert.That(Vector4.Dot(c, one), Is.EqualTo(0F));
        }

        [Test]
        public void Vector4_Project()
        {
            // b * (Dot(a,b) / Dot(b,b)): a = (1,2,3,4), b = (0,0,0,2): 8/4 = 2 → (0,0,0,4).
            AssertBits(Vector4.Project(new Vector4(1F, 2F, 3F, 4F), new Vector4(0F, 0F, 0F, 2F)), 0F, 0F, 0F, 4F);
            // b = (1,1,0,0): Dot = 3, Dot(b,b) = 2 → 1.5 → (1.5, 1.5, 0, 0).
            var a = new Vector4(1F, 2F, 3F, 4F);
            var b = new Vector4(1F, 1F, 0F, 0F);
            AssertBits(Vector4.Project(a, b), 1.5F, 1.5F, 0F, 0F);
            AssertBits(Vector4.Project(in a, in b), 1.5F, 1.5F, 0F, 0F);
        }

        [Test]
        public void Vector4_Project_ZeroBGivesNaN()
        {
            // Spec §3: no zero guard — 0/0 = NaN, then 0 * NaN = NaN on every component.
            Vector4 p = Vector4.Project(new Vector4(1F, 2F, 3F, 4F), Vector4.zero);
            Assert.That(float.IsNaN(p.x) && float.IsNaN(p.y) && float.IsNaN(p.z) && float.IsNaN(p.w), Is.True);
        }

        [Test]
        public void Vector4_Distance()
        {
            // Magnitude(a - b): (1,2,3,4) - (5,6,7,8) = (-4,-4,-4,-4); sqrt(64) = 8.
            var a = new Vector4(1F, 2F, 3F, 4F);
            var b = new Vector4(5F, 6F, 7F, 8F);
            Assert.That(Vector4.Distance(a, b), Is.EqualTo(8F));
            Assert.That(Vector4.Distance(in a, in b), Is.EqualTo(8F));
            Assert.That(Vector4.Distance(b, a), Is.EqualTo(8F));
            Assert.That(Vector4.Distance(a, a), Is.EqualTo(0F));
            // (0,0,0,0) to (1,2,3,4): sqrt(30) = 5.477226 [0x40AF456F].
            Assert.That(Bits(Vector4.Distance(Vector4.zero, a)), Is.EqualTo(0x40AF456F));
        }

        [Test]
        public void Vector4_MinMax_Componentwise()
        {
            var a = new Vector4(1F, 5F, -3F, 0F);
            var b = new Vector4(2F, 4F, -4F, 0F);
            AssertBits(Vector4.Min(a, b), 1F, 4F, -4F, 0F);
            AssertBits(Vector4.Max(a, b), 2F, 5F, -3F, 0F);
            AssertBits(Vector4.Min(in a, in b), 1F, 4F, -4F, 0F);
            AssertBits(Vector4.Max(in a, in b), 2F, 5F, -3F, 0F);
        }

        [Test]
        public void Vector4_MinMax_NaNOrderingFollowsMathf()
        {
            // Spec §11 [verified]: Min(NaN,1) = 1, Min(1,NaN) = NaN, Max(NaN,1) = 1, Max(1,NaN) = NaN.
            var nan = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            var one = Vector4.one;
            AssertBits(Vector4.Min(nan, one), 1F, 1F, 1F, 1F);
            AssertBits(Vector4.Max(nan, one), 1F, 1F, 1F, 1F);
            Vector4 m1 = Vector4.Min(one, nan);
            Vector4 m2 = Vector4.Max(one, nan);
            Assert.That(float.IsNaN(m1.x) && float.IsNaN(m1.y) && float.IsNaN(m1.z) && float.IsNaN(m1.w), Is.True);
            Assert.That(float.IsNaN(m2.x) && float.IsNaN(m2.y) && float.IsNaN(m2.z) && float.IsNaN(m2.w), Is.True);
        }

        // ------------------------------------------------------------------ operators

        [Test]
        public void Vector4_ArithmeticOperators()
        {
            var a = new Vector4(1F, 2F, 3F, 4F);
            var b = new Vector4(5F, 6F, 7F, 8F);
            AssertBits(a + b, 6F, 8F, 10F, 12F);
            AssertBits(a - b, -4F, -4F, -4F, -4F);
            AssertBits(-a, -1F, -2F, -3F, -4F);
            AssertBits(a * 2F, 2F, 4F, 6F, 8F);
            AssertBits(2F * a, 2F, 4F, 6F, 8F);
            AssertBits(a / 2F, 0.5F, 1F, 1.5F, 2F);
            AssertBits(a * 0.5F, 0.5F, 1F, 1.5F, 2F);
        }

        [Test]
        public void Vector4_UnaryMinusOnZeroGivesNegativeZero()
        {
            Vector4 n = -Vector4.zero;
            Assert.That(Bits(n.x), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(n.w), Is.EqualTo(NegativeZeroBits));
        }

        [Test]
        public void Vector4_DivisionByZeroFollowsIEEE()
        {
            Vector4 r = new Vector4(1F, -1F, 0F, 2F) / 0F;
            Assert.That(r.x, Is.EqualTo(float.PositiveInfinity));
            Assert.That(r.y, Is.EqualTo(float.NegativeInfinity));
            Assert.That(float.IsNaN(r.z), Is.True);
            Assert.That(r.w, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Vector4_EqualityOperator_Tolerance()
        {
            // Spec §1 [verified for Vector2, same constants]: zero == (1e-6,0) true; zero == (1e-5,0) false.
            // kEpsilon*kEpsilon = 9.9999994e-11 [0x2EDBE6FE]; (1e-6)² = 1e-12 < that; (1e-5)² == that → not less.
            Assert.That(Vector4.zero == new Vector4(1e-6F, 0F, 0F, 0F), Is.True);
            Assert.That(Vector4.zero == new Vector4(0F, 0F, 0F, 1e-6F), Is.True);
            Assert.That(Vector4.zero == new Vector4(1e-5F, 0F, 0F, 0F), Is.False);
            Assert.That(Vector4.zero == new Vector4(0F, 0F, 0F, 1e-5F), Is.False);
            Assert.That(Vector4.zero != new Vector4(1e-6F, 0F, 0F, 0F), Is.False);
            Assert.That(Vector4.zero != new Vector4(1e-5F, 0F, 0F, 0F), Is.True);
            // All four components contribute: 2 * (0.7e-5)² = 9.8e-11 < 9.9999994e-11 → equal.
            Assert.That(Vector4.zero == new Vector4(0.7e-5F, 0F, 0F, 0.7e-5F), Is.True);
            // 4 * (0.6e-5)² = 1.44e-10 → not equal.
            Assert.That(Vector4.zero == new Vector4(0.6e-5F, 0.6e-5F, 0.6e-5F, 0.6e-5F), Is.False);
            Assert.That(new Vector4(1F, 2F, 3F, 4F) == new Vector4(1F, 2F, 3F, 4F), Is.True);
        }

        [Test]
        public void Vector4_EqualityOperator_NaNAndInfinity()
        {
            var nan = new Vector4(float.NaN, 0F, 0F, 0F);
            Assert.That(nan == nan, Is.False);
            Assert.That(nan != nan, Is.True);
            // ∞ - ∞ = NaN → false even for identical infinite vectors.
            Assert.That(Vector4.positiveInfinity == Vector4.positiveInfinity, Is.False);
            Assert.That(Vector4.positiveInfinity != Vector4.positiveInfinity, Is.True);
        }

        // ------------------------------------------------------------------ conversions

        [Test]
        public void Vector4_ImplicitConversions_Vector3()
        {
            Vector4 v4 = new Vector3(1F, 2F, 3F);
            AssertBits(v4, 1F, 2F, 3F, 0F);
            Vector3 v3 = new Vector4(1F, 2F, 3F, 4F);
            AssertBits(v3, 1F, 2F, 3F);
        }

        [Test]
        public void Vector4_ImplicitConversions_Vector2()
        {
            Vector4 v4 = new Vector2(1F, 2F);
            AssertBits(v4, 1F, 2F, 0F, 0F);
            Vector2 v2 = new Vector4(1F, 2F, 3F, 4F);
            Assert.That(Bits(v2.x), Is.EqualTo(Bits(1F)));
            Assert.That(Bits(v2.y), Is.EqualTo(Bits(2F)));
        }

        [Test]
        public void Vector4_ConversionsAreDeclaredOnVector4()
        {
            // Spec §3: the Vector2/Vector3 ↔ Vector4 conversions are declared on Vector4.
            MethodInfo[] ops = typeof(Vector4).GetMethods(BindingFlags.Public | BindingFlags.Static);
            int count = 0;
            foreach (MethodInfo m in ops)
                if (m.Name == "op_Implicit") count++;
            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public void Vector4_ColorConversions_RoundTrip()
        {
            // Spec §4: implicit Vector4(Color) → (r,g,b,a); implicit Color(Vector4) → (x,y,z,w).
            Vector4 v = new Color(0.1F, 0.2F, 0.3F, 0.4F);
            AssertBits(v, 0.1F, 0.2F, 0.3F, 0.4F);
            Color c = new Vector4(0.5F, 0.6F, 0.7F, 0.8F);
            Assert.That(Bits(c.r), Is.EqualTo(Bits(0.5F)));
            Assert.That(Bits(c.g), Is.EqualTo(Bits(0.6F)));
            Assert.That(Bits(c.b), Is.EqualTo(Bits(0.7F)));
            Assert.That(Bits(c.a), Is.EqualTo(Bits(0.8F)));
        }

        // ==================================================================================================== Quaternion (§9)

        // ------------------------------------------------------------------ shape / constants / presets

        [Test]
        public void Quaternion_StructShape()
        {
            Type t = typeof(Quaternion);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Quaternion>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(4));
            Assert.That(fields[0].Name, Is.EqualTo("x"));
            Assert.That(fields[1].Name, Is.EqualTo("y"));
            Assert.That(fields[2].Name, Is.EqualTo("z"));
            Assert.That(fields[3].Name, Is.EqualTo("w"));
            Assert.That(Array.TrueForAll(fields, f => f.FieldType == typeof(float) && f.IsPublic), Is.True);
            Assert.That(Marshal.SizeOf<Quaternion>(), Is.EqualTo(16));
        }

        [Test]
        public void Quaternion_kEpsilon()
        {
            // Spec §9: 1e-6, ten times smaller than the vector epsilon.
            Assert.That(Bits(Quaternion.kEpsilon), Is.EqualTo(Bits(0.000001F)));
            Assert.That(Quaternion.kEpsilon * 10F, Is.EqualTo(Vector4.kEpsilon).Within(1).Ulps);
        }

        [Test]
        public void Quaternion_Identity()
        {
            AssertBits(Quaternion.identity, 0F, 0F, 0F, 1F);
            Quaternion q = Quaternion.identity;
            q.x = 5F;
            Assert.That(Quaternion.identity.x, Is.EqualTo(0F));
        }

        [Test]
        public void Quaternion_ConstructorAndSet()
        {
            var q = new Quaternion(1F, 2F, 3F, 4F);
            AssertBits(q, 1F, 2F, 3F, 4F);
            q.Set(5F, 6F, 7F, 8F);
            AssertBits(q, 5F, 6F, 7F, 8F);
            AssertBits(default(Quaternion), 0F, 0F, 0F, 0F);
        }

        [Test]
        public void Quaternion_Indexer()
        {
            var q = new Quaternion(1F, 2F, 3F, 4F);
            Assert.That(q[0], Is.EqualTo(1F));
            Assert.That(q[1], Is.EqualTo(2F));
            Assert.That(q[2], Is.EqualTo(3F));
            Assert.That(q[3], Is.EqualTo(4F));
            q[0] = 5F; q[1] = 6F; q[2] = 7F; q[3] = 8F;
            AssertBits(q, 5F, 6F, 7F, 8F);
        }

        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(100)]
        public void Quaternion_Indexer_InvalidIndexThrows(int index)
        {
            var q = Quaternion.identity;
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { float f = q[index]; });
            Assert.That(getEx.Message, Is.EqualTo("Invalid Quaternion index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { q[index] = 1F; });
            Assert.That(setEx.Message, Is.EqualTo("Invalid Quaternion index!"));
        }

        // ------------------------------------------------------------------ operators

        [Test]
        public void Quaternion_HamiltonProduct()
        {
            // lhs = (1,2,3,4), rhs = (5,6,7,8):
            // x = lw*rx + lx*rw + ly*rz - lz*ry = 4*5 + 1*8 + 2*7 - 3*6 = 20+8+14-18 = 24
            // y = lw*ry + ly*rw + lz*rx - lx*rz = 4*6 + 2*8 + 3*5 - 1*7 = 24+16+15-7 = 48
            // z = lw*rz + lz*rw + lx*ry - ly*rx = 4*7 + 3*8 + 1*6 - 2*5 = 28+24+6-10 = 48
            // w = lw*rw - lx*rx - ly*ry - lz*rz = 32 - 5 - 12 - 21 = -6
            AssertBits(new Quaternion(1F, 2F, 3F, 4F) * new Quaternion(5F, 6F, 7F, 8F), 24F, 48F, 48F, -6F);
            // Reverse order differs (non-commutative): (5,6,7,8)*(1,2,3,4):
            // x = 8*1 + 5*4 + 6*3 - 7*2 = 8+20+18-14 = 32; y = 8*2 + 6*4 + 7*1 - 5*3 = 16+24+7-15 = 32
            // z = 8*3 + 7*4 + 5*2 - 6*1 = 24+28+10-6 = 56; w = -6
            AssertBits(new Quaternion(5F, 6F, 7F, 8F) * new Quaternion(1F, 2F, 3F, 4F), 32F, 32F, 56F, -6F);
            // Identity is neutral on both sides.
            var q = new Quaternion(0.1F, 0.2F, 0.3F, 0.4F);
            AssertBits(q * Quaternion.identity, 0.1F, 0.2F, 0.3F, 0.4F);
            AssertBits(Quaternion.identity * q, 0.1F, 0.2F, 0.3F, 0.4F);
        }

        [Test]
        public void Quaternion_TimesVector3_Verified()
        {
            // Spec §9 [verified]: Euler(0,90,0) * forward = (0.99999994, 0, 5.96e-8).
            // 5.96e-8 printed at 3 digits: the float in that range produced by the formula is 2^-24 = 5.9604645e-8 [0x33800000].
            Vector3 r = Quaternion.Euler(0F, 90F, 0F) * Vector3.forward;
            Assert.That(Bits(r.x), Is.EqualTo(Bits(0.99999994F)));
            Assert.That(Bits(r.y), Is.EqualTo(0));
            Assert.That(r.z, Is.EqualTo(5.96e-8F).Within(1e-9F));
            Assert.That(Bits(r.z), Is.EqualTo(0x33800000));
        }

        [Test]
        public void Quaternion_TimesVector3_Formula()
        {
            // q = (0,1,0,0) (180° about y): y2 = 2, yy = 2, all other products 0.
            // res.x = (1 - (yy+zz))*p.x = -p.x; res.y = (1 - (xx+zz))*p.y = p.y; res.z = (1 - (xx+yy))*p.z = -p.z.
            AssertBits(new Quaternion(0F, 1F, 0F, 0F) * new Vector3(1F, 2F, 3F), -1F, 2F, -3F);
            // q = (1,0,0,0): x2 = 2, xx = 2 → (p.x, -p.y, -p.z).
            AssertBits(new Quaternion(1F, 0F, 0F, 0F) * new Vector3(1F, 2F, 3F), 1F, -2F, -3F);
            // Identity: p unchanged bit-for-bit.
            AssertBits(Quaternion.identity * new Vector3(1.5F, -2.25F, 3.125F), 1.5F, -2.25F, 3.125F);
            // Non-unit q is NOT normalised: (0,2,0,0): y2 = 4, yy = 8 → (1-8)*p.x = -7 p.x, p.y, -7 p.z.
            AssertBits(new Quaternion(0F, 2F, 0F, 0F) * new Vector3(1F, 1F, 1F), -7F, 1F, -7F);
        }

        [Test]
        public void Quaternion_EqualityOperator_UsesDot()
        {
            Assert.That(Quaternion.identity == Quaternion.identity, Is.True);
            Assert.That(Quaternion.identity != Quaternion.identity, Is.False);
            // q == -q is false (dot = -1).
            var q = Quaternion.Euler(10F, 20F, 30F);
            var neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            Assert.That(q == neg, Is.False);
            Assert.That(q != neg, Is.True);
            Assert.That(q == q, Is.True);
            // Boundary: dot must be > 1 - kEpsilon = 0.999999 [0x3F7FFFEF].
            Assert.That(Quaternion.identity == new Quaternion(0F, 0F, 0F, 0.9999995F), Is.True);
            Assert.That(Quaternion.identity == new Quaternion(0F, 0F, 0F, 0.999999F), Is.False);   // equal, not greater
            Assert.That(Quaternion.identity == new Quaternion(0F, 0F, 0F, 0.999998F), Is.False);
            Assert.That(Quaternion.identity != new Quaternion(0F, 0F, 0F, 0.999998F), Is.True);
            // Non-unit: dot of (0,0,0,2) with itself = 4 > 0.999999 → "equal"; zero with anything → false.
            Assert.That(new Quaternion(0F, 0F, 0F, 2F) == new Quaternion(0F, 0F, 0F, 2F), Is.True);
            Assert.That(default(Quaternion) == default(Quaternion), Is.False);
            Assert.That(default(Quaternion) != default(Quaternion), Is.True);
        }

        [Test]
        public void Quaternion_EqualityOperator_NaN()
        {
            var nan = new Quaternion(float.NaN, 0F, 0F, 1F);
            Assert.That(nan == nan, Is.False);
            Assert.That(nan != nan, Is.True);
        }

        // ------------------------------------------------------------------ managed statics

        [Test]
        public void Quaternion_Dot()
        {
            var a = new Quaternion(1F, 2F, 3F, 4F);
            var b = new Quaternion(5F, 6F, 7F, 8F);
            Assert.That(Quaternion.Dot(a, b), Is.EqualTo(70F));
            Assert.That(Quaternion.Dot(in a, in b), Is.EqualTo(70F));
            Assert.That(Quaternion.Dot(Quaternion.identity, Quaternion.identity), Is.EqualTo(1F));
        }

        [Test]
        public void Quaternion_Angle_Verified()
        {
            // Spec §9 [verified]: Angle(q, -q) = 0; Angle(identity, Euler(0,180,0)) = 180.
            var q = Quaternion.Euler(10F, 20F, 30F);
            var neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            Assert.That(Quaternion.Angle(q, neg), Is.EqualTo(0F));
            Assert.That(Quaternion.Angle(in q, in neg), Is.EqualTo(0F));
            Assert.That(Quaternion.Angle(Quaternion.identity, Quaternion.Euler(0F, 180F, 0F)), Is.EqualTo(180F));
            Assert.That(Quaternion.Angle(q, q), Is.EqualTo(0F));
        }

        [Test]
        public void Quaternion_Angle_Formula()
        {
            // dot = Min(Abs(Dot), 1); Acos(0.5) = (float)Math.Acos(0.5) = 1.0471976; *2 = 2.0943952; *Rad2Deg = 120.00001 [0x42F00001].
            Assert.That(Bits(Quaternion.Angle(Quaternion.identity, new Quaternion(0F, 0F, 0F, 0.5F))), Is.EqualTo(0x42F00001));
            // Abs: -0.5 gives the same.
            Assert.That(Bits(Quaternion.Angle(Quaternion.identity, new Quaternion(0F, 0F, 0F, -0.5F))), Is.EqualTo(0x42F00001));
            // Min(…, 1): dot 4 clamps to 1 → IsEqualUsingDot → 0.
            Assert.That(Quaternion.Angle(new Quaternion(0F, 0F, 0F, 2F), new Quaternion(0F, 0F, 0F, 2F)), Is.EqualTo(0F));
            // dot = 0 → Acos(0)*2*Rad2Deg = (float)(π/2) * 2 * 57.29578 = 1.5707964 * 2 = 3.1415927 * 57.29578 = 180.
            Assert.That(Quaternion.Angle(Quaternion.identity, new Quaternion(0F, 1F, 0F, 0F)), Is.EqualTo(Mathf.Acos(0F) * 2.0F * Mathf.Rad2Deg));
            // 1 - kEpsilon boundary: dot exactly 0.999999 is not > → Acos path (non-zero).
            Assert.That(Quaternion.Angle(Quaternion.identity, new Quaternion(0F, 0F, 0F, 0.999999F)), Is.GreaterThan(0F));
            Assert.That(Quaternion.Angle(Quaternion.identity, new Quaternion(0F, 0F, 0F, 0.9999995F)), Is.EqualTo(0F));
        }

        [Test]
        public void Quaternion_RotateTowards()
        {
            var from = Quaternion.identity;
            var to = Quaternion.Euler(0F, 90F, 0F);
            // angle == 0 → to (bit-for-bit, even with maxDegreesDelta 0).
            Assert.That(Quaternion.RotateTowards(to, to, 0F).Equals(to), Is.True);
            Assert.That(Quaternion.RotateTowards(in to, in to, 0F).Equals(to), Is.True);
            // maxDegreesDelta >= angle → t = Min(1, 90/90) = 1 → SlerpUnclamped(from, to, 1) ≈ to.
            AssertUlps(Quaternion.RotateTowards(from, to, 90F), to.x, to.y, to.z, to.w, 2);
            AssertUlps(Quaternion.RotateTowards(from, to, 1000F), to.x, to.y, to.z, to.w, 2);
            // Half way: t = 45/90 = 0.5 → the same as SlerpUnclamped(from, to, 0.5) exactly (same call).
            Quaternion half = Quaternion.RotateTowards(from, to, 45F);
            Quaternion expected = Quaternion.SlerpUnclamped(from, to, Mathf.Min(1F, 45F / Quaternion.Angle(from, to)));
            Assert.That(half.Equals(expected), Is.True);
            AssertUlps(half, 0F, 0.38268346F, 0F, 0.9238795F, 2);   // Euler(0,45,0) = (0, sin 22.5°, 0, cos 22.5°)
            // Zero delta on a non-zero angle → t = 0 → from.
            AssertUlps(Quaternion.RotateTowards(from, to, 0F), 0F, 0F, 0F, 1F, 1);
        }

        [Test]
        public void Quaternion_Normalize_Verified()
        {
            // Spec §9 [verified]: (1e-30,0,0,0) → identity (Dot underflows to 0); (1e-20,0,0,0) → (1.0000026,0,0,0).
            AssertBits(Quaternion.Normalize(new Quaternion(1e-30F, 0F, 0F, 0F)), 0F, 0F, 0F, 1F);
            Quaternion d = Quaternion.Normalize(new Quaternion(1e-20F, 0F, 0F, 0F));
            AssertBits(d, 1.0000026F, 0F, 0F, 0F);
            Assert.That(Bits(d.x), Is.EqualTo(0x3F800016));
            AssertBits(Quaternion.Normalize(default(Quaternion)), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void Quaternion_Normalize_Formula()
        {
            // (0,0,0,2): mag = 2 → (0,0,0,1). (3,0,0,4): mag = 5 → (0.6, 0, 0, 0.8).
            AssertBits(Quaternion.Normalize(new Quaternion(0F, 0F, 0F, 2F)), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.Normalize(new Quaternion(3F, 0F, 0F, 4F)), 0.6F, 0F, 0F, 0.8F);
            var q = new Quaternion(3F, 0F, 0F, 4F);
            AssertBits(Quaternion.Normalize(in q), 0.6F, 0F, 0F, 0.8F);
            AssertBits(q.normalized, 0.6F, 0F, 0F, 0.8F);
            AssertBits(q, 3F, 0F, 0F, 4F);   // getter does not mutate
            q.Normalize();
            AssertBits(q, 0.6F, 0F, 0F, 0.8F);
            // Sign preserved: (0,0,0,-2) → (0,0,0,-1).
            AssertBits(Quaternion.Normalize(new Quaternion(0F, 0F, 0F, -2F)), 0F, 0F, 0F, -1F);
        }

        [Test]
        public void Quaternion_Normalize_NaNPropagates()
        {
            Quaternion n = Quaternion.Normalize(new Quaternion(float.NaN, 0F, 0F, 1F));
            Assert.That(float.IsNaN(n.x) && float.IsNaN(n.y) && float.IsNaN(n.z) && float.IsNaN(n.w), Is.True);
            var m = new Quaternion(0F, float.NaN, 0F, 1F);
            m.Normalize();
            Assert.That(float.IsNaN(m.w), Is.True);
        }

        [Test]
        public void Quaternion_Inverse_IsPlainConjugate()
        {
            // Spec §9 [verified]: Inverse((1,2,3,4)) = (-1,-2,-3,4); zero → (-0,-0,-0,0).
            AssertBits(Quaternion.Inverse(new Quaternion(1F, 2F, 3F, 4F)), -1F, -2F, -3F, 4F);
            var q = new Quaternion(1F, 2F, 3F, 4F);
            AssertBits(Quaternion.Inverse(in q), -1F, -2F, -3F, 4F);
            Quaternion z = Quaternion.Inverse(default(Quaternion));
            Assert.That(Bits(z.x), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(z.y), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(z.z), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(z.w), Is.EqualTo(0));
            AssertBits(Quaternion.Inverse(Quaternion.identity), -0F, -0F, -0F, 1F);
        }

        // ------------------------------------------------------------------ equality / hash / ToString

        [Test]
        public void Quaternion_Equals_UsesFloatEquals_NaNEqual()
        {
            // Spec §0 [verified]: Quaternion.Equals(itself) with NaN is true.
            var nan = new Quaternion(float.NaN, 0F, 0F, 1F);
            Assert.That(nan.Equals(nan), Is.True);
            Assert.That(nan.Equals(in nan), Is.True);
            Assert.That(nan.Equals((object)nan), Is.True);
            var all = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
            Assert.That(all.Equals(all), Is.True);
            var a = new Quaternion(1F, 2F, 3F, 4F);
            Assert.That(a.Equals(new Quaternion(1F, 2F, 3F, 4F)), Is.True);
            Assert.That(a.Equals(new Quaternion(1F, 2F, 3F, 4.0000005F)), Is.False);
            Assert.That(a.Equals((object)new Vector4(1F, 2F, 3F, 4F)), Is.False);
            Assert.That(a.Equals(null), Is.False);
            // float.Equals(-0, +0) is true.
            Assert.That(new Quaternion(-0F, 0F, 0F, 1F).Equals(Quaternion.identity), Is.True);
            // Exact: Equals distinguishes what == tolerates.
            var near = new Quaternion(0F, 0F, 0F, 0.9999995F);
            Assert.That(Quaternion.identity == near, Is.True);
            Assert.That(Quaternion.identity.Equals(near), Is.False);
        }

        [Test]
        public void Quaternion_GetHashCode_AsVector4()
        {
            // Spec §9: same formula as Vector4 → (1,2,3,4) hashes to 265289728.
            Assert.That(new Quaternion(1F, 2F, 3F, 4F).GetHashCode(), Is.EqualTo(265289728));
            Assert.That(new Quaternion(5F, 6F, 7F, 8F).GetHashCode(), Is.EqualTo(new Vector4(5F, 6F, 7F, 8F).GetHashCode()));
            Assert.That(Quaternion.identity.GetHashCode(), Is.EqualTo(0x3F800000 >> 1));
        }

        [Test]
        public void Quaternion_ToString_DefaultF5()
        {
            Assert.That(Quaternion.identity.ToString(), Is.EqualTo("(0.00000, 0.00000, 0.00000, 1.00000)"));
            Assert.That(new Quaternion(1F, 2F, 3F, 4F).ToString(), Is.EqualTo("(1.00000, 2.00000, 3.00000, 4.00000)"));
            Assert.That(new Quaternion(0.123456789F, -0.5F, 0F, 1F).ToString(), Is.EqualTo("(0.12346, -0.50000, 0.00000, 1.00000)"));
            Assert.That(Quaternion.identity.ToString(null), Is.EqualTo("(0.00000, 0.00000, 0.00000, 1.00000)"));
            Assert.That(Quaternion.identity.ToString(""), Is.EqualTo("(0.00000, 0.00000, 0.00000, 1.00000)"));
            Assert.That(Quaternion.identity.ToString(null, null), Is.EqualTo("(0.00000, 0.00000, 0.00000, 1.00000)"));
        }

        [Test]
        public void Quaternion_ToString_FormatAndProvider()
        {
            var q = new Quaternion(1.5F, 2F, 3F, 4F);
            Assert.That(q.ToString("F1"), Is.EqualTo("(1.5, 2.0, 3.0, 4.0)"));
            Assert.That(q.ToString("F0"), Is.EqualTo("(2, 2, 3, 4)"));
            var de = new CultureInfo("de-DE");
            Assert.That(q.ToString("F2", de), Is.EqualTo("(1,50, 2,00, 3,00, 4,00)"));
            Assert.That(q.ToString(null, de), Is.EqualTo("(1,50000, 2,00000, 3,00000, 4,00000)"));
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = de;
                Assert.That(q.ToString(), Is.EqualTo("(1.50000, 2.00000, 3.00000, 4.00000)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
            Assert.That(((IFormattable)q).ToString("F1", CultureInfo.InvariantCulture), Is.EqualTo("(1.5, 2.0, 3.0, 4.0)"));
        }

        // ------------------------------------------------------------------ Euler (native; ZXY half-angle product)

        [Test]
        public void Quaternion_Euler_ReferenceValues()
        {
            // Spec §9 [verified]; probe "matches bit-for-bit in most cases and within 1 ulp otherwise" (native sinf/cosf).
            AssertUlps(Quaternion.Euler(10F, 20F, 30F), 0.12767944F, 0.14487813F, 0.23929833F, 0.9515485F, 1);
            AssertUlps(Quaternion.Euler(90F, 0F, 0F), 0.70710677F, 0F, 0F, 0.70710677F, 1);
            AssertUlps(Quaternion.Euler(360F, 0F, 0F), -8.742278e-08F, 0F, 0F, -1F, 1);
            AssertUlps(Quaternion.Euler(150F, 35F, 45F), 0.88087976F, -0.2806315F, -0.17388792F, 0.33920458F, 1);
            AssertBits(Quaternion.Euler(0F, 0F, 0F), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void Quaternion_Euler_SingleAxisHalfAngle()
        {
            // Euler(45,0,0) = qX = (sin(22.5°), 0, 0, cos(22.5°)): 45*Deg2Rad*0.5 = 0.3926991;
            // sin = 0.38268346 [0x3EC3EF16], cos = 0.9238795 [0x3F6C835E] (single-precision trig).
            AssertUlps(Quaternion.Euler(45F, 0F, 0F), 0.38268346F, 0F, 0F, 0.9238795F, 1);
            AssertUlps(Quaternion.Euler(0F, 45F, 0F), 0F, 0.38268346F, 0F, 0.9238795F, 1);
            AssertUlps(Quaternion.Euler(0F, 0F, 45F), 0F, 0F, 0.38268346F, 0.9238795F, 1);
            // Euler(0,180,0) = (0, sin 90°, 0, cos 90°) = (0, 1, 0, -4.371139e-08).
            AssertUlps(Quaternion.Euler(0F, 180F, 0F), 0F, 1F, 0F, -4.371139e-08F, 1);
        }

        [Test]
        public void Quaternion_Euler_IsZXYProductOfAxisRotations()
        {
            // Spec §9: Euler(x,y,z) == AngleAxis(y, up) * AngleAxis(x, right) * AngleAxis(z, forward), i.e. (qY * qX) * qZ.
            Quaternion e = Quaternion.Euler(10F, 20F, 30F);
            Quaternion p = Quaternion.AngleAxis(20F, Vector3.up) * Quaternion.AngleAxis(10F, Vector3.right) * Quaternion.AngleAxis(30F, Vector3.forward);
            AssertUlps(e, p.x, p.y, p.z, p.w, 2);
            // And NOT the XYZ product.
            Quaternion xyz = Quaternion.AngleAxis(10F, Vector3.right) * Quaternion.AngleAxis(20F, Vector3.up) * Quaternion.AngleAxis(30F, Vector3.forward);
            Assert.That(Quaternion.Dot(e, xyz), Is.LessThan(0.9999F));
        }

        [Test]
        public void Quaternion_Euler_VectorOverloadIsBitIdentical()
        {
            // Spec §9 [verified]: Euler(Vector3) is bit-identical to the 3-float form.
            foreach (var e in new[] { new Vector3(10F, 20F, 30F), new Vector3(150F, 35F, 45F), new Vector3(-10F, -20F, -30F), new Vector3(89.99F, 45F, 0F) })
            {
                Quaternion a = Quaternion.Euler(e.x, e.y, e.z);
                Quaternion b = Quaternion.Euler(e);
                Quaternion c = Quaternion.Euler(in e);
                AssertBits(b, a.x, a.y, a.z, a.w);
                AssertBits(c, a.x, a.y, a.z, a.w);
            }
        }

        [Test]
        public void Quaternion_eulerAngles_Setter()
        {
            Quaternion q = default;
            q.eulerAngles = new Vector3(10F, 20F, 30F);
            Quaternion e = Quaternion.Euler(10F, 20F, 30F);
            AssertBits(q, e.x, e.y, e.z, e.w);
        }

        [Test]
        public void Quaternion_eulerAngles_RoundTrips()
        {
            // Spec §9 [verified] round trips (native trig; §15.2 tolerance).
            AssertUlps(Quaternion.Euler(10F, 20F, 30F).eulerAngles, 9.999999F, 20.000002F, 30.000002F, 2);
            AssertUlps(Quaternion.Euler(150F, 35F, 45F).eulerAngles, 30.000002F, 215F, 225F, 2);
            AssertUlps(Quaternion.Euler(-10F, -20F, -30F).eulerAngles, 350F, 340F, 330F, 2);
            AssertBits(Quaternion.identity.eulerAngles, 0F, 0F, 0F);
        }

        [Test]
        public void Quaternion_eulerAngles_GimbalLock()
        {
            // Spec §9 [verified]: singular branch puts remaining rotation into y and sets z = 0.
            AssertUlps(Quaternion.Euler(90F, 45F, 0F).eulerAngles, 90F, 45F, 0F, 2);
            AssertUlps(Quaternion.Euler(-90F, 45F, 0F).eulerAngles, 270F, 45F, 0F, 2);
            AssertUlps(Quaternion.Euler(270F, 10F, 20F).eulerAngles, 270F, 29.999998F, 0F, 2);
            // Branch triggers when |sin x| rounds to 1 in float.
            AssertUlps(Quaternion.Euler(89.99F, 45F, 0F).eulerAngles, 90F, 45.000004F, 0F, 2);
        }

        [Test]
        public void Quaternion_eulerAngles_IsScaleInvariant()
        {
            // Spec §9: non-unit (1,2,3,4) gives the same result as its normalised form; (0,0,0,0) and (0,0,0,2) → (0,0,0).
            var q = new Quaternion(1F, 2F, 3F, 4F);
            Vector3 a = q.eulerAngles;
            Vector3 b = Quaternion.Normalize(q).eulerAngles;
            AssertUlps(a, b.x, b.y, b.z, 2);
            AssertBits(default(Quaternion).eulerAngles, 0F, 0F, 0F);
            AssertBits(new Quaternion(0F, 0F, 0F, 2F).eulerAngles, 0F, 0F, 0F);
        }

        [Test]
        public void Quaternion_eulerAngles_MakePositive_TinyNegativesNotWrapped()
        {
            // Spec §9 [verified]: negativeFlip = -0.0001 * Rad2Deg = -0.005729578; values below it get +360, above 359.99426 get -360.
            // Euler(0,-0.001,0).eulerAngles.y = -0.001 (not wrapped); Euler(0,-0.01,0) → 359.99; Euler(0,359.999,0) → -0.00100085.
            Assert.That(Quaternion.Euler(0F, -0.001F, 0F).eulerAngles.y, Is.EqualTo(-0.001F).Within(4).Ulps);
            Assert.That(Quaternion.Euler(0F, -0.01F, 0F).eulerAngles.y, Is.EqualTo(359.99F).Within(4).Ulps);
            Assert.That(Quaternion.Euler(0F, 359.999F, 0F).eulerAngles.y, Is.EqualTo(-0.00100085F).Within(4).Ulps);
            // Same per-component rule on x and z.
            Assert.That(Quaternion.Euler(-0.001F, 0F, 0F).eulerAngles.x, Is.EqualTo(-0.001F).Within(4).Ulps);
            Assert.That(Quaternion.Euler(0F, 0F, -0.01F).eulerAngles.z, Is.EqualTo(359.99F).Within(4).Ulps);
        }

        // ------------------------------------------------------------------ AngleAxis / ToAngleAxis

        [Test]
        public void Quaternion_AngleAxis_Verified()
        {
            // Spec §9 [verified]: AngleAxis(180, up) = (0, 1, 0, -4.371139e-08) [0xB33BBD2E].
            Quaternion q = Quaternion.AngleAxis(180F, Vector3.up);
            AssertUlps(q, 0F, 1F, 0F, -4.371139e-08F, 1);
            // Axis magnitude <= 1e-6 → identity; 2e-6 → a real rotation.
            AssertBits(Quaternion.AngleAxis(90F, new Vector3(0F, 1e-6F, 0F)), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.AngleAxis(90F, Vector3.zero), 0F, 0F, 0F, 1F);
            Quaternion small = Quaternion.AngleAxis(90F, new Vector3(0F, 2e-6F, 0F));
            Assert.That(small.Equals(Quaternion.identity), Is.False);
            AssertUlps(small, 0F, 0.70710677F, 0F, 0.70710677F, 2);
            // NaN axis → identity.
            AssertBits(Quaternion.AngleAxis(90F, new Vector3(float.NaN, 0F, 0F)), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void Quaternion_AngleAxis_Formula_AxisNormalised()
        {
            // half = 90*Deg2Rad*0.5 = 0.7853982; s = sin(half)/|axis| = 0.70710677/2; result = (0, s*2, 0, cos half) = (0, 0.70710677, 0, 0.70710677).
            var axis = new Vector3(0F, 2F, 0F);
            AssertUlps(Quaternion.AngleAxis(90F, axis), 0F, 0.70710677F, 0F, 0.70710677F, 1);
            AssertUlps(Quaternion.AngleAxis(90F, in axis), 0F, 0.70710677F, 0F, 0.70710677F, 1);
            // Same rotation regardless of axis length.
            Quaternion a = Quaternion.AngleAxis(33F, new Vector3(1F, 2F, 3F));
            Quaternion b = Quaternion.AngleAxis(33F, new Vector3(10F, 20F, 30F));
            AssertUlps(a, b.x, b.y, b.z, b.w, 2);
            // Zero angle → identity for any axis.
            AssertBits(Quaternion.AngleAxis(0F, new Vector3(1F, 2F, 3F)), 0F, 0F, 0F, 1F);
            // AngleAxis(90, up) == Euler(0,90,0).
            Quaternion e = Quaternion.Euler(0F, 90F, 0F);
            AssertUlps(Quaternion.AngleAxis(90F, Vector3.up), e.x, e.y, e.z, e.w, 1);
        }

        [Test]
        public void Quaternion_ToAngleAxis_Verified()
        {
            // Spec §9 [verified]: identity → 0, (1,0,0); (0,0,0,-1) → 360, (1,0,0).
            Quaternion.identity.ToAngleAxis(out float angle, out Vector3 axis);
            Assert.That(angle, Is.EqualTo(0F));
            AssertBits(axis, 1F, 0F, 0F);
            new Quaternion(0F, 0F, 0F, -1F).ToAngleAxis(out angle, out axis);
            Assert.That(angle, Is.EqualTo(360F).Within(2).Ulps);
            AssertBits(axis, 1F, 0F, 0F);
        }

        [Test]
        public void Quaternion_ToAngleAxis_Euler90()
        {
            // Spec §9 [verified]: Euler(0,90,0) → 89.99999°, axis (0, 1.0000001, 0) — a last-ulp artefact of the native
            // normalisation (§15.2: tolerate native rounding). 89.99999 is 1 ulp below 90; 1.0000001 is 1 ulp above 1.
            Quaternion.Euler(0F, 90F, 0F).ToAngleAxis(out float angle, out Vector3 axis);
            Assert.That(angle, Is.EqualTo(89.99999F).Within(2).Ulps);
            Assert.That(axis.x, Is.EqualTo(0F));
            Assert.That(axis.y, Is.EqualTo(1.0000001F).Within(2).Ulps);
            Assert.That(axis.z, Is.EqualTo(0F));
        }

        [Test]
        public void Quaternion_ToAngleAxis_RoundTripsAngleAxis()
        {
            Quaternion.AngleAxis(33F, new Vector3(0F, 0F, 1F)).ToAngleAxis(out float angle, out Vector3 axis);
            Assert.That(angle, Is.EqualTo(33F).Within(4).Ulps);
            AssertUlps(axis, 0F, 0F, 1F, 2);
        }

        // ------------------------------------------------------------------ Lerp / Slerp

        [Test]
        public void Quaternion_Lerp_Formula()
        {
            // LerpUnclamped(identity, (0,1,0,0), 0.5): dot = 0 → a + t*(b-a) = (0,0.5,0,0.5); / sqrt(0.5) → (0, 0.70710677, 0, 0.70710677).
            AssertUlps(Quaternion.LerpUnclamped(Quaternion.identity, new Quaternion(0F, 1F, 0F, 0F), 0.5F), 0F, 0.70710677F, 0F, 0.70710677F, 1);
            AssertUlps(Quaternion.Lerp(Quaternion.identity, new Quaternion(0F, 1F, 0F, 0F), 0.5F), 0F, 0.70710677F, 0F, 0.70710677F, 1);
            // Endpoints (unit inputs) come back unchanged.
            var b = new Quaternion(0F, 1F, 0F, 0F);
            AssertUlps(Quaternion.Lerp(Quaternion.identity, b, 0F), 0F, 0F, 0F, 1F, 1);
            AssertUlps(Quaternion.Lerp(Quaternion.identity, b, 1F), 0F, 1F, 0F, 0F, 1);
            var a = Quaternion.identity;
            AssertUlps(Quaternion.Lerp(in a, in b, 1F), 0F, 1F, 0F, 0F, 1);
            AssertUlps(Quaternion.LerpUnclamped(in a, in b, 0F), 0F, 0F, 0F, 1F, 1);
        }

        [Test]
        public void Quaternion_Lerp_NegativeDotInterpolatesTowardMinusB()
        {
            // a = identity, b = (0,0.6,0,-0.8): Dot = -0.8 < 0 → toward -b = (0,-0.6,0,0.8) at t = 0.5:
            // y = 0 + 0.5*(-0.6 - 0) = -0.3; w = 1 + 0.5*(0.8 - 1) = 0.9; mag = sqrt(0.09 + 0.81) = 0.94868326 [0x3F72DCE8];
            // → (0, -0.3162278 [0xBEA1E89C], 0, 0.9486833 [0x3F72DCE9]).
            Quaternion r = Quaternion.LerpUnclamped(Quaternion.identity, new Quaternion(0F, 0.6F, 0F, -0.8F), 0.5F);
            AssertUlps(r, 0F, -0.3162278F, 0F, 0.9486833F, 1);
            // Dot < 0 exactly at -1 (b = -identity): -b = identity → identity for every t.
            AssertBits(Quaternion.LerpUnclamped(Quaternion.identity, new Quaternion(0F, 0F, 0F, -1F), 0.3F), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.Lerp(Quaternion.identity, new Quaternion(0F, 0F, 0F, -1F), 1F), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void Quaternion_Lerp_ClampsT_And_NormalisesNonUnitInputs()
        {
            // Spec §9 [verified]: Lerp((0,0,0,2), …, 0) = identity (normalised, no epsilon guard).
            AssertBits(Quaternion.Lerp(new Quaternion(0F, 0F, 0F, 2F), new Quaternion(0F, 1F, 0F, 0F), 0F), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.Lerp(new Quaternion(0F, 0F, 0F, 2F), new Quaternion(0F, 1F, 0F, 0F), -5F), 0F, 0F, 0F, 1F);
            // t > 1 clamps to 1 → b (unit).
            AssertUlps(Quaternion.Lerp(Quaternion.identity, new Quaternion(0F, 1F, 0F, 0F), 7F), 0F, 1F, 0F, 0F, 1);
            // LerpUnclamped extrapolates instead: t = 2 with b = (0,1,0,0): q = (0,2,0,-1) / sqrt(5) → (0, 0.8944272, 0, -0.4472136).
            AssertUlps(Quaternion.LerpUnclamped(Quaternion.identity, new Quaternion(0F, 1F, 0F, 0F), 2F), 0F, 0.8944272F, 0F, -0.4472136F, 1);
            // Zero quaternion inputs: 0/0 = NaN (no guard).
            Quaternion n = Quaternion.LerpUnclamped(default, default, 0.5F);
            Assert.That(float.IsNaN(n.w), Is.True);
        }

        [Test]
        public void Quaternion_Slerp_QVersusMinusQReturnsA()
        {
            // Spec §9 [verified]: q vs -q returns a for every t (dot = -1 → b negated → dot 1 ≥ 0.95 → Lerp(a, a, t) = a).
            Quaternion q = Quaternion.Euler(10F, 20F, 30F);
            var neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            foreach (float t in new[] { 0F, 0.25F, 0.5F, 0.75F, 1F })
            {
                Quaternion s = Quaternion.Slerp(q, neg, t);
                AssertUlps(s, q.x, q.y, q.z, q.w, 1);
                AssertUlps(Quaternion.Slerp(in q, in neg, t), q.x, q.y, q.z, q.w, 1);
            }
        }

        [Test]
        public void Quaternion_Slerp_180DegreePairGoesShortWay()
        {
            // Spec §9 [verified]: Slerp(identity, Euler(0,180,0), 0.3) = (0, -0.4539905, 0, 0.8910065).
            AssertUlps(Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0F, 180F, 0F), 0.3F), 0F, -0.4539905F, 0F, 0.8910065F, 2);
        }

        [Test]
        public void Quaternion_SlerpUnclamped_Extrapolates()
        {
            // Spec §9 [verified]: SlerpUnclamped(identity, Euler(0,120,0), 1.5) = (0, 1, 0, -3.4e-08).
            Quaternion r = Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.Euler(0F, 120F, 0F), 1.5F);
            Assert.That(r.x, Is.EqualTo(0F));
            Assert.That(r.y, Is.EqualTo(1F).Within(2).Ulps);
            Assert.That(r.z, Is.EqualTo(0F));
            Assert.That(r.w, Is.EqualTo(-3.4e-08F).Within(2e-8F));
            var a = Quaternion.identity;
            var b = Quaternion.Euler(0F, 120F, 0F);
            Quaternion r2 = Quaternion.SlerpUnclamped(in a, in b, 1.5F);
            AssertBits(r2, r.x, r.y, r.z, r.w);
        }

        [Test]
        public void Quaternion_Slerp_SineFormulaBelow095()
        {
            // a = identity, b = (0,0.6,0,0.8): dot = 0.8 < 0.95 → angle = acos(0.8); invSin = 1/sin(angle); t = 0.25:
            // sa = sin(angle*0.25), sb = sin(angle*0.75); y = (0*sb + 0.6*sa)*invSin = 0.16018224 [0x3E2406D0];
            // w = (1*sb + 0.8*sa)*invSin = 0.9870875 [0x3F7CB1C4]. No final normalisation.
            Quaternion r = Quaternion.Slerp(Quaternion.identity, new Quaternion(0F, 0.6F, 0F, 0.8F), 0.25F);
            AssertUlps(r, 0F, 0.16018224F, 0F, 0.9870875F, 1);
            // Differs from the normalised Lerp for this pair (spec: "for dot 0.5 they differ").
            Quaternion l = Quaternion.Lerp(Quaternion.identity, new Quaternion(0F, 0.6F, 0F, 0.8F), 0.25F);
            Assert.That(r.Equals(l), Is.False);
        }

        [Test]
        public void Quaternion_Slerp_FallsBackToLerpAtOrAbove095()
        {
            // Spec §9: dot ≥ 0.95 → return Lerp(a, b, t) (bit-identical for a pair with dot 0.9976).
            Quaternion a = Quaternion.identity;
            Quaternion b = Quaternion.Euler(0F, 8F, 0F);   // cos(4°) = 0.99756
            Assert.That(Quaternion.Dot(a, b), Is.GreaterThan(0.95F));
            foreach (float t in new[] { 0.1F, 0.5F, 0.9F })
            {
                Quaternion s = Quaternion.Slerp(a, b, t);
                Quaternion l = Quaternion.Lerp(a, b, t);
                AssertBits(s, l.x, l.y, l.z, l.w);
            }
            // Negative dot above the threshold in magnitude: b flipped first, then Lerp — also bit-identical to Lerp(a, b, t)
            // because Lerp performs the same flip.
            var nb = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            Quaternion s2 = Quaternion.Slerp(a, nb, 0.5F);
            Quaternion l2 = Quaternion.Lerp(a, nb, 0.5F);
            AssertBits(s2, l2.x, l2.y, l2.z, l2.w);
        }

        [Test]
        public void Quaternion_SlerpUnclamped_NearParallelFallbackDoesNotClampT()
        {
            // Spec §9: the dot >= 0.95 branch returns the NORMALISED LERP of the (already flipped) pair. Because
            // SlerpUnclamped is the unclamped entry point — Slerp is defined as SlerpUnclamped(a, b, Clamp01(t)) —
            // that fallback must interpolate with the caller's t, not with Clamp01(t); clamping there would make the
            // member stop extrapolating for every pair closer than ~36 degrees and be discontinuous across dot = 0.95.
            Quaternion a = Quaternion.identity;
            Quaternion b = Quaternion.Euler(0F, 10F, 0F);       // dot = cos(5°) = 0.9962 > 0.95
            Assert.That(Quaternion.Dot(a, b), Is.GreaterThan(0.95F));

            Quaternion beyond = Quaternion.SlerpUnclamped(a, b, 2F);
            AssertBits(beyond, Quaternion.LerpUnclamped(a, b, 2F));
            // It really extrapolates: t = 2 lands past b, near Euler(0, 20, 0), and is nowhere near b itself.
            Assert.That(beyond.y, Is.GreaterThan(b.y * 1.5F));
            Assert.That(Quaternion.Angle(a, beyond), Is.EqualTo(20F).Within(0.2F));

            Quaternion before = Quaternion.SlerpUnclamped(a, b, -1F);
            AssertBits(before, Quaternion.LerpUnclamped(a, b, -1F));
            Assert.That(before.y, Is.LessThan(0F));
            Assert.That(Quaternion.Angle(a, before), Is.EqualTo(10F).Within(0.2F));

            // The in-twin agrees, and the clamped entry point still clamps.
            AssertBits(Quaternion.SlerpUnclamped(in a, in b, 2F), beyond);
            AssertBits(Quaternion.Slerp(a, b, 2F), Quaternion.SlerpUnclamped(a, b, 1F));
            AssertBits(Quaternion.Slerp(a, b, -1F), Quaternion.SlerpUnclamped(a, b, 0F));

            // Flipped pair (dot < 0 with |dot| >= 0.95): b is negated first, so the result matches the flipped nlerp.
            var nb = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            AssertBits(Quaternion.SlerpUnclamped(a, nb, 2F), beyond);
        }

        [Test]
        public void Quaternion_Slerp_ClampsT()
        {
            Quaternion a = Quaternion.identity;
            Quaternion b = Quaternion.Euler(0F, 120F, 0F);
            Quaternion at1 = Quaternion.Slerp(a, b, 1F);
            Quaternion at15 = Quaternion.Slerp(a, b, 1.5F);
            AssertBits(at15, at1.x, at1.y, at1.z, at1.w);
            Quaternion at0 = Quaternion.Slerp(a, b, 0F);
            Quaternion atNeg = Quaternion.Slerp(a, b, -0.5F);
            AssertBits(atNeg, at0.x, at0.y, at0.z, at0.w);
            // SlerpUnclamped at t = 1 lands on b, at t = 0 on a (sine formula: sin(0) terms vanish).
            AssertUlps(Quaternion.SlerpUnclamped(a, b, 1F), b.x, b.y, b.z, b.w, 2);
            AssertUlps(Quaternion.SlerpUnclamped(a, b, 0F), 0F, 0F, 0F, 1F, 2);
        }

        // ------------------------------------------------------------------ LookRotation / FromToRotation

        [Test]
        public void Quaternion_LookRotation_Verified()
        {
            // Spec §9 [verified] (native). All five reproduce BIT-EXACTLY, so they are asserted on the bit pattern:
            // the trace branch of the basis→quaternion conversion uses one shared 0.5/sqrt(trace+1) reciprocal (the
            // sqrt(trace+1)*2 divide form is 1 ulp low on x and z of the (0,1,1) case), and the result's signed zeros
            // are canonicalised to +0 (cross products of -Vector3.forward = (-0,-0,-1) otherwise leak -0 into z and w).
            AssertBits(Quaternion.LookRotation(Vector3.forward, Vector3.up), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.LookRotation(Vector3.right, Vector3.up), 0F, 0.70710677F, 0F, 0.70710677F);
            AssertBits(Quaternion.LookRotation(-Vector3.forward, Vector3.up), 0F, 1F, 0F, 0F);
            AssertBits(Quaternion.LookRotation(new Vector3(1F, 2F, 3F), Vector3.up), -0.27465674F, 0.15385647F, 0.04457066F, 0.94810617F);
            AssertBits(Quaternion.LookRotation(new Vector3(1F, 2F, 3F), new Vector3(0F, 1F, 1F)), -0.19896805F, 0.24396692F, 0.38962165F, 0.865498F);
            // No negative zeros anywhere in the (-forward, up) result: Unity prints (0.00000, 1.00000, 0.00000, 0.00000),
            // and a "-0.00000" here would fail any bit-pattern golden comparison.
            Quaternion back = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
            Assert.That(back.ToString(), Is.EqualTo("(0.00000, 1.00000, 0.00000, 0.00000)"));
            for (int i = 0; i < 4; i++)
                Assert.That(Bits(back[i]), Is.Not.EqualTo(unchecked((int)0x80000000)), $"component {i} is negative zero");
            // Single-argument overload uses Vector3.up.
            Quaternion one = Quaternion.LookRotation(new Vector3(1F, 2F, 3F));
            Quaternion two = Quaternion.LookRotation(new Vector3(1F, 2F, 3F), Vector3.up);
            AssertBits(one, two.x, two.y, two.z, two.w);
            Vector3 f = new Vector3(1F, 2F, 3F), u = Vector3.up;
            Quaternion three = Quaternion.LookRotation(in f, in u);
            AssertBits(three, two.x, two.y, two.z, two.w);
        }

        [Test]
        public void Quaternion_LookRotation_Degenerate()
        {
            // Spec §9: zero forward → identity; forward parallel to up (up, up) → (-0.70710677, 0, 0, 0.70710677).
            AssertBits(Quaternion.LookRotation(Vector3.zero, Vector3.up), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.LookRotation(Vector3.zero), 0F, 0F, 0F, 1F);
            AssertUlps(Quaternion.LookRotation(Vector3.up, Vector3.up), -0.70710677F, 0F, 0F, 0.70710677F, 2);
        }

        [Test]
        public void Quaternion_LookRotation_ZAxisIsForward()
        {
            // The resulting rotation maps +z onto normalize(forward) and keeps y "up-ish".
            Vector3 fwd = new Vector3(1F, 2F, 3F);
            Quaternion q = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 z = q * Vector3.forward;
            Vector3 n = fwd.normalized;
            AssertUlps(z, n.x, n.y, n.z, 8);
            Vector3 y = q * Vector3.up;
            Assert.That(Vector3.Dot(y, Vector3.up), Is.GreaterThan(0F));
        }

        [Test]
        public void Quaternion_SetLookRotation()
        {
            Quaternion q = default;
            q.SetLookRotation(new Vector3(1F, 2F, 3F));
            Quaternion e = Quaternion.LookRotation(new Vector3(1F, 2F, 3F), Vector3.up);
            AssertBits(q, e.x, e.y, e.z, e.w);
            q.SetLookRotation(new Vector3(1F, 2F, 3F), new Vector3(0F, 1F, 1F));
            e = Quaternion.LookRotation(new Vector3(1F, 2F, 3F), new Vector3(0F, 1F, 1F));
            AssertBits(q, e.x, e.y, e.z, e.w);
        }

        [Test]
        public void Quaternion_FromToRotation_Verified()
        {
            // Spec §9 [verified]: inputs normalised; (right*2, up*3) = (right, up) = (0,0,0.70710677,0.70710677).
            AssertUlps(Quaternion.FromToRotation(Vector3.right, Vector3.up), 0F, 0F, 0.70710677F, 0.70710677F, 2);
            AssertUlps(Quaternion.FromToRotation(Vector3.right * 2F, Vector3.up * 3F), 0F, 0F, 0.70710677F, 0.70710677F, 2);
            Quaternion a = Quaternion.FromToRotation(Vector3.right, Vector3.up);
            Quaternion b = Quaternion.FromToRotation(Vector3.right * 2F, Vector3.up * 3F);
            AssertBits(b, a.x, a.y, a.z, a.w);
            // Antiparallel: (right, left) → (0,1,0,0); (up, down) → (1,0,-0,0).
            AssertUlps(Quaternion.FromToRotation(Vector3.right, Vector3.left), 0F, 1F, 0F, 0F, 2);
            Quaternion ud = Quaternion.FromToRotation(Vector3.up, Vector3.down);
            AssertUlps(ud, 1F, 0F, 0F, 0F, 2);
            // Zero input → identity.
            AssertBits(Quaternion.FromToRotation(Vector3.zero, Vector3.up), 0F, 0F, 0F, 1F);
            AssertBits(Quaternion.FromToRotation(Vector3.up, Vector3.zero), 0F, 0F, 0F, 1F);
            // ((1,2,3),(3,2,1)) → (-0.15430337, 0.30860674, -0.15430337, 0.9258201).
            AssertUlps(Quaternion.FromToRotation(new Vector3(1F, 2F, 3F), new Vector3(3F, 2F, 1F)), -0.15430337F, 0.30860674F, -0.15430337F, 0.9258201F, 3);
            Vector3 f = new Vector3(1F, 2F, 3F), t = new Vector3(3F, 2F, 1F);
            Quaternion viaIn = Quaternion.FromToRotation(in f, in t);
            Quaternion viaVal = Quaternion.FromToRotation(f, t);
            AssertBits(viaIn, viaVal.x, viaVal.y, viaVal.z, viaVal.w);
        }

        [Test]
        public void Quaternion_FromToRotation_RotatesFromOntoTo()
        {
            Vector3 from = new Vector3(1F, 2F, 3F), to = new Vector3(3F, 2F, 1F);
            Vector3 r = Quaternion.FromToRotation(from, to) * from.normalized;
            Vector3 n = to.normalized;
            AssertUlps(r, n.x, n.y, n.z, 8);
            // Same direction → identity.
            AssertUlps(Quaternion.FromToRotation(Vector3.up, Vector3.up * 5F), 0F, 0F, 0F, 1F, 1);
        }

        [Test]
        public void Quaternion_SetFromToRotation()
        {
            Quaternion q = default;
            q.SetFromToRotation(Vector3.right, Vector3.up);
            Quaternion e = Quaternion.FromToRotation(Vector3.right, Vector3.up);
            AssertBits(q, e.x, e.y, e.z, e.w);
        }

        // ------------------------------------------------------------------ obsolete radian API

        [Test]
        public void Quaternion_ObsoleteRadianApi_WrapsDegreeApi()
        {
#pragma warning disable CS0618
            // Spec §9: AxisAngle(axis, rad) = AngleAxis(Rad2Deg * rad, axis).
            Quaternion a = Quaternion.AxisAngle(Vector3.up, 0.5F);
            Quaternion b = Quaternion.AngleAxis(Mathf.Rad2Deg * 0.5F, Vector3.up);
            AssertBits(a, b.x, b.y, b.z, b.w);
            // EulerRotation / EulerAngles take radians: EulerRotation(x*Deg2Rad, …) == Euler(x, …) up to the Vector3*float rounding.
            Quaternion e = Quaternion.Euler(10F, 20F, 30F);
            Quaternion r = Quaternion.EulerRotation(10F * Mathf.Deg2Rad, 20F * Mathf.Deg2Rad, 30F * Mathf.Deg2Rad);
            AssertBits(r, e.x, e.y, e.z, e.w);
            Quaternion r2 = Quaternion.EulerAngles(new Vector3(10F * Mathf.Deg2Rad, 20F * Mathf.Deg2Rad, 30F * Mathf.Deg2Rad));
            AssertBits(r2, e.x, e.y, e.z, e.w);
            // ToEulerAngles/ToEuler are in radians, without MakePositive: identity → (0,0,0).
            AssertBits(Quaternion.identity.ToEulerAngles(), 0F, 0F, 0F);
            AssertBits(Quaternion.identity.ToEuler(), 0F, 0F, 0F);
            // ToAxisAngle is radians: (0,0,0,-1) → 2π.
            new Quaternion(0F, 0F, 0F, -1F).ToAxisAngle(out Vector3 axis, out float rad);
            Assert.That(rad, Is.EqualTo(2F * Mathf.PI).Within(2).Ulps);
            AssertBits(axis, 1F, 0F, 0F);
            Quaternion s = default;
            s.SetAxisAngle(Vector3.up, 0.5F);
            AssertBits(s, b.x, b.y, b.z, b.w);
            s.SetEulerRotation(10F * Mathf.Deg2Rad, 20F * Mathf.Deg2Rad, 30F * Mathf.Deg2Rad);
            AssertBits(s, e.x, e.y, e.z, e.w);
            s = default;
            s.SetEulerAngles(new Vector3(10F * Mathf.Deg2Rad, 20F * Mathf.Deg2Rad, 30F * Mathf.Deg2Rad));
            AssertBits(s, e.x, e.y, e.z, e.w);
#pragma warning restore CS0618
        }

        // ------------------------------------------------------------------ additional surface / edge cases

        [Test]
        public void Vector4_HasNoAngleCrossReflectClampMagnitudeOrSmoothDamp()
        {
            // Spec §3: "There is no Angle, Cross, Reflect, ClampMagnitude or SmoothDamp on Vector4."
            foreach (string name in new[] { "Angle", "SignedAngle", "Cross", "Reflect", "ClampMagnitude", "SmoothDamp", "Perpendicular", "ProjectOnPlane", "Slerp", "RotateTowards" })
                Assert.That(typeof(Vector4).GetMethod(name, BindingFlags.Public | BindingFlags.Static), Is.Null, name);
        }

        [Test]
        public void Vector4_kEpsilonIsACompileTimeConstant()
        {
            // Spec §3: "Constant. kEpsilon = 0.00001F" — a const, so kEpsilon*kEpsilon folds at compile time in operator==.
            FieldInfo f = typeof(Vector4).GetField("kEpsilon", BindingFlags.Public | BindingFlags.Static);
            Assert.That(f, Is.Not.Null);
            Assert.That(f.IsLiteral, Is.True);
            Assert.That((float)f.GetRawConstantValue(), Is.EqualTo(0.00001F));
        }

        [Test]
        public void Vector4_GetHashCode_NormalisesSignedZeroAndNaN()
        {
            // Spec §0: float.GetHashCode on .NET Core normalises -0 and every NaN payload; the verified hash values are
            // the oracle, so the shim must simply forward to float.GetHashCode rather than hash raw bits itself.
            Assert.That(new Vector4(-0F, -0F, -0F, -0F).GetHashCode(), Is.EqualTo(new Vector4(0F, 0F, 0F, 0F).GetHashCode()));
            Assert.That(new Vector4(0F, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0));
            float nanA = float.NaN;                                                     // 0x7FC00000
            float nanB = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00001));    // a different NaN payload and sign
            Assert.That(Bits(nanA), Is.Not.EqualTo(Bits(nanB)));
            Assert.That(new Vector4(nanA, 1F, 2F, 3F).GetHashCode(), Is.EqualTo(new Vector4(nanB, 1F, 2F, 3F).GetHashCode()));
            // Infinity is NOT normalised (it falls outside the .NET Core folding branch).
            Assert.That(new Vector4(float.PositiveInfinity, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0x7F800000));
        }

        [Test]
        public void Vector4_MoveTowards_ZeroDeltaStaysPut()
        {
            // sqDist = 25 is neither 0 nor <= 0*0, so the divide branch runs: current + to/dist*0 = current.
            AssertBits(Vector4.MoveTowards(Vector4.one, new Vector4(4F, 5F, 1F, 1F), 0F), 1F, 1F, 1F, 1F);
        }

        [Test]
        public void Vector4_SqrMagnitude_KeepsSinglePrecisionIntermediates()
        {
            // 1e20f * 1e20f = 1e40 overflows float (max 3.4e38) → +∞. A double intermediate would give 1e40 and a
            // finite magnitude of 1e20, so this pins "keep every intermediate in float" (spec preamble, Notation).
            var big = new Vector4(1e20F, 0F, 0F, 0F);
            Assert.That(big.sqrMagnitude, Is.EqualTo(float.PositiveInfinity));
            Assert.That(big.magnitude, Is.EqualTo(float.PositiveInfinity));
            Assert.That(Vector4.Distance(Vector4.zero, big), Is.EqualTo(float.PositiveInfinity));
            // NaN propagates through Dot.
            Assert.That(float.IsNaN(new Vector4(float.NaN, 0F, 0F, 0F).sqrMagnitude), Is.True);
        }

        [Test]
        public void Vector4_EqualityOperatorIsTolerantWhereEqualsIsExact()
        {
            // Spec §0: operator== uses the squared-distance tolerance, Equals uses exact component equality.
            var a = Vector4.zero;
            var b = new Vector4(1e-6F, 0F, 0F, 0F);
            Assert.That(a == b, Is.True);
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a.Equals(a), Is.True);
        }

        [Test]
        public void Quaternion_kEpsilonIsACompileTimeConstant()
        {
            // Spec §9: "public const float kEpsilon = 0.000001F".
            FieldInfo f = typeof(Quaternion).GetField("kEpsilon", BindingFlags.Public | BindingFlags.Static);
            Assert.That(f, Is.Not.Null);
            Assert.That(f.IsLiteral, Is.True);
            Assert.That((float)f.GetRawConstantValue(), Is.EqualTo(0.000001F));
        }

        [Test]
        public void Quaternion_IdentityIsTheOnlyPreset()
        {
            // Spec §9 lists only `identity`, exposed as a static property (spec §0).
            Assert.That(typeof(Quaternion).GetProperty("identity", BindingFlags.Public | BindingFlags.Static), Is.Not.Null);
            foreach (string name in new[] { "zero", "one", "up", "down", "left", "right", "forward", "back" })
                Assert.That(typeof(Quaternion).GetProperty(name, BindingFlags.Public | BindingFlags.Static), Is.Null, name);
            Assert.That(Quaternion.identity.Equals(default(Quaternion)), Is.False);
        }

        [Test]
        public void Quaternion_GetHashCode_NormalisesSignedZeroAndNaN()
        {
            Assert.That(new Quaternion(-0F, -0F, -0F, -0F).GetHashCode(), Is.EqualTo(0));
            float nanB = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00001));
            Assert.That(new Quaternion(float.NaN, 1F, 2F, 3F).GetHashCode(), Is.EqualTo(new Quaternion(nanB, 1F, 2F, 3F).GetHashCode()));
            // Two differently-encoded NaN quaternions are Equals-equal (float.Equals) AND hash-equal — the hash/equals
            // contract holds for Quaternion where it deliberately does not for Vector4.
            var q = new Quaternion(float.NaN, 0F, 0F, 1F);
            var r = new Quaternion(nanB, 0F, 0F, 1F);
            Assert.That(q.Equals(r), Is.True);
            Assert.That(q.GetHashCode(), Is.EqualTo(r.GetHashCode()));
        }

        [Test]
        public void Quaternion_Angle_IsSymmetricAndSignInsensitive()
        {
            // dot enters through Abs, and Dot is symmetric.
            var a = Quaternion.Euler(10F, 20F, 30F);
            var b = Quaternion.Euler(-40F, 5F, 70F);
            Assert.That(Bits(Quaternion.Angle(a, b)), Is.EqualTo(Bits(Quaternion.Angle(b, a))));
            var nb = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            Assert.That(Bits(Quaternion.Angle(a, nb)), Is.EqualTo(Bits(Quaternion.Angle(a, b))));
        }

        [Test]
        public void Quaternion_RotateTowards_NegativeDeltaRotatesAway()
        {
            // Angle(identity, Euler(0,90,0)) = 90; t = Min(1, -45/90) = -0.5; dot = 0.70710677 < 0.95 so the sine
            // branch extrapolates backwards: sa = sin(0.7853982*-0.5) = -0.38268346, sb = sin(0.7853982*1.5) = 0.9238795,
            // invSin = 1/0.70710677 = 1.4142137 → y = 0.70710677*sa*invSin ≈ -0.3826835,
            // w = (0.9238795 + 0.70710677*sa)*invSin ≈ 0.9238796 — i.e. Euler(0, -45, 0).
            Quaternion r = Quaternion.RotateTowards(Quaternion.identity, Quaternion.Euler(0F, 90F, 0F), -45F);
            Quaternion expected = Quaternion.Euler(0F, -45F, 0F);
            AssertUlps(r, expected.x, expected.y, expected.z, expected.w, 8);
            Assert.That(r.y, Is.LessThan(0F));
        }

        [Test]
        public void Quaternion_eulerAngles_GetterDoesNotMutate()
        {
            var q = new Quaternion(1F, 2F, 3F, 4F);
            Vector3 e = q.eulerAngles;
            AssertBits(q, 1F, 2F, 3F, 4F);
            Assert.That(float.IsNaN(e.x), Is.False);
        }

        [Test]
        public void Quaternion_ObsoleteMembersAreMarkedObsolete()
        {
            foreach (string name in new[] { "EulerRotation", "SetEulerRotation", "ToEuler", "EulerAngles", "ToAxisAngle", "SetEulerAngles", "ToEulerAngles", "SetAxisAngle", "AxisAngle" })
            {
                MethodInfo[] methods = typeof(Quaternion).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                bool found = false;
                foreach (MethodInfo m in methods)
                {
                    if (m.Name != name) continue;
                    found = true;
                    Assert.That(m.GetCustomAttribute<ObsoleteAttribute>(), Is.Not.Null, name);
                }
                Assert.That(found, Is.True, name);
            }
        }
    }
}
