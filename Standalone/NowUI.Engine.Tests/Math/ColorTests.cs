// Tests for the UnityEngine.Color / Color32 / ColorSpace shims against Docs/Standalone/UnityValueTypeSemantics.md
// §4 (Color), §5 (Color32), §14 (ColorSpace) plus the cross-cutting rules in §0 and the notes in §15.
// Every [verified] value in the spec is asserted bit-exactly, except where the spec itself allows ulp slack (the native
// pow behind GammaToLinearSpace / LinearToGammaSpace: <= 3 ulp and <= 1 ulp respectively). Formula-only members are
// checked with values worked out by hand from the spec's expression; the arithmetic is shown in the comments.
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class ColorTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void AssertBits(Color actual, float r, float g, float b, float a)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Bits(actual.r), Is.EqualTo(Bits(r)), "r");
                Assert.That(Bits(actual.g), Is.EqualTo(Bits(g)), "g");
                Assert.That(Bits(actual.b), Is.EqualTo(Bits(b)), "b");
                Assert.That(Bits(actual.a), Is.EqualTo(Bits(a)), "a");
            });
        }

        private static void AssertBytes(Color32 actual, byte r, byte g, byte b, byte a)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.r, Is.EqualTo(r), "r");
                Assert.That(actual.g, Is.EqualTo(g), "g");
                Assert.That(actual.b, Is.EqualTo(b), "b");
                Assert.That(actual.a, Is.EqualTo(a), "a");
            });
        }

        // ==================================================================================================
        // §0 / §4: Color type shape
        // ==================================================================================================

        [Test]
        public void Color_StructShape()
        {
            Type t = typeof(Color);
            Assert.Multiple(() =>
            {
                Assert.That(t.IsValueType, Is.True);
                Assert.That(t.IsSerializable, Is.True, "[Serializable]");
                Assert.That(t.IsLayoutSequential, Is.True, "LayoutKind.Sequential");
                Assert.That(typeof(IEquatable<Color>).IsAssignableFrom(t), Is.True);
                Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
                Assert.That(Marshal.SizeOf<Color>(), Is.EqualTo(16), "four floats, no extra instance fields");
            });

            // Field order r, g, b, a is observable (arrays of Color are reinterpreted as float*).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(4));
            Assert.That(Array.ConvertAll(fields, f => f.Name), Is.EqualTo(new[] { "r", "g", "b", "a" }));
            Assert.That(Array.TrueForAll(fields, f => f.FieldType == typeof(float) && f.IsPublic), Is.True);
        }

        [Test]
        public unsafe void Color_MemoryLayoutMatchesFieldOrder()
        {
            Color c = new Color(1F, 2F, 3F, 4F);
            float* p = (float*)&c;
            Assert.That(new[] { p[0], p[1], p[2], p[3] }, Is.EqualTo(new[] { 1F, 2F, 3F, 4F }));
        }

        // ==================================================================================================
        // §4: constructors
        // ==================================================================================================

        [Test]
        public void Constructor_FourArgs_AssignsAllChannels()
        {
            AssertBits(new Color(0.1F, 0.2F, 0.3F, 0.4F), 0.1F, 0.2F, 0.3F, 0.4F);
        }

        [Test]
        public void Constructor_ThreeArgs_SetsAlphaToOne()
        {
            AssertBits(new Color(0.1F, 0.2F, 0.3F), 0.1F, 0.2F, 0.3F, 1F);
        }

        [Test]
        public void DefaultColor_IsAllZero()
        {
            AssertBits(default(Color), 0F, 0F, 0F, 0F);
        }

        // ==================================================================================================
        // §4: indexer
        // ==================================================================================================

        [Test]
        public void Indexer_Get_MapsToRGBA()
        {
            Color c = new Color(0.1F, 0.2F, 0.3F, 0.4F);
            Assert.Multiple(() =>
            {
                Assert.That(c[0], Is.EqualTo(0.1F));
                Assert.That(c[1], Is.EqualTo(0.2F));
                Assert.That(c[2], Is.EqualTo(0.3F));
                Assert.That(c[3], Is.EqualTo(0.4F));
            });
        }

        [Test]
        public void Indexer_Set_MapsToRGBA()
        {
            Color c = default;
            c[0] = 0.5F;
            c[1] = 0.6F;
            c[2] = 0.7F;
            c[3] = 0.8F;
            AssertBits(c, 0.5F, 0.6F, 0.7F, 0.8F);
        }

        [TestCase(4)]
        [TestCase(-1)]
        [TestCase(100)]
        public void Indexer_Get_InvalidIndex_ThrowsWithIndexEmbedded(int index)
        {
            Color c = Color.red;
            var ex = Assert.Throws<IndexOutOfRangeException>(() => _ = c[index]);
            Assert.That(ex.Message, Is.EqualTo("Invalid Color index(" + index + ")!"));
        }

        [TestCase(4)]
        [TestCase(-1)]
        public void Indexer_Set_InvalidIndex_ThrowsWithIndexEmbedded(int index)
        {
            var ex = Assert.Throws<IndexOutOfRangeException>(() =>
            {
                Color c = Color.red;
                c[index] = 1F;
            });
            Assert.That(ex.Message, Is.EqualTo("Invalid Color index(" + index + ")!"));
        }

        [Test]
        public void Indexer_ExactMessageSample()
        {
            // Spec §4 sample: "Invalid Color index(4)!"
            Color c = default;
            var ex = Assert.Throws<IndexOutOfRangeException>(() => _ = c[4]);
            Assert.That(ex.Message, Is.EqualTo("Invalid Color index(4)!"));
        }

        // ==================================================================================================
        // §0 / §4: ToString family
        // ==================================================================================================

        [Test]
        public void ToString_Default_UsesF3AndTemplate()
        {
            // Spec §4 [verified]: Color.red.ToString() = "RGBA(1.000, 0.000, 0.000, 1.000)".
            Assert.That(Color.red.ToString(), Is.EqualTo("RGBA(1.000, 0.000, 0.000, 1.000)"));
        }

        [Test]
        public void ToString_Default_RoundsToThreeDecimals()
        {
            Assert.That(Color.yellow.ToString(), Is.EqualTo("RGBA(1.000, 0.922, 0.016, 1.000)"));
            Assert.That(new Color(0.12345F, 1.5F, -0.5F, 0F).ToString(), Is.EqualTo("RGBA(0.123, 1.500, -0.500, 0.000)"));
        }

        [Test]
        public void ToString_WithFormat_AppliesToEachChannel()
        {
            // The channel values are deliberately not decimal midpoints: .NET's own "F"/"E" formatting rounds a
            // midpoint to even (0.25f.ToString("F1") is "0.2"), which is a float-formatting property, not a Color
            // one. Spec §0 only says each channel goes through component.ToString(format, provider) and the results
            // are spliced into "RGBA({0}, {1}, {2}, {3})", so the test pins the splice, not .NET's tie-breaking.
            Color c = new Color(0.5F, 0.26F, 1F, 0F);
            Assert.Multiple(() =>
            {
                Assert.That(c.ToString("F1"), Is.EqualTo("RGBA(0.5, 0.3, 1.0, 0.0)"));
                Assert.That(c.ToString("F2"), Is.EqualTo("RGBA(0.50, 0.26, 1.00, 0.00)"));
                Assert.That(c.ToString("E2"), Is.EqualTo("RGBA(5.00E-001, 2.60E-001, 1.00E+000, 0.00E+000)"));
                Assert.That(new Color(1.4F, 2.6F, 0.9F, 0F).ToString("F0"), Is.EqualTo("RGBA(1, 3, 1, 0)"));
            });
        }

        [Test]
        public void ToString_NullOrEmptyFormat_FallsBackToF3()
        {
            Color c = new Color(0.5F, 0.25F, 1F, 0F);
            Assert.Multiple(() =>
            {
                Assert.That(c.ToString(null), Is.EqualTo("RGBA(0.500, 0.250, 1.000, 0.000)"));
                Assert.That(c.ToString(""), Is.EqualTo("RGBA(0.500, 0.250, 1.000, 0.000)"));
                Assert.That(c.ToString(null, null), Is.EqualTo("RGBA(0.500, 0.250, 1.000, 0.000)"));
                Assert.That(((IFormattable)c).ToString(null, null), Is.EqualTo("RGBA(0.500, 0.250, 1.000, 0.000)"));
            });
        }

        [Test]
        public void ToString_WithProvider_UsesThatCulture()
        {
            Color c = new Color(0.5F, 0.26F, 1F, 0F);
            var de = CultureInfo.GetCultureInfo("de-DE");
            Assert.Multiple(() =>
            {
                Assert.That(c.ToString(null, de), Is.EqualTo("RGBA(0,500, 0,260, 1,000, 0,000)"));
                Assert.That(c.ToString("F1", de), Is.EqualTo("RGBA(0,5, 0,3, 1,0, 0,0)"));
            });
        }

        [Test]
        public void ToString_NullProvider_IsInvariantEvenUnderCommaCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Color c = new Color(0.5F, 0.26F, 1F, 0F);
                Assert.That(c.ToString(), Is.EqualTo("RGBA(0.500, 0.260, 1.000, 0.000)"));
                Assert.That(c.ToString("F1"), Is.EqualTo("RGBA(0.5, 0.3, 1.0, 0.0)"));
                Assert.That(c.ToString("F1", null), Is.EqualTo("RGBA(0.5, 0.3, 1.0, 0.0)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void ToString_SpecialValues()
        {
            Assert.That(new Color(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0F).ToString(),
                Is.EqualTo("RGBA(NaN, Infinity, -Infinity, 0.000)"));
        }

        // ==================================================================================================
        // §0 / §4: GetHashCode
        // ==================================================================================================

        [Test]
        public void GetHashCode_MatchesFormulaAndVector4()
        {
            // r ^ (g << 2) ^ (b >> 2) ^ (a >> 1) on raw float bits:
            // 1 = 0x3F800000, 2 = 0x40000000 (<<2 overflows to 0), 3 = 0x40400000 (>>2 = 0x10100000),
            // 4 = 0x40800000 (>>1 = 0x20400000) → 0x3F800000 ^ 0 ^ 0x10100000 ^ 0x20400000 = 0x0FD00000 = 265289728,
            // the same value the spec verifies for Vector4(1,2,3,4) (§3).
            Assert.That(new Color(1F, 2F, 3F, 4F).GetHashCode(), Is.EqualTo(265289728));
            Assert.That(new Color(1F, 2F, 3F, 4F).GetHashCode(), Is.EqualTo(new Vector4(1F, 2F, 3F, 4F).GetHashCode()));
        }

        [Test]
        public void GetHashCode_SecondSample()
        {
            // 0.5 = 0x3F000000; 0.25 = 0x3E800000 << 2 = 0xFA000000; -1 = 0xBF800000 >> 2 (arithmetic) = 0xEFE00000;
            // 2 = 0x40000000 >> 1 = 0x20000000. 0x3F000000 ^ 0xFA000000 = 0xC5000000; ^ 0xEFE00000 = 0x2AE00000;
            // ^ 0x20000000 = 0x0AE00000 = 182452224.
            Assert.That(new Color(0.5F, 0.25F, -1F, 2F).GetHashCode(), Is.EqualTo(182452224));
            Assert.That(new Color(0.5F, 0.25F, -1F, 2F).GetHashCode(), Is.EqualTo(
                (0.5F).GetHashCode() ^ ((0.25F).GetHashCode() << 2) ^ ((-1F).GetHashCode() >> 2) ^ ((2F).GetHashCode() >> 1)));
        }

        [Test]
        public void GetHashCode_NormalisesSignedZeroAndNaN_OnDotNetCore()
        {
            // Spec §0 flags this explicitly: float.GetHashCode returns the raw IEEE bits, except that .NET Core
            // normalises -0 (-> +0) and every NaN payload (-> 0x7F800000) first. The spec's verified hash oracles
            // (Vector2(1,2) = 0x3F800000 etc.) were probed on Mono, where the raw bits are returned; they agree with
            // .NET Core for all non-zero, non-NaN inputs, which is every oracle the spec gives. There is no verified
            // Unity value for a NaN or -0 channel, so this test pins the runtime the shim actually runs on.
            Assert.Multiple(() =>
            {
                Assert.That(new Color(-0F, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(new Color(0F, 0F, 0F, 0F).GetHashCode()));
                Assert.That(new Color(-0F, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0));
                // NaN normalises to the positive-infinity bit pattern 0x7F800000, whatever the payload or sign.
                float nanA = float.NaN;
                float nanB = BitConverter.Int32BitsToSingle(0x7FC00001);
                float nanC = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00000));
                Assert.That(Bits(nanB), Is.Not.EqualTo(Bits(nanA)), "distinct NaN payloads");
                Assert.That(new Color(nanA, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0x7F800000));
                Assert.That(new Color(nanB, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0x7F800000));
                Assert.That(new Color(nanC, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0x7F800000));
            });
        }

        [Test]
        public void Lerp_NaNT_PropagatesThroughClamp01()
        {
            // Spec §11: Mathf.Clamp01 passes NaN through unchanged (NaN < 0 and NaN > 1 are both false), so
            // a + (b - a) * NaN = NaN on every channel.
            Color res = Color.Lerp(Color.black, Color.white, float.NaN);
            Assert.Multiple(() =>
            {
                Assert.That(res.r, Is.NaN);
                Assert.That(res.g, Is.NaN);
                Assert.That(res.b, Is.NaN);
                Assert.That(res.a, Is.NaN, "alpha: 1 + (1 - 1) * NaN = NaN");
            });
            Assert.That(Color.LerpUnclamped(Color.black, Color.white, float.NaN).r, Is.NaN);
        }

        [Test]
        public void GetHashCode_ZeroIsZero_AndDiffersFromRed()
        {
            Assert.That(new Color(0F, 0F, 0F, 0F).GetHashCode(), Is.EqualTo(0));
            Assert.That(Color.red.GetHashCode(), Is.Not.EqualTo(Color.green.GetHashCode()));
            Assert.That(Color.red.GetHashCode(), Is.EqualTo(Color.red.GetHashCode()));
        }

        // ==================================================================================================
        // §0 / §4: Equals vs ==
        // ==================================================================================================

        [Test]
        public void Equals_Color_IsExactComponentwise()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Color.red.Equals(new Color(1F, 0F, 0F, 1F)), Is.True);
                Assert.That(Color.red.Equals(new Color(1F, 1e-7F, 0F, 1F)), Is.False, "exact, no tolerance");
                Assert.That(Color.red.Equals(new Color(1F, 0F, 0F, 0.9999999F)), Is.False, "alpha compared");
            });
        }

        [Test]
        public void Equals_UsesFloatEquals_SoNaNEqualsNaN()
        {
            // Spec §0 [verified]: new Color(NaN,0,0,0).Equals(itself) = true.
            Color nan = new Color(float.NaN, 0F, 0F, 0F);
            Assert.That(nan.Equals(nan), Is.True);
            Assert.That(nan.Equals(new Color(float.NaN, 0F, 0F, 0F)), Is.True);
            Assert.That(new Color(0F, float.NaN, float.NaN, float.NaN).Equals(new Color(0F, float.NaN, float.NaN, float.NaN)), Is.True);
            Assert.That(nan.Equals(Color.black), Is.False);
        }

        [Test]
        public void Equals_NegativeZeroEqualsPositiveZero()
        {
            // float.Equals(-0f, 0f) is true (numeric equality), so signed zeros are equal.
            Assert.That(new Color(-0F, 0F, 0F, 0F).Equals(new Color(0F, 0F, 0F, 0F)), Is.True);
        }

        [Test]
        public void Equals_Object()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Color.red.Equals((object)Color.red), Is.True);
                Assert.That(Color.red.Equals((object)Color.blue), Is.False);
                Assert.That(Color.red.Equals(null), Is.False);
                Assert.That(Color.red.Equals("RGBA(1.000, 0.000, 0.000, 1.000)"), Is.False);
                Assert.That(Color.red.Equals((object)new Vector4(1F, 0F, 0F, 1F)), Is.False, "Vector4 box is not a Color");
                Assert.That(Color.red.Equals((object)new Color32(255, 0, 0, 255)), Is.False, "Color32 box is not a Color");
            });
        }

        [Test]
        public void OperatorEquals_VerifiedToleranceBoundary()
        {
            // Spec §4 [verified]: red == (1,1e-6,0,1) true; (1,1e-5,0,1) false.
            Assert.That(Color.red == new Color(1F, 1e-6F, 0F, 1F), Is.True);
            Assert.That(Color.red == new Color(1F, 1e-5F, 0F, 1F), Is.False);
            Assert.That(Color.red != new Color(1F, 1e-6F, 0F, 1F), Is.False);
            Assert.That(Color.red != new Color(1F, 1e-5F, 0F, 1F), Is.True);
        }

        [Test]
        public void OperatorEquals_IsSquaredDistanceOverAllFourChannels()
        {
            // sqrmag < kEpsilon*kEpsilon, with kEpsilon*kEpsilon = f32(1e-5f*1e-5f) = 9.9999994e-11 [0x2EDBE6FE].
            // Four deltas of 5e-6: 4 * (5e-6)^2 = f32 9.9999994e-11 → not strictly less → false.
            // Four deltas of 4e-6: 4 * 1.6e-11 = 6.4e-11 < 1e-10 → true.
            // Color.black is (0,0,0,1), so the all-zero reference here is Color.clear.
            Color z = Color.clear;
            Assert.That(z == new Color(4e-6F, 4e-6F, 4e-6F, 4e-6F), Is.True);
            Assert.That(z == new Color(5e-6F, 5e-6F, 5e-6F, 5e-6F), Is.False);
            // Alpha alone participates: an alpha delta of 1e-5 fails, 1e-6 passes.
            Assert.That(z == new Color(0F, 0F, 0F, 1e-5F), Is.False);
            Assert.That(z == new Color(0F, 0F, 0F, 1e-6F), Is.True);
            // A single delta of exactly kEpsilon: 1e-5^2 = kEpsilon^2, not strictly less → false.
            Assert.That(z == new Color(Vector4.kEpsilon, 0F, 0F, 0F), Is.False);
            // Black itself keeps alpha 1, so black == (0,0,0,1) but black != Color.clear.
            Assert.That(Color.black == new Color(0F, 0F, 0F, 1F), Is.True);
            Assert.That(Color.black == Color.clear, Is.False);
        }

        [Test]
        public void OperatorEquals_NaN_IsFalse_AndNotEqualsIsTrue()
        {
            Color nan = new Color(float.NaN, 0F, 0F, 0F);
#pragma warning disable CS1718 // comparison made to same variable is intentional
            Assert.That(nan == nan, Is.False);
            Assert.That(nan != nan, Is.True);
#pragma warning restore CS1718
            Assert.That(nan == Color.black, Is.False);
            Assert.That(nan != Color.black, Is.True);
        }

        [Test]
        public void OperatorEquals_Infinity()
        {
            // inf - inf = NaN → sqrmag NaN → false, even for identical infinite colours.
            Color inf = new Color(float.PositiveInfinity, 0F, 0F, 1F);
#pragma warning disable CS1718
            Assert.That(inf == inf, Is.False);
#pragma warning restore CS1718
            Assert.That(inf.Equals(inf), Is.True, "float.Equals(inf, inf) is true");
        }

        [Test]
        public void OperatorEquals_IdenticalColours()
        {
            Assert.That(Color.red == Color.red, Is.True);
            Assert.That(Color.red != Color.red, Is.False);
            Assert.That(Color.red == Color.green, Is.False);
            Assert.That(Color.gray == Color.grey, Is.True);
        }

        // ==================================================================================================
        // §4: derived values
        // ==================================================================================================

        [Test]
        public void Grayscale_Formula()
        {
            // 0.299*r + 0.587*g + 0.114*b in float:
            // white: 0.299 + 0.587 = 0.886; + 0.114 = 1.0 (float: exactly 1 [0x3F800000]).
            Assert.That(Bits(Color.white.grayscale), Is.EqualTo(Bits(1F)));
            // red: 0.299 [0x3E991687]; green: 0.587; blue: 0.114.
            Assert.That(Bits(Color.red.grayscale), Is.EqualTo(Bits(0.299F)));
            Assert.That(Bits(Color.green.grayscale), Is.EqualTo(Bits(0.587F)));
            Assert.That(Bits(Color.blue.grayscale), Is.EqualTo(Bits(0.114F)));
            // (0.5, 0.25, 0.125): 0.1495 + 0.14675 + 0.01425 = 0.3105 (float 0x3E9EF9DB).
            Assert.That(Bits(new Color(0.5F, 0.25F, 0.125F, 0F).grayscale), Is.EqualTo(Bits(0.3105F)));
            Assert.That(Bits(new Color(0.5F, 0.25F, 0.125F, 0F).grayscale), Is.EqualTo(0x3E9EF9DB));
            // Alpha is not part of the formula.
            Assert.That(new Color(0.5F, 0.25F, 0.125F, 0.9F).grayscale, Is.EqualTo(new Color(0.5F, 0.25F, 0.125F, 0F).grayscale));
            // Black is 0.
            Assert.That(Color.black.grayscale, Is.EqualTo(0F));
        }

        [Test]
        public void MaxColorComponent_ExcludesAlphaAndAllowsNegatives()
        {
            // Spec §4 [verified]: (0.2, 0.5, -3) → 0.5.
            Assert.That(new Color(0.2F, 0.5F, -3F).maxColorComponent, Is.EqualTo(0.5F));
            Assert.That(new Color(0.2F, 0.5F, -3F, 7F).maxColorComponent, Is.EqualTo(0.5F), "alpha excluded");
            Assert.That(new Color(-3F, -2F, -1F, 1F).maxColorComponent, Is.EqualTo(-1F));
            Assert.That(new Color(0.9F, 0.1F, 0.1F, 0F).maxColorComponent, Is.EqualTo(0.9F));
            Assert.That(new Color(0.1F, 0.1F, 0.9F, 0F).maxColorComponent, Is.EqualTo(0.9F));
            Assert.That(new Color(2F, 0.5F, 0.5F).maxColorComponent, Is.EqualTo(2F), "HDR not clamped");
        }

        [Test]
        public void MaxColorComponent_NaNFollowsMathfMaxOrdering()
        {
            // Mathf.Max(a, b) = a > b ? a : b → Max(NaN, x) = x, Max(x, NaN) = NaN (spec §11).
            // Max(Max(NaN, 0.5), 0.2) = Max(0.5, 0.2) = 0.5.
            Assert.That(new Color(float.NaN, 0.5F, 0.2F).maxColorComponent, Is.EqualTo(0.5F));
            // Max(Max(0.5, 0.2), NaN) = Max(0.5, NaN) = NaN.
            Assert.That(new Color(0.5F, 0.2F, float.NaN).maxColorComponent, Is.NaN);
            // Max(Max(0.5, NaN), 0.2) = Max(NaN, 0.2) = 0.2.
            Assert.That(new Color(0.5F, float.NaN, 0.2F).maxColorComponent, Is.EqualTo(0.2F));
        }

        [Test]
        public void Linear_AppliesGammaToLinearSpacePerChannel_AlphaUntouched()
        {
            // Spec §11 [verified]: G2L(1) = 1; G2L(1.5) = 2.4400616; G2L(2) = 4.594794; G2L(-0.5) = -0.03869969.
            // Native pow may differ by up to 3 ulp from MathF.Pow, which the spec allows.
            Color c = new Color(1F, 1.5F, 2F, 0.3F).linear;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(c.r), Is.EqualTo(Bits(1F)), "G2L(1) = 1 exactly");
                Assert.That(c.g, Is.EqualTo(2.4400616F).Within(3).Ulps, "G2L(1.5)");
                Assert.That(c.b, Is.EqualTo(4.594794F).Within(3).Ulps, "G2L(2)");
                Assert.That(Bits(c.a), Is.EqualTo(Bits(0.3F)), "alpha untouched");
            });

            // Linear branch (v <= 0.04045 → v / 12.92) is plain float division: -0.5 / 12.92 = -0.03869969.
            Color neg = new Color(-0.5F, 0.04045F, 0F, 1F).linear;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(neg.r), Is.EqualTo(Bits(-0.03869969F)), "G2L(-0.5)");
                // 0.04045 / 12.92 = 0.003130805 [0x3B4D2E31]
                Assert.That(Bits(neg.g), Is.EqualTo(Bits(0.04045F / 12.92F)), "G2L(0.04045) boundary uses the linear branch");
                Assert.That(Bits(neg.b), Is.EqualTo(0), "G2L(0) = 0");
            });

            // 0.5: pow((0.5+0.055)/1.055, 2.4) = pow(0.52606636, 2.4) = 0.21404114 (spec: exact native match at 0.5).
            Assert.That(new Color(0.5F, 0.5F, 0.5F, 1F).linear.r, Is.EqualTo(0.21404114F).Within(3).Ulps);
            // Alpha is untouched even when it would be affected by the curve.
            Assert.That(Bits(new Color(0F, 0F, 0F, 0.5F).linear.a), Is.EqualTo(Bits(0.5F)));
            // Each channel goes through the same function.
            Assert.That(Bits(new Color(0.5F, 0.5F, 0.5F, 1F).linear.r), Is.EqualTo(Bits(Mathf.GammaToLinearSpace(0.5F))));
        }

        [Test]
        public void Linear_SpecialValues()
        {
            Color c = new Color(float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NaN).linear;
            Assert.Multiple(() =>
            {
                Assert.That(c.r, Is.NaN, "G2L(NaN) = NaN");
                Assert.That(c.g, Is.EqualTo(float.PositiveInfinity), "G2L(+inf) = +inf");
                Assert.That(c.b, Is.EqualTo(float.NegativeInfinity), "G2L(-inf) = -inf / 12.92 = -inf");
                Assert.That(c.a, Is.NaN, "alpha passes through");
            });
        }

        [Test]
        public void Gamma_AppliesLinearToGammaSpacePerChannel_AlphaUntouched()
        {
            // Spec §11 [verified]: L2G(1) = 1; L2G(1.5) = 1.2023792; L2G(2) = 1.370351; L2G(0.5) = 0.7353569 (<= 1 ulp).
            Color c = new Color(1F, 1.5F, 2F, 0.3F).gamma;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(c.r), Is.EqualTo(Bits(1F)), "L2G(1) = 1 exactly");
                Assert.That(c.g, Is.EqualTo(1.2023792F).Within(1).Ulps, "L2G(1.5)");
                Assert.That(c.b, Is.EqualTo(1.370351F).Within(1).Ulps, "L2G(2)");
                Assert.That(Bits(c.a), Is.EqualTo(Bits(0.3F)), "alpha untouched");
            });
            Assert.That(new Color(0.5F, 0F, 0F, 1F).gamma.r, Is.EqualTo(0.7353569F).Within(1).Ulps, "L2G(0.5)");

            // v <= 0 → +0 (negatives, -0, -inf all give +0 [0x00000000]); v <= 0.0031308 → 12.92 * v (0.001 → 0.012920001).
            Color low = new Color(-0.5F, -0F, 0.001F, -2F).gamma;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(low.r), Is.EqualTo(0), "L2G(-0.5) = +0");
                Assert.That(Bits(low.g), Is.EqualTo(0), "L2G(-0) = +0 (positive zero)");
                Assert.That(Bits(low.b), Is.EqualTo(Bits(12.92F * 0.001F)), "L2G(0.001) = 12.92 * 0.001");
                Assert.That(Bits(low.a), Is.EqualTo(Bits(-2F)), "alpha untouched");
            });
            Assert.That(Bits(new Color(float.NegativeInfinity, 0F, 0F, 1F).gamma.r), Is.EqualTo(0), "L2G(-inf) = +0");
            Assert.That(new Color(float.NaN, float.PositiveInfinity, 0F, 1F).gamma.r, Is.NaN, "L2G(NaN) = NaN");
            Assert.That(new Color(float.NaN, float.PositiveInfinity, 0F, 1F).gamma.g, Is.EqualTo(float.PositiveInfinity), "L2G(+inf) = +inf");
        }

        [Test]
        public void GammaLinearRoundTrip_IsNotExact()
        {
            // Spec §11 [verified]: G2L(L2G(0.1)) = 0.09999997.
            float roundTrip = new Color(0.1F, 0F, 0F, 1F).gamma.linear.r;
            Assert.That(roundTrip, Is.EqualTo(0.09999997F).Within(3).Ulps);
        }

        [Test]
        public void RGBMultiplied_AndAlphaMultiplied_Internal()
        {
            Color c = new Color(0.5F, 0.25F, 1F, 0.8F);
            // RGBMultiplied(float): (r*m, g*m, b*m, a) → (0.5*2, 0.25*2, 1*2, 0.8) = (1, 0.5, 2, 0.8).
            AssertBits(c.RGBMultiplied(2F), 1F, 0.5F, 2F, 0.8F);
            // RGBMultiplied(Color): (r*m.r, g*m.g, b*m.b, a) → (0.5*0.5, 0.25*2, 1*0.1, 0.8); multiplier alpha ignored.
            AssertBits(c.RGBMultiplied(new Color(0.5F, 2F, 0.1F, 0F)), 0.25F, 0.5F, 0.1F, 0.8F);
            // AlphaMultiplied(float): (r, g, b, a*m) → alpha 0.8*0.5 = 0.4.
            AssertBits(c.AlphaMultiplied(0.5F), 0.5F, 0.25F, 1F, 0.8F * 0.5F);
        }

        // ==================================================================================================
        // §4: Lerp / LerpUnclamped
        // ==================================================================================================

        [Test]
        public void Lerp_ClampsT_AndInterpolatesAlpha()
        {
            Color a = new Color(0F, 0F, 0F, 0F);
            Color b = new Color(1F, 1F, 1F, 1F);
            AssertBits(Color.Lerp(a, b, 0.25F), 0.25F, 0.25F, 0.25F, 0.25F);
            AssertBits(Color.Lerp(a, b, 0F), 0F, 0F, 0F, 0F);
            AssertBits(Color.Lerp(a, b, 1F), 1F, 1F, 1F, 1F);
            AssertBits(Color.Lerp(a, b, 2F), 1F, 1F, 1F, 1F);
            AssertBits(Color.Lerp(a, b, -1F), 0F, 0F, 0F, 0F);
            AssertBits(Color.Lerp(a, b, float.PositiveInfinity), 1F, 1F, 1F, 1F);
            AssertBits(Color.Lerp(a, b, float.NegativeInfinity), 0F, 0F, 0F, 0F);
        }

        [Test]
        public void Lerp_UsesAPlusDeltaTimesT()
        {
            // a + (b - a) * t per channel: 0.1 + (0.7 - 0.1) * 0.3 = 0.1 + 0.59999996 * 0.3 = 0.28 [0x3E8F5C29].
            Color res = Color.Lerp(new Color(0.1F, 0.1F, 0.1F, 0.1F), new Color(0.7F, 0.7F, 0.7F, 0.7F), 0.3F);
            float expected = 0.1F + (0.7F - 0.1F) * 0.3F;
            AssertBits(res, expected, expected, expected, expected);
            Assert.That(Bits(res.r), Is.EqualTo(0x3E8F5C29));
            // Per-channel independence: red channel 1 → 0 at t = 0.25 gives 1 + (0 - 1) * 0.25 = 0.75.
            AssertBits(Color.Lerp(Color.red, Color.blue, 0.25F), 0.75F, 0F, 0.25F, 1F);
        }

        [Test]
        public void Lerp_AtT1_ReturnsBExactly_WhenRepresentable()
        {
            // a + (b - a) * 1 = a + (b - a); with a = 0.1, b = 0.7: 0.1 + 0.59999996 = 0.7 [0x3F333333]? 0.1 + 0.59999996 = 0.69999996
            // rounds to the float nearest 0.7 which is 0.7 itself (0x3F333333) — verified by direct evaluation.
            float expected = 0.1F + (0.7F - 0.1F) * 1F;
            Assert.That(Bits(Color.Lerp(new Color(0.1F, 0F, 0F, 1F), new Color(0.7F, 0F, 0F, 1F), 1F).r), Is.EqualTo(Bits(expected)));
        }

        [Test]
        public void LerpUnclamped_DoesNotClamp()
        {
            // 0.1 + (0.7 - 0.1) * 1.5 = 0.1 + 0.59999996 * 1.5 = 0.1 + 0.89999994 = 1.0 (float rounds to exactly 1).
            Color res = Color.LerpUnclamped(new Color(0.1F, 0.1F, 0.1F, 0.1F), new Color(0.7F, 0.7F, 0.7F, 0.7F), 1.5F);
            float expected = 0.1F + (0.7F - 0.1F) * 1.5F;
            AssertBits(res, expected, expected, expected, expected);
            Assert.That(Bits(res.r), Is.EqualTo(Bits(1F)));
            // 1 + (0.25 - 1) * -0.5 = 1 + (-0.75 * -0.5) = 1 + 0.375 = 1.375.
            AssertBits(Color.LerpUnclamped(Color.white, new Color(0.25F, 0.25F, 0.25F, 0.25F), -0.5F), 1.375F, 1.375F, 1.375F, 1.375F);
            // t = 2 from black to white → 2 everywhere, alpha included.
            AssertBits(Color.LerpUnclamped(Color.clear, Color.white, 2F), 2F, 2F, 2F, 2F);
        }

        [Test]
        public void Lerp_And_LerpUnclamped_AgreeInsideRange()
        {
            Color a = new Color(0.9F, 0.2F, 0.6F, 0.1F);
            Color b = new Color(0.3F, 0.8F, 0.4F, 0.7F);
            foreach (float t in new[] { 0F, 0.1F, 0.33F, 0.5F, 0.75F, 1F })
                Assert.That(Color.Lerp(a, b, t).Equals(Color.LerpUnclamped(a, b, t)), Is.True, "t = " + t);
        }

        // ==================================================================================================
        // §4: RGBToHSV
        // ==================================================================================================

        [Test]
        public void RGBToHSV_Yellow_Verified()
        {
            // Spec §4 [verified]: RGBToHSV(Color.yellow) = (0.15338646, 0.9843137, 1).
            // By hand: red dominant (offset 0, one = g = 0.92156863, two = b = 0.015686275); small = two;
            // diff = 1 - 0.015686275 = 0.98431373; S = diff / 1; H = (0.92156863 - 0.015686275) / 0.98431373 = 0.9203187;
            // H / 6 = 0.15338646 [0x3E1D1157], S = 0.9843137 [0x3F7BFBFC].
            Color.RGBToHSV(Color.yellow, out float h, out float s, out float v);
            Assert.Multiple(() =>
            {
                Assert.That(Bits(h), Is.EqualTo(Bits(0.15338646F)));
                Assert.That(Bits(s), Is.EqualTo(Bits(0.9843137F)));
                Assert.That(Bits(v), Is.EqualTo(Bits(1F)));
            });
        }

        [Test]
        public void RGBToHSV_HDR_VIsNotClamped()
        {
            // Spec §4 [verified]: (2, 0.5, 0.5) → H 0, S 0.75, V 2.
            // red dominant; small = 0.5; diff = 1.5; S = 1.5 / 2 = 0.75; H = 0 + (0.5 - 0.5) / 1.5 = 0.
            Color.RGBToHSV(new Color(2F, 0.5F, 0.5F), out float h, out float s, out float v);
            Assert.Multiple(() =>
            {
                Assert.That(Bits(h), Is.EqualTo(0));
                Assert.That(Bits(s), Is.EqualTo(Bits(0.75F)));
                Assert.That(Bits(v), Is.EqualTo(Bits(2F)));
            });
        }

        [Test]
        public void RGBToHSV_Primaries()
        {
            // red: offset 0, one = g = 0, two = b = 0 → small 0, diff 1, S 1, H 0 / 6 = 0.
            Color.RGBToHSV(Color.red, out float h, out float s, out float v);
            Assert.That((h, s, v), Is.EqualTo((0F, 1F, 1F)));
            // green: g > r → offset 2, one = b = 0, two = r = 0 → H = 2 / 6 = 0.33333334 [0x3EAAAAAB].
            Color.RGBToHSV(Color.green, out h, out s, out v);
            Assert.That(Bits(h), Is.EqualTo(Bits(2F / 6F)));
            Assert.That((s, v), Is.EqualTo((1F, 1F)));
            // blue: b > g && b > r → offset 4, one = r = 0, two = g = 0 → H = 4 / 6 = 0.6666667 [0x3F2AAAAB].
            Color.RGBToHSV(Color.blue, out h, out s, out v);
            Assert.That(Bits(h), Is.EqualTo(Bits(4F / 6F)));
            Assert.That((s, v), Is.EqualTo((1F, 1F)));
            // cyan (0,1,1): b > g is false (tie) → g > r → green case, one = b = 1, two = r = 0; diff = 1; H = 2 + 1 = 3; /6 = 0.5.
            Color.RGBToHSV(Color.cyan, out h, out s, out v);
            Assert.That((h, s, v), Is.EqualTo((0.5F, 1F, 1F)));
            // magenta (1,0,1): b > r false (tie) → g > r false → red case, one = g = 0, two = b = 1; diff = 1;
            // H = 0 + (0 - 1) / 1 = -1; /6 = -0.16666667; < 0 → +1 = 0.8333333.
            Color.RGBToHSV(Color.magenta, out h, out s, out v);
            Assert.That(Bits(h), Is.EqualTo(Bits(-1F / 6F + 1F)));
            Assert.That((s, v), Is.EqualTo((1F, 1F)));
            // yellow-ish (1,1,0): red case, one = g = 1, two = b = 0; H = 1 / 6 = 0.16666667.
            Color.RGBToHSV(new Color(1F, 1F, 0F), out h, out s, out v);
            Assert.That(Bits(h), Is.EqualTo(Bits(1F / 6F)));
            Assert.That((s, v), Is.EqualTo((1F, 1F)));
        }

        [Test]
        public void RGBToHSV_NegativeHueWraps()
        {
            // (1, 0, 0.5): red case, one = g = 0, two = b = 0.5; small = 0; diff = 1; S = 1;
            // H = (0 - 0.5) / 1 = -0.5; / 6 = -0.083333336; +1 = 0.9166667 [0x3F6AAAAB].
            Color.RGBToHSV(new Color(1F, 0F, 0.5F), out float h, out float s, out float v);
            Assert.Multiple(() =>
            {
                Assert.That(Bits(h), Is.EqualTo(Bits(0.9166667F)));
                Assert.That(Bits(h), Is.EqualTo(0x3F6AAAAB));
                Assert.That(s, Is.EqualTo(1F));
                Assert.That(v, Is.EqualTo(1F));
            });
        }

        [Test]
        public void RGBToHSV_BlueDominant_GeneralCase()
        {
            // (0.25, 0.5, 0.75): b dominant, offset 4, one = r = 0.25, two = g = 0.5; small = 0.25; diff = 0.5;
            // S = 0.5 / 0.75 = 0.6666667; H = 4 + (0.25 - 0.5) / 0.5 = 3.5; / 6 = 0.5833333 [0x3F155555].
            Color.RGBToHSV(new Color(0.25F, 0.5F, 0.75F), out float h, out float s, out float v);
            Assert.Multiple(() =>
            {
                Assert.That(Bits(h), Is.EqualTo(0x3F155555));
                Assert.That(Bits(s), Is.EqualTo(Bits(0.5F / 0.75F)));
                Assert.That(Bits(v), Is.EqualTo(Bits(0.75F)));
            });
        }

        [Test]
        public void RGBToHSV_GreenDominant_GeneralCase()
        {
            // (0.2, 0.9, 0.4): b > g false → g > r → offset 2, one = b = 0.4, two = r = 0.2; small = 0.2; diff = 0.7;
            // S = 0.7 / 0.9 = 0.7777778 [0x3F471C72]; H = 2 + (0.4 - 0.2) / 0.7 = 2.2857143; / 6 = 0.3809524 [0x3EC30C31].
            Color.RGBToHSV(new Color(0.2F, 0.9F, 0.4F), out float h, out float s, out float v);
            float diff = 0.9F - 0.2F;
            float expectedS = diff / 0.9F;
            float expectedH = (2F + (0.4F - 0.2F) / diff) / 6F;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(h), Is.EqualTo(Bits(expectedH)));
                Assert.That(Bits(h), Is.EqualTo(0x3EC30C31));
                Assert.That(Bits(s), Is.EqualTo(Bits(expectedS)));
                Assert.That(Bits(s), Is.EqualTo(0x3F471C72));
                Assert.That(Bits(v), Is.EqualTo(Bits(0.9F)));
            });
        }

        [Test]
        public void RGBToHSV_Greys_GiveZeroHueAndSaturation()
        {
            // (0.5,0.5,0.5): red case, one = two = 0.5; diff = 0 → S = 0; H = 0 + (0.5 - 0.5) = 0 → 0.
            Color.RGBToHSV(Color.gray, out float h, out float s, out float v);
            Assert.That((h, s, v), Is.EqualTo((0F, 0F, 0.5F)));
            Color.RGBToHSV(Color.white, out h, out s, out v);
            Assert.That((h, s, v), Is.EqualTo((0F, 0F, 1F)));
            // Black: V == 0 → S = 0, H = 0.
            Color.RGBToHSV(Color.black, out h, out s, out v);
            Assert.That((h, s, v), Is.EqualTo((0F, 0F, 0F)));
            Assert.That(Bits(h), Is.EqualTo(0), "positive zero");
            Assert.That(Bits(s), Is.EqualTo(0), "positive zero");
        }

        [Test]
        public void RGBToHSV_IgnoresAlpha()
        {
            Color.RGBToHSV(new Color(0.2F, 0.9F, 0.4F, 0F), out float h1, out float s1, out float v1);
            Color.RGBToHSV(new Color(0.2F, 0.9F, 0.4F, 0.37F), out float h2, out float s2, out float v2);
            Assert.That((h1, s1, v1), Is.EqualTo((h2, s2, v2)));
        }

        [Test]
        public void RGBToHSV_HSVToRGB_RoundTripsPrimaries()
        {
            foreach (Color c in new[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta, Color.white, Color.black, Color.gray })
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                Assert.That(Color.HSVToRGB(h, s, v).Equals(c), Is.True, c.ToString());
            }
        }

        // ==================================================================================================
        // §4: HSVToRGB
        // ==================================================================================================

        [Test]
        public void HSVToRGB_VerifiedSamples()
        {
            // Spec §4 [verified]: HSVToRGB(1,1,1) = red: h6 = 6, sector 6, t = 0, p = 0, q = 1, u = 1*(1 - 1*(1 - 0)) = 0 → (V,u,p) = (1,0,0).
            AssertBits(Color.HSVToRGB(1F, 1F, 1F), 1F, 0F, 0F, 1F);
            // HSVToRGB(-0.1,1,1) = (1,0,0.6): h6 = -0.6, sector = -1, t = -0.6 - (-1) = 0.39999998, p = 0, q = 1 - 0.39999998 = 0.6 → (V,p,q).
            AssertBits(Color.HSVToRGB(-0.1F, 1F, 1F), 1F, 0F, 0.6F, 1F);
            // HSVToRGB(1.2,1,2,true) = (0,0,0,1): h6 = 7.2, sector 7 has no case → rgb stays (0,0,0), alpha from white.
            AssertBits(Color.HSVToRGB(1.2F, 1F, 2F, true), 0F, 0F, 0F, 1F);
            // HSVToRGB(0,0,0.5) = (0.5,0.5,0.5): S == 0 → (V,V,V).
            AssertBits(Color.HSVToRGB(0F, 0F, 0.5F), 0.5F, 0.5F, 0.5F, 1F);
        }

        [Test]
        public void HSVToRGB_ThreeArgOverload_IsHdr()
        {
            // HSVToRGB(H,S,V) = HSVToRGB(H,S,V,true): V = 2, sector 0 → (2, u, p) with u = 2*(1 - 1*(1 - 0)) = 0, p = 0 → unclamped 2.
            AssertBits(Color.HSVToRGB(0F, 1F, 2F), 2F, 0F, 0F, 1F);
            Assert.That(Color.HSVToRGB(0F, 1F, 2F).Equals(Color.HSVToRGB(0F, 1F, 2F, true)), Is.True);
        }

        [Test]
        public void HSVToRGB_NonHdr_ClampsChannels()
        {
            // Same inputs with hdr = false: each of r, g, b is Clamp01'd → (1, 0, 0).
            AssertBits(Color.HSVToRGB(0F, 1F, 2F, false), 1F, 0F, 0F, 1F);
            // Sector 4 with V = 2, S = 0.25: h6 = 4.5, t = 0.5, p = 2*(1-0.25) = 1.5, q = 2*(1-0.125) = 1.75,
            // u = 2*(1 - 0.25*0.5) = 1.75 → (u,p,V) = (1.75, 1.5, 2); clamped → (1,1,1).
            AssertBits(Color.HSVToRGB(0.75F, 0.25F, 2F, true), 1.75F, 1.5F, 2F, 1F);
            AssertBits(Color.HSVToRGB(0.75F, 0.25F, 2F, false), 1F, 1F, 1F, 1F);
        }

        [Test]
        public void HSVToRGB_NonHdr_ClampAppliesToEveryBranch()
        {
            // Spec §4 states the "if !hdr, each of r,g,b is Clamp01'd" step after, and outside, the sector table, i.e.
            // as a final unconditional step — so it also covers the achromatic S == 0 and V == 0 short-circuits, which
            // is the case an HSV colour picker hits constantly (a saturation-0 swatch with an HDR value).
            // unverified vs Unity: no [verified] oracle covers !hdr with S == 0; hdr = true is unaffected either way.
            AssertBits(Color.HSVToRGB(0.3F, 0F, 2F, false), 1F, 1F, 1F, 1F);
            AssertBits(Color.HSVToRGB(0.3F, 0F, 2F, true), 2F, 2F, 2F, 1F);
            AssertBits(Color.HSVToRGB(0.3F, 0F, -1F, false), 0F, 0F, 0F, 1F);
            AssertBits(Color.HSVToRGB(0.3F, 0F, -1F, true), -1F, -1F, -1F, 1F);
            // In-range greys are untouched by the clamp, and the V == 0 branch is already (0,0,0).
            AssertBits(Color.HSVToRGB(0.3F, 0F, 0.25F, false), 0.25F, 0.25F, 0.25F, 1F);
            AssertBits(Color.HSVToRGB(0.3F, 1F, 0F, false), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void HSVToRGB_ZeroValue_GivesBlack()
        {
            // S != 0, V == 0 → (0,0,0), alpha 1.
            AssertBits(Color.HSVToRGB(0.3F, 1F, 0F), 0F, 0F, 0F, 1F);
            AssertBits(Color.HSVToRGB(0.3F, 1F, 0F, false), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void HSVToRGB_ZeroSaturation_GivesGreyOfV_ForAnyHue()
        {
            AssertBits(Color.HSVToRGB(0.7F, 0F, 0.25F), 0.25F, 0.25F, 0.25F, 1F);
            AssertBits(Color.HSVToRGB(5F, 0F, 1F), 1F, 1F, 1F, 1F);
            AssertBits(Color.HSVToRGB(0F, 0F, 0F), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void HSVToRGB_AllSectors()
        {
            // With S = 1, V = 1 and t = 0 at each sector start: p = 0, q = 1, u = 0.
            AssertBits(Color.HSVToRGB(0F / 6F, 1F, 1F), 1F, 0F, 0F, 1F);        // sector 0: (V,u,p)
            AssertBits(Color.HSVToRGB(1F / 6F, 1F, 1F), 1F, 1F, 0F, 1F);        // sector 1: (q,V,p)
            AssertBits(Color.HSVToRGB(2F / 6F, 1F, 1F), 0F, 1F, 0F, 1F);        // sector 2: (p,V,u)
            AssertBits(Color.HSVToRGB(3F / 6F, 1F, 1F), 0F, 1F, 1F, 1F);        // sector 3: (p,q,V)
            AssertBits(Color.HSVToRGB(4F / 6F, 1F, 1F), 0F, 0F, 1F, 1F);        // sector 4: (u,p,V)
            AssertBits(Color.HSVToRGB(5F / 6F, 1F, 1F), 1F, 0F, 1F, 1F);        // sector 5: (V,p,q)
            AssertBits(Color.HSVToRGB(6F / 6F, 1F, 1F), 1F, 0F, 0F, 1F);        // sector 6: (V,u,p)
            // Note: 1F/6F*6F, 2F/6F*6F etc. round to exactly 1, 2, ... in float (checked below), so t = 0 in each case.
            Assert.That(Mathf.FloorToInt(1F / 6F * 6F), Is.EqualTo(1));
            Assert.That(Mathf.FloorToInt(5F / 6F * 6F), Is.EqualTo(5));
        }

        [Test]
        public void HSVToRGB_Sector1_GeneralCase()
        {
            // (0.3, 0.5, 0.8): h6 = 1.8000001, sector 1, t = 0.8000001, p = 0.8*(1-0.5) = 0.4, q = 0.8*(1 - 0.5*0.8000001) = 0.48,
            // u = 0.8*(1 - 0.5*(1-0.8000001)) = 0.72 → (q,V,p) = (0.48, 0.8, 0.4). Float intermediates reproduced below.
            float h6 = 0.3F * 6F;
            int sector = Mathf.FloorToInt(h6);
            float t = h6 - (float)sector;
            float p = 0.8F * (1F - 0.5F);
            float q = 0.8F * (1F - 0.5F * t);
            Assert.That(sector, Is.EqualTo(1));
            AssertBits(Color.HSVToRGB(0.3F, 0.5F, 0.8F), q, 0.8F, p, 1F);
        }

        [Test]
        public void HSVToRGB_Sector5_GeneralCase()
        {
            // (0.9, 1, 1): h6 = 5.3999996, sector 5, t = 0.39999962, p = 0, q = 1 - 0.39999962 = 0.6000004 [0x3F1999A0] → (V,p,q).
            float h6 = 0.9F * 6F;
            int sector = Mathf.FloorToInt(h6);
            float t = h6 - (float)sector;
            float q = 1F * (1F - 1F * t);
            Assert.That(sector, Is.EqualTo(5));
            Assert.That(Bits(q), Is.EqualTo(0x3F1999A0));
            AssertBits(Color.HSVToRGB(0.9F, 1F, 1F), 1F, 0F, q, 1F);
        }

        [Test]
        public void HSVToRGB_OutOfRangeHue_IsBlackNotWrapped()
        {
            // Spec §15 item 12: H*6 outside [-1, 7) returns black (alpha 1) rather than wrapping.
            AssertBits(Color.HSVToRGB(1.2F, 1F, 1F), 0F, 0F, 0F, 1F);      // sector 7
            AssertBits(Color.HSVToRGB(-0.2F, 1F, 1F), 0F, 0F, 0F, 1F);     // h6 = -1.2 → sector -2
            AssertBits(Color.HSVToRGB(2F, 1F, 1F), 0F, 0F, 0F, 1F);        // sector 12
            AssertBits(Color.HSVToRGB(-1F, 1F, 1F), 0F, 0F, 0F, 1F);       // sector -6
            AssertBits(Color.HSVToRGB(1.2F, 1F, 1F, false), 0F, 0F, 0F, 1F);
        }

        [Test]
        public void HSVToRGB_SectorMinusOne_UsesVPQ()
        {
            // h6 in [-1, 0): sector -1 → (V,p,q). H = -1/6 exactly: h6 = -1 → sector -1, t = 0, p = 0, q = 1 → (1,0,1) = magenta.
            AssertBits(Color.HSVToRGB(-1F / 6F, 1F, 1F), 1F, 0F, 1F, 1F);
        }

        // ==================================================================================================
        // §4: arithmetic operators
        // ==================================================================================================

        [Test]
        public void OperatorAdd_AllFourChannels()
        {
            // 0.1 + 0.2 = 0.3 [0x3E99999A] in float (not the double 0.30000000000000004).
            Color c = new Color(0.1F, 0.5F, 1F, 0.25F) + new Color(0.2F, 0.5F, 1F, 0.25F);
            AssertBits(c, 0.1F + 0.2F, 1F, 2F, 0.5F);
            Assert.That(Bits(c.r), Is.EqualTo(0x3E99999A));
        }

        [Test]
        public void OperatorSubtract_AllFourChannels()
        {
            // 0.7 - 0.1 = 0.59999996 [0x3F199999] in float.
            Color c = new Color(0.7F, 1F, 0.5F, 1F) - new Color(0.1F, 1F, 1F, 0.25F);
            AssertBits(c, 0.7F - 0.1F, 0F, -0.5F, 0.75F);
            Assert.That(Bits(c.r), Is.EqualTo(0x3F199999));
        }

        [Test]
        public void OperatorMultiply_ColorColor_AllFourChannels()
        {
            // 0.1 * 0.2 = 0.020000001 [0x3CA3D70B] in float.
            Color c = new Color(0.1F, 0.5F, 2F, 0.5F) * new Color(0.2F, 0.5F, 3F, 0.5F);
            AssertBits(c, 0.1F * 0.2F, 0.25F, 6F, 0.25F);
            Assert.That(Bits(c.r), Is.EqualTo(0x3CA3D70B));
        }

        [Test]
        public void OperatorMultiply_ColorVector4()
        {
            // (a.r*b.x, a.g*b.y, a.b*b.z, a.a*b.w)
            Color c = new Color(0.5F, 0.25F, 2F, 0.8F) * new Vector4(2F, 4F, 0.5F, 0.5F);
            AssertBits(c, 1F, 1F, 1F, 0.4F);
        }

        [Test]
        public void OperatorMultiply_Float_BothSides_AllFourChannels()
        {
            // 0.6 * 1.5 = 0.90000004 [0x3F666667] in float.
            Color c = new Color(0.6F, 0.5F, -1F, 0.25F);
            AssertBits(c * 1.5F, 0.6F * 1.5F, 0.75F, -1.5F, 0.375F);
            AssertBits(1.5F * c, 0.6F * 1.5F, 0.75F, -1.5F, 0.375F);
            Assert.That(Bits((c * 1.5F).r), Is.EqualTo(0x3F666667));
            Assert.That((c * 1.5F).Equals(1.5F * c), Is.True);
        }

        [Test]
        public void OperatorDivide_Float_AllFourChannels()
        {
            // 0.6 / 4 = 0.15 [0x3E19999A]; 0.3 / 0.7 = 0.42857146 [0x3EDB6DB8].
            Color c = new Color(0.6F, 1F, -2F, 0.5F) / 4F;
            AssertBits(c, 0.6F / 4F, 0.25F, -0.5F, 0.125F);
            Assert.That(Bits(c.r), Is.EqualTo(0x3E19999A));
            Assert.That(Bits((new Color(0.3F, 0F, 0F, 0F) / 0.7F).r), Is.EqualTo(0x3EDB6DB8));
            // Division by zero: IEEE semantics, no guard.
            Color z = new Color(1F, -1F, 0F, 1F) / 0F;
            Assert.That(z.r, Is.EqualTo(float.PositiveInfinity));
            Assert.That(z.g, Is.EqualTo(float.NegativeInfinity));
            Assert.That(z.b, Is.NaN);
        }

        [Test]
        public void Operators_AreNotClamped()
        {
            AssertBits(Color.white + Color.white, 2F, 2F, 2F, 2F);
            AssertBits(Color.black - Color.white, -1F, -1F, -1F, 0F);
            AssertBits(Color.white * 3F, 3F, 3F, 3F, 3F);
        }

        // ==================================================================================================
        // §4: Vector4 conversions
        // ==================================================================================================

        [Test]
        public void ImplicitConversion_ColorToVector4()
        {
            Vector4 v = new Color(0.1F, 0.2F, 0.3F, 0.4F);
            Assert.That((v.x, v.y, v.z, v.w), Is.EqualTo((0.1F, 0.2F, 0.3F, 0.4F)));
        }

        [Test]
        public void ImplicitConversion_Vector4ToColor()
        {
            Color c = new Vector4(0.1F, 0.2F, 0.3F, 0.4F);
            AssertBits(c, 0.1F, 0.2F, 0.3F, 0.4F);
            // Round trip preserves bits, including NaN payloads and out-of-range values.
            Color odd = new Color(float.NaN, -5F, 1e30F, float.NegativeInfinity);
            Vector4 v = odd;
            Color back = v;
            AssertBits(back, odd.r, odd.g, odd.b, odd.a);
        }

        [Test]
        public void ConversionOperators_AreDeclaredOnColor()
        {
            MethodInfo[] ops = typeof(Color).GetMethods(BindingFlags.Public | BindingFlags.Static);
            int implicitCount = 0;
            foreach (MethodInfo m in ops)
            {
                if (m.Name != "op_Implicit") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 1) continue;
                if ((ps[0].ParameterType == typeof(Color) && m.ReturnType == typeof(Vector4))
                    || (ps[0].ParameterType == typeof(Vector4) && m.ReturnType == typeof(Color)))
                    implicitCount++;
            }
            Assert.That(implicitCount, Is.EqualTo(2), "implicit Vector4(Color) and implicit Color(Vector4) live on Color");
        }

        // ==================================================================================================
        // §4: presets
        // ==================================================================================================

        [Test]
        public void Presets_BasicColours()
        {
            AssertBits(Color.red, 1F, 0F, 0F, 1F);
            AssertBits(Color.green, 0F, 1F, 0F, 1F);
            AssertBits(Color.blue, 0F, 0F, 1F, 1F);
            AssertBits(Color.white, 1F, 1F, 1F, 1F);
            AssertBits(Color.black, 0F, 0F, 0F, 1F);
            AssertBits(Color.cyan, 0F, 1F, 1F, 1F);
            AssertBits(Color.magenta, 1F, 0F, 1F, 1F);
            AssertBits(Color.gray, 0.5F, 0.5F, 0.5F, 1F);
            AssertBits(Color.grey, 0.5F, 0.5F, 0.5F, 1F);
            AssertBits(Color.gray5, 0.5F, 0.5F, 0.5F, 1F);
            AssertBits(Color.clear, 0F, 0F, 0F, 0F);
        }

        [Test]
        public void Preset_Yellow_IsByteOver255_NotDocsValue()
        {
            // Spec §4 [verified]: yellow = (1, 235/255, 4/255, 1) = (1, 0.92156863 [0x3F6BEBEC], 0.015686275 [0x3C808081], 1).
            Color y = Color.yellow;
            Assert.Multiple(() =>
            {
                Assert.That(Bits(y.r), Is.EqualTo(Bits(1F)));
                Assert.That(Bits(y.g), Is.EqualTo(0x3F6BEBEC));
                Assert.That(Bits(y.b), Is.EqualTo(0x3C808081));
                Assert.That(Bits(y.a), Is.EqualTo(Bits(1F)));
                Assert.That(y.g, Is.EqualTo(235F / 255F));
                Assert.That(y.b, Is.EqualTo(4F / 255F));
                Assert.That(y.g, Is.Not.EqualTo(0.92F), "docs print 0.92, source is 235/255");
                Assert.That(y.b, Is.Not.EqualTo(0.016F), "docs print 0.016, source is 4/255");
            });
            Assert.That(Color.yellowNice.Equals(Color.yellow), Is.True, "yellowNice = yellow");
        }

        [Test]
        public void Presets_GrayLevels()
        {
            AssertBits(Color.gray1, 0.1F, 0.1F, 0.1F, 1F);
            AssertBits(Color.gray2, 0.2F, 0.2F, 0.2F, 1F);
            AssertBits(Color.gray3, 0.3F, 0.3F, 0.3F, 1F);
            AssertBits(Color.gray4, 0.4F, 0.4F, 0.4F, 1F);
            AssertBits(Color.gray5, 0.5F, 0.5F, 0.5F, 1F);
            AssertBits(Color.gray6, 0.6F, 0.6F, 0.6F, 1F);
            AssertBits(Color.gray7, 0.7F, 0.7F, 0.7F, 1F);
            AssertBits(Color.gray8, 0.8F, 0.8F, 0.8F, 1F);
            AssertBits(Color.gray9, 0.9F, 0.9F, 0.9F, 1F);
        }

        [Test]
        public void Presets_AreStaticProperties_ReturningFreshValues()
        {
            foreach (string name in new[] { "red", "green", "blue", "white", "black", "yellow", "cyan", "magenta", "gray", "grey", "clear", "gray1", "gray9", "yellowNice" })
            {
                PropertyInfo p = typeof(Color).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(p, Is.Not.Null, name + " should be a public static property");
                Assert.That(p.PropertyType, Is.EqualTo(typeof(Color)), name);
                Assert.That(p.CanWrite, Is.False, name + " is read-only");
            }
            // Mutating a returned copy does not affect the preset (value type).
            Color r = Color.red;
            r.g = 1F;
            Assert.That(Color.red.g, Is.EqualTo(0F));
        }

        // ==================================================================================================
        // §5: Color32 type shape
        // ==================================================================================================

        [Test]
        public void Color32_StructShape()
        {
            Type t = typeof(Color32);
            Assert.Multiple(() =>
            {
                Assert.That(t.IsValueType, Is.True);
                Assert.That(t.IsSerializable, Is.True, "[Serializable]");
                Assert.That(t.IsExplicitLayout, Is.True, "explicit layout");
                Assert.That(typeof(IEquatable<Color32>).IsAssignableFrom(t), Is.True);
                Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
                Assert.That(Marshal.SizeOf<Color32>(), Is.EqualTo(4), "4 bytes");
            });
            FieldInfo[] pub = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.That(Array.ConvertAll(pub, f => f.Name), Is.EquivalentTo(new[] { "r", "g", "b", "a" }));
            Assert.That(Array.TrueForAll(pub, f => f.FieldType == typeof(byte)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(t.GetField("r").GetCustomAttribute<FieldOffsetAttribute>().Value, Is.EqualTo(0));
                Assert.That(t.GetField("g").GetCustomAttribute<FieldOffsetAttribute>().Value, Is.EqualTo(1));
                Assert.That(t.GetField("b").GetCustomAttribute<FieldOffsetAttribute>().Value, Is.EqualTo(2));
                Assert.That(t.GetField("a").GetCustomAttribute<FieldOffsetAttribute>().Value, Is.EqualTo(3));
            });
            FieldInfo rgba = t.GetField("rgba", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rgba, Is.Not.Null, "private int rgba");
            Assert.That(rgba.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(rgba.GetCustomAttribute<FieldOffsetAttribute>().Value, Is.EqualTo(0));
        }

        [Test]
        public void Color32_HasNoEqualityOperators()
        {
            // Spec §5: no ==/!= operators are defined on Color32.
            Assert.That(typeof(Color32).GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static), Is.Null);
            Assert.That(typeof(Color32).GetMethod("op_Inequality", BindingFlags.Public | BindingFlags.Static), Is.Null);
        }

        [Test]
        public unsafe void Color32_PackedLayout_LittleEndian()
        {
            Assume.That(BitConverter.IsLittleEndian, Is.True);
            Color32 c = new Color32(1, 2, 3, 4);
            int packed = *(int*)&c;
            // rgba == r | g<<8 | b<<16 | a<<24 = 0x04030201
            Assert.That(packed, Is.EqualTo(0x04030201));
            byte* p = (byte*)&c;
            Assert.That(new[] { p[0], p[1], p[2], p[3] }, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void Color32_Constructor_AndDefault()
        {
            AssertBytes(new Color32(10, 20, 30, 40), 10, 20, 30, 40);
            AssertBytes(default(Color32), 0, 0, 0, 0);
            AssertBytes(new Color32(255, 255, 255, 255), 255, 255, 255, 255);
        }

        // ==================================================================================================
        // §5: Color32 indexer
        // ==================================================================================================

        [Test]
        public void Color32_Indexer_GetAndSet()
        {
            Color32 c = new Color32(10, 20, 30, 40);
            Assert.That((c[0], c[1], c[2], c[3]), Is.EqualTo(((byte)10, (byte)20, (byte)30, (byte)40)));
            c[0] = 1; c[1] = 2; c[2] = 3; c[3] = 4;
            AssertBytes(c, 1, 2, 3, 4);
            Assert.That(c.GetHashCode(), Is.EqualTo(0x04030201), "indexer writes reach the packed int");
        }

        [TestCase(4)]
        [TestCase(-1)]
        [TestCase(255)]
        public void Color32_Indexer_InvalidIndex_ThrowsWithIndexEmbedded(int index)
        {
            Color32 c = new Color32(1, 2, 3, 4);
            var get = Assert.Throws<IndexOutOfRangeException>(() => _ = c[index]);
            Assert.That(get.Message, Is.EqualTo("Invalid Color32 index(" + index + ")!"));
            var set = Assert.Throws<IndexOutOfRangeException>(() =>
            {
                Color32 d = new Color32(1, 2, 3, 4);
                d[index] = 9;
            });
            Assert.That(set.Message, Is.EqualTo("Invalid Color32 index(" + index + ")!"));
        }

        // ==================================================================================================
        // §5: Color → Color32 (banker's rounding, clamp, NaN)
        // ==================================================================================================

        [Test]
        public void ColorToColor32_BankersRounding_Verified()
        {
            // Spec §5 [verified]: 127.5 → 128, 128.5 → 128, 0.5 → 0, 254.5 → 254.
            // Inputs chosen so that Clamp01(x) * 255f is exactly the half-way product in float:
            // 0.5 * 255 = 127.5; (128.5/255 = 0.50392157 [0x3F010101]) * 255 = 128.5; (0.5/255 = 0.0019607844) * 255 = 0.5;
            // (254.5/255 = 0.9980392 [0x3F7F7F7F]) * 255 = 254.5.
            Assert.That(Bits(0.5F * 255F), Is.EqualTo(Bits(127.5F)));
            Assert.That(Bits((128.5F / 255F) * 255F), Is.EqualTo(Bits(128.5F)));
            Assert.That(Bits((0.5F / 255F) * 255F), Is.EqualTo(Bits(0.5F)));
            Assert.That(Bits((254.5F / 255F) * 255F), Is.EqualTo(Bits(254.5F)));

            Color32 c = new Color(0.5F, 128.5F / 255F, 0.5F / 255F, 254.5F / 255F);
            AssertBytes(c, 128, 128, 0, 254);
        }

        [Test]
        public void ColorToColor32_MoreRoundingSamples()
        {
            // 1.5/255 * 255 = 1.5 → to-even 2; 0.25 * 255 = 63.75 → 64; 0.7 * 255 = 178.5 → to-even 178.
            Assert.That(Bits(0.7F * 255F), Is.EqualTo(Bits(178.5F)));
            Color32 c = new Color(1.5F / 255F, 0.25F, 0.7F, 1F);
            AssertBytes(c, 2, 64, 178, 255);
            // 1 → 255, 0 → 0; 128/255 → 128 (0.5019608 * 255 = 128 exactly)
            AssertBytes((Color32)new Color(1F, 0F, 128F / 255F, 77F / 255F), 255, 0, 128, 77);
        }

        [Test]
        public void ColorToColor32_ClampsOutOfRange()
        {
            // 1.5 alpha → Clamp01 → 1 → 255; negative → 0; huge → 255; -inf → 0; +inf → 255.
            AssertBytes((Color32)new Color(-0.5F, 2F, float.NegativeInfinity, 1.5F), 0, 255, 0, 255);
            AssertBytes((Color32)new Color(float.PositiveInfinity, -1e30F, 1e30F, -0F), 255, 0, 255, 0);
        }

        [Test]
        public void ColorToColor32_NaNBecomesZero()
        {
            // Spec §5 [verified]: NaN → 0 (Clamp01(NaN) = NaN; Round(NaN) = NaN; the shim special-cases the cast).
            AssertBytes((Color32)new Color(float.NaN, float.NaN, float.NaN, float.NaN), 0, 0, 0, 0);
            AssertBytes((Color32)new Color(1F, float.NaN, 0.5F, 1F), 255, 0, 128, 255);
        }

        [Test]
        public void ColorToColor32_Presets()
        {
            AssertBytes(Color.red, 255, 0, 0, 255);
            AssertBytes(Color.clear, 0, 0, 0, 0);
            AssertBytes(Color.gray, 128, 128, 128, 255);
            // yellow: 235/255 * 255 = 235 exactly? (0.92156863 * 255 = 235.00000 → 235); 4/255 * 255 → 4.
            AssertBytes(Color.yellow, 255, 235, 4, 255);
        }

        // ==================================================================================================
        // §5: Color32 → Color
        // ==================================================================================================

        [Test]
        public void Color32ToColor_DividesBy255_Verified()
        {
            // Spec §5 [verified]: (128,255,1,0) → (0.5019608, 1, 0.003921569, 0).
            Color c = new Color32(128, 255, 1, 0);
            AssertBits(c, 0.5019608F, 1F, 0.003921569F, 0F);
            Assert.Multiple(() =>
            {
                Assert.That(Bits(c.r), Is.EqualTo(Bits(128F / 255F)));
                Assert.That(Bits(c.r), Is.EqualTo(0x3F008081));
                Assert.That(Bits(c.b), Is.EqualTo(Bits(1F / 255F)));
                Assert.That(Bits(c.b), Is.EqualTo(0x3B808081));
            });
            // 77/255 = 0.3019608 [0x3E9A9A9B]
            Assert.That(Bits(((Color)new Color32(77, 0, 0, 0)).r), Is.EqualTo(0x3E9A9A9B));
        }

        [Test]
        public void Color32ToColor_RoundTripsEveryByte()
        {
            for (int i = 0; i <= 255; i++)
            {
                Color32 src = new Color32((byte)i, (byte)(255 - i), (byte)(i / 2), (byte)(i % 7));
                Color f = src;
                Color32 back = f;
                Assert.That(back.Equals(src), Is.True, "byte " + i);
            }
        }

        // ==================================================================================================
        // §5: Color32 Lerp / LerpUnclamped
        // ==================================================================================================

        [Test]
        public void Color32_Lerp_TruncatesTowardZero_Verified()
        {
            // Spec §5 [verified]: Lerp(0,255,0.5) → 127 (127.5 truncated); Lerp(0,255,0.999) → 254 (254.745 truncated).
            Color32 a = new Color32(0, 0, 0, 0);
            Color32 b = new Color32(255, 255, 255, 255);
            AssertBytes(Color32.Lerp(a, b, 0.5F), 127, 127, 127, 127);
            AssertBytes(Color32.Lerp(a, b, 0.999F), 254, 254, 254, 254);
        }

        [Test]
        public void Color32_Lerp_ClampsT()
        {
            Color32 a = new Color32(10, 20, 30, 40);
            Color32 b = new Color32(20, 10, 60, 0);
            AssertBytes(Color32.Lerp(a, b, -1F), 10, 20, 30, 40);
            AssertBytes(Color32.Lerp(a, b, 0F), 10, 20, 30, 40);
            AssertBytes(Color32.Lerp(a, b, 1F), 20, 10, 60, 0);
            AssertBytes(Color32.Lerp(a, b, 2F), 20, 10, 60, 0);
            AssertBytes(Color32.Lerp(a, b, float.PositiveInfinity), 20, 10, 60, 0);
        }

        [Test]
        public void Color32_Lerp_FloatArithmeticPerChannel()
        {
            // 10 + (20 - 10) * 0.25 = 12.5 → 12; 20 + (10 - 20) * 0.25 = 17.5 → 17; 30 + (60 - 30) * 0.25 = 37.5 → 37;
            // 40 + (0 - 40) * 0.25 = 30.
            Color32 a = new Color32(10, 20, 30, 40);
            Color32 b = new Color32(20, 10, 60, 0);
            AssertBytes(Color32.Lerp(a, b, 0.25F), 12, 17, 37, 30);
            // 200 + (100 - 200) * 0.3 = 200 - 30.000002 = 170 (float: 169.99999 → 169? check: (100-200)*0.3f = -30.000002; 200 + that = 170 exactly in float).
            Assert.That(Bits(200F + (100F - 200F) * 0.3F), Is.EqualTo(Bits(170F)));
            AssertBytes(Color32.Lerp(new Color32(200, 200, 200, 200), new Color32(100, 100, 100, 100), 0.3F), 170, 170, 170, 170);
        }

        [Test]
        public void Color32_LerpUnclamped_WrapsThroughByteCast_Verified()
        {
            // Spec §5 [verified]: LerpUnclamped(100, 0, 1.5) → 100 + (0 - 100) * 1.5 = -50f → byte 206.
            Color32 a = new Color32(100, 100, 100, 100);
            Color32 b = new Color32(0, 0, 0, 0);
            AssertBytes(Color32.LerpUnclamped(a, b, 1.5F), 206, 206, 206, 206);
            // Overflow the other way: 0 + (255 - 0) * 1.5 = 382.5 → truncate 382 → 382 & 0xFF = 126.
            AssertBytes(Color32.LerpUnclamped(b, new Color32(255, 255, 255, 255), 1.5F), 126, 126, 126, 126);
            // Inside [0,1] LerpUnclamped matches Lerp.
            AssertBytes(Color32.LerpUnclamped(b, new Color32(255, 255, 255, 255), 0.5F), 127, 127, 127, 127);
            AssertBytes(Color32.LerpUnclamped(new Color32(10, 20, 30, 40), new Color32(20, 10, 60, 0), 0.25F), 12, 17, 37, 30);
        }

        // ==================================================================================================
        // §5: Color32 hashing / equality
        // ==================================================================================================

        [Test]
        public void Color32_GetHashCode_IsPackedInt_Verified()
        {
            // Spec §5 [verified]: (1,2,3,4) → 67305985 = 0x04030201.
            Assert.That(new Color32(1, 2, 3, 4).GetHashCode(), Is.EqualTo(67305985));
            Assert.That(new Color32(1, 2, 3, 4).GetHashCode(), Is.EqualTo(0x04030201));
            Assert.That(new Color32(0, 0, 0, 0).GetHashCode(), Is.EqualTo(0));
            // a = 255 sets the sign bit: 255 << 24 = 0xFF000000 → -16777216.
            Assert.That(new Color32(0, 0, 0, 255).GetHashCode(), Is.EqualTo(unchecked((int)0xFF000000)));
            Assert.That(new Color32(255, 255, 255, 255).GetHashCode(), Is.EqualTo(-1));
            Assert.That(new Color32(0x78, 0x56, 0x34, 0x12).GetHashCode(), Is.EqualTo(0x12345678));
        }

        [Test]
        public void Color32_Equals_ComparesPackedInt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new Color32(1, 2, 3, 4).Equals(new Color32(1, 2, 3, 4)), Is.True);
                Assert.That(new Color32(1, 2, 3, 4).Equals(new Color32(1, 2, 3, 5)), Is.False, "alpha compared");
                Assert.That(new Color32(1, 2, 3, 4).Equals(new Color32(2, 1, 3, 4)), Is.False, "channel order matters");
                Assert.That(new Color32(1, 2, 3, 4).Equals((object)new Color32(1, 2, 3, 4)), Is.True);
                Assert.That(new Color32(1, 2, 3, 4).Equals((object)new Color32(1, 2, 3, 5)), Is.False);
                Assert.That(new Color32(1, 2, 3, 4).Equals(null), Is.False);
                Assert.That(new Color32(1, 2, 3, 4).Equals(67305985), Is.False, "boxed int is not a Color32");
                Assert.That(new Color32(255, 0, 0, 255).Equals((object)Color.red), Is.False, "Color box is not a Color32");
            });
        }

        // ==================================================================================================
        // §5: Color32 ToString
        // ==================================================================================================

        [Test]
        public void Color32_ToString_NoDefaultFormat()
        {
            // Spec §5: "RGBA(1, 2, 3, 4)" — bytes print as plain integers.
            Assert.That(new Color32(1, 2, 3, 4).ToString(), Is.EqualTo("RGBA(1, 2, 3, 4)"));
            Assert.That(new Color32(255, 0, 128, 7).ToString(), Is.EqualTo("RGBA(255, 0, 128, 7)"));
            Assert.That(new Color32(1, 2, 3, 4).ToString(null), Is.EqualTo("RGBA(1, 2, 3, 4)"));
            Assert.That(new Color32(1, 2, 3, 4).ToString(""), Is.EqualTo("RGBA(1, 2, 3, 4)"));
            Assert.That(new Color32(1, 2, 3, 4).ToString(null, null), Is.EqualTo("RGBA(1, 2, 3, 4)"));
        }

        [Test]
        public void Color32_ToString_FormatPassesThroughToBytes()
        {
            Assert.That(new Color32(1, 2, 3, 255).ToString("X2"), Is.EqualTo("RGBA(01, 02, 03, FF)"));
            Assert.That(new Color32(1, 2, 3, 255).ToString("D3"), Is.EqualTo("RGBA(001, 002, 003, 255)"));
            Assert.That(new Color32(1, 2, 3, 255).ToString("F1"), Is.EqualTo("RGBA(1.0, 2.0, 3.0, 255.0)"));
        }

        [Test]
        public void Color32_ToString_ProviderHandling()
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            Assert.That(new Color32(1, 2, 3, 255).ToString("F1", de), Is.EqualTo("RGBA(1,0, 2,0, 3,0, 255,0)"));
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = de;
                Assert.That(new Color32(1, 2, 3, 255).ToString("F1"), Is.EqualTo("RGBA(1.0, 2.0, 3.0, 255.0)"), "null provider → invariant");
                Assert.That(new Color32(1, 2, 3, 255).ToString("N0"), Is.EqualTo("RGBA(1, 2, 3, 255)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ==================================================================================================
        // §0: readonly members (getters and pure instance methods are readonly; mutators are not)
        // ==================================================================================================

        private static bool IsReadOnlyMember(MethodInfo m)
        {
            foreach (object a in m.GetCustomAttributes(false))
                if (a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute")
                    return true;
            return false;
        }

        [TestCase(typeof(Color))]
        [TestCase(typeof(Color32))]
        public void PureInstanceMembers_AreReadonly(Type t)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.Multiple(() =>
            {
                foreach (MethodInfo m in t.GetMethods(Flags))
                {
                    if (m.DeclaringType != t) continue;
                    if (m.Name == "ToString" || m.Name == "GetHashCode" || m.Name == "Equals")
                        Assert.That(IsReadOnlyMember(m), Is.True, t.Name + "." + m.Name + " should be readonly");
                }
                foreach (PropertyInfo p in t.GetProperties(Flags))
                {
                    MethodInfo get = p.GetGetMethod(true);
                    if (get != null)
                        Assert.That(IsReadOnlyMember(get), Is.True, t.Name + "." + p.Name + " getter should be readonly");
                    MethodInfo set = p.GetSetMethod(true);
                    if (set != null)
                        Assert.That(IsReadOnlyMember(set), Is.False, t.Name + "." + p.Name + " setter must not be readonly");
                }
            });
        }

        // ==================================================================================================
        // §14: ColorSpace enum
        // ==================================================================================================

        [Test]
        public void ColorSpace_Values()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)ColorSpace.Uninitialized, Is.EqualTo(-1));
                Assert.That((int)ColorSpace.Gamma, Is.EqualTo(0));
                Assert.That((int)ColorSpace.Linear, Is.EqualTo(1));
                Assert.That(Enum.GetUnderlyingType(typeof(ColorSpace)), Is.EqualTo(typeof(int)));
                Assert.That(Enum.GetValues(typeof(ColorSpace)).Length, Is.EqualTo(3));
                Assert.That(typeof(ColorSpace).Namespace, Is.EqualTo("UnityEngine"));
                Assert.That(default(ColorSpace), Is.EqualTo(ColorSpace.Gamma));
                Assert.That((ColorSpace)(-1), Is.EqualTo(ColorSpace.Uninitialized));
                Assert.That(ColorSpace.Linear.ToString(), Is.EqualTo("Linear"));
            });
        }

        [Test]
        public void GraphicsEnums_HaveUnitysExactValues()
        {
            // Spec §14 lists these with Unity's exact numeric values; NowUI's shared sources reference every one of them
            // (NowSdf uses RenderTextureFormat.R8/ARGB32, RenderTextureReadWrite.Linear, FilterMode.Bilinear,
            // TextureWrapMode.Clamp; NowMarkdownImages uses TextureFormat.RGBA32), so the numbers are contract.
            Assert.Multiple(() =>
            {
                Assert.That((int)FilterMode.Point, Is.EqualTo(0));
                Assert.That((int)FilterMode.Bilinear, Is.EqualTo(1));
                Assert.That((int)FilterMode.Trilinear, Is.EqualTo(2));

                Assert.That((int)TextureWrapMode.Repeat, Is.EqualTo(0));
                Assert.That((int)TextureWrapMode.Clamp, Is.EqualTo(1));
                Assert.That((int)TextureWrapMode.Mirror, Is.EqualTo(2));
                Assert.That((int)TextureWrapMode.MirrorOnce, Is.EqualTo(3));

                Assert.That((int)TextureFormat.Alpha8, Is.EqualTo(1));
                Assert.That((int)TextureFormat.RGB24, Is.EqualTo(3));
                Assert.That((int)TextureFormat.RGBA32, Is.EqualTo(4));
                Assert.That((int)TextureFormat.ARGB32, Is.EqualTo(5));
                Assert.That((int)TextureFormat.RGBAFloat, Is.EqualTo(20));
                Assert.That((int)TextureFormat.RG16, Is.EqualTo(62));
                Assert.That((int)TextureFormat.R8, Is.EqualTo(63));
                Assert.That((int)TextureFormat.ASTC_HDR_12x12, Is.EqualTo(71));
                Assert.That((int)TextureFormat.RGBA64_SIGNED, Is.EqualTo(82));
                Assert.That((int)TextureFormat.ETC_RGB4_3DS, Is.EqualTo(-60));
                Assert.That((int)TextureFormat.ASTC_RGBA_12x12, Is.EqualTo(-59));

                Assert.That((int)RenderTextureFormat.ARGB32, Is.EqualTo(0));
                Assert.That((int)RenderTextureFormat.ARGBHalf, Is.EqualTo(2));
                Assert.That((int)RenderTextureFormat.Default, Is.EqualTo(7));
                Assert.That((int)RenderTextureFormat.ARGBFloat, Is.EqualTo(11));
                Assert.That((int)RenderTextureFormat.RFloat, Is.EqualTo(14));
                Assert.That((int)RenderTextureFormat.RHalf, Is.EqualTo(15));
                Assert.That((int)RenderTextureFormat.R8, Is.EqualTo(16));
                Assert.That((int)RenderTextureFormat.BGRA32, Is.EqualTo(20));
                Assert.That((int)RenderTextureFormat.RGB111110Float, Is.EqualTo(22), "21 is a gap in Unity's table");
                Assert.That((int)RenderTextureFormat.R16, Is.EqualTo(28));
                Assert.That(Enum.IsDefined(typeof(RenderTextureFormat), 21), Is.False, "21 must stay undefined");

                Assert.That((int)RenderTextureReadWrite.Default, Is.EqualTo(0));
                Assert.That((int)RenderTextureReadWrite.Linear, Is.EqualTo(1));
                Assert.That((int)RenderTextureReadWrite.sRGB, Is.EqualTo(2));

                Assert.That((int)CubemapFace.Unknown, Is.EqualTo(-1));
                Assert.That((int)CubemapFace.PositiveX, Is.EqualTo(0));
                Assert.That((int)CubemapFace.NegativeZ, Is.EqualTo(5));

                foreach (Type t in new[] { typeof(FilterMode), typeof(TextureWrapMode), typeof(TextureFormat), typeof(RenderTextureFormat), typeof(RenderTextureReadWrite), typeof(CubemapFace) })
                {
                    Assert.That(t.Namespace, Is.EqualTo("UnityEngine"), t.Name + " namespace");
                    Assert.That(Enum.GetUnderlyingType(t), Is.EqualTo(typeof(int)), t.Name + " underlying type");
                    Assert.That(t.IsPublic, Is.True, t.Name + " must be public");
                }
            });
        }
    }
}
