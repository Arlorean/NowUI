// Mirrors UnityEngine.ImageConversion for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 ("ImageConversion.cs" - extension methods over
// INowImageDecoder, NOT over the render backend), §4.4 (INowImageDecoder), §1.2 (image decode stays synchronous in
// M1 because the browser fetch provider pre-decodes).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.ImageConversion` - the single caller is
// NowMarkdownImages.cs:424-473, which writes `texture.LoadImage(bytes, markNonReadable: true)` and treats `false` as
// "this image failed to decode".
//
// Why these are extension methods and not instance members: that is what they are in Unity. ImageConversion lives in
// its own module there, so `LoadImage` is `ImageConversion.LoadImage(this Texture2D, byte[])` and NowMarkdownImages
// calls it in the extension form. Declaring them on Texture2D instead would compile the shim but not the core.
//
// Why the decoder is a HOST service and not a backend call: a codec and a GL context are different capabilities. A
// headless host wants PNG decoding with no renderer at all, and in a browser the decoder is createImageBitmap while
// the renderer is WebGL2. With no decoder installed LoadImage returns false, which is exactly the path
// NowMarkdownImages already has for a corrupt download.
using System;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>Encodes and decodes <see cref="Texture2D"/> contents to and from PNG/JPEG byte arrays.</summary>
    public static class ImageConversion
    {
        /// <summary>Decodes <paramref name="data"/> into <paramref name="tex"/>, keeping it readable.</summary>
        public static bool LoadImage(this Texture2D tex, byte[] data)
        {
            return LoadImage(tex, data, false);
        }

        /// <summary>
        /// Decodes <paramref name="data"/> into <paramref name="tex"/>, resizing and re-formatting the texture to
        /// match the image exactly as Unity does. Returns false - and leaves the texture untouched - when there is no
        /// decoder installed or the bytes are not a picture.
        /// </summary>
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable)
        {
            if (tex == null)
                throw new ArgumentNullException(nameof(tex));

            if (data == null || data.Length == 0)
                return false;

            INowImageDecoder decoder = NowRuntime.host.imageDecoder;

            if (decoder == null)
                return false;

            int width;
            int height;
            byte[] rgba;
            string error;

            if (!decoder.TryDecode(new ReadOnlySpan<byte>(data), out width, out height, out rgba, out error))
                return false;

            if (rgba == null || width <= 0 || height <= 0)
                return false;

            long needed = (long)width * height * 4;

            if (rgba.Length < needed)
                return false;

            // Unity's LoadImage re-creates the texture at the image's size and in RGBA32, discarding whatever the
            // texture was before - which is why NowMarkdownImages can hand it a 2x2 placeholder (NowMarkdownImages.cs:441).
            tex.Reinitialize(width, height, TextureFormat.RGBA32, false);

            // The decoder contract is tightly packed, BOTTOM-UP RGBA32 rows (design §4.4), which is precisely the CPU
            // store's own layout, so this is a straight copy with no flip.
            Buffer.BlockCopy(rgba, 0, tex.pixels, 0, (int)needed);

            tex.dirtyRect = new RectInt(0, 0, width, height);
            tex.uploadPending = true;
            tex.Apply(true, markNonReadable);
            return true;
        }

        /// <summary>PNG bytes for <paramref name="tex"/>, or null when the host has no encoder for it.</summary>
        public static byte[] EncodeToPNG(this Texture2D tex)
        {
            return Encode(tex, NowImageFormat.Png, 100);
        }

        /// <summary>JPEG bytes for <paramref name="tex"/>, or null when the host has no encoder for it.</summary>
        public static byte[] EncodeToJPG(this Texture2D tex, int quality = 75)
        {
            return Encode(tex, NowImageFormat.Jpg, quality);
        }

        private static byte[] Encode(Texture2D tex, NowImageFormat format, int quality)
        {
            if (tex == null)
                throw new ArgumentNullException(nameof(tex));

            INowImageDecoder decoder = NowRuntime.host.imageDecoder;

            if (decoder == null)
                return null;

            // Unity clamps the JPEG quality into [1, 100] rather than rejecting it.
            int clamped = quality < 1 ? 1 : (quality > 100 ? 100 : quality);
            return decoder.TryEncode(tex, format, clamped);
        }
    }
}
