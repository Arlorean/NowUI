// Mirrors UnityEngine.Mathf (and UnityEngineInternal.MathfInternal) for the NowUI standalone build. Spec: Docs/Standalone/UnityValueTypeSemantics.md (§11).
using System;
using UnityEngineInternal;

namespace UnityEngineInternal
{
    /// <summary>
    /// Spec §11: the two float constants are read through volatile loads at type initialisation so that a CPU running in
    /// flush-to-zero mode reads the denormal as 0 and <see cref="IsFlushToZeroEnabled"/> becomes true.
    /// </summary>
    public partial struct MathfInternal
    {
        public static volatile float FloatMinNormal = 1.17549435E-38f;
        public static volatile float FloatMinDenormal = float.Epsilon;
        public static bool IsFlushToZeroEnabled = (FloatMinDenormal == 0);
    }
}

namespace UnityEngine
{
    /// <summary>Static math helpers; a value type with only static members, exactly like Unity's.</summary>
    public partial struct Mathf
    {
        // ---------------------------------------------------------------- constants (spec §11 "Constants")

        public const float PI = (float)Math.PI;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = PI * 2F / 360F;
        public const float Rad2Deg = 1F / Deg2Rad;

        /// <summary>float.Epsilon on desktop; the smallest normal float only when the CPU flushes denormals to zero.</summary>
        public static readonly float Epsilon =
            MathfInternal.IsFlushToZeroEnabled ? MathfInternal.FloatMinNormal : MathfInternal.FloatMinDenormal;

        internal const int kMaxDecimals = 15;

        private static readonly float ApproxEpsilon = Epsilon * 8;

        // ---------------------------------------------------------------- trig / exp: (float) casts of System.Math doubles

        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Tan(float f) { return (float)Math.Tan(f); }
        public static float Asin(float f) { return (float)Math.Asin(f); }
        public static float Acos(float f) { return (float)Math.Acos(f); }
        public static float Atan(float f) { return (float)Math.Atan(f); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static int Abs(int value) { return Math.Abs(value); }
        public static float Pow(float f, float p) { return (float)Math.Pow(f, p); }
        public static float Exp(float power) { return (float)Math.Exp(power); }
        public static float Log(float f, float p) { return (float)Math.Log(f, p); }
        public static float Log(float f) { return (float)Math.Log(f); }
        public static float Log10(float f) { return (float)Math.Log10(f); }

        // ---------------------------------------------------------------- min / max (NaN ordering follows from the expressions)

        public static float Min(float a, float b) { return a < b ? a : b; }

        public static float Min(params float[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;

            float m = values[0];
            for (int i = 1; i < len; i++)
            {
                if (values[i] < m)
                    m = values[i];
            }
            return m;
        }

        public static int Min(int a, int b) { return a < b ? a : b; }

        public static int Min(params int[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;

            int m = values[0];
            for (int i = 1; i < len; i++)
            {
                if (values[i] < m)
                    m = values[i];
            }
            return m;
        }

        public static float Max(float a, float b) { return a > b ? a : b; }

        public static float Max(params float[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;

            float m = values[0];
            for (int i = 1; i < len; i++)
            {
                if (values[i] > m)
                    m = values[i];
            }
            return m;
        }

        public static int Max(int a, int b) { return a > b ? a : b; }

        public static int Max(params int[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;

            int m = values[0];
            for (int i = 1; i < len; i++)
            {
                if (values[i] > m)
                    m = values[i];
            }
            return m;
        }

        // ---------------------------------------------------------------- rounding (Math.Round is banker's rounding; Round(-0.5) = -0)

        public static float Ceil(float f) { return (float)Math.Ceiling(f); }
        public static float Floor(float f) { return (float)Math.Floor(f); }
        public static float Round(float f) { return (float)Math.Round(f); }
        public static int CeilToInt(float f) { return ToIntUnchecked(Math.Ceiling(f)); }
        public static int FloorToInt(float f) { return ToIntUnchecked(Math.Floor(f)); }
        public static int RoundToInt(float f) { return ToIntUnchecked(Math.Round(f)); }

        /// <summary>
        /// Unity's <c>(int)double</c> casts are unchecked: Mono/IL2CPP on x64 lower them to cvttsd2si, which yields the
        /// "integer indefinite" value int.MinValue for NaN, ±∞ and anything outside the int range (spec §11). .NET 9
        /// saturates those conversions instead (NaN → 0, +∞ → int.MaxValue), so the x64 result is reproduced explicitly.
        /// // unverified vs Unity: the spec only measured NaN/±∞ on Windows x64; ARM backends may saturate.
        /// </summary>
        private static int ToIntUnchecked(double d)
        {
            if (d >= -2147483648.0 && d < 2147483648.0)
                return (int)d;
            return int.MinValue;
        }

        /// <summary>Sign(0) = Sign(-0) = 1; Sign(NaN) = -1 (spec §11).</summary>
        public static float Sign(float f) { return f >= 0F ? 1F : -1F; }

        // ---------------------------------------------------------------- clamp / lerp

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
                value = min;
            else if (value > max)
                value = max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
                value = min;
            else if (value > max)
                value = max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0F)
                return 0F;
            else if (value > 1F)
                return 1F;
            else
                return value;
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        public static float LerpUnclamped(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>Result is not re-wrapped into [0, 360): LerpAngle(350, 10, 0.5) = 360 (spec §11).</summary>
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = Repeat((b - a), 360);
            if (delta > 180)
                delta -= 360;
            return a + delta * Clamp01(t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta)
                return target;
            return current + Sign(target - current) * maxDelta;
        }

        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float deltaAngle = DeltaAngle(current, target);
            if (-maxDelta < deltaAngle && deltaAngle < maxDelta)
                return target;
            target = current + deltaAngle;
            return MoveTowards(current, target, maxDelta);
        }

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2F * t * t * t + 3F * t * t;
            return to * t + from * (1F - t);
        }

        public static float Gamma(float value, float absmax, float gamma)
        {
            bool negative = value < 0F;
            float absval = Abs(value);
            if (absval > absmax)
                return negative ? -absval : absval;

            float result = Pow(absval / absmax, gamma) * absmax;
            return negative ? -result : result;
        }

        /// <summary>Relative tolerance of 1e-6 with an absolute floor of Epsilon * 8 (spec §11).</summary>
        public static bool Approximately(float a, float b)
        {
            return Abs(b - a) < Max(0.000001f * Max(Abs(a), Abs(b)), ApproxEpsilon);
        }

        // ---------------------------------------------------------------- SmoothDamp family (spec §11 "SmoothDamp (float)")

        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            float maxSpeed = Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        /// <summary>
        /// Spec §11 step order. Precision note: the spec's [verified] oracle values (0.05165842 / 6.392509, 0.007748774 /
        /// 0.95887625) are only reproduced bit-exactly when every expression is evaluated in double and rounded to float at
        /// each local store — the Mono JIT's default float evaluation model on the probe machine; a pure single-precision
        /// evaluation lands ~200 ulp away on the output because of the cancellation in the last step. The verified values
        /// are the oracle, so the intermediates are widened explicitly here. // unverified vs Unity: IL2CPP builds may differ.
        /// </summary>
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            // Step 1: critically damped spring approximation.
            smoothTime = Max(0.0001F, smoothTime);
            float omega = (float)(2.0 / smoothTime);

            float x = (float)((double)omega * deltaTime);
            float exp = (float)(1.0 / (1.0 + x + 0.48F * (double)x * x + 0.235F * (double)x * x * x));

            // Step 2: clamp the change by maxSpeed.
            float change = (float)((double)current - target);
            float originalTo = target;

            float maxChange = (float)((double)maxSpeed * smoothTime);
            change = Clamp(change, -maxChange, maxChange);
            target = (float)((double)current - change);

            // Step 3: integrate.
            float temp = (float)(((double)currentVelocity + (double)omega * change) * deltaTime);
            float originalVelocity = currentVelocity;
            currentVelocity = (float)(((double)currentVelocity - (double)omega * temp) * exp);
            float output = (float)((double)target + ((double)change + temp) * exp);

            // Step 4: prevent overshoot (scalar form: sign comparison, deltaTime != 0 guard).
            if (originalTo - current > 0.0F == output > originalTo)
            {
                output = originalTo;
                currentVelocity = deltaTime != 0F ? (output - originalTo) / deltaTime : originalVelocity;
            }

            return output;
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            float maxSpeed = Infinity;
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            target = current + DeltaAngle(current, target);
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        // ---------------------------------------------------------------- repeat / angles

        /// <summary>Repeat(x, 0) = NaN (0 / 0 inside the Floor), per spec §11.</summary>
        public static float Repeat(float t, float length)
        {
            return Clamp(t - Floor(t / length) * length, 0.0f, length);
        }

        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2F);
            return length - Abs(t - length);
        }

        /// <summary>Returns 0 when a == b (spec §11), otherwise the clamped parametric position of value.</summary>
        public static float InverseLerp(float a, float b, float value)
        {
            if (a != b)
                return Clamp01((value - a) / (b - a));
            else
                return 0.0f;
        }

        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat((target - current), 360.0F);
            if (delta > 180.0F)
                delta -= 360.0F;
            return delta;
        }

        // ---------------------------------------------------------------- power of two

        /// <summary>Bit-smearing form; NextPowerOfTwo(0) = 0 and negative inputs give 0 (spec §11).</summary>
        public static int NextPowerOfTwo(int value)
        {
            value -= 1;
            value |= value >> 16;
            value |= value >> 8;
            value |= value >> 4;
            value |= value >> 2;
            value |= value >> 1;
            return value + 1;
        }

        public static int ClosestPowerOfTwo(int value)
        {
            int nextPower = NextPowerOfTwo(value);
            int prevPower = nextPower >> 1;
            if (value - prevPower < nextPower - value)
                return prevPower;
            else
                return nextPower;
        }

        public static bool IsPowerOfTwo(int value)
        {
            return (value & (value - 1)) == 0;
        }

        // ---------------------------------------------------------------- internal helpers (internal in Unity too; visible to the test project)

        /// <summary>Intersection of the infinite lines p1→p2 and p3→p4 (spec §11 "Internal helpers").</summary>
        internal static bool LineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, ref Vector2 result)
        {
            float bx = p2.x - p1.x;
            float by = p2.y - p1.y;
            float dx = p4.x - p3.x;
            float dy = p4.y - p3.y;
            float bDotDPerp = bx * dy - by * dx;
            if (bDotDPerp == 0)
                return false;

            float cx = p3.x - p1.x;
            float cy = p3.y - p1.y;
            float t = (cx * dy - cy * dx) / bDotDPerp;

            result.x = p1.x + t * bx;
            result.y = p1.y + t * by;
            return true;
        }

        /// <summary>Intersection of the segments p1–p2 and p3–p4; false when either parameter leaves [0, 1].</summary>
        internal static bool LineSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, ref Vector2 result)
        {
            float bx = p2.x - p1.x;
            float by = p2.y - p1.y;
            float dx = p4.x - p3.x;
            float dy = p4.y - p3.y;
            float bDotDPerp = bx * dy - by * dx;
            if (bDotDPerp == 0)
                return false;

            float cx = p3.x - p1.x;
            float cy = p3.y - p1.y;
            float t = (cx * dy - cy * dx) / bDotDPerp;
            if (t < 0 || t > 1)
                return false;

            float u = (cx * by - cy * bx) / bDotDPerp;
            if (u < 0 || u > 1)
                return false;

            result.x = p1.x + t * bx;
            result.y = p1.y + t * by;
            return true;
        }

        internal static long RandomToLong(System.Random r)
        {
            var buffer = new byte[8];
            r.NextBytes(buffer);
            return (long)(BitConverter.ToUInt64(buffer, 0) & long.MaxValue);
        }

        /// <summary>Saturating double→float; ±∞ is preserved, NaN passes through the plain cast.</summary>
        internal static float ClampToFloat(double value)
        {
            if (double.IsPositiveInfinity(value))
                return float.PositiveInfinity;
            if (double.IsNegativeInfinity(value))
                return float.NegativeInfinity;
            if (value < float.MinValue)
                return float.MinValue;
            if (value > float.MaxValue)
                return float.MaxValue;
            return (float)value;
        }

        internal static int ClampToInt(long value)
        {
            if (value < int.MinValue)
                return int.MinValue;
            if (value > int.MaxValue)
                return int.MaxValue;
            return (int)value;
        }

        internal static uint ClampToUInt(long value)
        {
            if (value < uint.MinValue)
                return uint.MinValue;
            if (value > uint.MaxValue)
                return uint.MaxValue;
            return (uint)value;
        }

        internal static short ClampToShort(long value)
        {
            if (value < short.MinValue)
                return short.MinValue;
            if (value > short.MaxValue)
                return short.MaxValue;
            return (short)value;
        }

        internal static ushort ClampToUShort(long value)
        {
            if (value < ushort.MinValue)
                return ushort.MinValue;
            if (value > ushort.MaxValue)
                return ushort.MaxValue;
            return (ushort)value;
        }

        internal static float RoundToMultipleOf(float value, float roundingValue)
        {
            if (roundingValue == 0)
                return value;
            return Round(value / roundingValue) * roundingValue;
        }

        internal static float GetClosestPowerOfTen(float positiveNumber)
        {
            if (positiveNumber <= 0)
                return 1;
            return Pow(10, RoundToInt(Log10(positiveNumber)));
        }

        internal static int GetNumberOfDecimalsForMinimumDifference(float minDifference)
        {
            return Clamp(-FloorToInt(Log10(Abs(minDifference))), 0, kMaxDecimals);
        }

        internal static int GetNumberOfDecimalsForMinimumDifference(double minDifference)
        {
            return (int)Math.Max(0.0, -Math.Floor(Math.Log10(Math.Abs(minDifference))));
        }

        internal static float RoundBasedOnMinimumDifference(float valueToRound, float minDifference)
        {
            if (minDifference == 0)
                return DiscardLeastSignificantDecimal(valueToRound);
            return (float)Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
        }

        internal static double RoundBasedOnMinimumDifference(double valueToRound, double minDifference)
        {
            if (minDifference == 0)
                return DiscardLeastSignificantDecimal(valueToRound);
            return Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
        }

        internal static float DiscardLeastSignificantDecimal(float v)
        {
            int decimals = Clamp((int)(5 - Log10(Abs(v))), 0, kMaxDecimals);
            return (float)Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        }

        internal static double DiscardLeastSignificantDecimal(double v)
        {
            int decimals = Math.Max(0, (int)(5 - Math.Log10(Math.Abs(v))));
            try
            {
                // unverified vs Unity: whether the double overload passes MidpointRounding.AwayFromZero like the float one.
                return Math.Round(v, decimals, MidpointRounding.AwayFromZero);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Math.Round throws when decimals > 15; Unity returns 0 in that case.
                return 0;
            }
        }

        // ---------------------------------------------------------------- native members (spec §11 "Native members")

        /// <summary>
        /// sRGB decode without clamping (spec §11). Unity's native pow is single precision and differs from MathF.Pow by up
        /// to ~3 ulp on some inputs; MathF.Pow is the closest available candidate, so those differences are expected.
        /// </summary>
        public static float GammaToLinearSpace(float value)
        {
            if (value <= 0.04045F)
                return value / 12.92F;
            if (value < 1.0F)
                return MathF.Pow((value + 0.055F) / 1.055F, 2.4F);
            return MathF.Pow(value, 2.2F);
        }

        /// <summary>sRGB encode; every value &lt;= 0 (including -0 and -∞) yields +0 (spec §11).</summary>
        public static float LinearToGammaSpace(float value)
        {
            if (value <= 0.0F)
                return 0.0F;
            if (value <= 0.0031308F)
                return 12.92F * value;
            if (value < 1.0F)
                return 1.055F * MathF.Pow(value, 0.4166667F) - 0.055F;
            return MathF.Pow(value, 0.45454545F);
        }

        /// <summary>
        /// IEEE half encode with round-to-nearest, ties away from zero (spec §11: 1+2^-11 → 0x3C01, 2049 → 0x6801,
        /// 1024.5 → 0x6401, 2^-25 → 0x0001, ≥ 65520 → 0x7C00, 6.1e-5 → 0x03FF, 1e-8 → 0, signed zero kept).
        /// </summary>
        public static ushort FloatToHalf(float val)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(val);
            uint sign = (bits >> 16) & 0x8000u;
            uint abs = bits & 0x7FFFFFFFu;

            if (abs >= 0x7F800000u)
            {
                if (abs == 0x7F800000u)
                    return (ushort)(sign | 0x7C00u);
                // unverified vs Unity: only float.NaN (0xFFC00000 → 0xFF00) was probed; other NaN payloads are unknown,
                // so every NaN maps to the same signed pattern here.
                return (ushort)(sign | 0x7F00u);
            }

            if (abs >= 0x38800000u)
            {
                // Normal half range (or overflow). Adding half a half-ulp before truncating the low 13 bits rounds the
                // magnitude half-up, i.e. ties away from zero; a carry rolls into the exponent naturally.
                uint rounded = ((abs + 0x1000u) >> 13) - 0x1C000u; // rebias exponent: (127 - 15) << 10
                if (rounded > 0x7C00u)
                    rounded = 0x7C00u;
                return (ushort)(sign | rounded);
            }

            // Below 2^-14: half subnormal (units of 2^-24) or zero.
            uint exponent = abs >> 23;
            if (exponent == 0)
                return (ushort)sign; // float subnormals are far below half resolution

            uint mantissa = (abs & 0x7FFFFFu) | 0x800000u;
            int shift = 126 - (int)exponent; // value = mantissa * 2^(exponent-150); in 2^-24 units that is >> (126 - exponent)
            if (shift >= 32)
                return (ushort)sign;
            uint half = (mantissa + (1u << (shift - 1))) >> shift;
            return (ushort)(sign | half);
        }

        /// <summary>Standard IEEE half decode (spec §11: 0x7C00 → +∞, 0x7E00 → NaN 0x7FC00000, 0x0001 → 5.9604645e-8).</summary>
        public static float HalfToFloat(ushort val)
        {
            uint sign = ((uint)val & 0x8000u) << 16;
            uint exponent = ((uint)val >> 10) & 0x1Fu;
            uint mantissa = (uint)val & 0x3FFu;
            uint bits;

            if (exponent == 0x1Fu)
            {
                bits = sign | 0x7F800000u | (mantissa << 13);
            }
            else if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign;
                }
                else
                {
                    // Subnormal half: renormalise so the leading mantissa bit becomes the implicit one.
                    int e = 113; // 127 - 14
                    while ((mantissa & 0x400u) == 0)
                    {
                        mantissa <<= 1;
                        e--;
                    }
                    mantissa &= 0x3FFu;
                    bits = sign | ((uint)e << 23) | (mantissa << 13);
                }
            }
            else
            {
                bits = sign | ((exponent + 112u) << 23) | (mantissa << 13);
            }

            return BitConverter.Int32BitsToSingle((int)bits);
        }

        /// <summary>
        /// Black-body colour for a temperature in kelvin, clamped to [1000, 40000], as linear RGB normalised so the largest
        /// channel is 1, alpha 1 (spec §11). Red, blue and the green branch for k ≥ 6.57 use the spec's rational fits.
        /// </summary>
        public static Color CorrelatedColorTemperatureToRGB(float kelvin)
        {
            float k = Clamp(kelvin, 1000F, 40000F) / 1000F;
            float k2 = k * k;

            float r = k < 6.57F
                ? 1F
                : Clamp01((1.35651F + 0.216422F * k + 0.000633715F * k2) / (-3.24223F + 0.918711F * k));

            float g;
            if (k >= 6.57F)
            {
                g = Clamp01((1370.38F + 734.616F * k + 0.689955F * k2) / (-4625.69F + 1699.87F * k));
            }
            else
            {
                // unverified vs Unity: the green coefficients for k < 6.57 are not published. This (2,2) rational fit was
                // least-squares fitted to the spec's probe samples and reproduces every sample at 1000..6500 K to within
                // one float ulp (max |error| 1.4e-8 in double), but it is not Unity's own polynomial; there is a ~2e-3 step
                // against the k >= 6.57 branch, exactly as the probe samples suggest.
                g = Clamp01((-0.143855287747F + 0.149058287833F * k + 0.040135014055F * k2)
                    / (1F + 0.059062202099F * k + 0.030489060664F * k2));
            }

            float b = k > 6.57F
                ? 1F
                : Clamp01((348.963F - 523.53F * k + 183.62F * k2) / (2848.82F - 214.52F * k + 78.8614F * k2));

            return new Color(r, g, b, 1F);
        }

        // unverified vs Unity: lattice/hash differ; do not rely on equality — this member matches NONE of spec §11's
        // samples and no golden test may treat it as exact. Classic improved Perlin noise (Perlin 2002) over the
        // reference permutation, remapped from [-1, 1] to [0, 1]. Unity's native noise uses its own lattice hashing,
        // which spec §15 note 4 records as unrecoverable from the outside (only samples are published, and the clean-room
        // rule forbids reading Unity's source). Spec sample vs this implementation:
        //   (0,0) 0.46527308 / 0.5   (0.5,0.5) 0.2966959 / 0.25   (1.3,2.7) 0.57015276 / 0.8081539
        //   (10.25,3.75) 0.559137    (-1.5,2.25) 0.70068854       PerlinNoise1D(0.5) 0.46527308 / 0.375
        // NowUI does not call it; it exists only so the public surface is complete.
        public static float PerlinNoise(float x, float y)
        {
            float fx = Floor(x);
            float fy = Floor(y);
            int xi = (int)fx & 255;
            int yi = (int)fy & 255;
            x -= fx;
            y -= fy;

            float u = PerlinFade(x);
            float v = PerlinFade(y);

            int a = s_PerlinPerm[xi] + yi;
            int b = s_PerlinPerm[xi + 1] + yi;

            float x1 = PerlinLerp(u, PerlinGrad(s_PerlinPerm[a], x, y), PerlinGrad(s_PerlinPerm[b], x - 1, y));
            float x2 = PerlinLerp(u, PerlinGrad(s_PerlinPerm[a + 1], x, y - 1), PerlinGrad(s_PerlinPerm[b + 1], x - 1, y - 1));
            float n = PerlinLerp(v, x1, x2);
            return n * 0.5F + 0.5F;
        }

        /// <summary>Equivalent to PerlinNoise(x, 0) (spec §11: verified on Unity for x = 1.3).</summary>
        public static float PerlinNoise1D(float x)
        {
            return PerlinNoise(x, 0F);
        }

        private static float PerlinFade(float t)
        {
            return t * t * t * (t * (t * 6F - 15F) + 10F);
        }

        private static float PerlinLerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }

        private static float PerlinGrad(int hash, float x, float y)
        {
            // 8 gradient directions: (±1,0), (0,±1), (±1,±1).
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        // Ken Perlin's reference permutation (public domain), duplicated so index + 1 never wraps.
        private static readonly int[] s_PerlinPerm = BuildPerlinPermutation();

        private static int[] BuildPerlinPermutation()
        {
            int[] p =
            {
                151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23,
                190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20,
                125, 136, 171, 168, 68, 175, 74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211, 133, 230, 220,
                105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169, 200, 196,
                135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255,
                82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154, 163, 70, 221,
                153, 101, 155, 167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104, 218, 246, 97, 228,
                251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106,
                157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78,
                66, 215, 61, 156, 180
            };
            int[] perm = new int[512];
            for (int i = 0; i < 512; i++)
                perm[i] = p[i & 255];
            return perm;
        }
    }
}
