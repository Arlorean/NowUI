// INowImageDecoder for the browser host: a pre-decode cache in front of a managed PNG codec.
//
// THE AWKWARDNESS, AND WHY IT IS RESOLVED THIS WAY. INowImageDecoder.TryDecode is SYNCHRONOUS and returns tightly
// packed RGBA32 rows BOTTOM-UP. Every image API a browser has - createImageBitmap, HTMLImageElement.decode, the
// Image onload event - is asynchronous, and there is no way to await one from inside a synchronous call: a wasm
// page has ONE thread, so blocking it is not slow, it is a deadlock (the promise that would unblock it can only
// settle on the loop that is blocked). So the browser's decoder is never called from inside TryDecode. It is
// called AHEAD of it, by the fetch provider, while a response whose content type is an image is still arriving,
// and the result is parked here keyed by the encoded bytes. TryDecode is then a lookup. That is what
// StandaloneCoreDesign.md §1.2 means by "image decode stays synchronous in M1 because the browser fetch provider
// pre-decodes"; this is the file that makes it true.
//
// WHAT DECODES, PRECISELY. Three answers, and the third is a refusal:
//
//   1. Any PNG this file's own decoder can read - 8- and 16-bit greyscale, RGB, greyscale+alpha, RGBA, and 1/2/4/8
//      bit palette, with tRNS transparency, non-interlaced - decodes MANAGED, from any source: a fetch, a local
//      asset, a byte[] built in code. Tried FIRST even when a pre-decode is cached, because it is exact: the
//      browser path below round-trips through a 2D canvas, which stores premultiplied alpha, so a partially
//      transparent pixel loses a little precision there and none here.
//   2. Anything the BROWSER can decode - JPEG, GIF, WebP, AVIF, BMP, an interlaced PNG - decodes if and only if
//      the bytes arrived through WebFetchProvider, because that is the only path that can decode ahead of the call.
//   3. Everything else returns false with an error saying which of the two it missed. In particular a JPEG read
//      from a local asset does NOT decode, and no attempt is made to hide that: a hand-written JPEG decoder is a
//      DCT, four Huffman tables and a chroma upsampler, which is a great deal of code to maintain for a case the
//      fetch path already covers.
//
// The managed PNG decoder is not a nicety. Without it, an image that never passed through fetch() has no decoder
// at all, and "local PNG assets do not load" would be a strange thing for a UI toolkit to say. System.IO.Compression
// supplies the inflate, which is the only part of PNG that is genuinely hard.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 and §4.4; Docs/Standalone/M2-FeatureMatrix.md §8.1.
using System;
using System.Collections.Generic;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>Decodes images for the browser host: cached browser decodes first-class, PNG decoded here.</summary>
    public sealed class WebImageDecoder : INowImageDecoder
    {
        /// <summary>
        /// The most decoded RGBA this will hold. A pre-decode is a convenience, not a store: an entry that is
        /// evicted before anyone asks for it costs one failed decode, and one that is never evicted costs a leak.
        /// </summary>
        private const long k_CacheByteBudget = 64L * 1024L * 1024L;

        private readonly Dictionary<Key, Entry> m_Cache = new Dictionary<Key, Entry>();

        /// <summary>Insertion order, for eviction. A queue rather than an LRU because these are read once, soon.</summary>
        private readonly Queue<Key> m_Order = new Queue<Key>();

        private long m_CacheBytes;

        /// <summary>How many browser decodes are parked, for the diagnostics line.</summary>
        public int cachedImageCount
        {
            get { return m_Cache.Count; }
        }

        /// <summary>Bytes of decoded RGBA parked.</summary>
        public long cachedByteCount
        {
            get { return m_CacheBytes; }
        }

        /// <summary>Decodes that were answered from the browser pre-decode cache.</summary>
        public int cacheHitCount { get; private set; }

        /// <summary>Decodes that were answered by the managed PNG decoder.</summary>
        public int managedDecodeCount { get; private set; }

        /// <summary>Decodes that could not be answered at all.</summary>
        public int refusedDecodeCount { get; private set; }

        /// <summary>
        /// Why the last refusal happened, or null.
        /// </summary>
        /// <remarks>
        /// Kept because a refusal COUNT with no reason attached is a number nobody can act on - and because during
        /// this port's own bring-up a refusal appeared that no probe accounted for, which is exactly the situation
        /// a stored reason answers in one glance instead of by bisecting the page.
        /// </remarks>
        public string lastRefusal { get; private set; }

        /// <inheritdoc />
        public bool TryDecode(ReadOnlySpan<byte> encoded, out int width, out int height, out byte[] rgba32BottomUp,
                              out string error)
        {
            width = 0;
            height = 0;
            rgba32BottomUp = null;
            error = null;

            if (encoded.Length == 0)
            {
                error = "The image is empty.";
                Refuse(error);
                return false;
            }

            // 1. Exact, and from any source. A PNG decoded here never went through a canvas, so its alpha is the
            //    alpha the file carries rather than the alpha a premultiplied store could round-trip.
            if (NowWebPng.LooksLikePng(encoded))
            {
                string pngError;

                if (NowWebPng.TryDecode(encoded, out width, out height, out rgba32BottomUp, out pngError))
                {
                    ++managedDecodeCount;
                    return true;
                }

                // Not a hard failure yet: an interlaced or 16-bit-palette PNG that this decoder refuses may still
                // be in the cache, because the browser reads every PNG there is.
                error = pngError;
            }

            // 2. The pre-decode, keyed by the encoded bytes exactly as WebFetchProvider hashed them on the way to
            //    the sink.
            Key key = Key.Of(encoded);
            Entry entry;

            if (m_Cache.TryGetValue(key, out entry))
            {
                width = entry.width;
                height = entry.height;
                rgba32BottomUp = entry.rgba32BottomUp;
                error = null;
                ++cacheHitCount;
                return true;
            }

            if (error == null)
                error = Describe(encoded);

            Refuse(error);
            return false;
        }

        private void Refuse(string reason)
        {
            ++refusedDecodeCount;
            lastRefusal = reason;
        }

        /// <summary>
        /// PNG bytes for <paramref name="texture"/>. JPEG returns null.
        /// </summary>
        /// <remarks>
        /// The asymmetry is deliberate and is the same one the decoder has: PNG is a filter, an inflate and a CRC,
        /// all of which are here; JPEG is a DCT and an entropy coder, which are not, and a browser offers no
        /// synchronous encoder either (<c>canvas.toBlob</c> and <c>convertToBlob</c> are both asynchronous, and
        /// <c>toDataURL</c> is synchronous but hands back base64 text that would have to be decoded back to bytes -
        /// which is a JPEG encoder's output through two extra transcodes, not a JPEG encoder). Returning null is
        /// what the contract asks for when the codec is unsupported, and <c>ImageConversion.EncodeToJPG</c> passes
        /// it straight through.
        /// </remarks>
        public byte[] TryEncode(Texture2D texture, NowImageFormat format, int quality)
        {
            if (texture == null)
                return null;

            if (format != NowImageFormat.Png)
                return null;

            Color32[] pixels;

            try
            {
                // Bottom-up, row-major - the same order this file's decoder produces.
                pixels = texture.GetPixels32();
            }
            catch (Exception)
            {
                // A non-readable texture. Unity returns null from EncodeToPNG in the same situation.
                return null;
            }

            if (pixels == null || pixels.Length < texture.width * texture.height)
                return null;

            return NowWebPng.Encode(pixels, texture.width, texture.height);
        }

        /// <summary>
        /// Parks one browser decode against the encoded bytes it came from.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="WebFetchProvider"/> from inside the transfer, strictly BEFORE that transfer is
        /// reported complete - which is the ordering the whole design rests on, because completion is what the
        /// consumer polls before it calls <c>ImageConversion.LoadImage</c>.
        /// </remarks>
        internal void Register(ulong hashLow, ulong hashHigh, long length, int width, int height,
                               byte[] rgba32BottomUp, string url)
        {
            if (rgba32BottomUp == null || width <= 0 || height <= 0)
                return;

            var key = new Key(hashLow, hashHigh, length);

            if (m_Cache.ContainsKey(key))
                return;

            m_Cache.Add(key, new Entry(width, height, rgba32BottomUp, url));
            m_Order.Enqueue(key);
            m_CacheBytes += rgba32BottomUp.LongLength;

            while (m_CacheBytes > k_CacheByteBudget && m_Order.Count > 1)
            {
                Key oldest = m_Order.Dequeue();
                Entry evicted;

                if (m_Cache.TryGetValue(oldest, out evicted))
                {
                    m_CacheBytes -= evicted.rgba32BottomUp.LongLength;
                    m_Cache.Remove(oldest);
                }
            }
        }

        /// <summary>Turns top-down RGBA rows (what <c>getImageData</c> gives) into the bottom-up ones the contract wants.</summary>
        internal static byte[] FlipVertically(byte[] topDown, int width, int height)
        {
            int stride = width * 4;
            var flipped = new byte[stride * height];

            for (int y = 0; y < height; ++y)
                Buffer.BlockCopy(topDown, y * stride, flipped, (height - 1 - y) * stride, stride);

            return flipped;
        }

        /// <summary>Names what the bytes look like and which of the two decode paths they missed.</summary>
        private static string Describe(ReadOnlySpan<byte> encoded)
        {
            string format = SniffFormat(encoded);

            if (format == null)
            {
                return "These bytes are not an image this host recognises (" + encoded.Length +
                       " bytes, first four 0x" + Hex(encoded, 4) + ").";
            }

            if (format == "PNG")
            {
                return "This PNG could not be decoded by the managed decoder and was not pre-decoded by the " +
                       "browser, so it did not arrive through the fetch provider.";
            }

            return "This is a " + format + ", and this host has no managed " + format + " decoder. A " + format +
                   " decodes only when its bytes arrive through WebFetchProvider, which pre-decodes an image " +
                   "response with the browser's own codec before the transfer completes. Bytes from any other " +
                   "source - a local asset, a byte[] built in code - reach this call with nothing cached for them.";
        }

        private static string SniffFormat(ReadOnlySpan<byte> b)
        {
            if (NowWebPng.LooksLikePng(b))
                return "PNG";

            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return "JPEG";

            if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46)
                return "GIF";

            if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
                return "WebP";

            if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D)
                return "BMP";

            if (b.Length >= 5 && b[0] == (byte)'<' &&
                (b[1] == (byte)'s' || b[1] == (byte)'?' || b[1] == (byte)'!'))
                return "SVG or XML";

            return null;
        }

        private static string Hex(ReadOnlySpan<byte> bytes, int count)
        {
            int n = Math.Min(count, bytes.Length);
            var text = new System.Text.StringBuilder(n * 2);

            for (int i = 0; i < n; ++i)
                text.Append(bytes[i].ToString("x2"));

            return text.ToString();
        }

        /// <summary>One parked decode.</summary>
        private readonly struct Entry
        {
            internal Entry(int width, int height, byte[] rgba32BottomUp, string url)
            {
                this.width = width;
                this.height = height;
                this.rgba32BottomUp = rgba32BottomUp;
                this.url = url;
            }

            internal readonly int width;
            internal readonly int height;
            internal readonly byte[] rgba32BottomUp;

            /// <summary>Where it came from. Diagnostic only; the key is the content, never the URL.</summary>
            internal readonly string url;
        }

        /// <summary>
        /// The cache key: the exact byte length plus two independent 64-bit hashes of the content.
        /// </summary>
        /// <remarks>
        /// Content-addressed rather than URL-addressed on purpose. The consumer that asks for a decode
        /// (<c>ImageConversion.LoadImage</c>) is handed BYTES and knows nothing about where they came from, so a
        /// URL key could not be looked up from inside <c>TryDecode</c> at all. It also means the same image fetched
        /// from two URLs decodes once, and a URL whose content changed does not serve a stale decode.
        /// <para>The two hashes must be computed the same way on both sides; <c>WebFetchProvider.Handle.Absorb</c>
        /// is the streaming half of exactly this function.</para>
        /// </remarks>
        private readonly struct Key : IEquatable<Key>
        {
            private const ulong k_FnvOffsetA = 14695981039346656037UL;
            private const ulong k_FnvOffsetB = 0x9E3779B97F4A7C15UL;
            private const ulong k_FnvPrimeA = 0x100000001B3UL;
            private const ulong k_FnvPrimeB = 0x880355F21E6D1965UL;

            internal Key(ulong low, ulong high, long length)
            {
                m_Low = low;
                m_High = high;
                m_Length = length;
            }

            private readonly ulong m_Low;
            private readonly ulong m_High;
            private readonly long m_Length;

            internal static Key Of(ReadOnlySpan<byte> bytes)
            {
                ulong a = k_FnvOffsetA;
                ulong b = k_FnvOffsetB;

                for (int i = 0; i < bytes.Length; ++i)
                {
                    byte value = bytes[i];
                    a = (a ^ value) * k_FnvPrimeA;
                    b = (b ^ value) * k_FnvPrimeB;
                }

                return new Key(a, b, bytes.Length);
            }

            public bool Equals(Key other)
            {
                return m_Low == other.m_Low && m_High == other.m_High && m_Length == other.m_Length;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (int)(m_Low ^ (m_Low >> 32) ^ m_High);
            }
        }
    }
}
