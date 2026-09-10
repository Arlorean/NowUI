using System;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Hosting
{
    /// <summary>PNG output for captured frames in tightly packed, bottom-up RGBA byte order.</summary>
    public static class NowPng
    {
        public static byte[] EncodeRgba(int width, int height, byte[] bottomUpRgba)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (bottomUpRgba == null)
                throw new ArgumentNullException(nameof(bottomUpRgba));

            int pixelCount = checked(width * height);
            int byteCount = checked(pixelCount * 4);
            // The codec also stores a one-byte filter selector per row.
            _ = checked(byteCount + height);
            if (bottomUpRgba.Length != byteCount)
                throw new ArgumentException("Expected exactly width * height * 4 RGBA bytes.", nameof(bottomUpRgba));

            var pixels = new Color32[pixelCount];
            for (int i = 0, offset = 0; i < pixels.Length; ++i, offset += 4)
                pixels[i] = new Color32(bottomUpRgba[offset], bottomUpRgba[offset + 1],
                    bottomUpRgba[offset + 2], bottomUpRgba[offset + 3]);
            return NowPngCodec.Encode(pixels, width, height);
        }
    }

    /// <summary>A synchronous PNG codec for desktop hosts; other formats and interlaced PNGs are unsupported.</summary>
    public sealed class NowPngImageDecoder : INowImageDecoder
    {
        public bool TryDecode(ReadOnlySpan<byte> encoded, out int width, out int height,
            out byte[] rgba32BottomUp, out string error)
        {
            return NowPngCodec.TryDecode(encoded, out width, out height, out rgba32BottomUp, out error);
        }

        public byte[] TryEncode(Texture2D texture, NowImageFormat format, int quality)
        {
            if (texture == null || format != NowImageFormat.Png)
                return null;

            Color32[] pixels;
            try
            {
                pixels = texture.GetPixels32();
            }
            catch (Exception)
            {
                return null;
            }

            if (pixels == null || pixels.Length != (long)texture.width * texture.height)
                return null;
            return NowPngCodec.Encode(pixels, texture.width, texture.height);
        }
    }
}
