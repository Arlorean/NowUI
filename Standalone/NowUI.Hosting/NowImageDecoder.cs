using System;
using System.IO;
using NowUI.Engine;
using StbImageSharp;
using UnityEngine;

namespace NowUI.Hosting
{
    /// <summary>Native PNG/JPEG/TGA/BMP decoding with bottom-up RGBA pixels and PNG encoding.</summary>
    public sealed class NowImageDecoder : INowImageDecoder
    {
        readonly NowPngImageDecoder encoder = new();
        public bool TryDecode(ReadOnlySpan<byte> encoded, out int width, out int height, out byte[] rgba32BottomUp, out string error)
        {
            width = height = 0; rgba32BottomUp = null; error = null;
            try
            {
                using var stream = new MemoryStream(encoded.ToArray(), writable: false);
                var info = ImageInfo.FromStream(stream);
                if (!info.HasValue) throw new InvalidDataException("Unrecognized image; supported formats include PNG, JPEG, TGA and BMP.");
                if (info.Value.Width <= 0 || info.Value.Height <= 0 || info.Value.Width > 16384 || info.Value.Height > 16384
                    || (long)info.Value.Width * info.Value.Height > 16 * 1024 * 1024)
                    throw new InvalidDataException("Image exceeds the native 16 million pixel limit.");
                stream.Position = 0;
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                width = image.Width; height = image.Height;
                int stride = checked(width * 4);
                rgba32BottomUp = new byte[checked(stride * height)];
                for (int y = 0; y < height; y++) Buffer.BlockCopy(image.Data, y * stride, rgba32BottomUp, (height - 1 - y) * stride, stride);
                return true;
            }
            catch (Exception exception)
            {
                width = height = 0; rgba32BottomUp = null; error = exception.Message; return false;
            }
        }
        public byte[] TryEncode(Texture2D texture, NowImageFormat format, int quality) => encoder.TryEncode(texture, format, quality);
    }
}
