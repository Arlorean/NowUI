// Writes a baked font atlas page out as a PNG the browser bundle can load without Unity.
//
// WHY THIS EXISTS. NowFontBaker hands back BakedPage.texture as an RGBA32 Texture2D, and Unity keeps those pages as
// texture sub-assets of the .asset - a serialization the standalone/browser side cannot read. The browser needs the
// pixels as a file it can fetch and push straight into Texture2D.LoadRawTextureData, so the fixture format is ours to
// define, and this is the encoder half of it. NowWebPng (Standalone/Web/NowUI.Web/NowWebPng.cs) is the decoder half.
//
// WHY GREY+ALPHA AND NOT RGBA. A managed SDF page is not four independent channels. NowFont packs a 16-bit normalized
// distance into the RGBA32 payload with the HIGH byte in R, G and A and the LOW byte in B (see CreateDynamicPageFont's
// comment in NowFont.cs), so three of the four bytes per pixel are the same byte written three times. That is not a
// compression detail, it is the actual information content: two bytes per pixel, not four. Deflate cannot exploit the
// R/G/A duplication across a four-byte stride, so an RGBA PNG of these pages is far bigger than the data in it - a
// measured 1,302,464 B across the four NotoSans faces, against 891,569 B for the two-plane form. Storing the two real
// planes as an 8-bit greyscale+alpha PNG and reconstructing on load is EXACTLY lossless, and 411 KB smaller in a file
// that is committed to git twice (once as a fixture, once inside the shipped bundle).
//
// The R == G == A property is CHECKED per page, not assumed: a page that does not have it (a colour font, or a future
// non-packed encoding) is written as a plain RGBA PNG instead and says so in "pageEncoding", so a change in the baker
// degrades to a bigger file rather than to wrong pixels.
//
// Two-plane greyscale (one file for the high plane, one for the low) was measured at 885,889 B - 0.6% better than
// grey+alpha for four extra files and four extra fetches per bundle. Not worth it.
//
// WHY A HAND-WRITTEN ENCODER. ImageConversion.EncodeToPNG only emits the texture's own format, and there is no
// two-channel Texture2D that Unity is documented to encode as PNG colour type 4. Writing it here is about ninety lines
// over DeflateStream and gives exact control of the one thing that has to match the decoder: row order. It also keeps
// the exporter's determinism promise, because the filter choice is a fixed heuristic rather than a library's mood.
//
// ROW ORDER, the one thing that silently produces upside-down text. Texture2D raw data is BOTTOM-UP; PNG scanlines are
// TOP-DOWN; NowWebPng decodes back to BOTTOM-UP for LoadRawTextureData. So this encoder reads the texture's last row
// first. Standalone/Tests asserts the round trip pixel-for-pixel rather than trusting that sentence.
using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace NowUI.Editor
{
    /// <summary>Encodes a baked atlas page as a PNG for the standalone font fixtures.</summary>
    static class NowStandaloneFontPageWriter
    {
        /// <summary>High byte of the packed distance in the grey plane, low byte in the alpha plane.</summary>
        internal const string ENCODING_GREY8_ALPHA8 = "grey8-alpha8";

        /// <summary>Straight RGBA, for a page whose channels are not the packed-SDF duplication.</summary>
        internal const string ENCODING_RGBA8 = "rgba8";

        /// <summary>
        /// Writes <paramref name="texture"/> to <paramref name="path"/> as a PNG and reports which of the two
        /// encodings it used. Returns false with a reason rather than writing a file it is unsure of.
        /// </summary>
        internal static bool TryWrite(Texture2D texture, string path, out string encoding, out int byteCount, out string error)
        {
            encoding = null;
            byteCount = 0;
            error = null;

            if (texture == null)
            {
                error = "the page has no texture";
                return false;
            }

            if (texture.format != TextureFormat.RGBA32)
            {
                error = $"the page texture is {texture.format}, and only RGBA32 is understood";
                return false;
            }

            byte[] rgba;

            try
            {
                rgba = texture.GetRawTextureData();
            }
            catch (Exception exception)
            {
                error = "the page texture is not readable: " + exception.Message;
                return false;
            }

            int width = texture.width;
            int height = texture.height;

            if (rgba == null || rgba.Length != width * height * 4)
            {
                error = $"the page texture holds {rgba?.Length ?? 0} bytes, not the {width * height * 4} an RGBA32 {width}x{height} needs";
                return false;
            }

            bool greyAlpha = IsPackedGreyAlpha(rgba);
            encoding = greyAlpha ? ENCODING_GREY8_ALPHA8 : ENCODING_RGBA8;

            byte[] png = greyAlpha
                ? EncodePng(BuildGreyAlphaTopDown(rgba, width, height), width, height, colorType: 4, bytesPerPixel: 2)
                : EncodePng(FlipRowsTopDown(rgba, width, height, 4), width, height, colorType: 6, bytesPerPixel: 4);

            File.WriteAllBytes(path, png);
            byteCount = png.Length;
            return true;
        }

        /// <summary>
        /// True when every pixel has R == G == A, which is what NowFont's packed 16-bit SDF encoding produces and what
        /// makes the two-plane form lossless. Checked, never assumed.
        /// </summary>
        static bool IsPackedGreyAlpha(byte[] rgba)
        {
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i] != rgba[i + 1] || rgba[i] != rgba[i + 3])
                    return false;
            }

            return true;
        }

        /// <summary>Grey = R (the high byte), alpha = B (the low byte), rows reversed into PNG's top-down order.</summary>
        static byte[] BuildGreyAlphaTopDown(byte[] rgba, int width, int height)
        {
            var output = new byte[width * height * 2];

            for (int y = 0; y < height; ++y)
            {
                int source = (height - 1 - y) * width * 4;
                int destination = y * width * 2;

                for (int x = 0; x < width; ++x)
                {
                    output[destination + x * 2] = rgba[source + x * 4];         // R: high byte
                    output[destination + x * 2 + 1] = rgba[source + x * 4 + 2]; // B: low byte
                }
            }

            return output;
        }

        static byte[] FlipRowsTopDown(byte[] pixels, int width, int height, int bytesPerPixel)
        {
            int stride = width * bytesPerPixel;
            var output = new byte[stride * height];

            for (int y = 0; y < height; ++y)
                Buffer.BlockCopy(pixels, (height - 1 - y) * stride, output, y * stride, stride);

            return output;
        }

        // ------------------------------------------------------------------------------------------------ PNG

        static byte[] EncodePng(byte[] topDown, int width, int height, int colorType, int bytesPerPixel)
        {
            var stream = new MemoryStream(1 << 18);
            stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;                   // bit depth
            header[9] = (byte)colorType;     // 4 = greyscale+alpha, 6 = truecolour+alpha
            header[10] = 0;                  // compression: deflate
            header[11] = 0;                  // filter method: adaptive
            header[12] = 0;                  // interlace: none
            WriteChunk(stream, "IHDR", header);

            byte[] filtered = FilterRows(topDown, width, height, bytesPerPixel);
            WriteChunk(stream, "IDAT", Deflate(filtered));
            WriteChunk(stream, "IEND", Array.Empty<byte>());
            return stream.ToArray();
        }

        /// <summary>
        /// Per-row adaptive filtering with the PNG specification's own heuristic: pick whichever of the five filters
        /// gives the smallest sum of absolute signed differences. Deterministic, which the exporter needs.
        /// </summary>
        static byte[] FilterRows(byte[] raw, int width, int height, int bytesPerPixel)
        {
            int stride = width * bytesPerPixel;
            var output = new byte[(stride + 1) * height];
            var previous = new byte[stride];
            var candidate = new byte[stride];
            var best = new byte[stride];

            for (int y = 0; y < height; ++y)
            {
                int rowStart = y * stride;
                int bestType = 0;
                long bestScore = long.MaxValue;

                for (int type = 0; type < 5; ++type)
                {
                    long score = 0;

                    for (int i = 0; i < stride; ++i)
                    {
                        byte left = i >= bytesPerPixel ? raw[rowStart + i - bytesPerPixel] : (byte)0;
                        byte up = previous[i];
                        byte upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;
                        byte value = raw[rowStart + i];

                        byte encoded;

                        switch (type)
                        {
                            case 0: encoded = value; break;
                            case 1: encoded = (byte)(value - left); break;
                            case 2: encoded = (byte)(value - up); break;
                            case 3: encoded = (byte)(value - ((left + up) >> 1)); break;
                            default: encoded = (byte)(value - Paeth(left, up, upLeft)); break;
                        }

                        candidate[i] = encoded;
                        score += encoded < 128 ? encoded : 256 - encoded;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestType = type;
                        Buffer.BlockCopy(candidate, 0, best, 0, stride);
                    }
                }

                output[y * (stride + 1)] = (byte)bestType;
                Buffer.BlockCopy(best, 0, output, y * (stride + 1) + 1, stride);
                Buffer.BlockCopy(raw, rowStart, previous, 0, stride);
            }

            return output;
        }

        static byte Paeth(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc)
                return a;

            return pb <= pc ? b : c;
        }

        /// <summary>
        /// A zlib stream by hand around <see cref="DeflateStream"/>. Unity's API profile has no ZLibStream, and PNG
        /// needs the two-byte header and the Adler-32 trailer that raw deflate does not carry.
        /// </summary>
        static byte[] Deflate(byte[] data)
        {
            var stream = new MemoryStream(data.Length / 2 + 64);
            stream.WriteByte(0x78); // CMF: deflate, 32 KB window
            stream.WriteByte(0x01); // FLG: no preset dictionary, and 0x7801 % 31 == 0 as the spec requires

            // Fully qualified: UnityEngine declares a CompressionLevel of its own (for AssetBundles), so the bare name
            // is ambiguous inside Unity even though it compiles fine against the engine-free shim.
            using (var deflate = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(data, 0, data.Length);

            var trailer = new byte[4];
            WriteBigEndian(trailer, 0, Adler32(data));
            stream.Write(trailer, 0, 4);
            return stream.ToArray();
        }

        static uint Adler32(byte[] data)
        {
            const uint MOD = 65521;
            uint a = 1;
            uint b = 0;

            // Chunked so the accumulators cannot overflow before the modulo; 5552 is the largest run that is safe.
            for (int start = 0; start < data.Length; start += 5552)
            {
                int end = Math.Min(start + 5552, data.Length);

                for (int i = start; i < end; ++i)
                {
                    a += data[i];
                    b += a;
                }

                a %= MOD;
                b %= MOD;
            }

            return (b << 16) | a;
        }

        static void WriteChunk(MemoryStream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);

            var body = new byte[4 + data.Length];
            body[0] = (byte)type[0];
            body[1] = (byte)type[1];
            body[2] = (byte)type[2];
            body[3] = (byte)type[3];
            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            stream.Write(body, 0, body.Length);

            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc32(body));
            stream.Write(crc, 0, 4);
        }

        static uint[] s_crcTable;

        static uint Crc32(byte[] data)
        {
            if (s_crcTable == null)
            {
                s_crcTable = new uint[256];

                for (uint n = 0; n < 256; ++n)
                {
                    uint c = n;

                    for (int k = 0; k < 8; ++k)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

                    s_crcTable[n] = c;
                }
            }

            uint crc = 0xFFFFFFFFu;

            for (int i = 0; i < data.Length; ++i)
                crc = s_crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }

        static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
