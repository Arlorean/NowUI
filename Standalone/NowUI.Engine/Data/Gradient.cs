// Mirrors UnityEngine.Gradient for the NowUI standalone build.
// Spec: Docs/Standalone/GradientCurveSemantics.md §2, §3 (GC §3); design: StandaloneCoreDesign.md §3.6 (`Gradient.cs`).
//
// Every rule here is [verified] against Unity 6000.4 in GC §3, and several of them look wrong until you read the spec:
//   * key times are quantised to 16-bit fixed point on write and the interpolation factor is computed in that
//     fixed-point domain (GC §3.1, §3.3) — the float round-trip form does NOT reproduce Unity's bits;
//   * a key array is applied only when 1..8 keys long; 0, null, 9 or 10 keys are silently ignored (GC §3.1);
//   * a single key is expanded to two keys at k = 0 and k = 65535, discarding its own time (GC §3.1);
//   * Evaluate(NaN) is (0,0,0,0) (GC §3.3);
//   * Fixed mode steps the alpha keys too (GC §3.5);
//   * Equals is a content comparison while GetHashCode is an identity hash (GC §3.7).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// A colour ramp with up to eight colour keys and eight alpha keys, evaluated over t in [0,1].
    /// Mirrors <c>UnityEngine.Gradient</c> (GC §3). Not sealed, matching Unity.
    /// </summary>
    public class Gradient : IEquatable<Gradient>
    {
        /// <summary>The maximum number of keys per array. Longer arrays are rejected outright, never truncated.</summary>
        internal const int MaxKeys = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct ColorEntry
        {
            public Color color;
            public ushort k;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AlphaEntry
        {
            public float alpha;
            public ushort k;
        }

        // Fixed-capacity backing stores: SetKeys never allocates, and Evaluate is allocation-free (design §1.2).
        private readonly ColorEntry[] _colorKeys = new ColorEntry[MaxKeys];
        private readonly AlphaEntry[] _alphaKeys = new AlphaEntry[MaxKeys];
        private int _colorKeyCount;
        private int _alphaKeyCount;

        // `mode` is stored in 8 bits: setting (GradientMode)(-1) reads back as 255 (GC §2).
        private byte _mode;

        // `colorSpace` is a full int: 7 and -1 round-trip (GC §3.1). Default is Uninitialized (-1).
        private int _colorSpace = (int)ColorSpace.Uninitialized;

        /// <summary>
        /// A default gradient: Blend mode, colour space Uninitialized, white at k = 0 and k = 65535, alpha 1 at both
        /// ends — so Evaluate is (1,1,1,1) everywhere (GC §3.2).
        /// </summary>
        public Gradient()
        {
            Color white = new Color(1f, 1f, 1f, 1f);
            _colorKeys[0].color = white;
            _colorKeys[0].k = 0;
            _colorKeys[1].color = white;
            _colorKeys[1].k = 65535;
            _colorKeyCount = 2;

            _alphaKeys[0].alpha = 1f;
            _alphaKeys[0].k = 0;
            _alphaKeys[1].alpha = 1f;
            _alphaKeys[1].k = 65535;
            _alphaKeyCount = 2;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Storage model (GC §3.1)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Quantises a key time to Unity's 16-bit fixed point: clamp to [0,1], scale, round half up.</summary>
        private static ushort Quantise(float time)
        {
            float t = Mathf.Clamp01(time);
            // Round half up, not to-even: a key at 0.3 must take 19660.5 to 19661 for the Fixed-mode probe to hold.
            // NaN cannot survive Clamp01, so guard it here rather than relying on an undefined float->ushort cast.
            if (float.IsNaN(t))
                return 0;
            return (ushort)(t * 65535f + 0.5f);
        }

        /// <summary>The time a stored key reads back as: the fixed-point value divided by 65535 (GC §3.1).</summary>
        private static float Dequantise(ushort k) => k / 65535f;

        public int colorKeyCount => _colorKeyCount;

        public int alphaKeyCount => _alphaKeyCount;

        /// <summary>
        /// The colour keys. The getter allocates a <b>fresh array on every access</b> — NowUI sorts the returned array
        /// in place, so sharing one would corrupt the gradient (GC §3.1). The setter follows the silent validation
        /// rules: 1..8 keys are applied, anything else is ignored.
        /// </summary>
        public GradientColorKey[] colorKeys
        {
            get
            {
                GradientColorKey[] result = new GradientColorKey[_colorKeyCount];
                for (int i = 0; i < _colorKeyCount; i++)
                {
                    result[i].color = _colorKeys[i].color;
                    result[i].time = Dequantise(_colorKeys[i].k);
                }
                return result;
            }
            set => SetColorKeys(AsSpanOrEmpty(value));
        }

        /// <summary>The alpha keys; same fresh-array getter and same silent validation as <see cref="colorKeys"/>.</summary>
        public GradientAlphaKey[] alphaKeys
        {
            get
            {
                GradientAlphaKey[] result = new GradientAlphaKey[_alphaKeyCount];
                for (int i = 0; i < _alphaKeyCount; i++)
                {
                    result[i].alpha = _alphaKeys[i].alpha;
                    result[i].time = Dequantise(_alphaKeys[i].k);
                }
                return result;
            }
            set => SetAlphaKeys(AsSpanOrEmpty(value));
        }

        /// <summary>
        /// Copies the colour keys into <paramref name="keys"/>.
        /// Throws when the destination is too small; the message and parameter name match Unity (GC §3).
        /// </summary>
        public void GetColorKeys(Span<GradientColorKey> keys)
        {
            if (keys.Length < _colorKeyCount)
                throw new ArgumentException("Destination array must be large enough to store the keys", "keys");
            for (int i = 0; i < _colorKeyCount; i++)
            {
                keys[i].color = _colorKeys[i].color;
                keys[i].time = Dequantise(_colorKeys[i].k);
            }
        }

        /// <summary>Copies the alpha keys into <paramref name="keys"/>; same argument check as <see cref="GetColorKeys"/>.</summary>
        public void GetAlphaKeys(Span<GradientAlphaKey> keys)
        {
            if (keys.Length < _alphaKeyCount)
                throw new ArgumentException("Destination array must be large enough to store the keys", "keys");
            for (int i = 0; i < _alphaKeyCount; i++)
            {
                keys[i].alpha = _alphaKeys[i].alpha;
                keys[i].time = Dequantise(_alphaKeys[i].k);
            }
        }

        /// <summary>Sets the colour keys, silently ignoring an array of 0 or more than 8 keys (GC §3.1).</summary>
        public void SetColorKeys(ReadOnlySpan<GradientColorKey> keys)
        {
            int n = keys.Length;
            if (n < 1 || n > MaxKeys)
                return;

            if (n == 1)
            {
                // A single key becomes two, at both ends — its own time is discarded (GC §3.1).
                _colorKeys[0].color = keys[0].color;
                _colorKeys[0].k = 0;
                _colorKeys[1].color = keys[0].color;
                _colorKeys[1].k = 65535;
                _colorKeyCount = 2;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                _colorKeys[i].color = keys[i].color;
                _colorKeys[i].k = Quantise(keys[i].time);
            }
            _colorKeyCount = n;
            StableSortColors(n);
        }

        /// <summary>Sets the alpha keys, silently ignoring an array of 0 or more than 8 keys (GC §3.1).</summary>
        public void SetAlphaKeys(ReadOnlySpan<GradientAlphaKey> keys)
        {
            int n = keys.Length;
            if (n < 1 || n > MaxKeys)
                return;

            if (n == 1)
            {
                _alphaKeys[0].alpha = keys[0].alpha;
                _alphaKeys[0].k = 0;
                _alphaKeys[1].alpha = keys[0].alpha;
                _alphaKeys[1].k = 65535;
                _alphaKeyCount = 2;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                _alphaKeys[i].alpha = keys[i].alpha;
                _alphaKeys[i].k = Quantise(keys[i].time);
            }
            _alphaKeyCount = n;
            StableSortAlphas(n);
        }

        /// <summary>
        /// Sets both key arrays. Each array is validated independently and silently: an invalid one leaves the
        /// previous keys of that kind in place while the other array is still applied (GC §3.1).
        /// </summary>
        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
            => SetKeys(AsSpanOrEmpty(colorKeys), AsSpanOrEmpty(alphaKeys));

        /// <summary>Sets both key arrays; see <see cref="SetKeys(GradientColorKey[], GradientAlphaKey[])"/>.</summary>
        public void SetKeys(ReadOnlySpan<GradientColorKey> colorKeys, ReadOnlySpan<GradientAlphaKey> alphaKeys)
        {
            SetColorKeys(colorKeys);
            SetAlphaKeys(alphaKeys);
        }

        // Insertion sort: stable (keys at the same time keep the order they were given, GC §3.1) and allocation-free.
        private void StableSortColors(int n)
        {
            for (int i = 1; i < n; i++)
            {
                ColorEntry key = _colorKeys[i];
                int j = i - 1;
                while (j >= 0 && _colorKeys[j].k > key.k)
                {
                    _colorKeys[j + 1] = _colorKeys[j];
                    j--;
                }
                _colorKeys[j + 1] = key;
            }
        }

        private void StableSortAlphas(int n)
        {
            for (int i = 1; i < n; i++)
            {
                AlphaEntry key = _alphaKeys[i];
                int j = i - 1;
                while (j >= 0 && _alphaKeys[j].k > key.k)
                {
                    _alphaKeys[j + 1] = _alphaKeys[j];
                    j--;
                }
                _alphaKeys[j + 1] = key;
            }
        }

        /// <summary>The blend mode. Stored in 8 bits, so <c>(GradientMode)(-1)</c> reads back as 255 (GC §2).</summary>
        public GradientMode mode
        {
            get => (GradientMode)_mode;
            set => _mode = unchecked((byte)(int)value);
        }

        /// <summary>
        /// The colour space the key colours are interpreted in. A full int (7 and -1 round-trip); default
        /// Uninitialized (-1). Only PerceptualBlend reads it (GC §3.4 verified Blend is colour-space independent).
        /// </summary>
        public ColorSpace colorSpace
        {
            get => (ColorSpace)_colorSpace;
            set => _colorSpace = (int)value;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Evaluate (GC §3.3 - §3.6)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Evaluates the gradient. <c>Evaluate(NaN)</c> is <c>(0,0,0,0)</c> (GC §3.3).</summary>
        public Color Evaluate(float time)
        {
            if (float.IsNaN(time))
                return new Color(0f, 0f, 0f, 0f);

            // `time` itself is not quantised; only the keys are. tq lives in the same fixed-point domain as the keys.
            float tq = time * 65535f;

            byte m = _mode;
            bool fixedMode = m == (byte)GradientMode.Fixed;
            bool perceptual = m == (byte)GradientMode.PerceptualBlend;

            // Colour segment.
            int ci = FindColorSegment(tq, out float ctq, out float cu);
            Color color;
            if (fixedMode)
            {
                color = ctq <= _colorKeys[ci].k ? _colorKeys[ci].color : _colorKeys[ci + 1].color;
            }
            else
            {
                Color a = _colorKeys[ci].color;
                Color b = _colorKeys[ci + 1].color;
                color = perceptual ? PerceptualMix(a, b, cu) : new Color(
                    a.r + (b.r - a.r) * cu,
                    a.g + (b.g - a.g) * cu,
                    a.b + (b.b - a.b) * cu);
            }

            // Alpha segment: searched independently, and Fixed steps it too (GC §3.5). PerceptualBlend still lerps.
            int ai = FindAlphaSegment(tq, out float atq, out float au);
            float alpha;
            if (fixedMode)
            {
                alpha = atq <= _alphaKeys[ai].k ? _alphaKeys[ai].alpha : _alphaKeys[ai + 1].alpha;
            }
            else
            {
                float a0 = _alphaKeys[ai].alpha;
                float a1 = _alphaKeys[ai + 1].alpha;
                alpha = a0 + (a1 - a0) * au;
            }

            return new Color(color.r, color.g, color.b, alpha);
        }

        // The common search of GC §3.3, in the fixed-point domain. Returns the segment's left index; `clamped` is tq
        // clamped to the key range (Fixed mode compares against it) and `u` the interpolation factor.
        private int FindColorSegment(float tq, out float clamped, out float u)
        {
            int n = _colorKeyCount;
            float first = _colorKeys[0].k;
            float last = _colorKeys[n - 1].k;
            clamped = Mathf.Clamp(tq, first, last);

            // Largest index with key[i].k <= clamped; with duplicate times that is the LAST duplicate (GC §3.3).
            int i = n - 1;
            while (i > 0 && _colorKeys[i].k > clamped)
                i--;
            if (i > n - 2)
                i = n - 2;

            // Integer subtraction first, then a single conversion: this exact form is what reproduced all 34 probe
            // samples; computing u from the k/65535f floats does not (GC §3.3, §3.4).
            float denom = (float)(_colorKeys[i + 1].k - _colorKeys[i].k);
            u = denom > 0f ? (clamped - _colorKeys[i].k) / denom : 0f;
            return i;
        }

        private int FindAlphaSegment(float tq, out float clamped, out float u)
        {
            int n = _alphaKeyCount;
            float first = _alphaKeys[0].k;
            float last = _alphaKeys[n - 1].k;
            clamped = Mathf.Clamp(tq, first, last);

            int i = n - 1;
            while (i > 0 && _alphaKeys[i].k > clamped)
                i--;
            if (i > n - 2)
                i = n - 2;

            float denom = (float)(_alphaKeys[i + 1].k - _alphaKeys[i].k);
            u = denom > 0f ? (clamped - _alphaKeys[i].k) / denom : 0f;
            return i;
        }

        // ---------------------------------------------------------------------------------------------------------
        // PerceptualBlend: Oklab (GC §3.6)
        // ---------------------------------------------------------------------------------------------------------
        //
        // The spec's reference implementation is double precision Oklab with Ottosson's 2021 constants, and it
        // reproduces every probed byte except the red channel of one sample (~2-5e-4 relative, up to +-1 byte); the
        // cause is unidentified and the deviation is accepted by design §3.6 rule 9. Double is used deliberately here
        // rather than the single-precision policy of design §12.6: the fixture is a byte table, the reference that
        // produced it is double, and single precision would add error on top of an already-unexplained deviation.

        private Color PerceptualMix(Color a, Color b, float u)
        {
            bool linearSpace = _colorSpace == (int)ColorSpace.Linear;

            double ar = a.r, ag = a.g, ab = a.b;
            double br = b.r, bg = b.g, bb = b.b;
            if (!linearSpace)
            {
                ar = SrgbToLinear(ar); ag = SrgbToLinear(ag); ab = SrgbToLinear(ab);
                br = SrgbToLinear(br); bg = SrgbToLinear(bg); bb = SrgbToLinear(bb);
            }

            LinearToOklab(ar, ag, ab, out double l0, out double a0, out double b0);
            LinearToOklab(br, bg, bb, out double l1, out double a1, out double b1);

            double t = u;
            double L = l0 + (l1 - l0) * t;
            double A = a0 + (a1 - a0) * t;
            double B = b0 + (b1 - b0) * t;

            OklabToLinear(L, A, B, out double r, out double g, out double bl);

            if (!linearSpace)
            {
                r = LinearToSrgb(r);
                g = LinearToSrgb(g);
                bl = LinearToSrgb(bl);
                // In-gamut results come back quantised to n/255; out-of-range results are returned unquantised.
                r = QuantiseInGamut(r);
                g = QuantiseInGamut(g);
                bl = QuantiseInGamut(bl);
            }

            return new Color((float)r, (float)g, (float)bl);
        }

        // sRGB -> linear. Components above 1 use c^2.2 instead of the exact curve: that is what reproduces the probe's
        // HDR sample ((2,0,0) -> black at 0.5 giving 200/255, where the exact curve gives 206/255) (GC §3.6 step 1).
        private static double SrgbToLinear(double c)
        {
            if (c > 1.0)
                return Math.Pow(c, 2.2);
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        // linear -> sRGB, with the same above-1 exception (c^(1/2.2)); reproduces white->blue's 1.0372324 blue channel.
        private static double LinearToSrgb(double c)
        {
            if (c > 1.0)
                return Math.Pow(c, 1.0 / 2.2);
            return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        }

        // Every in-gamut probe result was exactly n/255, so the native path evidently rounds through a byte. Half-up
        // rounding matches Unity's own Color -> Color32 conversion; the exact tie rule was not probed.
        private static double QuantiseInGamut(double c)
        {
            if (c < 0.0 || c > 1.0)
                return c;
            return Math.Floor(c * 255.0 + 0.5) / 255.0;
        }

        private static void LinearToOklab(double r, double g, double b, out double L, out double A, out double B)
        {
            double l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
            double m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
            double s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

            // Cbrt, not Pow(x, 1/3): the cube root must be defined for negative components.
            double l_ = Math.Cbrt(l);
            double m_ = Math.Cbrt(m);
            double s_ = Math.Cbrt(s);

            L = 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_;
            A = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_;
            B = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_;
        }

        private static void OklabToLinear(double L, double A, double B, out double r, out double g, out double b)
        {
            double l_ = L + 0.3963377774 * A + 0.2158037573 * B;
            double m_ = L - 0.1055613458 * A - 0.0638541728 * B;
            double s_ = L - 0.0894841775 * A - 1.2914855480 * B;

            double l = l_ * l_ * l_;
            double m = m_ * m_ * m_;
            double s = s_ * s_ * s_;

            r = 4.0767416621 * l - 3.3077115913 * m + 0.2307590544 * s;
            g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Equality (GC §3.7)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Content equality: colour keys (colour and quantised time), alpha keys, <see cref="mode"/> and
        /// <see cref="colorSpace"/> must all match. Note the deliberate asymmetry with
        /// <see cref="GetHashCode"/> (GC §3.7).
        /// </summary>
        public bool Equals(Gradient other)
        {
            if (ReferenceEquals(other, null))
                return false;
            if (ReferenceEquals(other, this))
                return true;
            if (_mode != other._mode || _colorSpace != other._colorSpace)
                return false;
            if (_colorKeyCount != other._colorKeyCount || _alphaKeyCount != other._alphaKeyCount)
                return false;

            for (int i = 0; i < _colorKeyCount; i++)
            {
                if (_colorKeys[i].k != other._colorKeys[i].k)
                    return false;
                Color x = _colorKeys[i].color;
                Color y = other._colorKeys[i].color;
                if (x.r != y.r || x.g != y.g || x.b != y.b || x.a != y.a)
                    return false;
            }

            for (int i = 0; i < _alphaKeyCount; i++)
            {
                if (_alphaKeys[i].k != other._alphaKeys[i].k || _alphaKeys[i].alpha != other._alphaKeys[i].alpha)
                    return false;
            }

            return true;
        }

        /// <summary>Forwards to <see cref="Equals(Gradient)"/> when <paramref name="o"/> is a Gradient (GC §3.7).</summary>
        public override bool Equals(object o) => o is Gradient g && Equals(g);

        /// <summary>
        /// An <b>identity</b> hash, deliberately inconsistent with <see cref="Equals(Gradient)"/>. Unity hashes the
        /// native pointer, so equal gradients hash differently and a Dictionary never finds an entry by an equal copy
        /// (GC §3.7). A content hash here would be a silent divergence from Unity.
        /// </summary>
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        // A null array becomes an empty span, which the setters then reject as "0 keys" (GC §3.1).
        private static ReadOnlySpan<GradientColorKey> AsSpanOrEmpty(GradientColorKey[] array)
            => array == null ? ReadOnlySpan<GradientColorKey>.Empty : new ReadOnlySpan<GradientColorKey>(array);

        private static ReadOnlySpan<GradientAlphaKey> AsSpanOrEmpty(GradientAlphaKey[] array)
            => array == null ? ReadOnlySpan<GradientAlphaKey>.Empty : new ReadOnlySpan<GradientAlphaKey>(array);
    }
}
