// Managed PNG encoding and decoding for native captures and embedded font atlases.
// Decodes source samples without color-profile conversion; the renderer owns color-space handling.
// Supports PNG color types 0/2/3/4/6; Adam7 is explicitly unsupported. Output is bottom-up RGBA32.
using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace NowUI.Hosting
{
    /// <summary>A PNG decoder and encoder in managed code, over <see cref="ZLibStream"/>.</summary>
    internal static class NowPngCodec
    {
        private const uint k_Ihdr = 0x49484452; // "IHDR"
        private const uint k_Plte = 0x504C5445; // "PLTE"
        private const uint k_Idat = 0x49444154; // "IDAT"
        private const uint k_Iend = 0x49454E44; // "IEND"
        private const uint k_Trns = 0x74524E53; // "tRNS"

        /// <summary>The eight-byte PNG signature.</summary>
        internal static bool LooksLikePng(ReadOnlySpan<byte> data)
        {
            return data.Length >= 8 &&
                   data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                   data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        }

        // ----------------------------------------------------------------------------------------------- decode

        /// <summary>Decodes a non-interlaced PNG into bottom-up RGBA32. Returns false with a reason.</summary>
        internal static bool TryDecode(ReadOnlySpan<byte> data, out int width, out int height,
                                       out byte[] rgba32BottomUp, out string error)
        {
            width = 0;
            height = 0;
            rgba32BottomUp = null;
            error = null;

            if (!LooksLikePng(data))
            {
                error = "Not a PNG: the eight-byte signature is missing.";
                return false;
            }

            int bitDepth = 0;
            int colorType = 0;
            byte[] palette = null;
            byte[] paletteAlpha = null;
            byte[] transparency = null;
            var compressed = new MemoryStream();
            bool sawHeader = false;
            int position = 8;

            while (position + 8 <= data.Length)
            {
                long length = ReadUInt32(data, position);
                uint type = (uint)ReadUInt32(data, position + 4);
                position += 8;

                if (length < 0 || position + length + 4 > data.Length)
                {
                    error = "The PNG is truncated: a chunk claims " + length + " bytes and the file has " +
                            (data.Length - position) + " left.";
                    return false;
                }

                ReadOnlySpan<byte> chunk = data.Slice(position, (int)length);

                switch (type)
                {
                    case k_Ihdr:
                        if (length < 13)
                        {
                            error = "The PNG header chunk is " + length + " bytes; it must be 13.";
                            return false;
                        }

                        width = (int)ReadUInt32(chunk, 0);
                        height = (int)ReadUInt32(chunk, 4);
                        bitDepth = chunk[8];
                        colorType = chunk[9];

                        if (chunk[10] != 0)
                        {
                            error = "The PNG uses compression method " + chunk[10] + "; only 0 (deflate) exists.";
                            return false;
                        }

                        if (chunk[11] != 0)
                        {
                            error = "The PNG uses filter method " + chunk[11] + "; only 0 exists.";
                            return false;
                        }

                        if (chunk[12] != 0)
                        {
                            error = "The PNG is Adam7 interlaced, which this managed decoder does not implement. " +
                                    "Use a non-interlaced PNG, or load the file through the project asset provider.";
                            return false;
                        }

                        sawHeader = true;
                        break;

                    case k_Plte:
                        palette = chunk.ToArray();
                        break;

                    case k_Trns:
                        transparency = chunk.ToArray();
                        break;

                    case k_Idat:
                        // Concatenated: the spec allows a PNG to split its compressed stream over any number of
                        // IDAT chunks, and many encoders do at 8 KB or 32 KB boundaries.
                        compressed.Write(chunk);
                        break;

                    case k_Iend:
                        position = data.Length; // stop
                        break;
                }

                if (position >= data.Length)
                    break;

                position += (int)length + 4; // + CRC
            }

            if (!sawHeader)
            {
                error = "The PNG has no IHDR chunk.";
                return false;
            }

            if (width <= 0 || height <= 0)
            {
                error = "The PNG declares a " + width + "x" + height + " image.";
                return false;
            }

            int channels;

            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default:
                    error = "The PNG declares colour type " + colorType + ", which is not one of 0, 2, 3, 4 or 6.";
                    return false;
            }

            if (!IsLegalBitDepth(colorType, bitDepth))
            {
                error = "The PNG declares bit depth " + bitDepth + " for colour type " + colorType +
                        ", which the format does not allow.";
                return false;
            }

            if (colorType == 3 && palette == null)
            {
                error = "The PNG is palette-indexed and carries no PLTE chunk.";
                return false;
            }

            if (colorType == 3 && transparency != null)
                paletteAlpha = transparency;

            long stride = ((long)width * channels * bitDepth + 7) / 8;
            long expected = (stride + 1) * height;

            if (expected > int.MaxValue)
            {
                error = "The PNG decodes to " + expected + " bytes of scanline data, which is more than this host " +
                        "will allocate.";
                return false;
            }

            byte[] raw;

            try
            {
                raw = Inflate(compressed, (int)expected);
            }
            catch (Exception e)
            {
                error = "The PNG's compressed data could not be inflated: " + e.Message;
                return false;
            }

            if (raw == null)
            {
                error = "The PNG's compressed data is shorter than the " + expected +
                        " bytes its header describes; the file is truncated.";
                return false;
            }

            Unfilter(raw, width, height, (int)stride, Math.Max(1, channels * bitDepth / 8));

            rgba32BottomUp = Expand(raw, width, height, (int)stride, bitDepth, colorType, channels,
                                    palette, paletteAlpha, transparency);

            if (rgba32BottomUp == null)
            {
                error = "The PNG's palette is too small for an index the image uses.";
                return false;
            }

            return true;
        }

        private static bool IsLegalBitDepth(int colorType, int bitDepth)
        {
            switch (colorType)
            {
                case 0: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16;
                case 3: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8;
                default: return bitDepth == 8 || bitDepth == 16;
            }
        }

        /// <summary>Inflates exactly <paramref name="expected"/> bytes, or returns null when the stream is short.</summary>
        private static byte[] Inflate(MemoryStream compressed, int expected)
        {
            var raw = new byte[expected];

            compressed.Position = 0;

            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            {
                int read = 0;

                while (read < expected)
                {
                    int step = zlib.Read(raw, read, expected - read);

                    if (step <= 0)
                        return null;

                    read += step;
                }
            }

            return raw;
        }

        /// <summary>
        /// Reverses the five PNG row filters in place.
        /// </summary>
        /// <remarks>
        /// Each row is one filter byte followed by <paramref name="stride"/> filtered bytes, and every filter is
        /// defined over the RECONSTRUCTED bytes of the row to the left and the row above - so this must run in
        /// order and in place, which it does.
        /// </remarks>
        private static void Unfilter(byte[] raw, int width, int height, int stride, int unit)
        {
            for (int y = 0; y < height; ++y)
            {
                int row = y * (stride + 1);
                int filter = raw[row];
                int at = row + 1;
                int above = at - (stride + 1);

                switch (filter)
                {
                    case 0: // None
                        break;

                    case 1: // Sub
                        for (int i = unit; i < stride; ++i)
                            raw[at + i] = (byte)(raw[at + i] + raw[at + i - unit]);
                        break;

                    case 2: // Up
                        if (y > 0)
                        {
                            for (int i = 0; i < stride; ++i)
                                raw[at + i] = (byte)(raw[at + i] + raw[above + i]);
                        }
                        break;

                    case 3: // Average
                        for (int i = 0; i < stride; ++i)
                        {
                            int left = i >= unit ? raw[at + i - unit] : 0;
                            int up = y > 0 ? raw[above + i] : 0;
                            raw[at + i] = (byte)(raw[at + i] + ((left + up) >> 1));
                        }
                        break;

                    case 4: // Paeth
                        for (int i = 0; i < stride; ++i)
                        {
                            int left = i >= unit ? raw[at + i - unit] : 0;
                            int up = y > 0 ? raw[above + i] : 0;
                            int upLeft = y > 0 && i >= unit ? raw[above + i - unit] : 0;
                            raw[at + i] = (byte)(raw[at + i] + Paeth(left, up, upLeft));
                        }
                        break;

                    default:
                        // An unknown filter byte. Treated as None rather than throwing: the row will look wrong,
                        // and the alternative is refusing an entire image for one bad byte.
                        break;
                }
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = p > a ? p - a : a - p;
            int pb = p > b ? p - b : b - p;
            int pc = p > c ? p - c : c - p;

            if (pa <= pb && pa <= pc)
                return a;

            return pb <= pc ? b : c;
        }

        /// <summary>Turns unfiltered scanlines into bottom-up RGBA32. Returns null on a palette index out of range.</summary>
        private static byte[] Expand(byte[] raw, int width, int height, int stride, int bitDepth, int colorType,
                                     int channels, byte[] palette, byte[] paletteAlpha, byte[] transparency)
        {
            var output = new byte[width * height * 4];
            int outStride = width * 4;

            // tRNS for the non-palette colour types is a single fully-transparent SAMPLE VALUE, stored at the
            // file's own bit depth; compared at 8 bits here because that is what the output carries.
            int transparentGray = -1;
            int transparentR = -1, transparentG = -1, transparentB = -1;

            if (transparency != null && colorType == 0 && transparency.Length >= 2)
                transparentGray = ScaleSample(ReadUInt16(transparency, 0), bitDepth);

            if (transparency != null && colorType == 2 && transparency.Length >= 6)
            {
                transparentR = ScaleSample(ReadUInt16(transparency, 0), bitDepth);
                transparentG = ScaleSample(ReadUInt16(transparency, 2), bitDepth);
                transparentB = ScaleSample(ReadUInt16(transparency, 4), bitDepth);
            }

            for (int y = 0; y < height; ++y)
            {
                int row = y * (stride + 1) + 1;

                // Bottom-up: the contract's row order, and Texture2D.SetPixels32's.
                int dst = (height - 1 - y) * outStride;

                for (int x = 0; x < width; ++x)
                {
                    byte r, g, b, a;

                    switch (colorType)
                    {
                        case 0:
                        {
                            int sample = Sample(raw, row, x, bitDepth);
                            byte value = (byte)ScaleSample(sample, bitDepth);
                            r = g = b = value;
                            a = transparentGray >= 0 && value == transparentGray ? (byte)0 : (byte)255;
                            break;
                        }

                        case 2:
                        {
                            int step = bitDepth == 16 ? 2 : 1;
                            int at = row + x * 3 * step;
                            r = raw[at];
                            g = raw[at + step];
                            b = raw[at + 2 * step];
                            a = transparentR >= 0 && r == transparentR && g == transparentG && b == transparentB
                                ? (byte)0
                                : (byte)255;
                            break;
                        }

                        case 3:
                        {
                            int index = Sample(raw, row, x, bitDepth);
                            int at = index * 3;

                            if (palette == null || at + 2 >= palette.Length)
                                return null;

                            r = palette[at];
                            g = palette[at + 1];
                            b = palette[at + 2];
                            a = paletteAlpha != null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                            break;
                        }

                        case 4:
                        {
                            int step = bitDepth == 16 ? 2 : 1;
                            int at = row + x * 2 * step;
                            r = g = b = raw[at];
                            a = raw[at + step];
                            break;
                        }

                        default: // 6
                        {
                            int step = bitDepth == 16 ? 2 : 1;
                            int at = row + x * 4 * step;
                            r = raw[at];
                            g = raw[at + step];
                            b = raw[at + 2 * step];
                            a = raw[at + 3 * step];
                            break;
                        }
                    }

                    int o = dst + x * 4;
                    output[o] = r;
                    output[o + 1] = g;
                    output[o + 2] = b;
                    output[o + 3] = a;
                }
            }

            return output;
        }

        /// <summary>One single-channel sample, at any of the five bit depths. 16-bit takes the high byte.</summary>
        private static int Sample(byte[] raw, int rowStart, int index, int bitDepth)
        {
            if (bitDepth == 8)
                return raw[rowStart + index];

            if (bitDepth == 16)
                return raw[rowStart + index * 2];

            int bit = index * bitDepth;
            int value = raw[rowStart + (bit >> 3)];
            int shift = 8 - bitDepth - (bit & 7);
            return (value >> shift) & ((1 << bitDepth) - 1);
        }

        /// <summary>Widens a sample at <paramref name="bitDepth"/> to the 0..255 the output carries.</summary>
        private static int ScaleSample(int sample, int bitDepth)
        {
            switch (bitDepth)
            {
                case 1: return sample * 255;
                case 2: return sample * 85;
                case 4: return sample * 17;
                case 16: return (sample >> 8) & 0xFF;
                default: return sample & 0xFF;
            }
        }

        private static long ReadUInt32(ReadOnlySpan<byte> data, int at)
        {
            return ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3];
        }

        private static int ReadUInt16(byte[] data, int at)
        {
            return (data[at] << 8) | data[at + 1];
        }

        // ----------------------------------------------------------------------------------------------- encode

        /// <summary>
        /// Encodes bottom-up <see cref="Color32"/> pixels as an 8-bit RGBA PNG.
        /// </summary>
        /// <remarks>
        /// Every row is written with filter 0 (None) and the deflate is left to <see cref="ZLibStream"/>. Choosing
        /// a filter per row the way libpng does would shrink the output, and it is deliberately not done: this
        /// encoder exists so <c>EncodeToPNG</c> returns a real PNG rather than null, not to compete on size, and a
        /// filter heuristic is a page of code whose only effect is on bytes nobody in this host transmits.
        /// </remarks>
        internal static byte[] Encode(Color32[] bottomUp, int width, int height)
        {
            if (bottomUp == null || width <= 0 || height <= 0)
                return null;

            int stride = width * 4;
            var raw = new byte[(stride + 1) * height];

            for (int y = 0; y < height; ++y)
            {
                // PNG rows run top-down; the source runs bottom-up.
                int source = (height - 1 - y) * width;
                int at = y * (stride + 1);
                raw[at] = 0; // filter: None
                ++at;

                for (int x = 0; x < width; ++x)
                {
                    Color32 pixel = bottomUp[source + x];
                    raw[at++] = pixel.r;
                    raw[at++] = pixel.g;
                    raw[at++] = pixel.b;
                    raw[at++] = pixel.a;
                }
            }

            byte[] compressed;

            using (var buffer = new MemoryStream())
            {
                using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                    zlib.Write(raw, 0, raw.Length);

                compressed = buffer.ToArray();
            }

            using (var png = new MemoryStream())
            {
                png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                var header = new byte[13];
                WriteUInt32(header, 0, (uint)width);
                WriteUInt32(header, 4, (uint)height);
                header[8] = 8;  // bit depth
                header[9] = 6;  // colour type: truecolour + alpha
                header[10] = 0; // compression: deflate
                header[11] = 0; // filter method
                header[12] = 0; // interlace: none

                WriteChunk(png, k_Ihdr, header);
                WriteChunk(png, k_Idat, compressed);
                WriteChunk(png, k_Iend, Array.Empty<byte>());

                return png.ToArray();
            }
        }

        private static void WriteChunk(Stream stream, uint type, byte[] data)
        {
            var length = new byte[4];
            WriteUInt32(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);

            var tag = new byte[4];
            WriteUInt32(tag, 0, type);
            stream.Write(tag, 0, 4);
            stream.Write(data, 0, data.Length);

            uint crc = Crc32(Crc32(0xFFFFFFFFu, tag, tag.Length), data, data.Length) ^ 0xFFFFFFFFu;
            var trailer = new byte[4];
            WriteUInt32(trailer, 0, crc);
            stream.Write(trailer, 0, 4);
        }

        private static void WriteUInt32(byte[] data, int at, uint value)
        {
            data[at] = (byte)(value >> 24);
            data[at + 1] = (byte)(value >> 16);
            data[at + 2] = (byte)(value >> 8);
            data[at + 3] = (byte)value;
        }

        private static readonly uint[] k_CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];

            for (uint n = 0; n < 256; ++n)
            {
                uint c = n;

                for (int k = 0; k < 8; ++k)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

                table[n] = c;
            }

            return table;
        }

        private static uint Crc32(uint crc, byte[] data, int count)
        {
            for (int i = 0; i < count; ++i)
                crc = k_CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

            return crc;
        }
    }
}
