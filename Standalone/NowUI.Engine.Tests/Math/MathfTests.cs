// Tests for the UnityEngine.Mathf shim against Docs/Standalone/UnityValueTypeSemantics.md §11 (and §0, §15).
// Every [verified] value in the spec is asserted bit-exactly unless the spec itself allows ulp slack (the native pow
// members). Formula-only members are checked with values worked out by hand from the spec's expression (arithmetic in
// the comments).
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngineInternal;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class MathfTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        private static float FromBits(int bits) => BitConverter.Int32BitsToSingle(bits);
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        // ------------------------------------------------------------------ §0 / §11: type shape and constants

        [Test]
        public void Mathf_IsAValueTypeWithOnlyStaticMembers()
        {
            Assert.That(typeof(Mathf).IsValueType, Is.True);
            Assert.That(typeof(Mathf).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic), Is.Empty);
        }

        [Test]
        public void PI_HasUnityBitPattern()
        {
            Assert.That(Bits(Mathf.PI), Is.EqualTo(0x40490FDB));
            Assert.That(Mathf.PI, Is.EqualTo((float)Math.PI));
        }

        [Test]
        public void Deg2Rad_And_Rad2Deg_HaveUnityBitPatterns()
        {
            Assert.That(Bits(Mathf.Deg2Rad), Is.EqualTo(0x3C8EFA35));
            Assert.That(Bits(Mathf.Rad2Deg), Is.EqualTo(0x42652EE1));
            // 30 * 0.017453292 = 0.52359877 -> nearest float 0.5235988
            Assert.That(30F * Mathf.Deg2Rad, Is.EqualTo(0.5235988F));
        }

        [Test]
        public void Infinity_Constants()
        {
            Assert.That(Mathf.Infinity, Is.EqualTo(float.PositiveInfinity));
            Assert.That(Mathf.NegativeInfinity, Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void Epsilon_IsFloatEpsilonOnDesktop()
        {
            // Spec §11 [verified on Windows x64]: 1.401298E-45 [0x00000001].
            Assert.That(Bits(Mathf.Epsilon), Is.EqualTo(0x00000001));
            Assert.That(Mathf.Epsilon, Is.EqualTo(float.Epsilon));
            Assert.That(MathfInternal.IsFlushToZeroEnabled, Is.False);
        }

        [Test]
        public void MathfInternal_Constants()
        {
            Assert.That(Bits(MathfInternal.FloatMinNormal), Is.EqualTo(0x00800000)); // 1.17549435E-38 = smallest normal
            Assert.That(MathfInternal.FloatMinNormal, Is.EqualTo(1.17549435E-38F));
            Assert.That(MathfInternal.FloatMinDenormal, Is.EqualTo(float.Epsilon));
        }

        [Test]
        public void KMaxDecimals_Is15()
        {
            Assert.That(Mathf.kMaxDecimals, Is.EqualTo(15));
        }

        // ------------------------------------------------------------------ trig / exp: (float) casts of System.Math doubles

        [TestCase(0F)]
        [TestCase(0.5F)]
        [TestCase(1F)]
        [TestCase(-2.5F)]
        [TestCase(3.1415927F)]
        [TestCase(100F)]
        public void Trig_MatchesFloatCastOfSystemMath(float f)
        {
            Assert.That(Bits(Mathf.Sin(f)), Is.EqualTo(Bits((float)Math.Sin(f))));
            Assert.That(Bits(Mathf.Cos(f)), Is.EqualTo(Bits((float)Math.Cos(f))));
            Assert.That(Bits(Mathf.Tan(f)), Is.EqualTo(Bits((float)Math.Tan(f))));
            Assert.That(Bits(Mathf.Atan(f)), Is.EqualTo(Bits((float)Math.Atan(f))));
            Assert.That(Bits(Mathf.Atan2(f, 0.75F)), Is.EqualTo(Bits((float)Math.Atan2(f, 0.75F))));
            Assert.That(Bits(Mathf.Exp(f)), Is.EqualTo(Bits((float)Math.Exp(f))));
        }

        [Test]
        public void Trig_ConcreteValues()
        {
            Assert.That(Mathf.Sin(Mathf.PI), Is.EqualTo(-8.742278E-08F)); // sin((double)3.1415927f) = -8.742278e-8, not 0
            Assert.That(Mathf.Cos(Mathf.PI), Is.EqualTo(-1F));
            Assert.That(Mathf.Sin(Mathf.PI / 2F), Is.EqualTo(1F));
            Assert.That(Mathf.Tan(Mathf.PI / 4F), Is.EqualTo(1F));
            Assert.That(Mathf.Asin(1F), Is.EqualTo(1.5707964F));
            Assert.That(Mathf.Acos(-1F), Is.EqualTo(3.1415927F));
            Assert.That(Mathf.Atan(1F), Is.EqualTo(0.7853982F));
            Assert.That(Mathf.Atan2(1F, 1F), Is.EqualTo(0.7853982F));
            Assert.That(Mathf.Atan2(1F, 0F), Is.EqualTo(1.5707964F));
            Assert.That(Mathf.Atan2(0F, -1F), Is.EqualTo(3.1415927F));
            Assert.That(float.IsNaN(Mathf.Asin(2F)), Is.True);
        }

        [Test]
        public void Sqrt_Pow_Exp_Log()
        {
            Assert.That(Bits(Mathf.Sqrt(2F)), Is.EqualTo(0x3FB504F3)); // 1.4142135
            Assert.That(Mathf.Sqrt(4F), Is.EqualTo(2F));
            Assert.That(float.IsNaN(Mathf.Sqrt(-1F)), Is.True);
            Assert.That(Mathf.Pow(2F, 10F), Is.EqualTo(1024F));
            Assert.That(Mathf.Pow(2F, 0.5F), Is.EqualTo(1.4142135F));
            Assert.That(Bits(Mathf.Pow(0.5F, 2.2F)), Is.EqualTo(Bits((float)Math.Pow(0.5F, 2.2F))));
            Assert.That(Mathf.Exp(0F), Is.EqualTo(1F));
            Assert.That(Mathf.Exp(1F), Is.EqualTo(2.7182817F));
            Assert.That(Mathf.Log(8F, 2F), Is.EqualTo(3F));
            Assert.That(Mathf.Log(100F, 10F), Is.EqualTo(2F));
            Assert.That(Mathf.Log(1F), Is.EqualTo(0F));
            Assert.That(Bits(Mathf.Log(10F)), Is.EqualTo(Bits((float)Math.Log(10F))));
            Assert.That(Mathf.Log10(1000F), Is.EqualTo(3F));
            Assert.That(Mathf.Log10(0.01F), Is.EqualTo(-2F));
            Assert.That(Mathf.Log(0F), Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void Abs_FloatAndInt()
        {
            Assert.That(Mathf.Abs(-1.5F), Is.EqualTo(1.5F));
            Assert.That(Mathf.Abs(1.5F), Is.EqualTo(1.5F));
            Assert.That(Bits(Mathf.Abs(-0F)), Is.EqualTo(0)); // Math.Abs clears the sign bit
            Assert.That(Mathf.Abs(float.NegativeInfinity), Is.EqualTo(float.PositiveInfinity));
            Assert.That(Mathf.Abs(-7), Is.EqualTo(7));
            Assert.That(Mathf.Abs(int.MaxValue), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Abs_IntMinValue_ThrowsOverflow()
        {
            // Spec §11: Abs(int) = Math.Abs(int), which throws for int.MinValue.
            Assert.Throws<OverflowException>(() => Mathf.Abs(int.MinValue));
        }

        // ------------------------------------------------------------------ Min / Max

        [Test]
        public void Min_Max_Float_NaNOrdering()
        {
            // Spec §11 [verified]: Min(NaN,1)=1, Min(1,NaN)=NaN, Max(NaN,1)=1, Max(1,NaN)=NaN.
            Assert.That(Mathf.Min(float.NaN, 1F), Is.EqualTo(1F));
            Assert.That(float.IsNaN(Mathf.Min(1F, float.NaN)), Is.True);
            Assert.That(Mathf.Max(float.NaN, 1F), Is.EqualTo(1F));
            Assert.That(float.IsNaN(Mathf.Max(1F, float.NaN)), Is.True);
        }

        [Test]
        public void Min_Max_Float_Basic()
        {
            Assert.That(Mathf.Min(1F, 2F), Is.EqualTo(1F));
            Assert.That(Mathf.Min(2F, 1F), Is.EqualTo(1F));
            Assert.That(Mathf.Max(1F, 2F), Is.EqualTo(2F));
            Assert.That(Mathf.Max(2F, 1F), Is.EqualTo(2F));
            // a < b false for equal values -> returns b; a > b false -> returns b. Distinguish via signed zero.
            Assert.That(Bits(Mathf.Min(0F, -0F)), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(Mathf.Min(-0F, 0F)), Is.EqualTo(0));
            Assert.That(Bits(Mathf.Max(0F, -0F)), Is.EqualTo(NegativeZeroBits));
            Assert.That(Bits(Mathf.Max(-0F, 0F)), Is.EqualTo(0));
        }

        [Test]
        public void Min_Max_Int()
        {
            Assert.That(Mathf.Min(3, -4), Is.EqualTo(-4));
            Assert.That(Mathf.Min(-4, 3), Is.EqualTo(-4));
            Assert.That(Mathf.Max(3, -4), Is.EqualTo(3));
            Assert.That(Mathf.Max(-4, 3), Is.EqualTo(3));
            Assert.That(Mathf.Min(int.MinValue, int.MaxValue), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.Max(int.MinValue, int.MaxValue), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Min_Max_Params_EmptyReturnsZero()
        {
            Assert.That(Mathf.Min(new float[0]), Is.EqualTo(0F));
            Assert.That(Mathf.Max(new float[0]), Is.EqualTo(0F));
            Assert.That(Mathf.Min(new int[0]), Is.EqualTo(0));
            Assert.That(Mathf.Max(new int[0]), Is.EqualTo(0));
        }

        [Test]
        public void Min_Max_Params_Scan()
        {
            Assert.That(Mathf.Min(new[] { 3F, -1F, 2F }), Is.EqualTo(-1F));
            Assert.That(Mathf.Max(new[] { 3F, -1F, 7F, 2F }), Is.EqualTo(7F));
            Assert.That(Mathf.Min(new[] { 5 }), Is.EqualTo(5));
            Assert.That(Mathf.Min(new[] { 3, -1, 2 }), Is.EqualTo(-1));
            Assert.That(Mathf.Max(new[] { 3, -1, 7, 2 }), Is.EqualTo(7));
            Assert.That(Mathf.Max(new[] { -9 }), Is.EqualTo(-9));
        }

        [Test]
        public void Min_Max_Params_NaNAtElementZeroSticks()
        {
            // Scan starts at element 0 and only replaces it when values[i] < m, which is false against NaN — unlike the
            // two-argument form, Min(params NaN, 1) stays NaN.
            Assert.That(float.IsNaN(Mathf.Min(new[] { float.NaN, 1F })), Is.True);
            Assert.That(float.IsNaN(Mathf.Max(new[] { float.NaN, 1F })), Is.True);
            // A NaN later in the array is skipped (NaN < m is false).
            Assert.That(Mathf.Min(new[] { 1F, float.NaN, -2F }), Is.EqualTo(-2F));
            Assert.That(Mathf.Max(new[] { 1F, float.NaN, 5F }), Is.EqualTo(5F));
        }

        // ------------------------------------------------------------------ rounding

        [Test]
        public void Round_IsBankersRounding()
        {
            // Spec §11 [verified]: MidpointRounding.ToEven.
            Assert.That(Mathf.Round(0.5F), Is.EqualTo(0F));
            Assert.That(Mathf.Round(1.5F), Is.EqualTo(2F));
            Assert.That(Mathf.Round(2.5F), Is.EqualTo(2F));
            Assert.That(Mathf.Round(-1.5F), Is.EqualTo(-2F));
            Assert.That(Mathf.Round(-2.5F), Is.EqualTo(-2F));
            Assert.That(Mathf.Round(3.4F), Is.EqualTo(3F));
            Assert.That(Mathf.Round(3.6F), Is.EqualTo(4F));
        }

        [Test]
        public void Round_NegativeHalf_IsNegativeZero()
        {
            // Spec §11 / §15.11 [verified]: Round(-0.5) = -0 [0x80000000].
            Assert.That(Bits(Mathf.Round(-0.5F)), Is.EqualTo(NegativeZeroBits));
        }

        [Test]
        public void RoundToInt_IsBankersRounding()
        {
            Assert.That(Mathf.RoundToInt(0.5F), Is.EqualTo(0));
            Assert.That(Mathf.RoundToInt(1.5F), Is.EqualTo(2));
            Assert.That(Mathf.RoundToInt(2.5F), Is.EqualTo(2));
            Assert.That(Mathf.RoundToInt(-0.5F), Is.EqualTo(0));
            Assert.That(Mathf.RoundToInt(-1.5F), Is.EqualTo(-2));
            Assert.That(Mathf.RoundToInt(-2.5F), Is.EqualTo(-2));
            Assert.That(Mathf.RoundToInt(7.49F), Is.EqualTo(7));
        }

        [Test]
        public void Ceil_Floor()
        {
            Assert.That(Mathf.Ceil(1.2F), Is.EqualTo(2F));
            Assert.That(Mathf.Ceil(-1.2F), Is.EqualTo(-1F));
            Assert.That(Mathf.Ceil(3F), Is.EqualTo(3F));
            Assert.That(Mathf.Floor(1.8F), Is.EqualTo(1F));
            Assert.That(Mathf.Floor(-1.2F), Is.EqualTo(-2F));
            Assert.That(Mathf.Floor(-3F), Is.EqualTo(-3F));
            Assert.That(Bits(Mathf.Ceil(-0.5F)), Is.EqualTo(NegativeZeroBits)); // Math.Ceiling(-0.5) = -0
            Assert.That(Mathf.CeilToInt(1.2F), Is.EqualTo(2));
            Assert.That(Mathf.CeilToInt(-0.5F), Is.EqualTo(0));
            Assert.That(Mathf.CeilToInt(-1.5F), Is.EqualTo(-1));
            Assert.That(Mathf.FloorToInt(1.8F), Is.EqualTo(1));
            Assert.That(Mathf.FloorToInt(-0.5F), Is.EqualTo(-1));
            Assert.That(Mathf.FloorToInt(-1.5F), Is.EqualTo(-2));
        }

        [Test]
        public void ToInt_NaNAndInfinity_GiveIntMinValueLikeUnityX64()
        {
            // Spec §11: the (int) casts are unchecked and yield int.MinValue for NaN/∞ on x64 (Mono's cvttsd2si
            // "integer indefinite"). .NET 9 saturates instead, so the shim spells the conversion out.
            Assert.That(Mathf.RoundToInt(float.NaN), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.CeilToInt(float.NaN), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.FloorToInt(float.NaN), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.RoundToInt(float.PositiveInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.CeilToInt(float.PositiveInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.FloorToInt(float.PositiveInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.RoundToInt(float.NegativeInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.CeilToInt(float.NegativeInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.FloorToInt(float.NegativeInfinity), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.RoundToInt(3e9F), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.CeilToInt(-3e9F), Is.EqualTo(int.MinValue));
            // In-range extremes still convert normally.
            Assert.That(Mathf.FloorToInt(2147483520F), Is.EqualTo(2147483520)); // largest float below 2^31
            Assert.That(Mathf.CeilToInt(-2147483648F), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void Sign()
        {
            // Spec §11 [verified]: Sign(0)=1, Sign(-0)=1, Sign(NaN)=-1.
            Assert.That(Mathf.Sign(0F), Is.EqualTo(1F));
            Assert.That(Mathf.Sign(-0F), Is.EqualTo(1F));
            Assert.That(Mathf.Sign(float.NaN), Is.EqualTo(-1F));
            Assert.That(Mathf.Sign(2.5F), Is.EqualTo(1F));
            Assert.That(Mathf.Sign(-2.5F), Is.EqualTo(-1F));
            Assert.That(Mathf.Sign(float.NegativeInfinity), Is.EqualTo(-1F));
            Assert.That(Mathf.Sign(float.Epsilon), Is.EqualTo(1F));
        }

        // ------------------------------------------------------------------ clamp / lerp

        [Test]
        public void Clamp_Float()
        {
            Assert.That(Mathf.Clamp(5F, 0F, 1F), Is.EqualTo(1F));
            Assert.That(Mathf.Clamp(-5F, 0F, 1F), Is.EqualTo(0F));
            Assert.That(Mathf.Clamp(0.5F, 0F, 1F), Is.EqualTo(0.5F));
            Assert.That(Mathf.Clamp(0F, 0F, 1F), Is.EqualTo(0F));
            Assert.That(Mathf.Clamp(1F, 0F, 1F), Is.EqualTo(1F));
            // min > max: value < min wins first. Clamp(5, 10, 0): 5 < 10 -> 10. Clamp(20, 10, 0): not < 10, > 0 -> 0.
            Assert.That(Mathf.Clamp(5F, 10F, 0F), Is.EqualTo(10F));
            Assert.That(Mathf.Clamp(20F, 10F, 0F), Is.EqualTo(0F));
        }

        [Test]
        public void Clamp_NaNPassesThrough()
        {
            // Spec §11 [verified].
            Assert.That(float.IsNaN(Mathf.Clamp(float.NaN, 0F, 1F)), Is.True);
            Assert.That(float.IsNaN(Mathf.Clamp01(float.NaN)), Is.True);
            // NaN bounds: value < NaN and value > NaN are both false -> value returned.
            Assert.That(Mathf.Clamp(0.5F, float.NaN, float.NaN), Is.EqualTo(0.5F));
        }

        [Test]
        public void Clamp_Int()
        {
            Assert.That(Mathf.Clamp(5, 0, 3), Is.EqualTo(3));
            Assert.That(Mathf.Clamp(-5, 0, 3), Is.EqualTo(0));
            Assert.That(Mathf.Clamp(2, 0, 3), Is.EqualTo(2));
            Assert.That(Mathf.Clamp(5, 10, 0), Is.EqualTo(10));
            Assert.That(Mathf.Clamp(20, 10, 0), Is.EqualTo(0));
        }

        [Test]
        public void Clamp01()
        {
            Assert.That(Mathf.Clamp01(-0.1F), Is.EqualTo(0F));
            Assert.That(Mathf.Clamp01(1.1F), Is.EqualTo(1F));
            Assert.That(Mathf.Clamp01(0.25F), Is.EqualTo(0.25F));
            Assert.That(Mathf.Clamp01(float.NegativeInfinity), Is.EqualTo(0F));
            Assert.That(Mathf.Clamp01(float.PositiveInfinity), Is.EqualTo(1F));
            Assert.That(Bits(Mathf.Clamp01(-0F)), Is.EqualTo(NegativeZeroBits)); // -0 < 0 is false, so -0 passes through
        }

        [Test]
        public void Lerp_ClampsT()
        {
            // a + (b - a) * Clamp01(t)
            Assert.That(Mathf.Lerp(0F, 10F, 0.5F), Is.EqualTo(5F));
            Assert.That(Mathf.Lerp(0F, 10F, 2F), Is.EqualTo(10F));
            Assert.That(Mathf.Lerp(0F, 10F, -1F), Is.EqualTo(0F));
            Assert.That(Mathf.Lerp(10F, 0F, 0.25F), Is.EqualTo(7.5F)); // 10 + (-10)*0.25
            Assert.That(Mathf.Lerp(3F, 3F, 0.7F), Is.EqualTo(3F));
            Assert.That(float.IsNaN(Mathf.Lerp(0F, 10F, float.NaN)), Is.True);
            // Operation order: 0.1 + (0.3 - 0.1) * 1 in float, not 0.3 directly.
            Assert.That(Bits(Mathf.Lerp(0.1F, 0.3F, 1F)), Is.EqualTo(Bits(0.1F + (0.3F - 0.1F) * 1F)));
        }

        [Test]
        public void LerpUnclamped_DoesNotClamp()
        {
            Assert.That(Mathf.LerpUnclamped(0F, 10F, 2F), Is.EqualTo(20F));
            Assert.That(Mathf.LerpUnclamped(0F, 10F, -1F), Is.EqualTo(-10F));
            Assert.That(Mathf.LerpUnclamped(0F, 10F, 0.5F), Is.EqualTo(5F));
        }

        [Test]
        public void LerpAngle_NotRewrapped()
        {
            // Spec §11 [verified]: LerpAngle(350, 10, 0.5) = 360.
            Assert.That(Mathf.LerpAngle(350F, 10F, 0.5F), Is.EqualTo(360F));
            // LerpAngle(10, 350, 0.5): delta = Repeat(340, 360) = 340 > 180 -> -20; 10 + (-20)*0.5 = 0.
            Assert.That(Mathf.LerpAngle(10F, 350F, 0.5F), Is.EqualTo(0F));
            // LerpAngle(0, 270, 0.5): delta = 270 -> -90; 0 + (-90)*0.5 = -45.
            Assert.That(Mathf.LerpAngle(0F, 270F, 0.5F), Is.EqualTo(-45F));
            // delta exactly 180 is not flipped: LerpAngle(0, 180, 0.5) = 90.
            Assert.That(Mathf.LerpAngle(0F, 180F, 0.5F), Is.EqualTo(90F));
            // t clamped: LerpAngle(350, 10, 3) = 350 + 20*1 = 370.
            Assert.That(Mathf.LerpAngle(350F, 10F, 3F), Is.EqualTo(370F));
        }

        [Test]
        public void InverseLerp()
        {
            // Spec §11 [verified]: InverseLerp(1, 1, 5) = 0.
            Assert.That(Mathf.InverseLerp(1F, 1F, 5F), Is.EqualTo(0F));
            Assert.That(Mathf.InverseLerp(0F, 10F, 5F), Is.EqualTo(0.5F));
            Assert.That(Mathf.InverseLerp(0F, 10F, 20F), Is.EqualTo(1F));
            Assert.That(Mathf.InverseLerp(0F, 10F, -5F), Is.EqualTo(0F));
            // (2.5 - 10) / (0 - 10) = -7.5 / -10 = 0.75
            Assert.That(Mathf.InverseLerp(10F, 0F, 2.5F), Is.EqualTo(0.75F));
            Assert.That(float.IsNaN(Mathf.InverseLerp(0F, 10F, float.NaN)), Is.True);
        }

        [Test]
        public void SmoothStep()
        {
            // Spec §11 [verified]: SmoothStep(0, 1, 0.25) = 0.15625 (= -2*0.015625 + 3*0.0625).
            Assert.That(Mathf.SmoothStep(0F, 1F, 0.25F), Is.EqualTo(0.15625F));
            // t = 0.5: -2*0.125 + 3*0.25 = 0.5; 20*0.5 + 10*0.5 = 15.
            Assert.That(Mathf.SmoothStep(10F, 20F, 0.5F), Is.EqualTo(15F));
            // t clamped to 1: to*1 + from*0.
            Assert.That(Mathf.SmoothStep(10F, 20F, 2F), Is.EqualTo(20F));
            Assert.That(Mathf.SmoothStep(10F, 20F, -1F), Is.EqualTo(10F));
            // t = 0.75: -2*0.421875 + 3*0.5625 = 0.84375.
            Assert.That(Mathf.SmoothStep(0F, 1F, 0.75F), Is.EqualTo(0.84375F));
        }

        [Test]
        public void MoveTowards()
        {
            // Spec §11 [verified]: negative maxDelta moves away: MoveTowards(0, 10, -3) = -3.
            Assert.That(Mathf.MoveTowards(0F, 10F, -3F), Is.EqualTo(-3F));
            Assert.That(Mathf.MoveTowards(0F, 10F, 3F), Is.EqualTo(3F));
            Assert.That(Mathf.MoveTowards(10F, 0F, 3F), Is.EqualTo(7F));
            Assert.That(Mathf.MoveTowards(0F, 10F, 20F), Is.EqualTo(10F));
            Assert.That(Mathf.MoveTowards(0F, 10F, 10F), Is.EqualTo(10F)); // Abs(diff) <= maxDelta
            Assert.That(Mathf.MoveTowards(5F, 5F, 0F), Is.EqualTo(5F));
        }

        [Test]
        public void MoveTowardsAngle()
        {
            // d = DeltaAngle(350, 10) = 20; not within (-5, 5); target = 370; MoveTowards(350, 370, 5) = 355.
            Assert.That(Mathf.MoveTowardsAngle(350F, 10F, 5F), Is.EqualTo(355F));
            // d = 20 within (-30, 30) -> returns the original target (10), not 370.
            Assert.That(Mathf.MoveTowardsAngle(350F, 10F, 30F), Is.EqualTo(10F));
            // d = DeltaAngle(10, 350) = -20; target = -10; MoveTowards(10, -10, 5) = 5.
            Assert.That(Mathf.MoveTowardsAngle(10F, 350F, 5F), Is.EqualTo(5F));
            // maxDelta exactly |d| is not "within" (strict): d = 20, maxDelta 20 -> MoveTowards(350, 370, 20) = 370.
            Assert.That(Mathf.MoveTowardsAngle(350F, 10F, 20F), Is.EqualTo(370F));
        }

        [Test]
        public void Gamma()
        {
            // Spec §11 [verified]: Gamma(0.5, 1, 2.2) = 0.21763763; Gamma(-2, 1, 2.2) = -2.
            Assert.That(Mathf.Gamma(0.5F, 1F, 2.2F), Is.EqualTo(0.21763763F));
            Assert.That(Mathf.Gamma(-2F, 1F, 2.2F), Is.EqualTo(-2F));
            Assert.That(Mathf.Gamma(-0.5F, 1F, 2.2F), Is.EqualTo(-0.21763763F));
            Assert.That(Mathf.Gamma(2F, 1F, 2.2F), Is.EqualTo(2F));
            Assert.That(Mathf.Gamma(0.5F, 1F, 1F), Is.EqualTo(0.5F));
            // Gamma(1, 2, 2): Pow(0.5, 2) * 2 = 0.5
            Assert.That(Mathf.Gamma(1F, 2F, 2F), Is.EqualTo(0.5F));
            Assert.That(Mathf.Gamma(0F, 1F, 2.2F), Is.EqualTo(0F));
        }

        [Test]
        public void Approximately()
        {
            // Spec §11 [verified].
            Assert.That(Mathf.Approximately(1F, 1.000001F), Is.True);
            Assert.That(Mathf.Approximately(1F, 1.00001F), Is.False);
            Assert.That(Mathf.Approximately(0F, 1e-44F), Is.True);   // < Epsilon*8 = 1.12e-44
            Assert.That(Mathf.Approximately(0F, 1e-43F), Is.False);
            Assert.That(Mathf.Approximately(1F, 1F), Is.True);
            Assert.That(Mathf.Approximately(0F, 0F), Is.True);
            Assert.That(Mathf.Approximately(-1F, 1F), Is.False);
            Assert.That(Mathf.Approximately(float.NaN, float.NaN), Is.False);
            Assert.That(Mathf.Approximately(float.PositiveInfinity, float.PositiveInfinity), Is.False); // ∞-∞ = NaN < x false
        }

        [Test]
        public void Repeat()
        {
            // Spec §11 [verified]: Repeat(x, 0) = NaN.
            Assert.That(float.IsNaN(Mathf.Repeat(5F, 0F)), Is.True);
            Assert.That(float.IsNaN(Mathf.Repeat(0F, 0F)), Is.True);
            // 5 - Floor(5/3)*3 = 5 - 3 = 2
            Assert.That(Mathf.Repeat(5F, 3F), Is.EqualTo(2F));
            // -1 - Floor(-1/3)*3 = -1 - (-3) = 2
            Assert.That(Mathf.Repeat(-1F, 3F), Is.EqualTo(2F));
            // 3 - Floor(1)*3 = 0
            Assert.That(Mathf.Repeat(3F, 3F), Is.EqualTo(0F));
            // 7.5 - Floor(3)*2.5 = 0
            Assert.That(Mathf.Repeat(7.5F, 2.5F), Is.EqualTo(0F));
            Assert.That(Mathf.Repeat(0.5F, 1F), Is.EqualTo(0.5F));
            // -1e-8 - Floor(-1e-8)*1 = 1 - 1e-8, which rounds to 1.0f, then Clamp(…, 0, 1) keeps 1 (== length).
            Assert.That(Mathf.Repeat(-1e-8F, 1F), Is.EqualTo(1F));
        }

        [Test]
        public void PingPong()
        {
            // Spec §11 [verified]: PingPong(3.5, 1) = 0.5 (Repeat(3.5, 2) = 1.5; 1 - |1.5 - 1| = 0.5).
            Assert.That(Mathf.PingPong(3.5F, 1F), Is.EqualTo(0.5F));
            Assert.That(Mathf.PingPong(0.5F, 1F), Is.EqualTo(0.5F));
            Assert.That(Mathf.PingPong(1F, 1F), Is.EqualTo(1F));   // Repeat(1,2)=1; 1-0
            Assert.That(Mathf.PingPong(1.5F, 1F), Is.EqualTo(0.5F)); // 1 - |1.5-1|
            Assert.That(Mathf.PingPong(2F, 1F), Is.EqualTo(0F));   // Repeat(2,2)=0; 1-1
            // PingPong(2.5, 2): Repeat(2.5, 4) = 2.5; 2 - |2.5 - 2| = 1.5
            Assert.That(Mathf.PingPong(2.5F, 2F), Is.EqualTo(1.5F));
            Assert.That(float.IsNaN(Mathf.PingPong(1F, 0F)), Is.True);
        }

        [Test]
        public void DeltaAngle()
        {
            // Spec §11 [verified]: DeltaAngle(350, 10) = 20.
            Assert.That(Mathf.DeltaAngle(350F, 10F), Is.EqualTo(20F));
            // Repeat(340, 360) = 340 > 180 -> -20
            Assert.That(Mathf.DeltaAngle(10F, 350F), Is.EqualTo(-20F));
            // Exactly 180 is not flipped.
            Assert.That(Mathf.DeltaAngle(0F, 180F), Is.EqualTo(180F));
            // 181 > 180 -> -179
            Assert.That(Mathf.DeltaAngle(0F, 181F), Is.EqualTo(-179F));
            Assert.That(Mathf.DeltaAngle(90F, 90F), Is.EqualTo(0F));
            // Repeat(720, 360) = 720 - 2*360 = 0
            Assert.That(Mathf.DeltaAngle(0F, 720F), Is.EqualTo(0F));
        }

        // ------------------------------------------------------------------ SmoothDamp family

        [Test]
        public void SmoothDamp_VerifiedValues()
        {
            // Spec §11 [verified]: SmoothDamp(0, 10, ref 0, 0.3, ∞, 0.016) = 0.05165842, vel 6.392509.
            float v = 0F;
            float o = Mathf.SmoothDamp(0F, 10F, ref v, 0.3F, float.PositiveInfinity, 0.016F);
            Assert.That(o, Is.EqualTo(0.05165842F));
            Assert.That(v, Is.EqualTo(6.392509F));
        }

        [Test]
        public void SmoothDamp_VerifiedValues_MaxSpeed()
        {
            // Spec §11 [verified]: with maxSpeed = 5 -> 0.007748774, vel 0.95887625.
            float v = 0F;
            float o = Mathf.SmoothDamp(0F, 10F, ref v, 0.3F, 5F, 0.016F);
            Assert.That(o, Is.EqualTo(0.007748774F));
            Assert.That(v, Is.EqualTo(0.95887625F));
        }

        [Test]
        public void SmoothDamp_Overshoot_ClampsToTargetAndZeroesVelocity()
        {
            // Spec §11 [verified]: (0, 1, ref 100, 0.3, ∞, 0.5) -> 1, vel 0.
            float v = 100F;
            float o = Mathf.SmoothDamp(0F, 1F, ref v, 0.3F, float.PositiveInfinity, 0.5F);
            Assert.That(o, Is.EqualTo(1F));
            Assert.That(v, Is.EqualTo(0F));

            // Mirror image moving downwards: originalTo - current = -1 (not > 0) and output < 0 (not > 0) -> clamp.
            v = -100F;
            o = Mathf.SmoothDamp(1F, 0F, ref v, 0.3F, float.PositiveInfinity, 0.5F);
            Assert.That(o, Is.EqualTo(0F));
            Assert.That(v, Is.EqualTo(0F));
        }

        [Test]
        public void SmoothDamp_DeltaTimeZero_KeepsOriginalVelocity()
        {
            // deltaTime = 0: x = 0, exp = 1, temp = 0, output = target + change = current. With current == target the
            // overshoot test is (false == false) -> true, and the guard restores originalVelocity instead of dividing by 0.
            float v = 100F;
            float o = Mathf.SmoothDamp(1F, 1F, ref v, 0.3F, float.PositiveInfinity, 0F);
            Assert.That(o, Is.EqualTo(1F));
            Assert.That(v, Is.EqualTo(100F));
        }

        [Test]
        public void SmoothDamp_SmoothTimeFloor()
        {
            // smoothTime = Max(0.0001, smoothTime): 0 and 0.0001 behave identically.
            float v1 = 0F, v2 = 0F;
            float o1 = Mathf.SmoothDamp(0F, 10F, ref v1, 0F, float.PositiveInfinity, 0.016F);
            float o2 = Mathf.SmoothDamp(0F, 10F, ref v2, 0.0001F, float.PositiveInfinity, 0.016F);
            Assert.That(Bits(o1), Is.EqualTo(Bits(o2)));
            Assert.That(Bits(v1), Is.EqualTo(Bits(v2)));
        }

        [Test]
        public void SmoothDamp_OverloadsUseEngineClockAndInfinity()
        {
            float saved = EngineClock.deltaTime;
            try
            {
                EngineClock.deltaTime = 0.016F;
                float vA = 0F, vB = 0F, vC = 0F;
                float a = Mathf.SmoothDamp(0F, 10F, ref vA, 0.3F, float.PositiveInfinity, 0.016F);
                float b = Mathf.SmoothDamp(0F, 10F, ref vB, 0.3F, float.PositiveInfinity);
                float c = Mathf.SmoothDamp(0F, 10F, ref vC, 0.3F);
                Assert.That(Bits(b), Is.EqualTo(Bits(a)));
                Assert.That(Bits(vB), Is.EqualTo(Bits(vA)));
                Assert.That(Bits(c), Is.EqualTo(Bits(a)));
                Assert.That(Bits(vC), Is.EqualTo(Bits(vA)));

                // maxSpeed matters, so the 4-arg overload must be using Infinity, not e.g. 0.
                float vD = 0F;
                float d = Mathf.SmoothDamp(0F, 10F, ref vD, 0.3F, 5F);
                Assert.That(d, Is.EqualTo(0.007748774F));

                // Spec §0 requires the shim to expose a Time.deltaTime source. It has to be reachable from OUTSIDE
                // NowUI.Engine (this fixture only sees it because of InternalsVisibleTo), otherwise every host calling
                // the deltaTime-less overloads is stuck at deltaTime == 0.
                Type clock = typeof(EngineClock);
                Assert.That(clock.IsPublic, Is.True, "EngineClock must be public");
                PropertyInfo dt = clock.GetProperty("deltaTime", BindingFlags.Public | BindingFlags.Static);
                Assert.That(dt, Is.Not.Null, "EngineClock.deltaTime must be public static");
                Assert.That(dt.GetGetMethod(), Is.Not.Null.And.Property("IsPublic").True, "getter must be public");
                Assert.That(dt.GetSetMethod(), Is.Not.Null.And.Property("IsPublic").True, "setter must be public");
            }
            finally
            {
                EngineClock.deltaTime = saved;
            }
        }

        [Test]
        public void SmoothDampAngle_RetargetsThroughDeltaAngle()
        {
            // target = current + DeltaAngle(350, 10) = 370, then plain SmoothDamp.
            float v1 = 0F, v2 = 0F;
            float a = Mathf.SmoothDampAngle(350F, 10F, ref v1, 0.3F, float.PositiveInfinity, 0.016F);
            float b = Mathf.SmoothDamp(350F, 370F, ref v2, 0.3F, float.PositiveInfinity, 0.016F);
            Assert.That(Bits(a), Is.EqualTo(Bits(b)));
            Assert.That(Bits(v1), Is.EqualTo(Bits(v2)));
            Assert.That(a, Is.GreaterThan(350F));
        }

        [Test]
        public void SmoothDampAngle_Overloads()
        {
            float saved = EngineClock.deltaTime;
            try
            {
                EngineClock.deltaTime = 0.02F;
                float vA = 0F, vB = 0F, vC = 0F;
                float a = Mathf.SmoothDampAngle(350F, 10F, ref vA, 0.3F, float.PositiveInfinity, 0.02F);
                float b = Mathf.SmoothDampAngle(350F, 10F, ref vB, 0.3F, float.PositiveInfinity);
                float c = Mathf.SmoothDampAngle(350F, 10F, ref vC, 0.3F);
                Assert.That(Bits(b), Is.EqualTo(Bits(a)));
                Assert.That(Bits(c), Is.EqualTo(Bits(a)));
                Assert.That(Bits(vB), Is.EqualTo(Bits(vA)));
                Assert.That(Bits(vC), Is.EqualTo(Bits(vA)));
            }
            finally
            {
                EngineClock.deltaTime = saved;
            }
        }

        // ------------------------------------------------------------------ power of two

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(5, 8)]
        [TestCase(1024, 1024)]
        [TestCase(-3, 0)]
        [TestCase(2, 2)]
        [TestCase(3, 4)]
        [TestCase(1025, 2048)]
        [TestCase(1 << 30, 1 << 30)]
        public void NextPowerOfTwo(int input, int expected)
        {
            // Spec §11 [verified] for 0, 1, 5, 1024, -3.
            Assert.That(Mathf.NextPowerOfTwo(input), Is.EqualTo(expected));
        }

        [TestCase(0, 0)]
        [TestCase(3, 4)]
        [TestCase(5, 4)]
        [TestCase(6, 8)]
        [TestCase(7, 8)]
        [TestCase(12, 16)]
        [TestCase(1, 1)]
        [TestCase(1024, 1024)]
        public void ClosestPowerOfTwo(int input, int expected)
        {
            // Spec §11 [verified] for 0, 3, 5, 6, 7, 12. 6: next 8, prev 4, 6-4 < 8-6 false -> 8.
            Assert.That(Mathf.ClosestPowerOfTwo(input), Is.EqualTo(expected));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(-2, false)]
        [TestCase(1024, true)]
        [TestCase(1023, false)]
        [TestCase(int.MinValue, true)]
        public void IsPowerOfTwo(int input, bool expected)
        {
            // Spec §11 [verified] for 0, 1, 2, 3, -2; int.MinValue & (int.MinValue - 1) = 0x80000000 & 0x7FFFFFFF = 0.
            Assert.That(Mathf.IsPowerOfTwo(input), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------------ internal helpers

        [Test]
        public void LineIntersection_Crossing()
        {
            // p1=(0,0) p2=(2,2) p3=(0,2) p4=(2,0): bx=2 by=2 dx=2 dy=-2; perp = 2*-2 - 2*2 = -8; cx=0 cy=2;
            // t = (0*-2 - 2*2)/-8 = 0.5; result = (0 + 0.5*2, 0 + 0.5*2) = (1, 1).
            var r = new Vector2(-9F, -9F);
            bool hit = Mathf.LineIntersection(new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0), ref r);
            Assert.That(hit, Is.True);
            Assert.That(r.x, Is.EqualTo(1F));
            Assert.That(r.y, Is.EqualTo(1F));
        }

        [Test]
        public void LineIntersection_InfiniteLinesIgnoreSegmentBounds()
        {
            // Short segment (0,0)-(0.5,0.5) still intersects the infinite line through (0,2)-(2,0) at (1,1): t = 2.
            var r = Vector2.zero;
            bool hit = Mathf.LineIntersection(new Vector2(0, 0), new Vector2(0.5F, 0.5F), new Vector2(0, 2), new Vector2(2, 0), ref r);
            Assert.That(hit, Is.True);
            Assert.That(r.x, Is.EqualTo(1F));
            Assert.That(r.y, Is.EqualTo(1F));
        }

        [Test]
        public void LineIntersection_ParallelReturnsFalseAndLeavesResult()
        {
            var r = new Vector2(7F, 8F);
            bool hit = Mathf.LineIntersection(new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 2), ref r);
            Assert.That(hit, Is.False);
            Assert.That(r.x, Is.EqualTo(7F));
            Assert.That(r.y, Is.EqualTo(8F));
        }

        [Test]
        public void LineSegmentIntersection_Crossing()
        {
            // (0,0)-(1,1) with (0,2)-(2,0): bx=1 by=1 dx=2 dy=-2; perp = -2 - 2 = -4; cx=0 cy=2; t = (0 - 4)/-4 = 1 (in range);
            // u = (cx*by - cy*bx)/perp = (0 - 2)/-4 = 0.5 (in range); result = (1, 1).
            var r = Vector2.zero;
            bool hit = Mathf.LineSegmentIntersection(new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 2), new Vector2(2, 0), ref r);
            Assert.That(hit, Is.True);
            Assert.That(r.x, Is.EqualTo(1F));
            Assert.That(r.y, Is.EqualTo(1F));
        }

        [Test]
        public void LineSegmentIntersection_TOutOfRange()
        {
            // (0,0)-(0.5,0.5) with (0,2)-(2,0): perp = 0.5*-2 - 0.5*2 = -2; t = -4/-2 = 2 > 1 -> false.
            var r = new Vector2(7F, 8F);
            bool hit = Mathf.LineSegmentIntersection(new Vector2(0, 0), new Vector2(0.5F, 0.5F), new Vector2(0, 2), new Vector2(2, 0), ref r);
            Assert.That(hit, Is.False);
            Assert.That(r.x, Is.EqualTo(7F));
            Assert.That(r.y, Is.EqualTo(8F));
        }

        [Test]
        public void LineSegmentIntersection_UOutOfRange()
        {
            // (0,0)-(2,2) with (-1,3)-(0,2): bx=2 by=2 dx=1 dy=-1; perp = -2 - 2 = -4; cx=-1 cy=3;
            // t = (cx*dy - cy*dx)/perp = (1 - 3)/-4 = 0.5 (ok); u = (cx*by - cy*bx)/perp = (-2 - 6)/-4 = 2 -> false.
            var r = new Vector2(7F, 8F);
            bool hit = Mathf.LineSegmentIntersection(new Vector2(0, 0), new Vector2(2, 2), new Vector2(-1, 3), new Vector2(0, 2), ref r);
            Assert.That(hit, Is.False);
            Assert.That(r.x, Is.EqualTo(7F));
        }

        [Test]
        public void LineSegmentIntersection_Parallel()
        {
            var r = Vector2.zero;
            Assert.That(Mathf.LineSegmentIntersection(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), ref r), Is.False);
        }

        [Test]
        public void RandomToLong_IsNonNegativeAndDeterministic()
        {
            long a = Mathf.RandomToLong(new System.Random(42));
            long b = Mathf.RandomToLong(new System.Random(42));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.GreaterThanOrEqualTo(0));

            // 8 bytes -> UInt64 & Int64.MaxValue.
            var bytes = new byte[8];
            new System.Random(42).NextBytes(bytes);
            long expected = (long)(BitConverter.ToUInt64(bytes, 0) & long.MaxValue);
            Assert.That(a, Is.EqualTo(expected));
        }

        [Test]
        public void ClampToFloat()
        {
            Assert.That(Mathf.ClampToFloat(double.PositiveInfinity), Is.EqualTo(float.PositiveInfinity));
            Assert.That(Mathf.ClampToFloat(double.NegativeInfinity), Is.EqualTo(float.NegativeInfinity));
            Assert.That(Mathf.ClampToFloat(1e40), Is.EqualTo(float.MaxValue));
            Assert.That(Mathf.ClampToFloat(-1e40), Is.EqualTo(float.MinValue));
            Assert.That(Mathf.ClampToFloat(1.5), Is.EqualTo(1.5F));
            Assert.That(Mathf.ClampToFloat(0.1), Is.EqualTo(0.1F));
            Assert.That(float.IsNaN(Mathf.ClampToFloat(double.NaN)), Is.True);
        }

        [Test]
        public void ClampToIntegerTypes()
        {
            Assert.That(Mathf.ClampToInt(long.MaxValue), Is.EqualTo(int.MaxValue));
            Assert.That(Mathf.ClampToInt(long.MinValue), Is.EqualTo(int.MinValue));
            Assert.That(Mathf.ClampToInt(-5), Is.EqualTo(-5));
            Assert.That(Mathf.ClampToUInt(-1), Is.EqualTo(0u));
            Assert.That(Mathf.ClampToUInt(5000000000L), Is.EqualTo(uint.MaxValue));
            Assert.That(Mathf.ClampToUInt(123), Is.EqualTo(123u));
            Assert.That(Mathf.ClampToShort(40000), Is.EqualTo(short.MaxValue));
            Assert.That(Mathf.ClampToShort(-40000), Is.EqualTo(short.MinValue));
            Assert.That(Mathf.ClampToShort(-7), Is.EqualTo((short)-7));
            Assert.That(Mathf.ClampToUShort(-1), Is.EqualTo((ushort)0));
            Assert.That(Mathf.ClampToUShort(70000), Is.EqualTo(ushort.MaxValue));
            Assert.That(Mathf.ClampToUShort(65535), Is.EqualTo((ushort)65535));
        }

        [Test]
        public void RoundToMultipleOf()
        {
            Assert.That(Mathf.RoundToMultipleOf(7F, 0F), Is.EqualTo(7F));
            Assert.That(Mathf.RoundToMultipleOf(7F, 5F), Is.EqualTo(5F));     // Round(1.4)=1 -> 5
            Assert.That(Mathf.RoundToMultipleOf(12.5F, 5F), Is.EqualTo(10F)); // Round(2.5)=2 (banker's) -> 10
            Assert.That(Mathf.RoundToMultipleOf(17.5F, 5F), Is.EqualTo(20F)); // Round(3.5)=4 -> 20
            Assert.That(Mathf.RoundToMultipleOf(-7F, 5F), Is.EqualTo(-5F));   // Round(-1.4)=-1 -> -5
        }

        [Test]
        public void GetClosestPowerOfTen()
        {
            Assert.That(Mathf.GetClosestPowerOfTen(0F), Is.EqualTo(1F));
            Assert.That(Mathf.GetClosestPowerOfTen(-3F), Is.EqualTo(1F));
            Assert.That(Mathf.GetClosestPowerOfTen(250F), Is.EqualTo(100F));    // log10 = 2.398 -> 2
            Assert.That(Mathf.GetClosestPowerOfTen(5000F), Is.EqualTo(10000F)); // log10 = 3.699 -> 4
            Assert.That(Mathf.GetClosestPowerOfTen(1F), Is.EqualTo(1F));
            Assert.That(Mathf.GetClosestPowerOfTen(0.02F), Is.EqualTo(0.01F));  // log10 = -1.699 -> -2
        }

        [Test]
        public void GetNumberOfDecimalsForMinimumDifference_Float()
        {
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(0.01F), Is.EqualTo(2));  // -Floor(-2) = 2
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(0.05F), Is.EqualTo(2));  // -Floor(-1.3) = 2
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(0.5F), Is.EqualTo(1));   // -Floor(-0.3) = 1
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(100F), Is.EqualTo(0));   // -2 clamped to 0
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(1e-20F), Is.EqualTo(15)); // 20 clamped to 15
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(-0.01F), Is.EqualTo(2)); // Abs
        }

        [Test]
        public void GetNumberOfDecimalsForMinimumDifference_Double()
        {
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(0.01), Is.EqualTo(2));
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(0.5), Is.EqualTo(1));
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(100.0), Is.EqualTo(0));
            Assert.That(Mathf.GetNumberOfDecimalsForMinimumDifference(1e-20), Is.EqualTo(20)); // no upper clamp
        }

        [Test]
        public void RoundBasedOnMinimumDifference_Float()
        {
            // decimals = 0 and AwayFromZero: 2.5 -> 3 (banker's would give 2).
            Assert.That(Mathf.RoundBasedOnMinimumDifference(2.5F, 1F), Is.EqualTo(3F));
            Assert.That(Mathf.RoundBasedOnMinimumDifference(-2.5F, 1F), Is.EqualTo(-3F));
            Assert.That(Mathf.RoundBasedOnMinimumDifference(1.23456F, 0.01F), Is.EqualTo(1.23F));
            // minDifference 0 -> DiscardLeastSignificantDecimal: decimals = (int)(5 - log10(1.23456)) = 4 -> 1.2346
            Assert.That(Mathf.RoundBasedOnMinimumDifference(1.23456F, 0F), Is.EqualTo(1.2346F));
        }

        [Test]
        public void RoundBasedOnMinimumDifference_Double()
        {
            Assert.That(Mathf.RoundBasedOnMinimumDifference(2.5, 1.0), Is.EqualTo(3.0));
            Assert.That(Mathf.RoundBasedOnMinimumDifference(1.23456, 0.001), Is.EqualTo(1.235));
            Assert.That(Mathf.RoundBasedOnMinimumDifference(1.23456, 0.0), Is.EqualTo(1.2346));
        }

        [Test]
        public void DiscardLeastSignificantDecimal_Float()
        {
            // 1.2345679: log10 = 0.0915 -> 5 - 0.0915 = 4.9 -> (int) 4 -> Round(…, 4, Away) = 1.2346
            Assert.That(Mathf.DiscardLeastSignificantDecimal(1.23456789F), Is.EqualTo(1.2346F));
            // 123456.79: log10 = 5.09 -> (int)(-0.09) = 0 -> 123457
            Assert.That(Mathf.DiscardLeastSignificantDecimal(123456.789F), Is.EqualTo(123457F));
            // 0.000123456: log10 = -3.9 -> 8.9 -> 8 -> 0.00012346
            Assert.That(Mathf.DiscardLeastSignificantDecimal(0.000123456F), Is.EqualTo(0.00012346F));
            // 1e-11: 5 + 11 = 16 -> clamped to 15 -> Math.Round(1e-11, 15) = 1e-11 (no exception in the float path)
            Assert.That(Mathf.DiscardLeastSignificantDecimal(1e-11F), Is.EqualTo(1e-11F));
            // 0.5: 5 - (-0.301) = 5.3 -> 5 -> 0.5 unchanged
            Assert.That(Mathf.DiscardLeastSignificantDecimal(0.5F), Is.EqualTo(0.5F));
        }

        [Test]
        public void DiscardLeastSignificantDecimal_Double()
        {
            Assert.That(Mathf.DiscardLeastSignificantDecimal(1.23456789), Is.EqualTo(1.2346));
            Assert.That(Mathf.DiscardLeastSignificantDecimal(123456.789), Is.EqualTo(123457.0));
            Assert.That(Mathf.DiscardLeastSignificantDecimal(2.5), Is.EqualTo(2.5));
            // decimals = 5 + 11 = 16 > 15: Math.Round throws ArgumentOutOfRangeException -> Unity returns 0.
            Assert.That(Mathf.DiscardLeastSignificantDecimal(1e-11), Is.EqualTo(0.0));
            Assert.That(Mathf.DiscardLeastSignificantDecimal(1e-20), Is.EqualTo(0.0));
        }

        // ------------------------------------------------------------------ GammaToLinearSpace / LinearToGammaSpace

        [Test]
        public void GammaToLinearSpace_LinearBranch_NoClamping()
        {
            // Spec §11 [verified]: G2L(-0.5) = -0.03869969 (negatives go through v / 12.92).
            Assert.That(Mathf.GammaToLinearSpace(-0.5F), Is.EqualTo(-0.03869969F));
            Assert.That(Bits(Mathf.GammaToLinearSpace(-0.5F)), Is.EqualTo(Bits(-0.5F / 12.92F)));
            Assert.That(Bits(Mathf.GammaToLinearSpace(0.04045F)), Is.EqualTo(Bits(0.04045F / 12.92F)));
            Assert.That(Bits(Mathf.GammaToLinearSpace(0.001F)), Is.EqualTo(Bits(0.001F / 12.92F)));
            Assert.That(Mathf.GammaToLinearSpace(0F), Is.EqualTo(0F));
            Assert.That(Mathf.GammaToLinearSpace(float.NegativeInfinity), Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void GammaToLinearSpace_PowBranches()
        {
            // Spec §11 [verified]: G2L(1) = 1, G2L(1.5) = 2.4400616, G2L(2) = 4.594794; native pow differs from
            // MathF.Pow by up to 3 ulp, which the spec explicitly tolerates.
            Assert.That(Mathf.GammaToLinearSpace(1F), Is.EqualTo(1F));
            Assert.That(Mathf.GammaToLinearSpace(1.5F), Is.EqualTo(2.4400616F).Within(3).Ulps);
            Assert.That(Mathf.GammaToLinearSpace(2F), Is.EqualTo(4.594794F).Within(3).Ulps);
            // G2L(0.99): native 0.9774019 [0x3F7A3703].
            Assert.That(Mathf.GammaToLinearSpace(0.99F), Is.EqualTo(FromBits(0x3F7A3703)).Within(3).Ulps);
            // G2L(0.5) = pow((0.5 + 0.055)/1.055, 2.4) = pow(0.52606635, 2.4) = 0.21404114 (standard sRGB decode).
            Assert.That(Mathf.GammaToLinearSpace(0.5F), Is.EqualTo(0.21404114F).Within(3).Ulps);
            // Boundary: 0.04045 is the linear branch; the next float up takes the pow branch and lands close by.
            float justAbove = FromBits(Bits(0.04045F) + 1);
            Assert.That(Mathf.GammaToLinearSpace(justAbove), Is.EqualTo(0.04045F / 12.92F).Within(1e-5F));
            Assert.That(Mathf.GammaToLinearSpace(float.PositiveInfinity), Is.EqualTo(float.PositiveInfinity));
            Assert.That(float.IsNaN(Mathf.GammaToLinearSpace(float.NaN)), Is.True);
        }

        [Test]
        public void LinearToGammaSpace_NonPositiveGivesPositiveZero()
        {
            // Spec §11 [verified]: v <= 0 -> +0 for negatives, -0 and -∞.
            Assert.That(Bits(Mathf.LinearToGammaSpace(-0.5F)), Is.EqualTo(0));
            Assert.That(Bits(Mathf.LinearToGammaSpace(-0F)), Is.EqualTo(0));
            Assert.That(Bits(Mathf.LinearToGammaSpace(0F)), Is.EqualTo(0));
            Assert.That(Bits(Mathf.LinearToGammaSpace(float.NegativeInfinity)), Is.EqualTo(0));
        }

        [Test]
        public void LinearToGammaSpace_LinearBranch()
        {
            Assert.That(Bits(Mathf.LinearToGammaSpace(0.001F)), Is.EqualTo(Bits(12.92F * 0.001F)));
            Assert.That(Bits(Mathf.LinearToGammaSpace(0.0031308F)), Is.EqualTo(Bits(12.92F * 0.0031308F)));
        }

        [Test]
        public void LinearToGammaSpace_PowBranches()
        {
            // Spec §11 [verified]: L2G(1) = 1, L2G(1.5) = 1.2023792, L2G(2) = 1.370351, L2G(0.5) = 0.7353569 [0x3F3C405A];
            // ≤1 ulp vs MathF.Pow is expected.
            Assert.That(Mathf.LinearToGammaSpace(1F), Is.EqualTo(1F));
            Assert.That(Mathf.LinearToGammaSpace(0.5F), Is.EqualTo(FromBits(0x3F3C405A)).Within(1).Ulps);
            Assert.That(Mathf.LinearToGammaSpace(1.5F), Is.EqualTo(1.2023792F).Within(1).Ulps);
            Assert.That(Mathf.LinearToGammaSpace(2F), Is.EqualTo(1.370351F).Within(1).Ulps);
            Assert.That(Mathf.LinearToGammaSpace(float.PositiveInfinity), Is.EqualTo(float.PositiveInfinity));
            Assert.That(float.IsNaN(Mathf.LinearToGammaSpace(float.NaN)), Is.True);
        }

        [Test]
        public void GammaLinear_RoundTripIsNotExact()
        {
            // Spec §11 [verified]: G2L(L2G(0.1)) = 0.09999997.
            Assert.That(Mathf.GammaToLinearSpace(Mathf.LinearToGammaSpace(0.1F)), Is.EqualTo(0.09999997F).Within(3).Ulps);
        }

        // ------------------------------------------------------------------ FloatToHalf / HalfToFloat

        [Test]
        public void FloatToHalf_TiesAwayFromZero()
        {
            // Spec §11 [verified].
            Assert.That(Mathf.FloatToHalf(1F + 0.00048828125F), Is.EqualTo((ushort)0x3C01)); // 1 + 2^-11
            Assert.That(Mathf.FloatToHalf(2049F), Is.EqualTo((ushort)0x6801));               // -> 2050
            Assert.That(Mathf.FloatToHalf(1024.5F), Is.EqualTo((ushort)0x6401));             // -> 1025
            Assert.That(Mathf.FloatToHalf(2.9802322E-08F), Is.EqualTo((ushort)0x0001));      // 2^-25, tie -> away
            // Negative ties also go away from zero.
            Assert.That(Mathf.FloatToHalf(-1024.5F), Is.EqualTo((ushort)0xE401));
            Assert.That(Mathf.FloatToHalf(-2.9802322E-08F), Is.EqualTo((ushort)0x8001));
        }

        [Test]
        public void FloatToHalf_Overflow()
        {
            Assert.That(Mathf.FloatToHalf(65520F), Is.EqualTo((ushort)0x7C00));
            Assert.That(Mathf.FloatToHalf(65519F), Is.EqualTo((ushort)0x7BFF)); // below the 65520 tie point
            Assert.That(Mathf.FloatToHalf(65504F), Is.EqualTo((ushort)0x7BFF)); // half max
            Assert.That(Mathf.FloatToHalf(1e30F), Is.EqualTo((ushort)0x7C00));
            Assert.That(Mathf.FloatToHalf(-1e30F), Is.EqualTo((ushort)0xFC00));
            Assert.That(Mathf.FloatToHalf(float.PositiveInfinity), Is.EqualTo((ushort)0x7C00));
            Assert.That(Mathf.FloatToHalf(float.NegativeInfinity), Is.EqualTo((ushort)0xFC00));
        }

        [Test]
        public void FloatToHalf_Subnormals()
        {
            Assert.That(Mathf.FloatToHalf(6.1e-5F), Is.EqualTo((ushort)0x03FF));       // spec: 6.1e-5 -> 0x03FF
            Assert.That(Mathf.FloatToHalf(6.1035156E-05F), Is.EqualTo((ushort)0x0400)); // 2^-14, smallest normal
            Assert.That(Mathf.FloatToHalf(1e-8F), Is.EqualTo((ushort)0x0000));
            Assert.That(Mathf.FloatToHalf(5.9604645E-08F), Is.EqualTo((ushort)0x0001)); // 2^-24 exactly
            Assert.That(Mathf.FloatToHalf(1.4901161E-08F), Is.EqualTo((ushort)0x0000)); // 2^-26 rounds down
            // 1.5 * 2^-25 = 0.75 units of 2^-24 -> rounds to 1.
            Assert.That(Mathf.FloatToHalf(1.5F * 2.9802322E-08F), Is.EqualTo((ushort)0x0001));
            // Just below 2^-14 rounds up into the smallest normal.
            Assert.That(Mathf.FloatToHalf(FromBits(0x387FFFFF)), Is.EqualTo((ushort)0x0400));
            Assert.That(Mathf.FloatToHalf(float.Epsilon), Is.EqualTo((ushort)0x0000));
        }

        [Test]
        public void FloatToHalf_SignedZeroAndNaN()
        {
            Assert.That(Mathf.FloatToHalf(0F), Is.EqualTo((ushort)0x0000));
            Assert.That(Mathf.FloatToHalf(-0F), Is.EqualTo((ushort)0x8000));
            // Spec §11 [verified]: float.NaN (0xFFC00000) -> 0xFF00.
            Assert.That(Bits(float.NaN), Is.EqualTo(unchecked((int)0xFFC00000)));
            Assert.That(Mathf.FloatToHalf(float.NaN), Is.EqualTo((ushort)0xFF00));
            Assert.That(Mathf.FloatToHalf(FromBits(0x7FC00000)), Is.EqualTo((ushort)0x7F00));
        }

        [Test]
        public void FloatToHalf_ExactValues()
        {
            Assert.That(Mathf.FloatToHalf(1F), Is.EqualTo((ushort)0x3C00));
            Assert.That(Mathf.FloatToHalf(-2F), Is.EqualTo((ushort)0xC000));
            Assert.That(Mathf.FloatToHalf(0.5F), Is.EqualTo((ushort)0x3800));
            Assert.That(Mathf.FloatToHalf(1024F), Is.EqualTo((ushort)0x6400));
            Assert.That(Mathf.FloatToHalf(0.33333334F), Is.EqualTo((ushort)0x3555)); // 1/3 rounds down (0.3333 < tie)
        }

        [Test]
        public void HalfToFloat_VerifiedValues()
        {
            Assert.That(Mathf.HalfToFloat(0x7C00), Is.EqualTo(float.PositiveInfinity));
            Assert.That(Mathf.HalfToFloat(0xFC00), Is.EqualTo(float.NegativeInfinity));
            Assert.That(Bits(Mathf.HalfToFloat(0x7E00)), Is.EqualTo(0x7FC00000));
            Assert.That(Mathf.HalfToFloat(0x0001), Is.EqualTo(5.9604645E-08F));
            Assert.That(Mathf.HalfToFloat(0x03FF), Is.EqualTo(6.097555E-05F));
            Assert.That(Mathf.HalfToFloat(0x0400), Is.EqualTo(6.1035156E-05F));
        }

        [Test]
        public void HalfToFloat_MoreValues()
        {
            Assert.That(Mathf.HalfToFloat(0x3C00), Is.EqualTo(1F));
            Assert.That(Mathf.HalfToFloat(0x3C01), Is.EqualTo(1.0009766F)); // 1 + 2^-10
            Assert.That(Mathf.HalfToFloat(0xC000), Is.EqualTo(-2F));
            Assert.That(Mathf.HalfToFloat(0x7BFF), Is.EqualTo(65504F));
            Assert.That(Bits(Mathf.HalfToFloat(0x0000)), Is.EqualTo(0));
            Assert.That(Bits(Mathf.HalfToFloat(0x8000)), Is.EqualTo(NegativeZeroBits));
            Assert.That(Mathf.HalfToFloat(0x8001), Is.EqualTo(-5.9604645E-08F));
            Assert.That(float.IsNaN(Mathf.HalfToFloat(0xFE00)), Is.True);
        }

        [Test]
        public void Half_RoundTripsEveryFiniteAndInfiniteHalf()
        {
            for (int h = 0; h < 65536; h++)
            {
                bool isNaN = ((h >> 10) & 0x1F) == 0x1F && (h & 0x3FF) != 0;
                if (isNaN)
                    continue;
                Assert.That(Mathf.FloatToHalf(Mathf.HalfToFloat((ushort)h)), Is.EqualTo((ushort)h), $"half 0x{h:X4}");
            }
        }

        // ------------------------------------------------------------------ CorrelatedColorTemperatureToRGB

        [Test]
        public void CorrelatedColorTemperatureToRGB_ClampsInput()
        {
            // Spec §11 [verified]: 500 K == 1000 K and 40000 K == 50000 K.
            Color lo1 = Mathf.CorrelatedColorTemperatureToRGB(500F);
            Color lo2 = Mathf.CorrelatedColorTemperatureToRGB(1000F);
            Assert.That(Bits(lo1.r), Is.EqualTo(Bits(lo2.r)));
            Assert.That(Bits(lo1.g), Is.EqualTo(Bits(lo2.g)));
            Assert.That(Bits(lo1.b), Is.EqualTo(Bits(lo2.b)));
            Color hi1 = Mathf.CorrelatedColorTemperatureToRGB(40000F);
            Color hi2 = Mathf.CorrelatedColorTemperatureToRGB(50000F);
            Assert.That(Bits(hi1.r), Is.EqualTo(Bits(hi2.r)));
            Assert.That(Bits(hi1.g), Is.EqualTo(Bits(hi2.g)));
            Assert.That(Bits(hi1.b), Is.EqualTo(Bits(hi2.b)));
        }

        // Spec §11 [verified] samples. Red, blue and green above 6.57 use the spec's own rational fits ("reproduce the
        // probe to float rounding"); the green branch below 6.57 is an unverified fit, so it gets a looser bound.
        [TestCase(1000F, 1F, 0.041611645F, 0.00333669F)]
        [TestCase(1500F, 1F, 0.14693816F, 0F)]
        [TestCase(2700F, 1F, 0.3989094F, 0.09633335F)]
        [TestCase(4000F, 1F, 0.6348557F, 0.3667193F)]
        [TestCase(5000F, 1F, 0.77996707F, 0.619521F)]
        [TestCase(6500F, 1F, 0.9433625F, 0.98279023F)]
        [TestCase(8000F, 0.7616496F, 0.81257623F, 1F)]
        [TestCase(10000F, 0.60288876F, 0.7100564F, 1F)]
        [TestCase(20000F, 0.39244252F, 0.5562728F, 1F)]
        [TestCase(40000F, 0.32911316F, 0.50275207F, 1F)]
        public void CorrelatedColorTemperatureToRGB_Samples(float kelvin, float r, float g, float b)
        {
            Color c = Mathf.CorrelatedColorTemperatureToRGB(kelvin);
            Assert.That(c.a, Is.EqualTo(1F));
            Assert.That(c.r, Is.EqualTo(r).Within(2).Ulps, "r");
            Assert.That(c.b, Is.EqualTo(b).Within(2).Ulps, "b");
            if (kelvin >= 6570F)
                Assert.That(c.g, Is.EqualTo(g).Within(2).Ulps, "g");
            else
                Assert.That(c.g, Is.EqualTo(g).Within(8).Ulps, "g (unverified fit below 6570 K)");
            // Largest channel is exactly 1.
            Assert.That(Mathf.Max(c.r, c.g, c.b), Is.EqualTo(1F));
        }

        [Test]
        public void CorrelatedColorTemperatureToRGB_BranchBoundaries()
        {
            // k < 6.57 -> red 1; k > 6.57 -> blue 1; every output channel is in [0, 1].
            for (float k = 1000F; k <= 40000F; k += 250F)
            {
                Color c = Mathf.CorrelatedColorTemperatureToRGB(k);
                Assert.That(c.r, Is.InRange(0F, 1F), $"r at {k}");
                Assert.That(c.g, Is.InRange(0F, 1F), $"g at {k}");
                Assert.That(c.b, Is.InRange(0F, 1F), $"b at {k}");
                if (k < 6570F)
                    Assert.That(c.r, Is.EqualTo(1F), $"r at {k}");
                if (k > 6570F)
                    Assert.That(c.b, Is.EqualTo(1F), $"b at {k}");
            }
        }

        // ------------------------------------------------------------------ PerlinNoise (unverified vs Unity by design)

        [Test]
        public void PerlinNoise_IsDeterministicAndInUnitRange()
        {
            // Spec §11: Unity's lattice/hash is not reproducible; only the nominal 0..1 range and determinism are checked.
            for (float x = -3F; x <= 3F; x += 0.37F)
            {
                for (float y = -2F; y <= 2F; y += 0.41F)
                {
                    float a = Mathf.PerlinNoise(x, y);
                    float b = Mathf.PerlinNoise(x, y);
                    Assert.That(Bits(a), Is.EqualTo(Bits(b)));
                    Assert.That(a, Is.InRange(0F, 1F), $"({x}, {y})");
                }
            }
        }

        [Test]
        public void PerlinNoise1D_EqualsPerlinNoiseWithZeroY()
        {
            // Spec §11 [verified on Unity for x = 1.3]: PerlinNoise1D(x) == PerlinNoise(x, 0).
            foreach (float x in new[] { 0F, 0.5F, 1.3F, -1.5F, 10.25F })
                Assert.That(Bits(Mathf.PerlinNoise1D(x)), Is.EqualTo(Bits(Mathf.PerlinNoise(x, 0F))));
        }

        [Test]
        public void PerlinNoise_VariesWithInput()
        {
            Assert.That(Mathf.PerlinNoise(0.5F, 0.5F), Is.Not.EqualTo(Mathf.PerlinNoise(1.3F, 2.7F)));
        }
    }
}
