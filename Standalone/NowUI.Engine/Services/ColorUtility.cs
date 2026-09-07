// Mirrors UnityEngine.ColorUtility.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 ColorUtility bullet).
// Behaviour spec: Docs/Standalone/GradientCurveSemantics.md §6 (the verified parse table and the rounding rule),
// §10.2 (the Mono non-finite divergence this file reproduces), §10.3 (the documented quirks).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine
{
    /// <summary>
    /// HTML colour string conversion. Declared <c>partial</c> and neither <c>static</c> nor <c>sealed</c>, matching
    /// Unity (GC §6): it is an ordinary class that happens to hold only static members.
    /// </summary>
    public partial class ColorUtility
    {
        /// <summary>
        /// Unity's 23 recognised names, packed as 0xRRGGBBAA and matched case-insensitively (GC §6.1 step 3).
        /// </summary>
        /// <remarks>
        /// The absences are as load-bearing as the entries: Unity knows <c>grey</c> but not <c>gray</c>, and knows
        /// neither <c>pink</c> nor <c>clear</c> - all three fail and therefore yield white. <c>aqua</c> duplicates
        /// <c>cyan</c> and <c>fuchsia</c> duplicates <c>magenta</c>; <c>transparent</c> is the only entry with a
        /// non-opaque alpha.
        /// </remarks>
        private static readonly Dictionary<string, uint> s_NamedColors =
            new Dictionary<string, uint>(23, StringComparer.OrdinalIgnoreCase)
            {
                { "red", 0xFF0000FFu },
                { "cyan", 0x00FFFFFFu },
                { "blue", 0x0000FFFFu },
                { "darkblue", 0x00008BFFu },
                { "lightblue", 0xADD8E6FFu },
                { "purple", 0x800080FFu },
                { "yellow", 0xFFFF00FFu },
                { "lime", 0x00FF00FFu },
                { "fuchsia", 0xFF00FFFFu },
                { "white", 0xFFFFFFFFu },
                { "silver", 0xC0C0C0FFu },
                { "grey", 0x808080FFu },
                { "black", 0x000000FFu },
                { "orange", 0xFFA500FFu },
                { "brown", 0xA52A2AFFu },
                { "maroon", 0x800000FFu },
                { "green", 0x008000FFu },
                { "olive", 0x808000FFu },
                { "navy", 0x000080FFu },
                { "teal", 0x008080FFu },
                { "aqua", 0x00FFFFFFu },
                { "magenta", 0xFF00FFFFu },
                { "transparent", 0x00000000u },
            };

        /// <summary>
        /// Parses <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c>, <c>#RRGGBBAA</c> or one of the 23 names.
        /// </summary>
        /// <remarks>
        /// Three Unity behaviours here look like bugs and are not (GC §6.1, §10.3), so none of them may be "fixed":
        /// the input is <b>trimmed</b> first; names match <b>case-insensitively</b> even though the docs list only
        /// lower-case ones; and on failure <paramref name="color"/> is <b>white</b>, not <c>default</c> - callers such
        /// as NowRichTextParser draw with the out value regardless of the return, and a black default would change
        /// what they render. Hex without a leading <c>#</c> fails, as does any length other than 3/4/6/8 and any
        /// non-ASCII digit.
        /// </remarks>
        public static bool TryParseHtmlString(string htmlString, out Color color)
        {
            // Assigned before any parse attempt so that every failure path reports white without repeating itself.
            color = Color.white;

            if (string.IsNullOrEmpty(htmlString))
                return false;

            string text = htmlString.Trim();
            if (text.Length == 0)
                return false;

            if (text[0] == '#')
                return TryParseHex(text, out color);

            if (s_NamedColors.TryGetValue(text, out uint packed))
            {
                color = FromPacked(packed);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The six-digit upper-case hex of the RGB channels, with no leading <c>#</c>.
        /// </summary>
        public static string ToHtmlStringRGB(Color color)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}",
                ToByte(color.r),
                ToByte(color.g),
                ToByte(color.b));
        }

        /// <summary>
        /// The eight-digit upper-case hex of the RGBA channels, with no leading <c>#</c>.
        /// </summary>
        public static string ToHtmlStringRGBA(Color color)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}{3:X2}",
                ToByte(color.r),
                ToByte(color.g),
                ToByte(color.b),
                ToByte(color.a));
        }

        /// <summary>
        /// One channel to a byte: <c>(byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255)</c> (GC §6.2).
        /// </summary>
        /// <remarks>
        /// Two details are verified and neither is incidental. The multiply is in <b>single</b> precision: widening to
        /// double would make 0.3f × 255 land at 76.500003 and round to 77, where Unity's float multiply lands exactly
        /// on 76.5 and banker's rounding gives 76 - the difference between the verified "1A334C66" and a wrong
        /// "1A334D66". And non-finite inputs are forced to 0 for Mono parity (GC §10.2): Mono's out-of-range
        /// <c>double → int</c> cast yields <c>int.MinValue</c>, which clamps to 0, so Unity renders +INF as "00" while
        /// .NET 9 would saturate it to "FF".
        /// </remarks>
        private static byte ToByte(float channel)
        {
            if (!float.IsFinite(channel))
                return 0;

            return (byte)Mathf.Clamp(Mathf.RoundToInt(channel * 255F), 0, 255);
        }

        private static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;

            int digits = text.Length - 1;
            if (digits != 3 && digits != 4 && digits != 6 && digits != 8)
                return false;

            // Eight nibbles is the widest form, so a fixed stack buffer covers every case with no allocation.
            Span<int> nibbles = stackalloc int[8];
            for (int i = 0; i < digits; i++)
            {
                int value = HexValue(text[i + 1]);
                if (value < 0)
                    return false;

                nibbles[i] = value;
            }

            byte r, g, b, a;
            if (digits <= 4)
            {
                // Short forms expand by doubling each digit, so #f00 is #ff0000 - which is a multiply by 17, not a
                // shift by 4.
                r = (byte)(nibbles[0] * 17);
                g = (byte)(nibbles[1] * 17);
                b = (byte)(nibbles[2] * 17);
                a = digits == 4 ? (byte)(nibbles[3] * 17) : (byte)255;
            }
            else
            {
                r = (byte)((nibbles[0] << 4) | nibbles[1]);
                g = (byte)((nibbles[2] << 4) | nibbles[3]);
                b = (byte)((nibbles[4] << 4) | nibbles[5]);
                a = digits == 8 ? (byte)((nibbles[6] << 4) | nibbles[7]) : (byte)255;
            }

            color = new Color32(r, g, b, a);
            return true;
        }

        /// <summary>
        /// ASCII hex digit to its value, or -1. Written out rather than delegating to <c>char.IsDigit</c> or
        /// <c>Convert</c>, both of which accept non-ASCII digits that Unity rejects (GC §6.1 step 2).
        /// </summary>
        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;

            return -1;
        }

        /// <summary>Unpacks 0xRRGGBBAA into a colour through <c>Color32</c>, whose conversion is <c>byte / 255f</c>.</summary>
        private static Color FromPacked(uint packed)
        {
            return new Color32(
                (byte)(packed >> 24),
                (byte)(packed >> 16),
                (byte)(packed >> 8),
                (byte)packed);
        }
    }
}
