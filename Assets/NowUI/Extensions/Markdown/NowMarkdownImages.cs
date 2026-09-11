using System;
using System.Collections.Generic;
using NowUI.Internal;
using UnityEngine;

namespace NowUI.Markdown
{
    public enum NowMarkdownImageState
    {
        Loading,
        Loaded,
        Failed
    }

    /// <summary>
    /// Bounded async cache for markdown images. Remote downloads are queued and
    /// documents poll the state while laying out. Non-http paths load from Resources.
    /// Downloaded textures are owned by this cache; injected and Resources textures
    /// remain caller/Unity owned.
    /// </summary>
    public static partial class NowMarkdownImages
    {
        sealed partial class Entry
        {
            public string url;
            public NowMarkdownImageState state;
            public Texture2D texture;
            public bool owned;
            public bool active;
            public bool completed;
            public long lastAccess;
            public long downloadedBytes;
            public int redirects;
            public string forcedError;
            public Uri currentUri;
            public LinkedListNode<Entry> pendingNode;
        }

        static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(16);

        static readonly LinkedList<Entry> _pending = new LinkedList<Entry>();

        static readonly List<Entry> _active = new List<Entry>(4);

        static int _version;

        static long _accessClock;

        /// <summary>Timeout applied to remote image requests, in seconds.</summary>
        public static int requestTimeoutSeconds = 30;

        /// <summary>Maximum encoded bytes accepted from one remote image (64 MiB).</summary>
        public static long maxDownloadBytes = 64L * 1024L * 1024L;

        /// <summary>Maximum width or height accepted for a decoded remote image.</summary>
        public static int maxTextureDimension = 16384;

        /// <summary>Maximum decoded pixels accepted for one remote image.</summary>
        public static long maxTexturePixels = 100L * 1024L * 1024L;

        /// <summary>
        /// Whether decoded images are given a mipmap chain. On by default, because these pictures are drawn at
        /// whatever size the layout gives them and that is usually smaller than the source.
        /// </summary>
        /// <remarks>
        /// <para>WHAT IT BUYS. Without a chain a minified picture samples one arbitrary texel per pixel, so fine
        /// detail turns into moire that also crawls whenever the box resizes. With one, the same picture resolves
        /// to a correctly averaged colour. The web surface made this the common case rather than the exceptional
        /// one: <c>ui.image</c> defaults to <c>fit: 'contain'</c>, and both contain and cover exist precisely to
        /// draw a source into a box that is not its own size.</para>
        /// <para>WHAT IT COSTS. About a third more memory per image, and one mip pyramid build on the frame the
        /// download lands. In the browser that third is paid twice over: the GPU holds the chain, and the
        /// engine-free Texture2D also sizes its CPU store for every level (Texture2D.ChainByteSize) and keeps it
        /// for context-loss recovery, even though only level 0 is ever written there - the rest are generated on
        /// the GPU. <see cref="maxCachedTexturePixels"/> counts the whole chain, so the budget stays honest
        /// rather than quietly holding a third more than it was told to.</para>
        /// <para>Turn it off for a document whose images are all drawn at their natural size, where the chain is
        /// memory spent on levels nothing will ever sample.</para>
        /// </remarks>
        public static bool generateMipmaps = true;

        /// <summary>Maximum remote image requests in flight at once.</summary>
        public static int maxConcurrentDownloads = 4;

        /// <summary>Maximum URL/resource entries retained by the image cache.</summary>
        public static int maxCacheEntries = 128;

        /// <summary>
        /// Maximum decoded pixels referenced by the cache before least-recently-used
        /// entries are evicted. This includes injected and Resources textures.
        /// </summary>
        public static long maxCachedTexturePixels = 128L * 1024L * 1024L;

        /// <summary>Maximum redirects followed by remote image requests.</summary>
        public static int maxRedirects = 8;

        /// <summary>
        /// Whether plain HTTP image URLs are allowed. This defaults to true for
        /// backwards compatibility; untrusted-content applications should prefer HTTPS.
        /// </summary>
        public static bool allowInsecureHttp = true;

        /// <summary>
        /// Optional application URL policy. Return false to reject a remote URL before
        /// a request starts (for example, to allow-list hosts). This is not a DNS or
        /// network sandbox and is invoked on the calling thread.
        /// </summary>
        public static Func<Uri, bool> remoteUrlPolicy;

        /// <summary>Bumps whenever a cached image settles or is removed.</summary>
        public static int version => _version;

        /// <summary>Current number of queued, loaded, or failed cache entries.</summary>
        public static int cachedEntryCount => _entries.Count;

        /// <summary>Current number of remote requests in flight.</summary>
        public static int activeDownloadCount => _active.Count;

        public static NowMarkdownImageState GetState(string url, out Texture2D texture)
        {
            texture = null;
            TrimCache();

            if (string.IsNullOrEmpty(url))
                return NowMarkdownImageState.Failed;

            if (_entries.TryGetValue(url, out var entry))
            {
                Touch(entry);
                TrimCache(entry);
                texture = entry.texture;
                return entry.state;
            }

            bool remote = IsHttpUrl(url);

            if (!remote)
            {
                var resource = Resources.Load<Texture2D>(url);
                entry = new Entry
                {
                    url = url,
                    texture = resource,
                    state = resource != null ? NowMarkdownImageState.Loaded : NowMarkdownImageState.Failed
                };
                Touch(entry);
                _entries[url] = entry;
                ++_version;
                TrimCache(entry);
                texture = resource;
                return entry.state;
            }

            if (!TryValidateRemoteUrl(url, out var remoteUri, out _))
                return NowMarkdownImageState.Failed;

            entry = new Entry
            {
                url = url,
                state = NowMarkdownImageState.Loading,
                currentUri = remoteUri
            };
            Touch(entry);
            _entries[url] = entry;
            entry.pendingNode = _pending.AddLast(entry);
            TrimCache(entry);
            PumpDownloads();
            return entry.state;
        }

        /// <summary>Injects a texture for a URL without downloading (tests, local art).</summary>
        public static void SetTexture(string url, Texture2D texture)
        {
            if (url == null)
                throw new ArgumentNullException(nameof(url));

            if (_entries.TryGetValue(url, out var previous))
                RemoveEntry(previous, false);

            var entry = new Entry
            {
                url = url,
                state = texture != null ? NowMarkdownImageState.Loaded : NowMarkdownImageState.Failed,
                texture = texture
            };
            Touch(entry);
            _entries[url] = entry;
            ++_version;
            TrimCache(entry);
            PumpDownloads();
        }

        /// <summary>
        /// Aborts queued and active downloads, destroys downloaded textures, and clears
        /// the cache. Configuration fields are left unchanged.
        /// </summary>
        public static void Reset()
        {
            foreach (var entry in _entries.Values)
                CancelEntry(entry);

            _entries.Clear();
            _pending.Clear();
            _active.Clear();
            _accessClock = 0L;

            DestroyRunner();

            ++_version;
        }

        static void Tick()
        {
            PollDownloads();
            TrimCache();
            PumpDownloads();
        }

        static void PumpDownloads()
        {
            int concurrency = Mathf.Max(1, maxConcurrentDownloads);

            while (_active.Count < concurrency && _pending.First != null)
            {
                var node = _pending.First;
                _pending.RemoveFirst();
                var entry = node.Value;
                entry.pendingNode = null;

                if (!_entries.TryGetValue(entry.url, out var current) ||
                    !ReferenceEquals(current, entry) ||
                    entry.state != NowMarkdownImageState.Loading)
                {
                    continue;
                }

                StartDownload(entry);
            }
        }

        /// <summary>
        /// Applies the completion policy shared by both transports once a request has finished and its response has
        /// been reduced to plain values: the redirect hop, the transport error, the decode, and the resulting cache
        /// state. <paramref name="error"/> carries the error already decided before the redirect is considered (the
        /// forced error and the byte-cap breach); <paramref name="requestError"/> carries the transport's own failure,
        /// which is applied only after the redirect hop, exactly as it was when this ran inside CompleteDownload.
        /// </summary>
        static void FinishDownload(
            Entry entry,
            byte[] bytes,
            long status,
            string location,
            string error,
            string requestError)
        {
            Texture2D decoded = null;

            if (error == null && IsRedirectStatus(status))
            {
                int redirectLimit = Mathf.Max(0, maxRedirects);

                if (entry.redirects >= redirectLimit)
                {
                    error = $"The image request exceeded the configured redirect limit of {redirectLimit}.";
                }
                else if (!TryResolveRedirect(
                    entry.currentUri,
                    location,
                    out var redirectUri,
                    out error))
                {
                    error = $"The image redirect was refused: {error}";
                }
                else if (!TryValidateRemoteUrl(redirectUri.AbsoluteUri, out var validatedUri, out error))
                {
                    error = $"The image redirect was refused: {error}";
                }
                else
                {
                    entry.currentUri = validatedUri;
                    ++entry.redirects;
                    entry.completed = false;
                    entry.forcedError = null;
                    entry.pendingNode = _pending.AddFirst(entry);
                    PumpDownloads();
                    return;
                }
            }

            if (error == null && requestError != null)
                error = requestError;

            if (error == null)
            {
                if (bytes == null)
                {
                    error = "The image response contained no data.";
                }
                else if (!TryDecodeDownloadedTexture(bytes, entry.url, out decoded, out error))
                {
                    decoded = null;
                }
            }

            if (decoded != null)
            {
                entry.texture = decoded;
                entry.owned = true;
                entry.state = NowMarkdownImageState.Loaded;
            }
            else
            {
                entry.state = NowMarkdownImageState.Failed;
            }

            ++_version;
            Touch(entry);
            TrimCache(entry);
            PumpDownloads();
        }

        internal static bool TryDecodeDownloadedTexture(
            byte[] data,
            string name,
            out Texture2D texture,
            out string error)
        {
            texture = null;

            if (data == null || data.LongLength > EffectiveLimit(maxDownloadBytes))
            {
                error = "Encoded image data is empty or exceeds the configured download limit.";
                return false;
            }

            if (TryGetEncodedDimensions(data, out int encodedWidth, out int encodedHeight) &&
                !AreDimensionsWithinLimits(encodedWidth, encodedHeight, out error))
            {
                return false;
            }

            // 2x2 is a placeholder: LoadImage re-creates the texture at the decoded image's size. The mip flag
            // is the one thing it CARRIES ACROSS that re-creation, which is why it has to be decided here rather
            // than after the bytes have landed. NowImageMipmapContractTests pins that behaviour against real
            // Unity, and Standalone/NowUI.Engine/Graphics/ImageConversion.cs holds the browser shim to it.
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, generateMipmaps)
            {
                name = string.IsNullOrEmpty(name) ? "Markdown Image" : name,
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                if (!decoded.LoadImage(data, markNonReadable: true))
                {
                    error = "Unity could not decode the downloaded image.";
                    DestroyTexture(decoded);
                    return false;
                }

                // Set after the decode, not in the initialiser above, because these two only mean anything once
                // there is a real image: they exist to serve the mip chain, and until LoadImage succeeds there is
                // no chain and no picture. Either order works - the upload carries filter and wrap alongside the
                // pixels (WebGL2Backend.UploadTexture2D packs them at m_TextureInfo[6..8]) and a later assignment
                // pushes them again through UpdateSampler - so this is about keeping the mip decisions together
                // under one guard rather than about correctness.
                if (generateMipmaps)
                {
                    // Trilinear rather than the Bilinear default, because bilinear-with-mips picks the nearest
                    // level and switches between levels abruptly. In an immediate-mode UI the drawn size changes
                    // every frame while a window is dragged, so that reads as the picture popping between two
                    // sharpnesses - trading a shimmer for a flicker. Trilinear blends the two levels instead.
                    decoded.filterMode = FilterMode.Trilinear;

                    // Clamp rather than the Repeat default. Repeat is invisible while only level 0 is sampled at
                    // exactly [0,1], but a coarse mip level averages across a wide footprint, and at the edge of
                    // the picture that footprint wraps and pulls in the opposite edge - a thin band of the wrong
                    // colour down one side. ui.image's `cover` samples a sub-rect, which makes it likelier still.
                    decoded.wrapMode = TextureWrapMode.Clamp;
                }

                if (!AreDimensionsWithinLimits(decoded.width, decoded.height, out error))
                {
                    DestroyTexture(decoded);
                    return false;
                }

                texture = decoded;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Unity could not decode the downloaded image: {exception.Message}";
                DestroyTexture(decoded);
                return false;
            }
        }

        internal static bool AreDimensionsWithinLimits(int width, int height, out string error)
        {
            int dimensionLimit = Mathf.Max(1, maxTextureDimension);
            long pixelLimit = EffectiveLimit(maxTexturePixels);
            long pixels = (long)width * height;

            if (width < 1 || height < 1 || width > dimensionLimit || height > dimensionLimit)
            {
                error =
                    $"Decoded image dimensions {width}x{height} exceed the configured {dimensionLimit}-pixel dimension limit.";
                return false;
            }

            if (pixels > pixelLimit)
            {
                error =
                    $"Decoded image dimensions {width}x{height} exceed the configured {pixelLimit}-pixel limit.";
                return false;
            }

            error = null;
            return true;
        }

        static bool TryGetEncodedDimensions(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;

            // PNG IHDR dimensions are fixed-position and big-endian.
            if (data.Length >= 24 &&
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47 &&
                data[12] == 0x49 && data[13] == 0x48 && data[14] == 0x44 && data[15] == 0x52)
            {
                width = ReadInt32BigEndian(data, 16);
                height = ReadInt32BigEndian(data, 20);
                return true;
            }

            // GIF logical screen dimensions are little-endian.
            if (data.Length >= 10 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            {
                width = data[6] | data[7] << 8;
                height = data[8] | data[9] << 8;
                return true;
            }

            // BMP dimensions are signed little-endian (negative height means top-down).
            if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
            {
                width = AbsoluteDimension(ReadInt32LittleEndian(data, 18));
                height = AbsoluteDimension(ReadInt32LittleEndian(data, 22));
                return true;
            }

            // JPEG dimensions live in one of several start-of-frame segments.
            if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8)
            {
                int position = 2;

                while (position + 8 < data.Length)
                {
                    if (data[position++] != 0xff)
                        continue;

                    while (position < data.Length && data[position] == 0xff)
                        ++position;

                    if (position >= data.Length)
                        break;

                    byte marker = data[position++];

                    if (marker == 0xd8 || marker == 0x01 || (marker >= 0xd0 && marker <= 0xd9))
                        continue;

                    if (position + 1 >= data.Length)
                        break;

                    int segmentLength = data[position] << 8 | data[position + 1];

                    if (segmentLength < 2 || position + segmentLength > data.Length)
                        break;

                    bool startOfFrame =
                        (marker >= 0xc0 && marker <= 0xc3) ||
                        (marker >= 0xc5 && marker <= 0xc7) ||
                        (marker >= 0xc9 && marker <= 0xcb) ||
                        (marker >= 0xcd && marker <= 0xcf);

                    if (startOfFrame && segmentLength >= 7)
                    {
                        height = data[position + 3] << 8 | data[position + 4];
                        width = data[position + 5] << 8 | data[position + 6];
                        return true;
                    }

                    position += segmentLength;
                }
            }

            return false;
        }

        static bool TryValidateRemoteUrl(string url, out Uri uri, out string error)
        {
            uri = null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "Only absolute http and https markdown image URLs are supported.";
                return false;
            }

            if (!allowInsecureHttp && parsed.Scheme == Uri.UriSchemeHttp)
            {
                error = "Plain HTTP markdown image URLs are disabled by NowMarkdownImages.allowInsecureHttp.";
                return false;
            }

            var policy = remoteUrlPolicy;

            if (policy != null)
            {
                bool allowed;

                try
                {
                    allowed = policy(parsed);
                }
                catch (Exception exception)
                {
                    error = $"The markdown image URL policy threw an exception: {exception.Message}";
                    return false;
                }

                if (!allowed)
                {
                    error = "The markdown image URL was rejected by NowMarkdownImages.remoteUrlPolicy.";
                    return false;
                }
            }

            uri = parsed;
            error = null;
            return true;
        }

        static bool IsHttpUrl(string url)
        {
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsRedirectStatus(long responseCode)
        {
            return responseCode == 301L ||
                responseCode == 302L ||
                responseCode == 303L ||
                responseCode == 307L ||
                responseCode == 308L;
        }

        static bool TryResolveRedirect(Uri currentUri, string location, out Uri redirectUri, out string error)
        {
            redirectUri = null;

            if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(currentUri, location, out var resolved))
            {
                error = "The server returned a redirect without a valid Location URL.";
                return false;
            }

            redirectUri = resolved;
            error = null;
            return true;
        }

        static void TrimCache(Entry preserve = null)
        {
            int entryLimit = Mathf.Max(1, maxCacheEntries);
            long pixelLimit = EffectiveLimit(maxCachedTexturePixels);

            while (_entries.Count > entryLimit || CachedPixels() > pixelLimit)
            {
                Entry oldest = null;

                foreach (var candidate in _entries.Values)
                {
                    if (ReferenceEquals(candidate, preserve))
                        continue;

                    if (oldest == null || candidate.lastAccess < oldest.lastAccess)
                        oldest = candidate;
                }

                if (oldest == null)
                    break;

                RemoveEntry(oldest, true);
            }
        }

        static long CachedPixels()
        {
            long total = 0L;

            foreach (var entry in _entries.Values)
            {
                if (entry.texture == null)
                    continue;

                long pixels = ResidentPixels(entry.texture);

                if (pixels > long.MaxValue - total)
                    return long.MaxValue;

                total += pixels;
            }

            return total;
        }

        /// <summary>
        /// Every pixel a texture actually holds, mip levels included.
        /// </summary>
        /// <remarks>
        /// <para>The budget this feeds is a MEMORY guard, so it has to count the memory. Counting only the base
        /// level was correct while nothing here had a mip chain; with <see cref="generateMipmaps"/> on it would
        /// under-report by a third, and a caller who sized <see cref="maxCachedTexturePixels"/> to fit a real
        /// budget would quietly overshoot it - the exact failure the setting exists to prevent.</para>
        /// <para>Summed level by level rather than multiplied by 4/3, because the geometric series only reaches
        /// 4/3 in the limit: a chain is truncated at 1x1 and each level rounds up, so small and non-square
        /// textures diverge from the ratio noticeably. The shape is <c>NowFont.GetRgbaTexturePayloadBytes</c>'s,
        /// in pixels rather than bytes.</para>
        /// </remarks>
        static long ResidentPixels(Texture2D texture)
        {
            long total = 0L;
            int width = Mathf.Max(1, texture.width);
            int height = Mathf.Max(1, texture.height);
            int levels = Mathf.Max(1, texture.mipmapCount);

            for (int mip = 0; mip < levels; ++mip)
            {
                total += (long)width * height;

                if (width == 1 && height == 1)
                    break;

                width = Mathf.Max(1, width >> 1);
                height = Mathf.Max(1, height >> 1);
            }

            return total;
        }

        static void RemoveEntry(Entry entry, bool bumpVersion)
        {
            if (entry == null)
                return;

            if (_entries.TryGetValue(entry.url, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.url);

            CancelEntry(entry);

            if (bumpVersion)
                ++_version;
        }

        static void CancelEntry(Entry entry)
        {
            if (entry.pendingNode != null)
            {
                _pending.Remove(entry.pendingNode);
                entry.pendingNode = null;
            }

            entry.completed = true;
            entry.active = false;
            _active.Remove(entry);

            AbortRequest(entry);

            if (entry.texture != null && entry.owned)
                DestroyTexture(entry.texture);

            entry.texture = null;
            entry.owned = false;
        }

        static void Touch(Entry entry)
        {
            if (_accessClock == long.MaxValue)
            {
                _accessClock = 0L;

                foreach (var value in _entries.Values)
                    value.lastAccess = 0L;
            }

            entry.lastAccess = ++_accessClock;
        }

        static int ReadInt32BigEndian(byte[] bytes, int offset)
        {
            return bytes[offset] << 24 |
                bytes[offset + 1] << 16 |
                bytes[offset + 2] << 8 |
                bytes[offset + 3];
        }

        static int ReadInt32LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset] |
                bytes[offset + 1] << 8 |
                bytes[offset + 2] << 16 |
                bytes[offset + 3] << 24;
        }

        static int AbsoluteDimension(int value)
        {
            return value == int.MinValue ? int.MaxValue : Math.Abs(value);
        }

        static long EffectiveLimit(long configuredLimit)
        {
            return Math.Max(1L, configuredLimit);
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForRuntimeLoad()
        {
            Reset();
        }
    }
}
