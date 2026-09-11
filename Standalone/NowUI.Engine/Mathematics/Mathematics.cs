// Mirrors Unity.Mathematics: float2, float3, float4 and the `math` function set NowUI uses.
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9 (which enumerates the members) and section 12.6
// (float policy: pure single precision, keeping the spec's operation order). The members NowUI actually calls are
// Docs/Standalone/UnityDependencyInventory.md section A row "Unity.Mathematics.*":
// float4(float,float,float,float) with .x/.y/.z/.w, float2(float,float) with .x/.y, float2 subtraction,
// float2 * float, and math.dot / math.saturate / math.min / math.sqrt.
//
// WHY the operation order is written out longhand rather than delegated to MathF or Vector helpers: NowSdfBakeJob
// bakes glyph SDFs and its output must be bit-identical to a Unity run with Burst disabled. Every expression below
// reproduces Unity.Mathematics' own expression, including its NaN handling, which differs from System.MathF:
// math.min/max ignore a NaN second operand instead of propagating it, and that is what makes math.saturate(NaN)
// return 1 rather than NaN.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Unity.Mathematics
{
    /// <summary>Mirrors <c>Unity.Mathematics.float2</c>.</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct float2 : IEquatable<float2>
    {
        public float x;
        public float y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        /// <summary>Splats one scalar across both components.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2(float v)
        {
            x = v;
            y = v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2(float2 v)
        {
            x = v.x;
            y = v.y;
        }

        /// <summary>Splat conversion, matching Unity's implicit <c>float</c> to <c>float2</c>.</summary>
        public static implicit operator float2(float v) => new float2(v);

        /// <summary>Mirrors Unity.Mathematics' implicit conversion from <c>UnityEngine.Vector2</c>.</summary>
        public static implicit operator float2(UnityEngine.Vector2 v) => new float2(v.x, v.y);

        /// <summary>Mirrors Unity.Mathematics' conversion to <c>UnityEngine.Vector2</c>.</summary>
        public static explicit operator UnityEngine.Vector2(float2 v) => new UnityEngine.Vector2(v.x, v.y);

        public static float2 operator +(float2 lhs, float2 rhs) => new float2(lhs.x + rhs.x, lhs.y + rhs.y);

        public static float2 operator +(float2 lhs, float rhs) => new float2(lhs.x + rhs, lhs.y + rhs);

        public static float2 operator +(float lhs, float2 rhs) => new float2(lhs + rhs.x, lhs + rhs.y);

        public static float2 operator -(float2 lhs, float2 rhs) => new float2(lhs.x - rhs.x, lhs.y - rhs.y);

        public static float2 operator -(float2 lhs, float rhs) => new float2(lhs.x - rhs, lhs.y - rhs);

        public static float2 operator -(float lhs, float2 rhs) => new float2(lhs - rhs.x, lhs - rhs.y);

        public static float2 operator *(float2 lhs, float2 rhs) => new float2(lhs.x * rhs.x, lhs.y * rhs.y);

        public static float2 operator *(float2 lhs, float rhs) => new float2(lhs.x * rhs, lhs.y * rhs);

        public static float2 operator *(float lhs, float2 rhs) => new float2(lhs * rhs.x, lhs * rhs.y);

        public static float2 operator /(float2 lhs, float2 rhs) => new float2(lhs.x / rhs.x, lhs.y / rhs.y);

        public static float2 operator /(float2 lhs, float rhs) => new float2(lhs.x / rhs, lhs.y / rhs);

        public static float2 operator /(float lhs, float2 rhs) => new float2(lhs / rhs.x, lhs / rhs.y);

        public static float2 operator -(float2 v) => new float2(-v.x, -v.y);

        public static float2 operator +(float2 v) => v;

        // WHY there is no operator == / != : in Unity.Mathematics those return bool2 (a per-component mask), not
        // bool. Shipping a bool-returning pair would compile the same call sites to different meanings, which is
        // exactly the silent divergence this shim exists to avoid. bool2 is not in NowUI's dependency inventory,
        // so neither ships. Equals() below is the ordinary all-components comparison.

        public bool Equals(float2 rhs) => x == rhs.x && y == rhs.y;

        public override bool Equals(object o) => o is float2 rhs && Equals(rhs);

        public override int GetHashCode() => HashCode.Combine(x, y);

        public override string ToString() => $"float2({x}f, {y}f)";
    }

    /// <summary>
    /// Mirrors <c>Unity.Mathematics.float3</c>. Present because <c>float4</c>'s <c>(float3, float)</c> constructor
    /// is part of the surface design section 3.9 specifies.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct float3 : IEquatable<float3>
    {
        public float x;
        public float y;
        public float z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3(float v)
        {
            x = v;
            y = v;
            z = v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3(float2 xy, float z)
        {
            x = xy.x;
            y = xy.y;
            this.z = z;
        }

        public float2 xy => new float2(x, y);

        public static implicit operator float3(float v) => new float3(v);

        public static implicit operator float3(UnityEngine.Vector3 v) => new float3(v.x, v.y, v.z);

        public static explicit operator UnityEngine.Vector3(float3 v) => new UnityEngine.Vector3(v.x, v.y, v.z);

        public static float3 operator +(float3 lhs, float3 rhs) => new float3(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z);

        public static float3 operator -(float3 lhs, float3 rhs) => new float3(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z);

        public static float3 operator *(float3 lhs, float3 rhs) => new float3(lhs.x * rhs.x, lhs.y * rhs.y, lhs.z * rhs.z);

        public static float3 operator *(float3 lhs, float rhs) => new float3(lhs.x * rhs, lhs.y * rhs, lhs.z * rhs);

        public static float3 operator *(float lhs, float3 rhs) => new float3(lhs * rhs.x, lhs * rhs.y, lhs * rhs.z);

        public static float3 operator /(float3 lhs, float3 rhs) => new float3(lhs.x / rhs.x, lhs.y / rhs.y, lhs.z / rhs.z);

        public static float3 operator /(float3 lhs, float rhs) => new float3(lhs.x / rhs, lhs.y / rhs, lhs.z / rhs);

        public static float3 operator -(float3 v) => new float3(-v.x, -v.y, -v.z);

        public bool Equals(float3 rhs) => x == rhs.x && y == rhs.y && z == rhs.z;

        public override bool Equals(object o) => o is float3 rhs && Equals(rhs);

        public override int GetHashCode() => HashCode.Combine(x, y, z);

        public override string ToString() => $"float3({x}f, {y}f, {z}f)";
    }

    /// <summary>Mirrors <c>Unity.Mathematics.float4</c>.</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct float4 : IEquatable<float4>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        /// <summary>Splats one scalar across all four components.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float v)
        {
            x = v;
            y = v;
            z = v;
            w = v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float3 xyz, float w)
        {
            x = xyz.x;
            y = xyz.y;
            z = xyz.z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float2 xy, float z, float w)
        {
            x = xy.x;
            y = xy.y;
            this.z = z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float2 xy, float2 zw)
        {
            x = xy.x;
            y = xy.y;
            z = zw.x;
            w = zw.y;
        }

        public float2 xy => new float2(x, y);

        public float2 zw => new float2(z, w);

        public float3 xyz => new float3(x, y, z);

        public static implicit operator float4(float v) => new float4(v);

        /// <summary>Mirrors Unity.Mathematics' implicit conversion from <c>UnityEngine.Vector4</c>.</summary>
        public static implicit operator float4(UnityEngine.Vector4 v) => new float4(v.x, v.y, v.z, v.w);

        /// <summary>Mirrors Unity.Mathematics' conversion to <c>UnityEngine.Vector4</c>.</summary>
        public static explicit operator UnityEngine.Vector4(float4 v) => new UnityEngine.Vector4(v.x, v.y, v.z, v.w);

        public static float4 operator +(float4 lhs, float4 rhs) => new float4(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z, lhs.w + rhs.w);

        public static float4 operator +(float4 lhs, float rhs) => new float4(lhs.x + rhs, lhs.y + rhs, lhs.z + rhs, lhs.w + rhs);

        public static float4 operator +(float lhs, float4 rhs) => new float4(lhs + rhs.x, lhs + rhs.y, lhs + rhs.z, lhs + rhs.w);

        public static float4 operator -(float4 lhs, float4 rhs) => new float4(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z, lhs.w - rhs.w);

        public static float4 operator -(float4 lhs, float rhs) => new float4(lhs.x - rhs, lhs.y - rhs, lhs.z - rhs, lhs.w - rhs);

        public static float4 operator -(float lhs, float4 rhs) => new float4(lhs - rhs.x, lhs - rhs.y, lhs - rhs.z, lhs - rhs.w);

        public static float4 operator *(float4 lhs, float4 rhs) => new float4(lhs.x * rhs.x, lhs.y * rhs.y, lhs.z * rhs.z, lhs.w * rhs.w);

        public static float4 operator *(float4 lhs, float rhs) => new float4(lhs.x * rhs, lhs.y * rhs, lhs.z * rhs, lhs.w * rhs);

        public static float4 operator *(float lhs, float4 rhs) => new float4(lhs * rhs.x, lhs * rhs.y, lhs * rhs.z, lhs * rhs.w);

        public static float4 operator /(float4 lhs, float4 rhs) => new float4(lhs.x / rhs.x, lhs.y / rhs.y, lhs.z / rhs.z, lhs.w / rhs.w);

        public static float4 operator /(float4 lhs, float rhs) => new float4(lhs.x / rhs, lhs.y / rhs, lhs.z / rhs, lhs.w / rhs);

        public static float4 operator /(float lhs, float4 rhs) => new float4(lhs / rhs.x, lhs / rhs.y, lhs / rhs.z, lhs / rhs.w);

        public static float4 operator -(float4 v) => new float4(-v.x, -v.y, -v.z, -v.w);

        public static float4 operator +(float4 v) => v;

        // No operator == / != — see the note on float2 (Unity returns bool4, not bool).

        public bool Equals(float4 rhs) => x == rhs.x && y == rhs.y && z == rhs.z && w == rhs.w;

        public override bool Equals(object o) => o is float4 rhs && Equals(rhs);

        public override int GetHashCode() => HashCode.Combine(x, y, z, w);

        public override string ToString() => $"float4({x}f, {y}f, {z}f, {w}f)";
    }

    // CS8981: the type name must stay all-lower-case because NowUI's sources call `math.dot(...)` verbatim; the
    // shim's job is to match Unity's spelling exactly, not to pick a safer identifier.
#pragma warning disable CS8981
    /// <summary>
    /// Mirrors <c>Unity.Mathematics.math</c>, limited to the functions design section 3.9 lists.
    /// </summary>
    public static class math
    {
        /// <summary>Reinterprets a float's bits as an unsigned integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint asuint(float x) => BitConverter.SingleToUInt32Bits(x);

        /// <summary>Reinterprets an unsigned integer's bits as a float.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float asfloat(uint x) => BitConverter.UInt32BitsToSingle(x);

        /// <summary>
        /// The smaller of two values. WHY the NaN test comes first: Unity.Mathematics defines
        /// <c>min</c> as <c>float.IsNaN(y) || x &lt; y ? x : y</c> so that a NaN operand is ignored the way HLSL's
        /// <c>min</c> ignores it, instead of propagating as <c>MathF.Min</c> does.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float min(float x, float y) => float.IsNaN(y) || x < y ? x : y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 min(float2 x, float2 y) => new float2(min(x.x, y.x), min(x.y, y.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 min(float4 x, float4 y) => new float4(min(x.x, y.x), min(x.y, y.y), min(x.z, y.z), min(x.w, y.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int min(int x, int y) => x < y ? x : y;

        /// <summary>The larger of two values, with the same NaN-ignoring rule as <see cref="min(float,float)"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float max(float x, float y) => float.IsNaN(y) || x > y ? x : y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 max(float2 x, float2 y) => new float2(max(x.x, y.x), max(x.y, y.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 max(float4 x, float4 y) => new float4(max(x.x, y.x), max(x.y, y.y), max(x.z, y.z), max(x.w, y.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int max(int x, int y) => x > y ? x : y;

        /// <summary>
        /// Clamps to <c>[lowerBound, upperBound]</c>. The nesting order is Unity's —
        /// <c>max(lowerBound, min(upperBound, valueToClamp))</c> — which is what gives <c>saturate</c> its NaN
        /// result.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float clamp(float valueToClamp, float lowerBound, float upperBound) => max(lowerBound, min(upperBound, valueToClamp));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int clamp(int valueToClamp, int lowerBound, int upperBound) => max(lowerBound, min(upperBound, valueToClamp));

        /// <summary>
        /// Clamps to <c>[0, 1]</c>. NaN saturates to 1, not to NaN, because <c>min(1, NaN)</c> returns 1 under
        /// Unity's NaN rule — a Unity-faithful oddity NowSdfBakeJob's output depends on.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float saturate(float x) => clamp(x, 0.0f, 1.0f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 saturate(float2 x) => new float2(saturate(x.x), saturate(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 saturate(float4 x) => new float4(saturate(x.x), saturate(x.y), saturate(x.z), saturate(x.w));

        /// <summary>
        /// Absolute value via a sign-bit mask, matching Unity's <c>asfloat(asuint(x) &amp; 0x7FFFFFFF)</c>. This
        /// also clears the sign of a negative NaN, which <c>MathF.Abs</c> does too, but the bit form leaves no
        /// doubt about the payload.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float abs(float x) => asfloat(asuint(x) & 0x7FFFFFFFu);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 abs(float2 x) => new float2(abs(x.x), abs(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 abs(float4 x) => new float4(abs(x.x), abs(x.y), abs(x.z), abs(x.w));

        /// <summary>Square root. Single precision throughout, per design section 12.6.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sqrt(float x) => MathF.Sqrt(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 sqrt(float2 x) => new float2(sqrt(x.x), sqrt(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 sqrt(float4 x) => new float4(sqrt(x.x), sqrt(x.y), sqrt(x.z), sqrt(x.w));

        /// <summary>Reciprocal square root, written as Unity writes it: <c>1 / sqrt(x)</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float rsqrt(float x) => 1.0f / sqrt(x);

        /// <summary>Reciprocal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float rcp(float x) => 1.0f / x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 rcp(float2 x) => new float2(rcp(x.x), rcp(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 rcp(float4 x) => new float4(rcp(x.x), rcp(x.y), rcp(x.z), rcp(x.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float floor(float x) => MathF.Floor(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 floor(float2 x) => new float2(floor(x.x), floor(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 floor(float4 x) => new float4(floor(x.x), floor(x.y), floor(x.z), floor(x.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ceil(float x) => MathF.Ceiling(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 ceil(float2 x) => new float2(ceil(x.x), ceil(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 ceil(float4 x) => new float4(ceil(x.x), ceil(x.y), ceil(x.z), ceil(x.w));

        /// <summary>
        /// Rounds to the nearest integer, ties to even. WHY not <c>floor(x + 0.5f)</c>: Unity's <c>math.round</c>
        /// forwards to <c>System.Math.Round</c>, whose midpoint rule is to-even, so 0.5 rounds to 0 and 1.5 to 2.
        /// <c>UnityEngine.Mathf.Round</c> behaves the same way; this is not the C-style "round half away from
        /// zero" some callers expect.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float round(float x) => MathF.Round(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 round(float2 x) => new float2(round(x.x), round(x.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 round(float4 x) => new float4(round(x.x), round(x.y), round(x.z), round(x.w));

        /// <summary>
        /// -1, 0 or +1 written as Unity writes it. A NaN input yields 0 because neither comparison holds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sign(float x) => (x > 0.0f ? 1.0f : 0.0f) - (x < 0.0f ? 1.0f : 0.0f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 sign(float2 x) => new float2(sign(x.x), sign(x.y));

        /// <summary>Linear interpolation, in Unity's <c>start + t * (end - start)</c> form.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lerp(float start, float end, float t) => start + t * (end - start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 lerp(float2 start, float2 end, float t) => start + t * (end - start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 lerp(float4 start, float4 end, float t) => start + t * (end - start);

        /// <summary>Dot product, summed left to right so the rounding order matches Unity's.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(float2 x, float2 y) => x.x * y.x + x.y * y.y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(float3 x, float3 y) => x.x * y.x + x.y * y.y + x.z * y.z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(float4 x, float4 y) => x.x * y.x + x.y * y.y + x.z * y.z + x.w * y.w;

        /// <summary>Squared length; the dot product of a vector with itself.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lengthsq(float2 x) => dot(x, x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lengthsq(float4 x) => dot(x, x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float length(float2 x) => sqrt(dot(x, x));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float length(float4 x) => sqrt(dot(x, x));

        /// <summary>
        /// Unit-length vector, as <c>rsqrt(dot(x, x)) * x</c> — Unity's exact expression, which is why it differs
        /// by an ulp or two from <c>x / length(x)</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 normalize(float2 x) => rsqrt(dot(x, x)) * x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 normalize(float4 x) => rsqrt(dot(x, x)) * x;

        /// <summary>
        /// Branch-free choice. Note the argument order Unity uses: the value returned when
        /// <paramref name="test"/> is false comes first.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float select(float falseValue, float trueValue, bool test) => test ? trueValue : falseValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int select(int falseValue, int trueValue, bool test) => test ? trueValue : falseValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 select(float2 falseValue, float2 trueValue, bool test) => test ? trueValue : falseValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 select(float4 falseValue, float4 trueValue, bool test) => test ? trueValue : falseValue;
    }
#pragma warning restore CS8981
}
