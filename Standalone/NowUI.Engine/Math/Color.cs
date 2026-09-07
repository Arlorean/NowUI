// Mirrors UnityEngine.Color for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§4).

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// RGBA colour with float channels. Every formula, tolerance and format below follows spec §4; all arithmetic
    /// is single precision with intermediates kept as <see cref="float"/>.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Color : IEquatable<Color>, IFormattable
    {
        // Field order is observable: arrays of Color are reinterpreted as float* by native/backend code.
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        /// <summary>Spec §4: the three-argument constructor sets alpha to 1.</summary>
        public Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = 1F;
        }

        // ------------------------------------------------------------------------------------------------------
        // Indexer (spec §4: message embeds the offending index, e.g. "Invalid Color index(4)!")
        // ------------------------------------------------------------------------------------------------------

        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: throw new IndexOutOfRangeException("Invalid Color index(" + index + ")!");
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
                    default: throw new IndexOutOfRangeException("Invalid Color index(" + index + ")!");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------
        // ToString family (spec §0 / §4: template "RGBA({0}, {1}, {2}, {3})", default format "F3")
        // ------------------------------------------------------------------------------------------------------

        public override readonly string ToString() => ToString(null, null);

        public readonly string ToString(string format) => ToString(format, null);

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F3";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return "RGBA(" + r.ToString(format, formatProvider) + ", " + g.ToString(format, formatProvider) + ", "
                + b.ToString(format, formatProvider) + ", " + a.ToString(format, formatProvider) + ")";
        }

        // ------------------------------------------------------------------------------------------------------
        // Equality (spec §4: Equals uses float.Equals so NaN equals NaN; == uses the Vector4 squared tolerance)
        // ------------------------------------------------------------------------------------------------------

        public override readonly int GetHashCode()
            => r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1);

        public override readonly bool Equals(object other) => other is Color c && Equals(c);

        public readonly bool Equals(Color other)
            => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);

        public static bool operator ==(Color lhs, Color rhs)
        {
            // Spec §4: d = lhs - rhs per channel; sqrmag < Vector4.kEpsilon * Vector4.kEpsilon. False with NaN.
            float dr = lhs.r - rhs.r;
            float dg = lhs.g - rhs.g;
            float db = lhs.b - rhs.b;
            float da = lhs.a - rhs.a;
            float sqrmag = dr * dr + dg * dg + db * db + da * da;
            return sqrmag < Vector4.kEpsilon * Vector4.kEpsilon;
        }

        public static bool operator !=(Color lhs, Color rhs) => !(lhs == rhs);

        // ------------------------------------------------------------------------------------------------------
        // Derived values (spec §4)
        // ------------------------------------------------------------------------------------------------------

        /// <summary>Spec §4: 0.299 r + 0.587 g + 0.114 b.</summary>
        public readonly float grayscale => 0.299F * r + 0.587F * g + 0.114F * b;

        /// <summary>Spec §4: sRGB-decoded copy; alpha untouched.</summary>
        public readonly Color linear
            => new Color(Mathf.GammaToLinearSpace(r), Mathf.GammaToLinearSpace(g), Mathf.GammaToLinearSpace(b), a);

        /// <summary>Spec §4: sRGB-encoded copy; alpha untouched.</summary>
        public readonly Color gamma
            => new Color(Mathf.LinearToGammaSpace(r), Mathf.LinearToGammaSpace(g), Mathf.LinearToGammaSpace(b), a);

        /// <summary>Spec §4: Max(Max(r, g), b); alpha excluded, negatives allowed.</summary>
        public readonly float maxColorComponent => Mathf.Max(Mathf.Max(r, g), b);

        // Internal in Unity as well (spec §4); visible to the test assemblies via InternalsVisibleTo.
        internal readonly Color RGBMultiplied(float multiplier) => new Color(r * multiplier, g * multiplier, b * multiplier, a);

        internal readonly Color RGBMultiplied(Color multiplier) => new Color(r * multiplier.r, g * multiplier.g, b * multiplier.b, a);

        internal readonly Color AlphaMultiplied(float multiplier) => new Color(r, g, b, a * multiplier);

        // ------------------------------------------------------------------------------------------------------
        // Interpolation (spec §4: a + (b - a) * t per channel, alpha included)
        // ------------------------------------------------------------------------------------------------------

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(
                a.r + (b.r - a.r) * t,
                a.g + (b.g - a.g) * t,
                a.b + (b.b - a.b) * t,
                a.a + (b.a - a.a) * t);
        }

        public static Color LerpUnclamped(Color a, Color b, float t)
        {
            return new Color(
                a.r + (b.r - a.r) * t,
                a.g + (b.g - a.g) * t,
                a.b + (b.b - a.b) * t,
                a.a + (b.a - a.a) * t);
        }

        // ------------------------------------------------------------------------------------------------------
        // HSV conversion (spec §4)
        // ------------------------------------------------------------------------------------------------------

        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            // Spec §4 step 1: pick the dominant channel; ties fall through to the red case.
            if (rgbColor.b > rgbColor.g && rgbColor.b > rgbColor.r)
                RGBToHSVHelper(4F, rgbColor.b, rgbColor.r, rgbColor.g, out H, out S, out V);
            else if (rgbColor.g > rgbColor.r)
                RGBToHSVHelper(2F, rgbColor.g, rgbColor.b, rgbColor.r, out H, out S, out V);
            else
                RGBToHSVHelper(0F, rgbColor.r, rgbColor.g, rgbColor.b, out H, out S, out V);
        }

        private static void RGBToHSVHelper(float offset, float dominant, float one, float two, out float H, out float S, out float V)
        {
            // Spec §4 step 2. V is never clamped (HDR input yields V > 1); greys give H = S = 0; H ends in [0, 1).
            V = dominant;
            if (V != 0F)
            {
                float small = (one > two) ? two : one;
                float diff = V - small;
                if (diff != 0F)
                {
                    S = diff / V;
                    H = offset + (one - two) / diff;
                }
                else
                {
                    S = 0F;
                    H = offset + (one - two);
                }
                H /= 6F;
                if (H < 0F)
                    H += 1F;
            }
            else
            {
                S = 0F;
                H = 0F;
            }
        }

        public static Color HSVToRGB(float H, float S, float V) => HSVToRGB(H, S, V, true);

        public static Color HSVToRGB(float H, float S, float V, bool hdr)
        {
            // Spec §4: start from white (alpha 1). Sectors outside [-1, 6] leave rgb at (0,0,0) — hue is not wrapped.
            Color result = white;
            if (S == 0F)
            {
                result.r = V;
                result.g = V;
                result.b = V;
            }
            else if (V == 0F)
            {
                result.r = 0F;
                result.g = 0F;
                result.b = 0F;
            }
            else
            {
                result.r = 0F;
                result.g = 0F;
                result.b = 0F;

                float h6 = H * 6F;
                int sector = Mathf.FloorToInt(h6);
                float t = h6 - (float)sector;
                float p = V * (1F - S);
                float q = V * (1F - S * t);
                float u = V * (1F - S * (1F - t));

                switch (sector)
                {
                    case 0:
                        result.r = V; result.g = u; result.b = p;
                        break;
                    case 1:
                        result.r = q; result.g = V; result.b = p;
                        break;
                    case 2:
                        result.r = p; result.g = V; result.b = u;
                        break;
                    case 3:
                        result.r = p; result.g = q; result.b = V;
                        break;
                    case 4:
                        result.r = u; result.g = p; result.b = V;
                        break;
                    case 5:
                        result.r = V; result.g = p; result.b = q;
                        break;
                    case 6:
                        result.r = V; result.g = u; result.b = p;
                        break;
                    case -1:
                        result.r = V; result.g = p; result.b = q;
                        break;
                    // Any other sector: rgb stays (0,0,0) [verified: HSVToRGB(1.2,1,2,true) = (0,0,0,1)].
                }
            }

            // Spec §4 states the !hdr clamp after, and outside, the sector table, i.e. as a final unconditional step,
            // so it also applies to the achromatic branches: HSVToRGB(0, 0, 2, hdr: false) = white, not (2,2,2,1).
            // unverified vs Unity: no [verified] oracle covers !hdr with S == 0 or V == 0 (every probed !hdr sample is
            // already in range), and Unity's own source may scope the clamp to the chromatic branch.
            if (!hdr)
            {
                result.r = Mathf.Clamp01(result.r);
                result.g = Mathf.Clamp01(result.g);
                result.b = Mathf.Clamp01(result.b);
            }
            return result;
        }

        // ------------------------------------------------------------------------------------------------------
        // Arithmetic operators (spec §4: componentwise on all four channels, alpha included)
        // ------------------------------------------------------------------------------------------------------

        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);

        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);

        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);

        public static Color operator *(Color a, Vector4 b) => new Color(a.r * b.x, a.g * b.y, a.b * b.z, a.a * b.w);

        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);

        public static Color operator *(float b, Color a) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);

        public static Color operator /(Color a, float b) => new Color(a.r / b, a.g / b, a.b / b, a.a / b);

        // ------------------------------------------------------------------------------------------------------
        // Conversions (spec §4: Vector4 twins declared here; Color32 conversions live on Color32)
        // ------------------------------------------------------------------------------------------------------

        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);

        public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);

        // ------------------------------------------------------------------------------------------------------
        // Presets (spec §4; constructed per call like Unity — behaviourally indistinguishable from a cached instance)
        // ------------------------------------------------------------------------------------------------------

        public static Color red => new Color(1F, 0F, 0F, 1F);
        public static Color green => new Color(0F, 1F, 0F, 1F);
        public static Color blue => new Color(0F, 0F, 1F, 1F);
        public static Color white => new Color(1F, 1F, 1F, 1F);
        public static Color black => new Color(0F, 0F, 0F, 1F);
        /// <summary>Spec §4: (1, 235/255, 4/255) = (1, 0.92156863, 0.015686275), not the docs' (1, 0.92, 0.016).</summary>
        public static Color yellow => new Color(1F, 235F / 255F, 4F / 255F, 1F);
        public static Color yellowNice => yellow;
        public static Color cyan => new Color(0F, 1F, 1F, 1F);
        public static Color magenta => new Color(1F, 0F, 1F, 1F);
        public static Color gray => new Color(0.5F, 0.5F, 0.5F, 1F);
        public static Color grey => new Color(0.5F, 0.5F, 0.5F, 1F);
        public static Color clear => new Color(0F, 0F, 0F, 0F);

        public static Color gray1 => new Color(0.1F, 0.1F, 0.1F, 1F);
        public static Color gray2 => new Color(0.2F, 0.2F, 0.2F, 1F);
        public static Color gray3 => new Color(0.3F, 0.3F, 0.3F, 1F);
        public static Color gray4 => new Color(0.4F, 0.4F, 0.4F, 1F);
        public static Color gray5 => new Color(0.5F, 0.5F, 0.5F, 1F);
        public static Color gray6 => new Color(0.6F, 0.6F, 0.6F, 1F);
        public static Color gray7 => new Color(0.7F, 0.7F, 0.7F, 1F);
        public static Color gray8 => new Color(0.8F, 0.8F, 0.8F, 1F);
        public static Color gray9 => new Color(0.9F, 0.9F, 0.9F, 1F);

        // Unity 6 also exposes ~140 named CSS/WPF-style presets (aliceBlue, cornflowerBlue, ...), each byte/255f.
        // The spec does not enumerate them and NowUI references none, so they are intentionally omitted.
    }
}
