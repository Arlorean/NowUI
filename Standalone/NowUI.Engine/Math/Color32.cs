// Mirrors UnityEngine.Color32 for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§5).

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// RGBA colour with byte channels packed into 4 bytes. Explicit layout: the four bytes overlap a private
    /// <c>int</c> so that <see cref="GetHashCode"/> / <see cref="Equals(Color32)"/> work on the packed value
    /// (spec §5; on little-endian targets rgba == r | g&lt;&lt;8 | b&lt;&lt;16 | a&lt;&lt;24).
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public partial struct Color32 : IEquatable<Color32>, IFormattable
    {
        [FieldOffset(0)] private int rgba;

        [FieldOffset(0)] public byte r;
        [FieldOffset(1)] public byte g;
        [FieldOffset(2)] public byte b;
        [FieldOffset(3)] public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            rgba = 0;
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        // ------------------------------------------------------------------------------------------------------
        // Indexer (spec §5: message embeds the offending index, e.g. "Invalid Color32 index(4)!")
        // ------------------------------------------------------------------------------------------------------

        public byte this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: throw new IndexOutOfRangeException("Invalid Color32 index(" + index + ")!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: r = value; break;
                    case 1: g = value; break;
                    case 2: b = value; break;
                    case 3: a = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Color32 index(" + index + ")!");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------
        // Conversions (spec §5)
        // ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Spec §5: each channel is <c>(byte)Mathf.Round(Mathf.Clamp01(ch) * 255f)</c>. Mathf.Round is to-even, so
        /// 127.5 → 128, 128.5 → 128, 0.5 → 0, 254.5 → 254. NaN is special-cased to 0 (C# leaves that cast unspecified).
        /// </summary>
        public static implicit operator Color32(Color c)
        {
            return new Color32(
                FloatChannelToByte(c.r),
                FloatChannelToByte(c.g),
                FloatChannelToByte(c.b),
                FloatChannelToByte(c.a));
        }

        /// <summary>Spec §5: each channel is <c>ch / 255f</c>.</summary>
        public static implicit operator Color(Color32 c) => new Color(c.r / 255F, c.g / 255F, c.b / 255F, c.a / 255F);

        private static byte FloatChannelToByte(float channel)
        {
            float rounded = Mathf.Round(Mathf.Clamp01(channel) * 255F);
            if (float.IsNaN(rounded))
                return 0;
            // rounded is within [0, 255] here, so the cast is exact.
            return (byte)rounded;
        }

        /// <summary>
        /// Unchecked float → byte cast with Unity/Mono x64 semantics for the verified range: the value is truncated
        /// toward zero to an int and then narrowed to 8 bits, so -50f → 206 and 127.99 → 127 (spec §5).
        /// </summary>
        private static byte TruncateToByte(float value)
        {
            // unverified vs Unity: results for |value| beyond the int range (or NaN/∞) depend on the runtime's
            // float→int conversion; only in-range wrapping (e.g. -50 → 206) is verified.
            return unchecked((byte)(int)value);
        }

        // ------------------------------------------------------------------------------------------------------
        // Interpolation (spec §5: float arithmetic, then a truncating byte cast — Lerp(0,255,0.5) = 127)
        // ------------------------------------------------------------------------------------------------------

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                TruncateToByte(a.r + (b.r - a.r) * t),
                TruncateToByte(a.g + (b.g - a.g) * t),
                TruncateToByte(a.b + (b.b - a.b) * t),
                TruncateToByte(a.a + (b.a - a.a) * t));
        }

        /// <summary>Spec §5: out-of-range results wrap through the unchecked byte cast (LerpUnclamped(100,0,1.5) → 206).</summary>
        public static Color32 LerpUnclamped(Color32 a, Color32 b, float t)
        {
            return new Color32(
                TruncateToByte(a.r + (b.r - a.r) * t),
                TruncateToByte(a.g + (b.g - a.g) * t),
                TruncateToByte(a.b + (b.b - a.b) * t),
                TruncateToByte(a.a + (b.a - a.a) * t));
        }

        // ------------------------------------------------------------------------------------------------------
        // Equality (spec §5: packed-int comparison; Unity defines NO ==/!= operators on Color32)
        // ------------------------------------------------------------------------------------------------------

        public override readonly int GetHashCode() => rgba.GetHashCode();

        public override readonly bool Equals(object other) => other is Color32 c && Equals(c);

        public readonly bool Equals(Color32 other) => rgba == other.rgba;

        // ------------------------------------------------------------------------------------------------------
        // ToString family (spec §5: template "RGBA({0}, {1}, {2}, {3})", no default format → bytes print as integers)
        // ------------------------------------------------------------------------------------------------------

        public override readonly string ToString() => ToString(null, null);

        public readonly string ToString(string format) => ToString(format, null);

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return "RGBA(" + r.ToString(format, formatProvider) + ", " + g.ToString(format, formatProvider) + ", "
                + b.ToString(format, formatProvider) + ", " + a.ToString(format, formatProvider) + ")";
        }
    }
}
