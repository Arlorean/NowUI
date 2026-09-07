// Tests for the UnityEngine.Gradient / GradientColorKey / GradientAlphaKey / Keyframe / AnimationCurve shims (unit U12)
// against Docs/Standalone/GradientCurveSemantics.md §1-§5 and StandaloneCoreDesign.md §3.6.
//
// Every [verified] value and every fixture table in GC §3 and GC §5 is asserted here. Values the spec gives as IEEE-754
// single bit patterns are asserted bit-exactly; the two paths the spec itself says are not bit-reproducible are given
// the tolerance the spec names: the weighted Bézier segments (GC §5.8, "verified to ~1e-6", because Unity's root solve
// is a separate numeric path) and the red channel of PerceptualBlend (GC §3.6, a documented ~2-5e-4 relative deviation
// of unknown origin, accepted by design §3.6 rule 9 as up to +-1 byte).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class GradientCurveTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void AssertBits(float actual, uint expectedBits, string label)
            => Assert.That(unchecked((uint)Bits(actual)), Is.EqualTo(expectedBits),
                $"{label}: expected 0x{expectedBits:X8} ({BitConverter.Int32BitsToSingle(unchecked((int)expectedBits))}), " +
                $"got 0x{Bits(actual):X8} ({actual})");

        private static Gradient MakeGradient(GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            Gradient g = new Gradient();
            g.SetKeys(colors, alphas);
            return g;
        }

        private static GradientAlphaKey[] OpaqueAlphas()
            => new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

        private static Gradient TwoColorGradient(Color a, Color b, float ta = 0f, float tb = 1f)
            => MakeGradient(new[] { new GradientColorKey(a, ta), new GradientColorKey(b, tb) }, OpaqueAlphas());

        // ===========================================================================================================
        // GC §1 / §4.2 - struct layout
        // ===========================================================================================================

        [Test]
        public void StructSizesMatchUnity()
        {
            // GC §0 [verified]: Marshal.SizeOf of Keyframe = 32, GradientColorKey = 20, GradientAlphaKey = 8.
            Assert.Multiple(() =>
            {
                Assert.That(Marshal.SizeOf(typeof(Keyframe)), Is.EqualTo(32));
                Assert.That(Marshal.SizeOf(typeof(GradientColorKey)), Is.EqualTo(20));
                Assert.That(Marshal.SizeOf(typeof(GradientAlphaKey)), Is.EqualTo(8));
            });
        }

        [Test]
        public void GradientKeyStructsArePlainData()
        {
            GradientColorKey c = new GradientColorKey(new Color(0.1f, 0.2f, 0.3f, 0.4f), 0.7f);
            GradientAlphaKey a = new GradientAlphaKey(0.25f, -3f);
            Assert.Multiple(() =>
            {
                Assert.That(c.color.a, Is.EqualTo(0.4f), "the colour key's own alpha is stored verbatim");
                Assert.That(c.time, Is.EqualTo(0.7f));
                Assert.That(a.alpha, Is.EqualTo(0.25f));
                Assert.That(a.time, Is.EqualTo(-3f), "the struct itself validates nothing (GC §1)");
            });
        }

        // ===========================================================================================================
        // GC §2 - GradientMode is stored in 8 bits
        // ===========================================================================================================

        [Test]
        public void ModeIsStoredInEightBits()
        {
            Gradient g = new Gradient();
            g.mode = (GradientMode)(-1);
            Assert.That((int)g.mode, Is.EqualTo(255), "(GradientMode)(-1) reads back as 255 (GC §2)");
            g.mode = (GradientMode)7;
            Assert.That((int)g.mode, Is.EqualTo(7));
            g.mode = GradientMode.PerceptualBlend;
            Assert.That((int)g.mode, Is.EqualTo(2));
        }

        [Test]
        public void UndefinedModeEvaluatesAsBlend()
        {
            // GC §2: evaluation with an undefined mode is unverified; the shim documents 1 = Fixed, 2 = PerceptualBlend
            // and everything else = Blend.
            Gradient g = TwoColorGradient(Color.black, Color.white);
            g.mode = (GradientMode)7;
            Assert.That(g.Evaluate(0.5f).r, Is.EqualTo(0.5f));
        }

        // ===========================================================================================================
        // GC §3.1 - storage model
        // ===========================================================================================================

        [Test]
        public void KeyTimesQuantiseToSixteenBitFixedPoint()
        {
            // k = (ushort)(clamp01(t) * 65535 + 0.5); read back as k / 65535f (GC §3.1).
            AssertBits(RoundTripTime(0.5f), 0x3F000080, "0.5");
            AssertBits(RoundTripTime(0.25f), 0x3E800080, "0.25");
            AssertBits(RoundTripTime(0.75f), 0x3F3FFFC0, "0.75");
            AssertBits(RoundTripTime(1f / 9f), 0x3DE390E4, "1/9");
            Assert.Multiple(() =>
            {
                Assert.That(RoundTripTime(0.2f), Is.EqualTo(0.2f), "0.2 is exact (k = 13107)");
                Assert.That(RoundTripTime(0.6f), Is.EqualTo(0.6f), "0.6 is exact (k = 39321)");
            });
        }

        [Test]
        public void KeyTimesAreClampedOnWrite()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RoundTripTime(-0.5f), Is.EqualTo(0f));
                Assert.That(RoundTripTime(-1f), Is.EqualTo(0f));
                Assert.That(RoundTripTime(1.5f), Is.EqualTo(1f));
                Assert.That(RoundTripTime(2f), Is.EqualTo(1f));
            });
        }

        // Round-trips one key time through a two-key gradient (a one-key array would be expanded, discarding the time).
        private static float RoundTripTime(float time)
        {
            Gradient g = MakeGradient(
                new[] { new GradientColorKey(Color.red, time), new GradientColorKey(Color.blue, 1f) },
                OpaqueAlphas());
            GradientColorKey[] keys = g.colorKeys;
            // The stable sort keeps the red key first even when both quantise to the same k.
            return keys[0].color.r == 1f && keys[0].color.b == 0f ? keys[0].time : keys[1].time;
        }

        [Test]
        public void ColorsAndAlphasAreStoredAsFloats()
        {
            Color hdr = new Color(2f, -1f, 0.789f);
            Gradient g = MakeGradient(
                new[] { new GradientColorKey(new Color(0.123f, 0.456f, 0.789f), 0f), new GradientColorKey(hdr, 1f) },
                OpaqueAlphas());

            Color at0 = g.Evaluate(0f);
            Color at1 = g.Evaluate(1f);
            Assert.Multiple(() =>
            {
                Assert.That(at0.r, Is.EqualTo(0.123f));
                Assert.That(at0.g, Is.EqualTo(0.456f));
                Assert.That(at0.b, Is.EqualTo(0.789f));
                Assert.That(at1.r, Is.EqualTo(2f), "HDR components survive unchanged");
                Assert.That(at1.g, Is.EqualTo(-1f), "negative components survive unchanged");
            });
        }

        [Test]
        public void KeysAreStableSortedByQuantisedTime()
        {
            Gradient g = MakeGradient(
                new[]
                {
                    new GradientColorKey(Color.red, 1f),
                    new GradientColorKey(Color.blue, 0f),
                    new GradientColorKey(Color.green, 0.5f),
                },
                OpaqueAlphas());

            GradientColorKey[] keys = g.colorKeys;
            Assert.Multiple(() =>
            {
                Assert.That(keys[0].color, Is.EqualTo(Color.blue));
                Assert.That(keys[1].color, Is.EqualTo(Color.green));
                Assert.That(keys[2].color, Is.EqualTo(Color.red));
            });
        }

        [Test]
        public void DuplicateTimesKeepTheirGivenOrder()
        {
            Gradient a = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0.5f), new GradientColorKey(Color.blue, 0.5f) },
                OpaqueAlphas());
            Gradient b = MakeGradient(
                new[] { new GradientColorKey(Color.blue, 0.5f), new GradientColorKey(Color.red, 0.5f) },
                OpaqueAlphas());

            Assert.Multiple(() =>
            {
                Assert.That(a.colorKeys[0].color, Is.EqualTo(Color.red));
                Assert.That(b.colorKeys[0].color, Is.EqualTo(Color.blue));
            });
        }

        [Test]
        public void InvalidKeyArraysAreSilentlyIgnored()
        {
            // 0, null, 9 and 10 keys are dropped wholesale; the previous keys stay (GC §3.1).
            Gradient g = TwoColorGradient(Color.red, Color.blue);

            g.colorKeys = new GradientColorKey[0];
            Assert.That(g.colorKeys.Length, Is.EqualTo(2), "an empty array is ignored");
            Assert.That(g.colorKeys[0].color, Is.EqualTo(Color.red));

            g.colorKeys = null;
            Assert.That(g.colorKeys.Length, Is.EqualTo(2), "null is ignored");

            g.colorKeys = MakeRamp(9);
            Assert.That(g.colorKeys.Length, Is.EqualTo(2), "9 keys are ignored, not truncated to 8");

            g.colorKeys = MakeRamp(10);
            Assert.That(g.colorKeys.Length, Is.EqualTo(2), "10 keys are ignored");

            g.colorKeys = MakeRamp(8);
            Assert.That(g.colorKeys.Length, Is.EqualTo(8), "8 keys are the maximum that applies");
        }

        [Test]
        public void EachKeyArrayIsValidatedIndependently()
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new GradientAlphaKey[0]);

            Assert.Multiple(() =>
            {
                Assert.That(g.colorKeys[0].color, Is.EqualTo(Color.red), "the valid colour array still applies");
                Assert.That(g.alphaKeyCount, Is.EqualTo(2), "the invalid alpha array leaves the previous keys");
                Assert.That(g.alphaKeys[0].alpha, Is.EqualTo(1f));
            });
        }

        private static GradientColorKey[] MakeRamp(int count)
        {
            GradientColorKey[] keys = new GradientColorKey[count];
            for (int i = 0; i < count; i++)
                keys[i] = new GradientColorKey(new Color(i / 16f, 0f, 0f), i / (float)(count - 1));
            return keys;
        }

        [Test]
        public void SingleKeyIsExpandedToTwoDiscardingItsTime()
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.red, 0.3f) },
                new[] { new GradientAlphaKey(0.5f, 0.7f) });

            GradientColorKey[] colors = g.colorKeys;
            GradientAlphaKey[] alphas = g.alphaKeys;
            Assert.Multiple(() =>
            {
                Assert.That(g.colorKeyCount, Is.EqualTo(2));
                Assert.That(colors[0].color, Is.EqualTo(Color.red));
                Assert.That(colors[0].time, Is.EqualTo(0f), "the key's own time is discarded");
                Assert.That(colors[1].time, Is.EqualTo(1f));
                Assert.That(g.alphaKeyCount, Is.EqualTo(2));
                Assert.That(alphas[0].alpha, Is.EqualTo(0.5f));
                Assert.That(alphas[0].time, Is.EqualTo(0f));
                Assert.That(alphas[1].time, Is.EqualTo(1f));
            });
        }

        [Test]
        public void GettersAllocateAFreshArrayEveryAccess()
        {
            // NowUI sorts the returned arrays in place, so sharing one would corrupt the gradient (GC §3.1).
            Gradient g = new Gradient();
            Assert.Multiple(() =>
            {
                Assert.That(ReferenceEquals(g.colorKeys, g.colorKeys), Is.False);
                Assert.That(ReferenceEquals(g.alphaKeys, g.alphaKeys), Is.False);
            });
        }

        [Test]
        public void ColorSpaceIsAFullInt()
        {
            Gradient g = new Gradient();
            Assert.That((int)g.colorSpace, Is.EqualTo(-1), "default is Uninitialized (GC §3.1)");
            g.colorSpace = (ColorSpace)7;
            Assert.That((int)g.colorSpace, Is.EqualTo(7));
            g.colorSpace = (ColorSpace)(-1);
            Assert.That((int)g.colorSpace, Is.EqualTo(-1));
        }

        [Test]
        public void GetKeysSpanChecksTheDestinationSize()
        {
            Gradient g = new Gradient();
            ArgumentException colorEx = Assert.Throws<ArgumentException>(() =>
            {
                Span<GradientColorKey> tooSmall = new GradientColorKey[1];
                g.GetColorKeys(tooSmall);
            });
            ArgumentException alphaEx = Assert.Throws<ArgumentException>(() =>
            {
                Span<GradientAlphaKey> tooSmall = new GradientAlphaKey[1];
                g.GetAlphaKeys(tooSmall);
            });

            Assert.Multiple(() =>
            {
                Assert.That(colorEx.ParamName, Is.EqualTo("keys"));
                Assert.That(colorEx.Message, Does.StartWith("Destination array must be large enough to store the keys"));
                Assert.That(alphaEx.ParamName, Is.EqualTo("keys"));
            });

            Span<GradientColorKey> big = new GradientColorKey[4];
            g.GetColorKeys(big);
            Assert.That(big[1].time, Is.EqualTo(1f));
        }

        [Test]
        public void SetColorKeysAndSetAlphaKeysSpansFollowTheSameRules()
        {
            Gradient g = new Gradient();
            g.SetColorKeys(new ReadOnlySpan<GradientColorKey>(new[]
            {
                new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f),
            }));
            g.SetAlphaKeys(new ReadOnlySpan<GradientAlphaKey>(new[] { new GradientAlphaKey(0.25f, 0.4f) }));

            Assert.Multiple(() =>
            {
                Assert.That(g.colorKeys[1].color, Is.EqualTo(Color.blue));
                Assert.That(g.alphaKeyCount, Is.EqualTo(2), "the single alpha key expanded");
                Assert.That(g.alphaKeys[0].alpha, Is.EqualTo(0.25f));
            });
        }

        // ===========================================================================================================
        // GC §3.2 - constructor defaults
        // ===========================================================================================================

        [Test]
        public void DefaultGradientIsOpaqueWhite()
        {
            Gradient g = new Gradient();
            GradientColorKey[] colors = g.colorKeys;
            GradientAlphaKey[] alphas = g.alphaKeys;

            Assert.Multiple(() =>
            {
                Assert.That(g.mode, Is.EqualTo(GradientMode.Blend));
                Assert.That((int)g.colorSpace, Is.EqualTo((int)ColorSpace.Uninitialized));
                Assert.That(g.colorKeyCount, Is.EqualTo(2));
                Assert.That(g.alphaKeyCount, Is.EqualTo(2));
                Assert.That(colors[0].color, Is.EqualTo(new Color(1f, 1f, 1f, 1f)));
                Assert.That(colors[0].time, Is.EqualTo(0f));
                Assert.That(colors[1].time, Is.EqualTo(1f));
                Assert.That(alphas[0].alpha, Is.EqualTo(1f));
                Assert.That(alphas[1].alpha, Is.EqualTo(1f));
                Assert.That(g.Evaluate(0f), Is.EqualTo(new Color(1f, 1f, 1f, 1f)));
                Assert.That(g.Evaluate(0.37f), Is.EqualTo(new Color(1f, 1f, 1f, 1f)));
                Assert.That(g.Evaluate(1f), Is.EqualTo(new Color(1f, 1f, 1f, 1f)));
            });
        }

        // ===========================================================================================================
        // GC §3.3 - the common search
        // ===========================================================================================================

        [Test]
        public void EvaluateNaNIsTransparentBlack()
        {
            Gradient g = TwoColorGradient(Color.red, Color.blue);
            Assert.That(g.Evaluate(float.NaN), Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
            Assert.That(new Gradient().Evaluate(float.NaN), Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void EvaluateClampsOutsideTheKeyRange()
        {
            Gradient g = TwoColorGradient(Color.red, Color.blue);
            Assert.Multiple(() =>
            {
                Assert.That(g.Evaluate(-1f).r, Is.EqualTo(1f));
                Assert.That(g.Evaluate(-1e-7f).r, Is.EqualTo(1f));
                Assert.That(g.Evaluate(float.NegativeInfinity).r, Is.EqualTo(1f));
                Assert.That(g.Evaluate(2f).b, Is.EqualTo(1f));
                Assert.That(g.Evaluate(1.0000001f).b, Is.EqualTo(1f));
                Assert.That(g.Evaluate(float.PositiveInfinity).b, Is.EqualTo(1f));
            });
        }

        [Test]
        public void DuplicateTimesSelectFirstBelowAndLastAbove()
        {
            // (…, red@X, blue@X, next): just below X interpolates toward red, just above interpolates from blue.
            Gradient g = MakeGradient(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.red, 0.5f),
                    new GradientColorKey(Color.blue, 0.5f),
                    new GradientColorKey(Color.white, 1f),
                },
                OpaqueAlphas());

            Color below = g.Evaluate(0.4999f);
            Color above = g.Evaluate(0.5001f);
            Assert.Multiple(() =>
            {
                Assert.That(below.r, Is.GreaterThan(0.99f), "approaching the red duplicate");
                Assert.That(below.b, Is.EqualTo(0f));
                Assert.That(above.b, Is.GreaterThan(0.99f), "leaving the blue duplicate");
                Assert.That(above.r, Is.LessThan(0.01f));
            });
        }

        [Test]
        public void AllKeysAtOneTimeReturnTheFirstKey()
        {
            // The 0/0 guard makes u = 0, so the first key wins everywhere (GC §3.3).
            Gradient half = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0.5f), new GradientColorKey(Color.blue, 0.5f) },
                OpaqueAlphas());
            Gradient zero = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 0f) },
                OpaqueAlphas());

            Assert.Multiple(() =>
            {
                Assert.That(half.Evaluate(0f).r, Is.EqualTo(1f));
                Assert.That(half.Evaluate(0.5f).r, Is.EqualTo(1f));
                Assert.That(half.Evaluate(1f).r, Is.EqualTo(1f));
                Assert.That(zero.Evaluate(1f).r, Is.EqualTo(1f));
            });
        }

        // ===========================================================================================================
        // GC §3.4 - Blend
        // ===========================================================================================================

        [Test]
        public void BlendReproducesTheProbeBitPatterns()
        {
            Gradient g = TwoColorGradient(new Color(0.2f, 0.4f, 0.6f), new Color(0.9f, 0.1f, 0.3f));
            Color c = g.Evaluate(0.1f);
            AssertBits(c.r, 0x3E8A3D71, "r at t=0.1");
            AssertBits(c.g, 0x3EBD70A4, "g at t=0.1");
            AssertBits(c.b, 0x3F11EB86, "b at t=0.1");
        }

        [Test]
        public void BlendComputesUInTheFixedPointDomain()
        {
            // Keys at 0.13 (k = 8520) and 0.61 (k = 39976); a black->white ramp makes the channel equal u itself.
            Gradient g = TwoColorGradient(Color.black, Color.white, 0.13f, 0.61f);
            AssertBits(g.Evaluate(0.2f).r, 0x3E15528E, "u at t=0.2");
        }

        [Test]
        public void BlendClampsNothing()
        {
            Gradient g = MakeGradient(
                new[] { new GradientColorKey(new Color(2f, 0f, 0f), 0f), new GradientColorKey(new Color(0f, 0f, -1f), 1f) },
                new[] { new GradientAlphaKey(2f, 0f), new GradientAlphaKey(-1f, 1f) });

            Color c = g.Evaluate(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(c.r, Is.EqualTo(1f), "2 + (0 - 2) * 0.5");
                Assert.That(c.g, Is.EqualTo(0f));
                Assert.That(c.b, Is.EqualTo(-0.5f));
                Assert.That(c.a, Is.EqualTo(0.5f), "alpha 2 -> -1 at the midpoint");
            });
        }

        [Test]
        public void BlendIgnoresColorSpace()
        {
            foreach (ColorSpace space in new[] { ColorSpace.Uninitialized, ColorSpace.Gamma, ColorSpace.Linear })
            {
                Gradient g = TwoColorGradient(Color.black, Color.white);
                g.colorSpace = space;
                Assert.That(g.Evaluate(0.5f).r, Is.EqualTo(0.5f), space.ToString());
            }
        }

        [Test]
        public void ColorKeyAlphaIsIgnoredByEvaluate()
        {
            Gradient g = MakeGradient(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0f, 0f, 0.1f), 0f),
                    new GradientColorKey(new Color(0f, 0f, 1f, 0.2f), 1f),
                },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.75f, 1f) });

            Assert.That(g.Evaluate(0.5f).a, Is.EqualTo(0.75f), "alpha comes from the alpha keys only (GC §1)");
        }

        // ===========================================================================================================
        // GC §3.5 - Fixed
        // ===========================================================================================================

        [Test]
        public void FixedModeStepsColoursAndAlphas()
        {
            Gradient g = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 0.75f) },
                new[] { new GradientAlphaKey(0f, 0.25f), new GradientAlphaKey(1f, 0.75f) });
            g.mode = GradientMode.Fixed;

            Assert.Multiple(() =>
            {
                Assert.That(g.Evaluate(0.25f).a, Is.EqualTo(0f), "at the key time the key itself wins");
                Assert.That(g.Evaluate(0.26f).a, Is.EqualTo(1f), "alpha steps too (GC §3.5)");
                Assert.That(g.Evaluate(0f).r, Is.EqualTo(1f), "at or below the first key");
                // Fixed returns the first key whose time is >= tq, so past a key the *next* colour shows at once.
                Assert.That(g.Evaluate(0.1f).b, Is.EqualTo(1f));
                Assert.That(g.Evaluate(0.9f).b, Is.EqualTo(1f), "after the last key the last key holds");
            });
        }

        [Test]
        public void FixedModeAtAKeySetToHalfReturnsThatKey()
        {
            // t = 0.5 is tq = 32767.5, which is *below* a key quantised to k = 32768, so the step still lands on it.
            Gradient g = TwoColorGradient(Color.red, Color.blue, 0f, 0.5f);
            g.mode = GradientMode.Fixed;
            Assert.That(g.Evaluate(0.5f).b, Is.EqualTo(1f));
        }

        [Test]
        public void FixedModeWithDuplicatesTakesTheFirstAtOrBelow()
        {
            Gradient g = MakeGradient(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.red, 0.5f),
                    new GradientColorKey(Color.blue, 0.5f),
                    new GradientColorKey(Color.white, 1f),
                },
                OpaqueAlphas());
            g.mode = GradientMode.Fixed;

            Assert.Multiple(() =>
            {
                Assert.That(g.Evaluate(0.49f).r, Is.EqualTo(1f), "the first duplicate at or below");
                Assert.That(g.Evaluate(0.6f).r, Is.EqualTo(1f), "above the duplicates the next key is white");
                Assert.That(g.Evaluate(0.6f).b, Is.EqualTo(1f));
            });
        }

        // ===========================================================================================================
        // GC §3.6 - PerceptualBlend (Oklab)
        // ===========================================================================================================

        private static Gradient PerceptualGradient(Color a, Color b)
        {
            Gradient g = TwoColorGradient(a, b);
            g.mode = GradientMode.PerceptualBlend;
            return g;
        }

        private static int Byte(float c) => (int)Math.Round(c * 255.0);

        private static void AssertPerceptual(Gradient g, float t, int r, int gr, int b, string label)
        {
            Color c = g.Evaluate(t);
            Assert.Multiple(() =>
            {
                // The red channel carries the documented, unexplained ~2-5e-4 relative deviation (GC §3.6).
                Assert.That(Byte(c.r), Is.EqualTo(r).Within(1), label + " r");
                Assert.That(Byte(c.g), Is.EqualTo(gr), label + " g");
                Assert.That(Byte(c.b), Is.EqualTo(b), label + " b");
            });
        }

        [Test]
        public void PerceptualBlendReproducesTheProbeByteTable()
        {
            Gradient redBlue = PerceptualGradient(Color.red, Color.blue);
            AssertPerceptual(redBlue, 0.25f, 198, 73, 109, "red->blue @0.25");
            AssertPerceptual(redBlue, 0.5f, 140, 83, 162, "red->blue @0.5");
            AssertPerceptual(redBlue, 0.75f, 81, 71, 210, "red->blue @0.75");

            Gradient blackWhite = PerceptualGradient(Color.black, Color.white);
            AssertPerceptual(blackWhite, 0.25f, 34, 34, 34, "black->white @0.25");
            AssertPerceptual(blackWhite, 0.5f, 99, 99, 99, "black->white @0.5");
            AssertPerceptual(blackWhite, 0.75f, 174, 174, 174, "black->white @0.75");

            AssertPerceptual(PerceptualGradient(Color.red, Color.green), 0.5f, 208, 168, 0, "red->green @0.5");

            Gradient pair = PerceptualGradient(new Color(0.2f, 0.4f, 0.6f), new Color(0.9f, 0.1f, 0.3f));
            AssertPerceptual(pair, 0.3f, 120, 97, 133, "(0.2,0.4,0.6)->(0.9,0.1,0.3) @0.3");
            AssertPerceptual(pair, 0.5f, 154, 89, 119, "(0.2,0.4,0.6)->(0.9,0.1,0.3) @0.5");

            AssertPerceptual(PerceptualGradient(Color.yellow, Color.blue), 0.5f, 112, 162, 195, "yellow->blue @0.5");
        }

        [Test]
        public void PerceptualBlendPassesOutOfGamutResultsThrough()
        {
            // white->blue at 0.5: the blue channel leaves the [0,1] range and is returned unquantised (GC §3.6).
            Gradient g = PerceptualGradient(Color.white, Color.blue);
            Color c = g.Evaluate(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(Byte(c.r), Is.EqualTo(116).Within(1), "the known red-channel deviation");
                Assert.That(Byte(c.g), Is.EqualTo(163));
                Assert.That(c.b, Is.EqualTo(1.0372324f).Within(2e-3f), "unquantised, above 1");
            });
        }

        [Test]
        public void PerceptualBlendUsesTheAboveOneTransferForHdrKeys()
        {
            // (2,0,0) -> black at 0.5 is 200/255; the exact sRGB curve would give 206/255 (GC §3.6 step 1).
            Gradient g = PerceptualGradient(new Color(2f, 0f, 0f), Color.black);
            Assert.That(Byte(g.Evaluate(0.5f).r), Is.EqualTo(200).Within(1));
        }

        [Test]
        public void PerceptualBlendIsIdentityForEqualKeys()
        {
            // 0.6 is used rather than 0.5 because 0.5 * 255 = 127.5 is a rounding tie: the Oklab round trip lands a
            // few 1e-8 below it and the byte falls to 127, which says nothing about gamut mapping either way.
            Gradient grey = PerceptualGradient(new Color(0.6f, 0.6f, 0.6f), new Color(0.6f, 0.6f, 0.6f));
            Gradient red = PerceptualGradient(Color.red, Color.red);
            Assert.Multiple(() =>
            {
                Assert.That(Byte(grey.Evaluate(0.5f).r), Is.EqualTo(153), "no gamut mapping occurs");
                Assert.That(Byte(grey.Evaluate(0.25f).b), Is.EqualTo(153));
                Assert.That(red.Evaluate(0.5f).r, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(red.Evaluate(0.5f).g, Is.EqualTo(0f).Within(1e-6f));
            });
        }

        [Test]
        public void PerceptualBlendInLinearColorSpaceSkipsTheTransfers()
        {
            Gradient g = PerceptualGradient(Color.red, Color.blue);
            g.colorSpace = ColorSpace.Linear;
            Color c = g.Evaluate(0.5f);
            Assert.Multiple(() =>
            {
                // Unity: (0.263734281, 0.08657174, 0.362824231); the double reference gives 0.263676 for red.
                Assert.That(c.r, Is.EqualTo(0.263734281f).Within(1e-4f), "the documented red deviation");
                Assert.That(c.g, Is.EqualTo(0.08657174f).Within(1e-6f));
                Assert.That(c.b, Is.EqualTo(0.362824231f).Within(1e-6f));
            });
        }

        [Test]
        public void PerceptualBlendStillLerpsAlphaLinearly()
        {
            Gradient g = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(0.3f, 0f), new GradientAlphaKey(0.8f, 1f) });
            g.mode = GradientMode.PerceptualBlend;
            Assert.That(g.Evaluate(0.5f).a, Is.EqualTo(0.55f).Within(1e-6f));
        }

        // ===========================================================================================================
        // GC §3.7 - equality and hashing
        // ===========================================================================================================

        [Test]
        public void GradientEqualsIsContentBased()
        {
            Gradient a = TwoColorGradient(Color.red, Color.blue);
            Gradient b = TwoColorGradient(Color.red, Color.blue);
            Assert.Multiple(() =>
            {
                Assert.That(a.Equals(b), Is.True, "independently built, same content");
                Assert.That(a.Equals((object)b), Is.True);
                Assert.That(a.Equals(a), Is.True);
                Assert.That(a.Equals((Gradient)null), Is.False);
                Assert.That(a.Equals((object)"not a gradient"), Is.False);
            });

            Gradient reordered = MakeGradient(
                new[] { new GradientColorKey(Color.blue, 1f), new GradientColorKey(Color.red, 0f) },
                OpaqueAlphas());
            Assert.That(a.Equals(reordered), Is.True, "keys that sort the same are equal");

            Gradient differentMode = TwoColorGradient(Color.red, Color.blue);
            differentMode.mode = GradientMode.Fixed;
            Gradient differentSpace = TwoColorGradient(Color.red, Color.blue);
            differentSpace.colorSpace = ColorSpace.Linear;
            Gradient differentAlpha = MakeGradient(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) });
            Gradient differentKeys = TwoColorGradient(Color.red, Color.green);

            Assert.Multiple(() =>
            {
                Assert.That(a.Equals(differentMode), Is.False);
                Assert.That(a.Equals(differentSpace), Is.False);
                Assert.That(a.Equals(differentAlpha), Is.False);
                Assert.That(a.Equals(differentKeys), Is.False);
            });
        }

        [Test]
        public void GradientHashIsIdentityBased()
        {
            // Equal gradients hash differently, so a Dictionary with the default comparer finds only the same instance
            // (GC §3.7). NowUI works around this with its own reference comparer.
            Gradient a = TwoColorGradient(Color.red, Color.blue);
            Gradient b = TwoColorGradient(Color.red, Color.blue);

            Dictionary<Gradient, int> map = new Dictionary<Gradient, int> { [a] = 1 };
            Assert.Multiple(() =>
            {
                Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
                Assert.That(a.GetHashCode(), Is.EqualTo(a.GetHashCode()), "stable per instance");
                Assert.That(map.ContainsKey(b), Is.False, "an equal copy is not found");
                Assert.That(map.ContainsKey(a), Is.True);
            });
        }

        // ===========================================================================================================
        // GC §4.2 - Keyframe
        // ===========================================================================================================

        [Test]
        public void KeyframeConstructorsMatchUnity()
        {
            Keyframe two = new Keyframe(1f, 2f);
            Keyframe four = new Keyframe(1f, 2f, 3f, 4f);
            Keyframe six = new Keyframe(1f, 2f, 3f, 4f, 0.25f, 0.75f);
            Keyframe zero = default;

            Assert.Multiple(() =>
            {
                Assert.That(two.inTangent, Is.EqualTo(0f));
                Assert.That(two.inWeight, Is.EqualTo(0f));
                Assert.That(two.weightedMode, Is.EqualTo(WeightedMode.None));

                Assert.That(four.inTangent, Is.EqualTo(3f));
                Assert.That(four.outTangent, Is.EqualTo(4f));
                Assert.That(four.outWeight, Is.EqualTo(0f));
                Assert.That(four.weightedMode, Is.EqualTo(WeightedMode.None));

                Assert.That(six.inWeight, Is.EqualTo(0.25f));
                Assert.That(six.outWeight, Is.EqualTo(0.75f));
                Assert.That(six.weightedMode, Is.EqualTo(WeightedMode.Both), "the six-argument ctor sets Both");

                Assert.That(zero.time, Is.EqualTo(0f));
                Assert.That(zero.weightedMode, Is.EqualTo(WeightedMode.None));
            });
        }

        [Test]
        public void KeyframeStoresRawWeightedAndTangentModes()
        {
#pragma warning disable 618 // tangentMode is [Obsolete] in Unity too; the spec requires it to round-trip.
            Keyframe k = new Keyframe(0f, 0f);
            k.weightedMode = (WeightedMode)7;
            k.tangentMode = 5;
            Keyframe roundTripped = new AnimationCurve(k)[0];
            Assert.Multiple(() =>
            {
                Assert.That((int)roundTripped.weightedMode, Is.EqualTo(7));
                Assert.That(roundTripped.tangentMode, Is.EqualTo(5));
            });

            k.weightedMode = (WeightedMode)(-1);
            k.tangentMode = -3;
            AnimationCurve viaAddKey = new AnimationCurve();
            viaAddKey.AddKey(k);
            roundTripped = viaAddKey[0];
            Assert.Multiple(() =>
            {
                Assert.That((int)roundTripped.weightedMode, Is.EqualTo(-1));
                Assert.That(roundTripped.tangentMode, Is.EqualTo(-3));
            });
#pragma warning restore 618
        }

        [Test]
        public void WeightedModeBothIsInOrOut()
        {
            Assert.That((int)WeightedMode.Both, Is.EqualTo((int)WeightedMode.In | (int)WeightedMode.Out));
        }

        // ===========================================================================================================
        // GC §5.1 - AnimationCurve storage
        // ===========================================================================================================

        [Test]
        public void CurveDefaultsAreEmptyAndClampForever()
        {
            AnimationCurve c = new AnimationCurve();
            Assert.Multiple(() =>
            {
                Assert.That(c.length, Is.EqualTo(0));
                Assert.That(c.preWrapMode, Is.EqualTo(WrapMode.ClampForever));
                Assert.That(c.postWrapMode, Is.EqualTo(WrapMode.ClampForever));
            });
        }

        [Test]
        public void KeysAreStableSortedAndStoredVerbatim()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(0.5f, 2f), new Keyframe(1f, 0f));
            AnimationCurve reversed = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

            Assert.Multiple(() =>
            {
                Assert.That(c[1].value, Is.EqualTo(1f), "duplicate times keep the given order");
                Assert.That(c[2].value, Is.EqualTo(2f));
                Assert.That(reversed[1].value, Is.EqualTo(2f));
                Assert.That(reversed[2].value, Is.EqualTo(1f));
            });
        }

        [Test]
        public void NullKeysGiveAnEmptyCurve()
        {
            AnimationCurve fromCtor = new AnimationCurve((Keyframe[])null);
            AnimationCurve fromSetter = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            fromSetter.keys = null;

            Assert.Multiple(() =>
            {
                Assert.That(fromCtor.length, Is.EqualTo(0));
                Assert.That(fromSetter.length, Is.EqualTo(0));
            });
        }

        [Test]
        public void ClearKeysEmptiesTheCurve()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            c.ClearKeys();
            Assert.That(c.length, Is.EqualTo(0));
            Assert.That(c.Evaluate(0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void KeysGetterCopiesAndEmptyCurveReturnsTheSameInstance()
        {
            AnimationCurve empty = new AnimationCurve();
            Assert.That(ReferenceEquals(empty.keys, empty.keys), Is.True, "the empty array instance is shared");

            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Keyframe[] first = c.keys;
            Assert.That(ReferenceEquals(first, c.keys), Is.False, "NowUI sorts the returned array in place");
            first[0].value = 99f;
            Assert.That(c[0].value, Is.EqualTo(0f), "mutating the copy does not touch the curve");
        }

        [Test]
        public void IndexerAndSpanHelpersThrowUnityMessages()
        {
            AnimationCurve empty = new AnimationCurve();
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(Assert.Throws<IndexOutOfRangeException>(() => { Keyframe _ = empty[0]; }).Message,
                    Is.EqualTo("GetKey"));
                Assert.That(Assert.Throws<IndexOutOfRangeException>(() => { Keyframe _ = c[-1]; }).Message,
                    Is.EqualTo("GetKey"));
                Assert.That(Assert.Throws<IndexOutOfRangeException>(() => { Keyframe _ = c[2]; }).Message,
                    Is.EqualTo("GetKey"));
            });

            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                Span<Keyframe> tooSmall = new Keyframe[1];
                c.GetKeys(tooSmall);
            });
            Assert.Multiple(() =>
            {
                Assert.That(ex.ParamName, Is.EqualTo("keys"));
                Assert.That(ex.Message, Does.StartWith("Destination array must be large enough to store the keys"));
            });

            Span<Keyframe> big = new Keyframe[4];
            c.GetKeys(big);
            Assert.That(big[1].time, Is.EqualTo(1f));

            AnimationCurve viaSpan = new AnimationCurve();
            viaSpan.SetKeys(new ReadOnlySpan<Keyframe>(new[] { new Keyframe(1f, 1f), new Keyframe(0f, 0f) }));
            Assert.That(viaSpan[0].time, Is.EqualTo(0f), "SetKeys sorts too");
        }

        // ===========================================================================================================
        // GC §5.7 - wrap modes
        // ===========================================================================================================

        [Test]
        public void WrapModeSettersNormalise()
        {
            AnimationCurve c = new AnimationCurve();
            foreach (int stored in new[] { 1, 8, 3, 16, -1 })
            {
                c.preWrapMode = (WrapMode)stored;
                Assert.That((int)c.preWrapMode, Is.EqualTo(8), $"{stored} stores as ClampForever");
            }

            c.preWrapMode = WrapMode.Loop;
            c.postWrapMode = WrapMode.PingPong;
            Assert.Multiple(() =>
            {
                Assert.That(c.preWrapMode, Is.EqualTo(WrapMode.Loop));
                Assert.That(c.postWrapMode, Is.EqualTo(WrapMode.PingPong), "pre and post are independent");
            });

            c.preWrapMode = WrapMode.Default;
            Assert.That((int)c.preWrapMode, Is.EqualTo(0), "Default stores as 0");
        }

        [Test]
        public void ClampForeverHoldsTheEdgeValueButYieldsNaNAtInfinity()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.Multiple(() =>
            {
                Assert.That(c.Evaluate(-1f), Is.EqualTo(0f));
                Assert.That(c.Evaluate(2f), Is.EqualTo(1f));
                Assert.That(c.Evaluate(1f), Is.EqualTo(1f));
                Assert.That(c.Evaluate(float.MaxValue), Is.EqualTo(1f));
                Assert.That(c.Evaluate(-float.MaxValue), Is.EqualTo(0f));
                // The zero-coefficient polynomial multiplies 0 by infinity, so this is NaN, not the edge value.
                Assert.That(float.IsNaN(c.Evaluate(float.PositiveInfinity)), Is.True);
                Assert.That(float.IsNaN(c.Evaluate(float.NegativeInfinity)), Is.True);
                Assert.That(float.IsNaN(c.Evaluate(float.NaN)), Is.True);
            });
        }

        [Test]
        public void DefaultWrapModeEvaluatesAsLoop()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            c.preWrapMode = WrapMode.Default;
            c.postWrapMode = WrapMode.Default;

            Assert.Multiple(() =>
            {
                Assert.That(c.Evaluate(-0.25f), Is.EqualTo(0.75f));
                Assert.That(c.Evaluate(1f), Is.EqualTo(0f), "Evaluate(t1) wraps to t0 under Loop");
                Assert.That(c.Evaluate(1.25f), Is.EqualTo(0.25f));
                Assert.That(c.Evaluate(3f), Is.EqualTo(0f));
            });
        }

        [Test]
        public void LoopSamplesMatchTheProbe()
        {
            AnimationCurve twoToFour = AnimationCurve.Linear(2f, 0f, 4f, 1f);
            twoToFour.preWrapMode = WrapMode.Loop;
            twoToFour.postWrapMode = WrapMode.Loop;
            Assert.Multiple(() =>
            {
                Assert.That(twoToFour.Evaluate(-0.5f), Is.EqualTo(0.75f));
                Assert.That(twoToFour.Evaluate(0f), Is.EqualTo(0f));
                Assert.That(twoToFour.Evaluate(4f), Is.EqualTo(0f));
                Assert.That(twoToFour.Evaluate(4.5f), Is.EqualTo(0.25f));
                Assert.That(twoToFour.Evaluate(8.5f), Is.EqualTo(0.25f));
            });

            AnimationCurve halfRange = new AnimationCurve(
                new Keyframe(0.5f, 1f, 0f, 2f), new Keyframe(1.5f, 3f, 2f, 0f));
            halfRange.preWrapMode = WrapMode.Loop;
            halfRange.postWrapMode = WrapMode.Loop;
            Assert.Multiple(() =>
            {
                Assert.That(halfRange.Evaluate(2f), Is.EqualTo(2f));
                Assert.That(halfRange.Evaluate(2.5f), Is.EqualTo(1f));
                Assert.That(halfRange.Evaluate(0f), Is.EqualTo(2f));
                Assert.That(halfRange.Evaluate(0.25f), Is.EqualTo(2.5f));
                Assert.That(halfRange.Evaluate(-0.5f), Is.EqualTo(1f));
            });

            AnimationCurve unit = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            unit.postWrapMode = WrapMode.Loop;
            AssertBits(unit.Evaluate(7.1f), 0x3DCCCCC0, "Loop 7.1");
            Assert.Multiple(() =>
            {
                Assert.That(unit.Evaluate(1000000.25f), Is.EqualTo(0.25f));
                Assert.That(float.IsNaN(unit.Evaluate(float.PositiveInfinity)), Is.True);
                Assert.That(unit.Evaluate(float.MaxValue), Is.EqualTo(0f));
            });
        }

        [Test]
        public void PingPongSamplesMatchTheProbe()
        {
            AnimationCurve unit = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            unit.preWrapMode = WrapMode.PingPong;
            unit.postWrapMode = WrapMode.PingPong;
            Assert.Multiple(() =>
            {
                Assert.That(unit.Evaluate(-2.25f), Is.EqualTo(0.25f));
                Assert.That(unit.Evaluate(-1f), Is.EqualTo(1f));
                Assert.That(unit.Evaluate(-0.25f), Is.EqualTo(0.25f));
                Assert.That(unit.Evaluate(1.25f), Is.EqualTo(0.75f));
                Assert.That(unit.Evaluate(2f), Is.EqualTo(0f));
                Assert.That(unit.Evaluate(3f), Is.EqualTo(1f));
                Assert.That(unit.Evaluate(3.5f), Is.EqualTo(0.5f));
            });
            AssertBits(unit.Evaluate(7.1f), 0x3F666668, "PingPong 7.1");

            AnimationCurve twoToFour = AnimationCurve.Linear(2f, 0f, 4f, 1f);
            twoToFour.preWrapMode = WrapMode.PingPong;
            twoToFour.postWrapMode = WrapMode.PingPong;
            Assert.Multiple(() =>
            {
                Assert.That(twoToFour.Evaluate(0f), Is.EqualTo(1f));
                Assert.That(twoToFour.Evaluate(0.5f), Is.EqualTo(0.75f));
                Assert.That(twoToFour.Evaluate(4f), Is.EqualTo(1f));
                Assert.That(twoToFour.Evaluate(5.5f), Is.EqualTo(0.25f));
                Assert.That(twoToFour.Evaluate(8f), Is.EqualTo(1f));
                Assert.That(twoToFour.Evaluate(8.5f), Is.EqualTo(0.75f));
            });
        }

        [Test]
        public void PingPongAtNegativeInfinityIsADocumentedDivergence()
        {
            // GC §5.7 records `-Infinity with pre = PingPong -> 1`, but the fold the same section specifies is
            // `x = (t - t0) mod (2 * range)`, and fmod of an infinity is NaN in IEEE-754 — no arrangement of the
            // spec's own steps produces 1. The shim implements the formula; this asserts what it does so the
            // divergence is visible rather than silent. Reported with the unit's result.
            AnimationCurve unit = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            unit.preWrapMode = WrapMode.PingPong;
            Assert.That(float.IsNaN(unit.Evaluate(float.NegativeInfinity)), Is.True);
        }

        // ===========================================================================================================
        // GC §5.4 / §5.6 - evaluation
        // ===========================================================================================================

        [Test]
        public void EmptyAndSingleKeyCurves()
        {
            AnimationCurve empty = new AnimationCurve();
            AnimationCurve one = new AnimationCurve(new Keyframe(3f, 7f));
            one.preWrapMode = WrapMode.Loop;
            one.postWrapMode = WrapMode.PingPong;

            Assert.Multiple(() =>
            {
                Assert.That(empty.Evaluate(0f), Is.EqualTo(0f));
                Assert.That(empty.Evaluate(float.NaN), Is.EqualTo(0f));
                Assert.That(one.Evaluate(-5f), Is.EqualTo(7f));
                Assert.That(one.Evaluate(500f), Is.EqualTo(7f));
                Assert.That(one.Evaluate(float.NaN), Is.EqualTo(7f));
            });
        }

        [Test]
        public void HermiteReproducesTheProbeBitPatterns()
        {
            AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            AssertBits(ease.Evaluate(0.3f), 0x3E5D2F1C, "EaseInOut @0.3");
            Assert.That(ease.Evaluate(0.25f), Is.EqualTo(0.15625f));
            Assert.That(ease.Evaluate(0.75f), Is.EqualTo(0.84375f));

            AnimationCurve a = new AnimationCurve(
                new Keyframe(1f, 2f, 0f, 0.5f), new Keyframe(3f, -1f, -2f, 0f));
            AssertBits(a.Evaluate(1.1f), 0x40021AA0, "a @1.1");
            AssertBits(a.Evaluate(1.7f), 0x3FCF8106, "a @1.7");
            AssertBits(a.Evaluate(2.2f), 0x3F3A5E34, "a @2.2");
            AssertBits(a.Evaluate(2.9f), 0xBF4B9DB4, "a @2.9");

            AnimationCurve b = new AnimationCurve(
                new Keyframe(0.13f, 0.37f, 0f, 1.9f), new Keyframe(0.61f, -0.42f, 0.25f, 0f));
            AssertBits(b.Evaluate(0.2f), 0x3ED6B587, "b @0.2");
            AssertBits(b.Evaluate(0.37f), 0x3D978D54, "b @0.37");
            AssertBits(b.Evaluate(0.55f), 0xBEC527F0, "b @0.55");

            AnimationCurve c = new AnimationCurve(
                new Keyframe(-3f, 10f, 0f, -4f), new Keyframe(7f, -20f, 3f, 0f));
            AssertBits(c.Evaluate(-1f), 0x3F4CCCD0, "c @-1");
            Assert.That(c.Evaluate(2f), Is.EqualTo(-13.75f));
            AssertBits(c.Evaluate(6f), 0xC1AF9998, "c @6");
        }

        [Test]
        public void HermiteClampsTheSegmentLengthToOneEMinusFour()
        {
            // A 1e-7-long segment: 1e-5 and 1e-6 minimums do not reproduce this sample (GC §5.6).
            AnimationCurve c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1e-7f, 1f));
            AssertBits(c.Evaluate(5e-8f), 0x3549426E, "1e-7 segment @5e-8");
        }

        [Test]
        public void HermiteAtLargeTimesUsesTheSegmentLocalX()
        {
            AnimationCurve c = new AnimationCurve(new Keyframe(1e6f, 0f), new Keyframe(1e6f + 1f, 1f));
            Assert.That(c.Evaluate(1e6f + 0.25f), Is.EqualTo(0.15625f));
        }

        [Test]
        public void EvaluatingAtAKeyTimeReturnsThatKeyExactly()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 0f));
            Assert.Multiple(() =>
            {
                Assert.That(c.Evaluate(1f), Is.EqualTo(1f), "x = 0 in the next segment");
                // GC §5.6 also records an overshoot just below a key (0.99999994 -> 1.00000012) but does not say on
                // which curve; on every curve the spec does name the shim lands within one ulp of the key value.
                Assert.That(c.Evaluate(0.99999994f), Is.EqualTo(1f).Within(2).Ulps);
            });
        }

        [Test]
        public void DuplicateTimesEvaluateToTheLastDuplicate()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(0.5f, 2f), new Keyframe(1f, 0f));
            Assert.Multiple(() =>
            {
                Assert.That(c.Evaluate(0.5f), Is.EqualTo(2f));
                Assert.That(c.Evaluate(0.49f), Is.EqualTo(0.998816f).Within(1e-6f));
                Assert.That(c.Evaluate(0.51f), Is.EqualTo(1.997632f).Within(1e-6f));
            });

            AnimationCurve pair = new AnimationCurve(new Keyframe(0.5f, 1f), new Keyframe(0.5f, 2f));
            Assert.That(pair.Evaluate(0.5f), Is.EqualTo(2f));
        }

        [Test]
        public void InfiniteTangentsMakeSteps()
        {
            AnimationCurve outPositive = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.PositiveInfinity), new Keyframe(1f, 1f));
            AnimationCurve inPositive = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 1f, float.PositiveInfinity, 0f));
            AnimationCurve outNegative = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.NegativeInfinity), new Keyframe(1f, 1f));
            AnimationCurve inNegative = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(1f, 1f, float.NegativeInfinity, 0f));
            AnimationCurve bothPositive = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.PositiveInfinity),
                new Keyframe(1f, 1f, float.PositiveInfinity, 0f));
            AnimationCurve otherTangents = new AnimationCurve(
                new Keyframe(0f, 0f, float.PositiveInfinity, 0f),
                new Keyframe(1f, 1f, 0f, float.PositiveInfinity));

            Assert.Multiple(() =>
            {
                Assert.That(outPositive.Evaluate(0.5f), Is.EqualTo(0f), "+inf holds the left value");
                Assert.That(outPositive.Evaluate(0.99f), Is.EqualTo(0f));
                Assert.That(outPositive.Evaluate(1f), Is.EqualTo(1f), "up to but excluding R.time");
                Assert.That(inPositive.Evaluate(0.5f), Is.EqualTo(0f));
                Assert.That(outNegative.Evaluate(0.5f), Is.EqualTo(1f), "-inf jumps to the right value");
                Assert.That(inNegative.Evaluate(0.5f), Is.EqualTo(1f));
                Assert.That(bothPositive.Evaluate(0.5f), Is.EqualTo(0f));
                Assert.That(otherTangents.Evaluate(0.5f), Is.EqualTo(0.5f),
                    "L.inTangent / R.outTangent do not make a step");
            });
        }

        [Test]
        public void HugeFiniteTangentsAreNotSteps()
        {
            AnimationCurve big = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1e30f), new Keyframe(1f, 1f));
            AssertBits(big.Evaluate(0.5f), 0x6FC9F2C8, "outTangent 1e30 @0.5");

            AnimationCurve max = new AnimationCurve(new Keyframe(0f, 0f, 0f, float.MaxValue), new Keyframe(1f, 1f));
            Assert.That(float.IsNegativeInfinity(max.Evaluate(0.5f)), Is.True);
        }

        [Test]
        public void NaNTangentsFallThroughToThePolynomial()
        {
            AnimationCurve c = new AnimationCurve(new Keyframe(0f, 0f, 0f, float.NaN), new Keyframe(1f, 1f));
            Assert.That(float.IsNaN(c.Evaluate(0.5f)), Is.True);
        }

        // ===========================================================================================================
        // GC §5.8 - weighted segments
        // ===========================================================================================================

        private const float WeightedTolerance = 2e-6f;

        private static AnimationCurve WeightedUnitCurve(
            float outWeight, WeightedMode leftMode, float inWeight, WeightedMode rightMode,
            float outTangent = 0f, float inTangent = 0f)
        {
            Keyframe left = new Keyframe(0f, 0f, 0f, outTangent);
            left.outWeight = outWeight;
            left.weightedMode = leftMode;
            Keyframe right = new Keyframe(1f, 1f, inTangent, 0f);
            right.inWeight = inWeight;
            right.weightedMode = rightMode;
            return new AnimationCurve(left, right);
        }

        [Test]
        public void WeightedSegmentsMatchTheProbeToOneEMinusSix()
        {
            AnimationCurve asymmetric = WeightedUnitCurve(0.9f, WeightedMode.Out, 0.1f, WeightedMode.In);
            Assert.Multiple(() =>
            {
                Assert.That(asymmetric.Evaluate(0.1f), Is.EqualTo(0.004332156f).Within(WeightedTolerance));
                Assert.That(asymmetric.Evaluate(0.25f), Is.EqualTo(0.0295022521f).Within(WeightedTolerance));
                Assert.That(asymmetric.Evaluate(0.5f), Is.EqualTo(0.140823036f).Within(WeightedTolerance));
                Assert.That(asymmetric.Evaluate(0.75f), Is.EqualTo(0.409674466f).Within(WeightedTolerance));
                Assert.That(asymmetric.Evaluate(0.9f), Is.EqualTo(0.7522198f).Within(WeightedTolerance));
            });

            AnimationCurve withTangents = WeightedUnitCurve(
                0.9f, WeightedMode.Out, 0.1f, WeightedMode.In, outTangent: 1f, inTangent: 1f);
            Assert.Multiple(() =>
            {
                Assert.That(withTangents.Evaluate(0.25f), Is.EqualTo(0.250000983f).Within(WeightedTolerance));
                Assert.That(withTangents.Evaluate(0.5f), Is.EqualTo(0.499999672f).Within(WeightedTolerance));
            });

            AnimationCurve half = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f, 0.5f, 0.5f), new Keyframe(1f, 1f, 0f, 0f, 0.5f, 0.5f));
            Assert.Multiple(() =>
            {
                Assert.That(half.Evaluate(0.1f), Is.EqualTo(0.0146218846f).Within(WeightedTolerance));
                Assert.That(half.Evaluate(0.25f), Is.EqualTo(0.105892561f).Within(WeightedTolerance));
                Assert.That(half.Evaluate(0.75f), Is.EqualTo(0.894107461f).Within(WeightedTolerance));
            });

            // Only one side weighted: the other contributes exactly 1/3.
            AnimationCurve leftOnly = WeightedUnitCurve(0.9f, WeightedMode.Out, 0f, WeightedMode.None);
            Assert.Multiple(() =>
            {
                Assert.That(leftOnly.Evaluate(0.25f), Is.EqualTo(0.03131967f).Within(WeightedTolerance));
                Assert.That(leftOnly.Evaluate(0.5f), Is.EqualTo(0.1658127f).Within(WeightedTolerance));
                Assert.That(leftOnly.Evaluate(0.75f), Is.EqualTo(0.606860757f).Within(WeightedTolerance));
            });

            AnimationCurve rightOnly = WeightedUnitCurve(0f, WeightedMode.None, 0.9f, WeightedMode.In);
            Assert.Multiple(() =>
            {
                Assert.That(rightOnly.Evaluate(0.25f), Is.EqualTo(0.393139154f).Within(WeightedTolerance));
                Assert.That(rightOnly.Evaluate(0.5f), Is.EqualTo(0.83418715f).Within(WeightedTolerance));
            });

            AnimationCurve mixed = new AnimationCurve(
                new Keyframe(1f, 2f, 0f, 0.5f, 0.2f, 0.2f), new Keyframe(3f, -1f, -2f, 0f, 0.6f, 0.6f));
            Assert.Multiple(() =>
            {
                Assert.That(mixed.Evaluate(1.5f), Is.EqualTo(1.76174855f).Within(WeightedTolerance));
                Assert.That(mixed.Evaluate(2f), Is.EqualTo(0.957175f).Within(WeightedTolerance));
                Assert.That(mixed.Evaluate(2.5f), Is.EqualTo(-0.00364238f).Within(WeightedTolerance));
                Assert.That(mixed.Evaluate(2.9f), Is.EqualTo(-0.8000241f).Within(WeightedTolerance));
            });

            AnimationCurve steep = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 5f, 0.5f, 0.5f), new Keyframe(1f, 1f, -5f, 0f, 0.5f, 0.5f));
            Assert.Multiple(() =>
            {
                Assert.That(steep.Evaluate(0.25f), Is.EqualTo(1.31470263f).Within(WeightedTolerance));
                Assert.That(steep.Evaluate(0.5f), Is.EqualTo(2.375f).Within(WeightedTolerance));
                Assert.That(steep.Evaluate(0.75f), Is.EqualTo(2.10291743f).Within(WeightedTolerance));
            });
        }

        [Test]
        public void WeightsOfOneThirdAgreeWithTheHermiteForm()
        {
            // Unity's own root solve returns 0.6999999 here; the shim's is more accurate, which GC §5.8 allows.
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Keyframe[] keys = c.keys;
            keys[0].inWeight = 1f / 3f;
            keys[0].outWeight = 1f / 3f;
            keys[0].weightedMode = WeightedMode.Both;
            keys[1].inWeight = 1f / 3f;
            keys[1].outWeight = 1f / 3f;
            keys[1].weightedMode = WeightedMode.Both;
            c.keys = keys;

            Assert.That(c.Evaluate(0.7f), Is.EqualTo(0.7f).Within(1e-6f));

            AnimationCurve zeroWeights = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f, 0f, 0f));
            Assert.That(zeroWeights.Evaluate(0.5f), Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void UndefinedWeightedModeBitsStillCountAsWeighted()
        {
            // The bit test is on the raw int, so 7 and -1 select the Bézier path (GC §5.4 step 6).
            AnimationCurve c = WeightedUnitCurve(0.9f, (WeightedMode)7, 0.1f, (WeightedMode)(-1));
            Assert.That(c.Evaluate(0.5f), Is.EqualTo(0.140823036f).Within(WeightedTolerance));
        }

        [Test]
        public void AnInfiniteTangentBeatsTheWeightedPath()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.PositiveInfinity, 0.5f, 0.5f),
                new Keyframe(1f, 1f, 0f, 0f, 0.5f, 0.5f));
            Assert.That(c.Evaluate(0.5f), Is.EqualTo(0f), "the step check comes first (GC §5.8)");
        }

        // ===========================================================================================================
        // GC §5.5 - SmoothTangents
        // ===========================================================================================================

        [Test]
        public void SmoothTangentsMatchesTheProbeTable()
        {
            foreach ((float weight, float expected) in new[]
            {
                (-1f, -1.5f), (0f, 0.75f), (0.25f, 1.3125f), (0.5f, 1.875f), (1f, 3f), (2f, 5.25f),
            })
            {
                AnimationCurve c = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(1f, 3f), new Keyframe(3f, 0f));
                c.SmoothTangents(1, weight);
                Assert.Multiple(() =>
                {
                    Assert.That(c[1].inTangent, Is.EqualTo(expected).Within(1e-6f), $"weight {weight}");
                    Assert.That(c[1].outTangent, Is.EqualTo(expected).Within(1e-6f), $"weight {weight}");
                    Assert.That(c[1].inWeight, Is.EqualTo(1f / 3f));
                    Assert.That(c[1].outWeight, Is.EqualTo(1f / 3f));
                });
            }

            foreach ((float weight, float expected) in new[]
            {
                (0f, 0.375f), (0.5f, 0.6875f), (1f, 1f), (-1f, -0.25f),
            })
            {
                AnimationCurve c = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(5f, 0f));
                c.SmoothTangents(1, weight);
                Assert.That(c[1].inTangent, Is.EqualTo(expected).Within(1e-6f),
                    $"plain average, not chord-weighted; weight {weight}");
            }
        }

        [Test]
        public void SmoothTangentsOnEndKeysUsesTheSingleSlope()
        {
            foreach (float weight in new[] { -1f, 0f, 1f, 2f })
            {
                AnimationCurve c = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(1f, 3f), new Keyframe(3f, 0f));
                c.SmoothTangents(0, weight);
                c.SmoothTangents(2, weight);
                Assert.Multiple(() =>
                {
                    Assert.That(c[0].outTangent, Is.EqualTo(3f).Within(1e-6f), $"first key, weight {weight}");
                    Assert.That(c[2].inTangent, Is.EqualTo(-1.5f).Within(1e-6f), $"last key, weight {weight}");
                    Assert.That(c[0].inWeight, Is.EqualTo(0f), "the outer weight of an end key is untouched");
                    Assert.That(c[0].outWeight, Is.EqualTo(1f / 3f));
                    Assert.That(c[2].inWeight, Is.EqualTo(1f / 3f));
                    Assert.That(c[2].outWeight, Is.EqualTo(0f));
                });
            }
        }

        [Test]
        public void SmoothTangentsOnALoneKeyChangesNothing()
        {
            AnimationCurve c = new AnimationCurve(new Keyframe(0f, 0f, 1f, 2f, 0.4f, 0.6f));
            c.SmoothTangents(0, 0f);
            Assert.Multiple(() =>
            {
                Assert.That(c[0].inTangent, Is.EqualTo(1f));
                Assert.That(c[0].outTangent, Is.EqualTo(2f));
                Assert.That(c[0].inWeight, Is.EqualTo(0.4f));
                Assert.That(c[0].outWeight, Is.EqualTo(0.6f));
            });
        }

        [Test]
        public void SmoothTangentsRangeCheck()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.That(Assert.Throws<IndexOutOfRangeException>(() => c.SmoothTangents(2, 0f)).Message,
                Is.EqualTo("SmoothTangents"));
        }

        // ===========================================================================================================
        // GC §5.2 / §5.3 - AddKey, MoveKey, RemoveKey
        // ===========================================================================================================

        [Test]
        public void AddKeyframeInsertsVerbatimAndRejectsDuplicates()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Keyframe inserted = new Keyframe(0.5f, 5f, 7f, 8f, 0.2f, 0.3f);
            int index = c.AddKey(inserted);

            Assert.Multiple(() =>
            {
                Assert.That(index, Is.EqualTo(1));
                Assert.That(c[1].value, Is.EqualTo(5f));
                Assert.That(c[1].inTangent, Is.EqualTo(7f), "inserted verbatim");
                Assert.That(c[1].outWeight, Is.EqualTo(0.3f));
                Assert.That(c[0].outTangent, Is.EqualTo(1f), "neighbours untouched");
            });

            Assert.Multiple(() =>
            {
                Assert.That(c.AddKey(new Keyframe(0.5f, 99f)), Is.EqualTo(-1), "duplicate time");
                Assert.That(c.length, Is.EqualTo(3));
                Assert.That(c[1].value, Is.EqualTo(5f), "nothing changed");
            });
        }

        [Test]
        public void AddKeySmoothsTheNeighbourhood()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            int index = c.AddKey(0.5f, 5f);

            Assert.Multiple(() =>
            {
                Assert.That(index, Is.EqualTo(1));
                Assert.That(c[0].inTangent, Is.EqualTo(10f));
                Assert.That(c[0].outTangent, Is.EqualTo(10f));
                Assert.That(c[0].inWeight, Is.EqualTo(0f), "the first key's inWeight stays 0");
                Assert.That(c[0].outWeight, Is.EqualTo(1f / 3f));
                Assert.That(c[1].inTangent, Is.EqualTo(1f));
                Assert.That(c[1].outTangent, Is.EqualTo(1f));
                Assert.That(c[1].inWeight, Is.EqualTo(1f / 3f));
                Assert.That(c[1].outWeight, Is.EqualTo(1f / 3f));
                Assert.That(c[2].inTangent, Is.EqualTo(-8f));
                Assert.That(c[2].outTangent, Is.EqualTo(-8f));
                Assert.That(c[2].inWeight, Is.EqualTo(1f / 3f));
                Assert.That(c[2].outWeight, Is.EqualTo(0f), "the last key's outWeight stays 0");
                Assert.That(c[1].weightedMode, Is.EqualTo(WeightedMode.None));
            });

            Assert.That(c.AddKey(0.5f, 1f), Is.EqualTo(-1), "the duplicate rule applies here too");
        }

        [Test]
        public void AddKeyOnAnEmptyCurve()
        {
            AnimationCurve c = new AnimationCurve();
            int index = c.AddKey(2f, 3f);
            Assert.Multiple(() =>
            {
                Assert.That(index, Is.EqualTo(0));
                Assert.That(c[0].inTangent, Is.EqualTo(0f));
                Assert.That(c[0].outTangent, Is.EqualTo(0f));
                Assert.That(c[0].inWeight, Is.EqualTo(1f / 3f));
                Assert.That(c[0].outWeight, Is.EqualTo(1f / 3f));
            });
        }

        [Test]
        public void MoveKeyReplacesResortsOrRemoves()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

            Assert.That(c.MoveKey(0, new Keyframe(0f, 9f, 1f, 2f)), Is.EqualTo(0), "same time keeps the index");
            Assert.That(c[0].value, Is.EqualTo(9f));
            Assert.That(c[0].outTangent, Is.EqualTo(2f), "replaced verbatim");

            Assert.That(c.MoveKey(0, new Keyframe(0.75f, 4f)), Is.EqualTo(1), "moved past the key at 0.5");
            Assert.That(c[1].time, Is.EqualTo(0.75f));

            // A collision with a *different* key removes the moved key (GC §5.3).
            Assert.That(c.MoveKey(1, new Keyframe(0.5f, 0f)), Is.EqualTo(-1));
            Assert.That(c.length, Is.EqualTo(2), "the curve lost a key");

            Assert.That(Assert.Throws<IndexOutOfRangeException>(() => c.MoveKey(5, new Keyframe(0f, 0f))).Message,
                Is.EqualTo("MoveKey"));
        }

        [Test]
        public void RemoveKeyRemovesAndRangeChecks()
        {
            AnimationCurve c = new AnimationCurve(
                new Keyframe(0f, 0f, 1f, 1f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f, 2f, 2f));
            c.RemoveKey(1);

            Assert.Multiple(() =>
            {
                Assert.That(c.length, Is.EqualTo(2));
                Assert.That(c[1].time, Is.EqualTo(1f));
                Assert.That(c[0].outTangent, Is.EqualTo(1f), "neighbours untouched");
                Assert.That(Assert.Throws<IndexOutOfRangeException>(() => c.RemoveKey(-1)).Message,
                    Is.EqualTo("RemoveKey"));
                Assert.That(Assert.Throws<IndexOutOfRangeException>(() => c.RemoveKey(2)).Message,
                    Is.EqualTo("RemoveKey"));
            });
        }

        // ===========================================================================================================
        // GC §5.9 - static helpers
        // ===========================================================================================================

        [Test]
        public void StaticHelpersBuildTheDocumentedCurves()
        {
            AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.Multiple(() =>
            {
                Assert.That(linear.length, Is.EqualTo(2));
                Assert.That(linear[0].outTangent, Is.EqualTo(1f));
                Assert.That(linear[1].inTangent, Is.EqualTo(1f));
                Assert.That(linear[0].inTangent, Is.EqualTo(0f));
                Assert.That(linear[1].outTangent, Is.EqualTo(0f));
                Assert.That(linear[0].inWeight, Is.EqualTo(0f));
                Assert.That(linear[0].weightedMode, Is.EqualTo(WeightedMode.None));
                Assert.That(linear.preWrapMode, Is.EqualTo(WrapMode.ClampForever));
                Assert.That(linear.Evaluate(0.25f), Is.EqualTo(0.25f));
                Assert.That(linear.Evaluate(0.3f), Is.EqualTo(0.3f));
            });

            // Reversed times re-sort the keys but leave the tangents with them, so this is an ease, not a line.
            AnimationCurve reversed = AnimationCurve.Linear(1f, 0f, 0f, 1f);
            Assert.Multiple(() =>
            {
                Assert.That(reversed[0].time, Is.EqualTo(0f));
                Assert.That(reversed[0].value, Is.EqualTo(1f));
                Assert.That(reversed[0].outTangent, Is.EqualTo(0f));
                Assert.That(reversed[1].inTangent, Is.EqualTo(0f));
                Assert.That(reversed.Evaluate(0.25f), Is.EqualTo(1f - 0.15625f).Within(1e-6f), "smoothstep shape");
            });

            AnimationCurve degenerate = AnimationCurve.Linear(2f, 5f, 2f, 9f);
            Assert.Multiple(() =>
            {
                Assert.That(degenerate.length, Is.EqualTo(1));
                Assert.That(degenerate[0].value, Is.EqualTo(5f));
            });

            AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            Assert.Multiple(() =>
            {
                Assert.That(ease[0].outTangent, Is.EqualTo(0f));
                Assert.That(ease[1].inTangent, Is.EqualTo(0f));
                Assert.That(AnimationCurve.EaseInOut(3f, 1f, 3f, 2f).length, Is.EqualTo(1));
            });

            AnimationCurve constant = AnimationCurve.Constant(0f, 2f, 7f);
            Assert.Multiple(() =>
            {
                Assert.That(constant.length, Is.EqualTo(2));
                Assert.That(constant[0].outTangent, Is.EqualTo(0f));
                Assert.That(constant.Evaluate(1f), Is.EqualTo(7f));
                Assert.That(constant.Evaluate(-5f), Is.EqualTo(7f));
            });
        }

        // ===========================================================================================================
        // GC §5.10 - equality, hashing, CopyFrom
        // ===========================================================================================================

        [Test]
        public void CurveEqualsCoversWrapModesAndIgnoresTangentMode()
        {
#pragma warning disable 618
            AnimationCurve a = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            AnimationCurve b = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((AnimationCurve)null), Is.False);
            Assert.That(a.Equals((object)"not a curve"), Is.False);
            Assert.That(a.Equals(a), Is.True);

            Keyframe[] keys = b.keys;
            keys[0].tangentMode = 5;
            b.keys = keys;
            Assert.That(a.Equals(b), Is.True, "tangentMode is ignored by Equals");

            AnimationCurve wrapped = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            wrapped.postWrapMode = WrapMode.Loop;
            Assert.That(a.Equals(wrapped), Is.False, "wrap modes are part of Equals");

            AnimationCurve weighted = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Keyframe[] weightedKeys = weighted.keys;
            weightedKeys[0].outWeight = 0.5f;
            weighted.keys = weightedKeys;
            Assert.That(a.Equals(weighted), Is.False, "weights are part of Equals");
#pragma warning restore 618
        }

        [Test]
        public void CurveHashIsContentBasedIgnoringWrapModes()
        {
#pragma warning disable 618
            AnimationCurve a = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            AnimationCurve b = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            Dictionary<AnimationCurve, int> map = new Dictionary<AnimationCurve, int> { [a] = 1 };
            Assert.Multiple(() =>
            {
                Assert.That(b.GetHashCode(), Is.EqualTo(a.GetHashCode()), "equal curves hash equal");
                Assert.That(map.ContainsKey(b), Is.True, "lookup by an equal copy succeeds");
                Assert.That(new AnimationCurve().GetHashCode(), Is.EqualTo(0), "an empty curve hashes to 0");
            });

            AnimationCurve wrapped = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            wrapped.postWrapMode = WrapMode.Loop;
            Assert.That(wrapped.GetHashCode(), Is.EqualTo(a.GetHashCode()), "the hash ignores the wrap modes");

            AnimationCurve tangentModed = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Keyframe[] keys = tangentModed.keys;
            keys[0].tangentMode = 5;
            tangentModed.keys = keys;
            Assert.That(tangentModed.GetHashCode(), Is.Not.EqualTo(a.GetHashCode()),
                "but includes tangentMode, so Equals and GetHashCode are inconsistent exactly as Unity's are");
#pragma warning restore 618
        }

        [Test]
        public void CopyFromCopiesKeysAndWrapModes()
        {
            AnimationCurve source = AnimationCurve.EaseInOut(0f, 0f, 2f, 4f);
            source.preWrapMode = WrapMode.PingPong;
            source.postWrapMode = WrapMode.Loop;

            AnimationCurve target = new AnimationCurve();
            target.CopyFrom(source);

            Assert.Multiple(() =>
            {
                Assert.That(target.length, Is.EqualTo(2));
                Assert.That(target.preWrapMode, Is.EqualTo(WrapMode.PingPong));
                Assert.That(target.postWrapMode, Is.EqualTo(WrapMode.Loop));
                Assert.That(target.Equals(source), Is.True);
            });
        }
    }
}
