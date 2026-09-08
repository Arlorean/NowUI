// INowFetchProvider over the browser's fetch() + ReadableStream, and the managed half of the image pre-decode.
//
// INowFetch.cs' own header says the contract "maps 1:1 onto fetch() + ReadableStream in a browser, and preserves
// the byte-cap-during-transfer contract": the sink sees each chunk as it arrives and aborts mid-transfer by
// returning false. That is what this does, chunk by chunk, with no whole-body buffer anywhere on the managed side.
// The body is never assembled here - assembling it is the SINK's business (NowMarkdownFetchSink writes into a
// MemoryStream and refuses at the cap), and doing it here as well would defeat the cap this exists to honour.
//
// Two things about this port are worth reading before the code:
//
//   THE DECODE ORDERING. INowImageDecoder.TryDecode is synchronous; createImageBitmap is not. The resolution
//   (which StandaloneCoreDesign.md §1.2 already anticipated - "image decode stays synchronous in M1 because the
//   browser fetch provider pre-decodes") is that an image response is decoded BEFORE its transfer is reported
//   complete. nowui-fetch.js awaits the decode, calls OnImage, and only then calls OnComplete; OnComplete is what
//   flips handle.isDone, and isDone is what NowMarkdownImages polls before it calls ImageConversion.LoadImage. So
//   by the time anything asks TryDecode for pixels, the pixels are in WebImageDecoder's cache. Nothing blocks and
//   nothing spins.
//
//   THE CACHE KEY. The decoded pixels are keyed by a 128-bit hash of the ENCODED bytes, computed here as the
//   chunks stream past on their way to the sink - so the key is available without ever holding the body. TryDecode
//   hashes the span it is handed with the same function and looks it up. The consumer hands LoadImage exactly the
//   bytes the sink accumulated, which are exactly the bytes hashed here, so the lookup hits.
//
// WHAT A BROWSER CANNOT DO, stated rather than papered over:
//
//   Redirects. NowUI sets followRedirects:false because it applies its OWN per-hop URL policy to each Location.
//   A browser will not show script a redirect: fetch(redirect:'manual') returns an opaque-redirect response with
//   status 0, no headers and no Location. So the honest answer to a redirect is a protocol error naming the cause,
//   which is what nowui-fetch.js sends. `?redirects=follow` opts into letting the BROWSER follow hops instead,
//   which trades NowUI's per-hop policy for reachability and is off by default for that reason.
//
//   Cross-origin. A response the CORS rules do not permit is not merely unreadable, it is indistinguishable: the
//   Fetch standard requires one opaque "Failed to fetch" TypeError for a CORS refusal, a DNS failure, a refused
//   connection and blocked mixed content alike. Reported as ConnectionError with the browser's own message and a
//   note saying the four cannot be told apart. And on a cross-origin response that CORS DOES permit, only the
//   safelisted response headers are visible unless the server sends Access-Control-Expose-Headers, so a sink that
//   wants Content-Length may not get one.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.4; Docs/Standalone/M2-FeatureMatrix.md §8.1.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using NowUI.Engine;

namespace NowUI.Web
{
    /// <summary>How the transport is asked to treat a 3xx.</summary>
    public enum NowWebRedirectMode
    {
        /// <summary>
        /// <c>fetch(redirect:'manual')</c>. Honours <c>followRedirects:false</c> exactly - the transport follows
        /// nothing - at the cost that the browser also hides the Location, so a redirect cannot be followed by the
        /// caller either. The default, because silently following a hop the caller asked to inspect is worse than
        /// failing at it.
        /// </summary>
        Manual = 0,

        /// <summary>
        /// <c>fetch(redirect:'follow')</c>. The browser follows the hops. NowUI's per-hop URL policy never sees
        /// them, which is a real weakening and is why this is opt-in (<c>?redirects=follow</c>).
        /// </summary>
        Follow = 1,

        /// <summary><c>fetch(redirect:'error')</c>. Any 3xx becomes a network error. For a caller that wants no hops at all.</summary>
        Error = 2,
    }

    /// <summary>
    /// The JS-to-managed half of the bridge: the callbacks <c>nowui-fetch.js</c> raises as a transfer progresses.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="WebFetchProvider"/> only because <c>[JSExport]</c> needs a partial class and a
    /// stable name that <c>main.js</c> can reach as <c>exports.NowUI.Web.WebFetchBridge</c>. Every method forwards
    /// to the single provider instance and swallows nothing: an exception thrown out of a <c>[JSExport]</c> lands
    /// in the middle of the reader loop in JS, so each one is caught, logged and turned into a value the loop can
    /// carry on with.
    /// <para>Not one byte crosses as an argument. JS parks a chunk and reports its LENGTH; this side allocates and
    /// calls back into <c>readChunk</c> with a <c>Span&lt;byte&gt;</c> over its own buffer, which arrives in JS as
    /// a .NET MemoryView and is written into. That is the same marshalling shape <c>WebGL2Backend</c> already uses
    /// for every texture and mesh upload, and it needs no assumption about how a managed array marshals.</para>
    /// </remarks>
    internal static partial class WebFetchBridge
    {
        /// <summary>The response line and headers arrived. <paramref name="statusCode"/> is 0 for an opaque response.</summary>
        [JSExport]
        internal static void OnResponse(int id, double statusCode, string headerText, string finalUrl, string responseType)
        {
            try
            {
                WebFetchProvider.current?.HandleResponse(id, (long)statusCode, headerText, finalUrl, responseType);
            }
            catch (Exception e)
            {
                Report("OnResponse", e);
            }
        }

        /// <summary>One chunk is parked in JS. Returns false to abort the transfer, which is the byte cap's hook.</summary>
        [JSExport]
        internal static bool OnData(int id, int byteCount)
        {
            try
            {
                WebFetchProvider provider = WebFetchProvider.current;
                return provider != null && provider.HandleData(id, byteCount);
            }
            catch (Exception e)
            {
                Report("OnData", e);

                // False stops the transfer. A sink that threw is a sink that cannot be fed, and carrying on would
                // deliver bytes nobody is accumulating.
                return false;
            }
        }

        /// <summary>The browser decoded the response as an image; the RGBA is parked in JS. Always precedes OnComplete.</summary>
        [JSExport]
        internal static void OnImage(int id, int width, int height, int byteCount)
        {
            try
            {
                WebFetchProvider.current?.HandleImage(id, width, height, byteCount);
            }
            catch (Exception e)
            {
                Report("OnImage", e);
            }
        }

        /// <summary>The browser could not decode the response as an image. Not a transfer failure; the bytes still arrived.</summary>
        [JSExport]
        internal static void OnImageFailed(int id, string error)
        {
            try
            {
                WebFetchProvider.current?.HandleImageFailed(id, error);
            }
            catch (Exception e)
            {
                Report("OnImageFailed", e);
            }
        }

        /// <summary>The transfer ended. <paramref name="outcome"/> is a <see cref="NowFetchOutcome"/>.</summary>
        [JSExport]
        internal static void OnComplete(int id, int outcome, string error)
        {
            try
            {
                WebFetchProvider.current?.HandleComplete(id, outcome, error);
            }
            catch (Exception e)
            {
                Report("OnComplete", e);
            }
        }

        private static void Report(string where, Exception e)
        {
            BrowserInterop.Log(2, "[NowUI] fetch bridge " + where + " threw." + Environment.NewLine + e);
        }
    }

    /// <summary>Starts browser fetches and streams their bodies into NowUI's sinks.</summary>
    public sealed partial class WebFetchProvider : INowFetchProvider
    {
        /// <summary>
        /// The instance the JS callbacks reach. One per page: the ids are minted here and the bridge is a module.
        /// </summary>
        internal static WebFetchProvider current { get; private set; }

        private readonly Dictionary<int, Handle> m_Live = new Dictionary<int, Handle>();
        private readonly WebImageDecoder m_Decoder;
        private readonly NowWebRedirectMode m_Redirects;

        private int m_NextId = 1;

        private WebFetchProvider(WebImageDecoder decoder, NowWebRedirectMode redirects)
        {
            m_Decoder = decoder;
            m_Redirects = redirects;
        }

        /// <summary>How this provider was asked to treat redirects.</summary>
        public NowWebRedirectMode redirects
        {
            get { return m_Redirects; }
        }

        /// <summary>Transfers started since the page loaded, for the diagnostics line.</summary>
        public int startedCount { get; private set; }

        /// <summary>Bytes handed to sinks since the page loaded.</summary>
        public long deliveredByteCount { get; private set; }

        /// <summary>
        /// Builds the provider, or returns null with a reason when the page's bridge is not wired up.
        /// </summary>
        /// <remarks>
        /// The probe is the point. <c>main.js</c> owns both directions of this bridge, so a page served with a
        /// stale cached <c>main.js</c> - which M2-Scouting.md records as a thing that genuinely happens, since the
        /// loader caches per URL and survives cache clearing - would otherwise install a provider whose requests
        /// never call back and whose downloads never finish. One named error at start-up is better than a hang.
        /// </remarks>
        public static WebFetchProvider TryCreate(WebImageDecoder decoder, NowWebRedirectMode redirects, out string error)
        {
            if (decoder == null)
                throw new ArgumentNullException(nameof(decoder));

            try
            {
                error = Interop.Probe();
            }
            catch (Exception e)
            {
                error = "the nowui-fetch.js module is not registered (" + e.Message + ").";
                return null;
            }

            if (!string.IsNullOrEmpty(error))
                return null;

            var provider = new WebFetchProvider(decoder, redirects);
            current = provider;
            return provider;
        }

        /// <inheritdoc />
        public INowFetchHandle Start(in NowFetchRequest request, INowFetchSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            if (string.IsNullOrEmpty(request.url))
                throw new ArgumentException("A URL is required.", nameof(request));

            int id = m_NextId++;
            var handle = new Handle(this, id, sink);
            m_Live.Add(id, handle);
            ++startedCount;

            // followRedirects is false for every request NowUI's core makes, and the page-level mode decides what
            // the transport does with that (see NowWebRedirectMode). A caller that DID ask the transport to follow
            // is honoured directly.
            NowWebRedirectMode mode = request.followRedirects ? NowWebRedirectMode.Follow : m_Redirects;

            try
            {
                Interop.Start(
                    id,
                    request.url,
                    string.IsNullOrEmpty(request.method) ? "GET" : request.method,
                    FormatHeaders(request.headers),
                    request.timeoutSeconds > 0 ? request.timeoutSeconds : 0,
                    (int)mode);
            }
            catch (Exception e)
            {
                // A synchronous failure to even start. Report it through the sink so the caller's one error path
                // covers it, and hand back a handle that is already done.
                m_Live.Remove(id);
                handle.Settle(NowFetchOutcome.ConnectionError, "The request could not be started: " + e.Message);
                return handle;
            }

            return handle;
        }

        // ------------------------------------------------------------------------------ the bridge's callbacks

        internal void HandleResponse(int id, long statusCode, string headerText, string finalUrl, string responseType)
        {
            Handle handle;
            if (!m_Live.TryGetValue(id, out handle))
                return;

            Dictionary<string, string> headers = ParseHeaders(headerText);
            handle.finalUrl = finalUrl;
            handle.responseType = responseType;
            handle.sink.OnResponse(statusCode, headers);

            // Content-Length is the cheap half of a byte cap - it lets a sink refuse before a byte is read - so it
            // is forwarded when the server sent one AND the CORS rules let us see it. It is deliberately NOT
            // synthesised when absent: a chunked, compressed or cross-origin response has no trustworthy length,
            // and an invented one would be worse than none.
            string contentLength;
            if (headers != null && headers.TryGetValue("content-length", out contentLength))
            {
                ulong declared;
                if (ulong.TryParse(contentLength, out declared))
                    handle.sink.OnContentLength(declared);
            }
        }

        internal bool HandleData(int id, int byteCount)
        {
            Handle handle;
            if (!m_Live.TryGetValue(id, out handle))
                return false;

            if (byteCount <= 0)
                return true;

            byte[] buffer = handle.Rent(byteCount);
            Interop.ReadChunk(id, new Span<byte>(buffer, 0, byteCount));

            // Hashed on the way past, so the pre-decode has a key without anyone holding the body.
            handle.Absorb(buffer, byteCount);
            deliveredByteCount += byteCount;

            return handle.sink.OnData(buffer, byteCount);
        }

        internal void HandleImage(int id, int width, int height, int byteCount)
        {
            Handle handle;
            if (!m_Live.TryGetValue(id, out handle))
                return;

            long expected = (long)width * height * 4;

            if (width <= 0 || height <= 0 || byteCount < expected)
            {
                BrowserInterop.Log(1, "[NowUI] pre-decoded image for '" + handle.finalUrl + "' is " + width + "x" +
                    height + " but carries " + byteCount + " bytes; ignored.");
                return;
            }

            byte[] topDown = new byte[expected];
            Interop.ReadImage(id, new Span<byte>(topDown, 0, (int)expected));

            // getImageData is top-down; INowImageDecoder.TryDecode is bottom-up (SetPixels32's order). Flipped
            // here rather than in JS so the row order lives next to the contract that states it.
            m_Decoder.Register(handle.hashLow, handle.hashHigh, handle.byteCount, width, height,
                WebImageDecoder.FlipVertically(topDown, width, height), handle.finalUrl);
        }

        internal void HandleImageFailed(int id, string error)
        {
            Handle handle;
            if (!m_Live.TryGetValue(id, out handle))
                return;

            // A warning, not an error: the bytes arrived and the managed PNG decoder may still be able to read
            // them. Only a TryDecode that then also fails is a failure the user sees.
            BrowserInterop.Log(1, "[NowUI] the browser could not pre-decode '" + handle.finalUrl + "' as an image: " +
                (error ?? "no reason given") + ". TryDecode will fall back to the managed PNG decoder.");
        }

        internal void HandleComplete(int id, int outcome, string error)
        {
            Handle handle;
            if (!m_Live.TryGetValue(id, out handle))
                return;

            m_Live.Remove(id);
            handle.Settle((NowFetchOutcome)outcome, string.IsNullOrEmpty(error) ? null : error);
        }

        internal void Forget(int id)
        {
            m_Live.Remove(id);
        }

        // ---------------------------------------------------------------------------------------------- headers

        /// <summary>
        /// Headers as "Name: value" lines.
        /// </summary>
        /// <remarks>
        /// Newline-delimited rather than JSON, because a header VALUE cannot contain a newline (RFC 9110 forbids
        /// it and every HTTP stack strips it), so this needs no escaping and no parser - where JSON would have
        /// pulled a serializer into a payload that publishes trimmed.
        /// </remarks>
        private static string FormatHeaders(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null || headers.Count == 0)
                return "";

            var builder = new System.Text.StringBuilder();

            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.IsNullOrEmpty(header.Key))
                    continue;

                if (builder.Length > 0)
                    builder.Append('\n');

                builder.Append(header.Key).Append(": ").Append(header.Value ?? "");
            }

            return builder.ToString();
        }

        /// <summary>Header lines back into a dictionary. Keys are lower case, as the Fetch standard delivers them.</summary>
        private static Dictionary<string, string> ParseHeaders(string headerText)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(headerText))
                return headers;

            string[] lines = headerText.Split('\n');

            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i];
                int colon = line.IndexOf(':');

                if (colon <= 0)
                    continue;

                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();

                if (name.Length == 0)
                    continue;

                // A repeated header (Set-Cookie is the one that matters, and script never sees it) is joined the
                // way the Fetch standard's own header list does.
                string existing;
                headers[name] = headers.TryGetValue(name, out existing) && existing.Length > 0
                    ? existing + ", " + value
                    : value;
            }

            return headers;
        }

        // ----------------------------------------------------------------------------------------------- handle

        /// <summary>One transfer in flight.</summary>
        private sealed class Handle : INowFetchHandle
        {
            // FNV-1a, twice, with different bases, over the encoded bytes as they stream past. Two independent
            // 64-bit hashes plus the exact length is what keys the decoder cache; the alternative - keeping the
            // body to key it by content - is the buffering this port exists to avoid.
            // Two DIFFERENT primes, not two offsets: FNV with one prime and two offsets produces hashes related by
            // a fixed factor, so the second would add no independent bits.
            private const ulong k_FnvOffsetA = 14695981039346656037UL; // the FNV-1a 64 basis
            private const ulong k_FnvOffsetB = 0x9E3779B97F4A7C15UL;   // the golden-ratio constant
            private const ulong k_FnvPrimeA = 0x100000001B3UL;         // the FNV-1a 64 prime
            private const ulong k_FnvPrimeB = 0x880355F21E6D1965UL;    // a second odd 64-bit multiplier

            private readonly WebFetchProvider m_Owner;
            private readonly int m_Id;

            private byte[] m_Buffer = Array.Empty<byte>();
            private bool m_Done;
            private bool m_Disposed;

            internal Handle(WebFetchProvider owner, int id, INowFetchSink sink)
            {
                m_Owner = owner;
                m_Id = id;
                this.sink = sink;
                hashLow = k_FnvOffsetA;
                hashHigh = k_FnvOffsetB;
            }

            internal INowFetchSink sink { get; }

            internal ulong hashLow { get; private set; }

            internal ulong hashHigh { get; private set; }

            internal long byteCount { get; private set; }

            /// <summary>The URL the response came from - after any hops the browser followed. May be empty.</summary>
            internal string finalUrl { get; set; }

            /// <summary><c>Response.type</c>: basic, cors, opaque, opaqueredirect. Diagnostic only.</summary>
            internal string responseType { get; set; }

            /// <inheritdoc />
            public bool isDone
            {
                get { return m_Done; }
            }

            /// <inheritdoc />
            public void Abort()
            {
                if (m_Done)
                    return;

                // Asynchronous by nature: the reader loop's own catch turns the AbortController into
                // OnComplete(Aborted), which is what flips isDone. The contract says as much - "Disposing it
                // releases the transport; it does not by itself report an abort" - and every caller polls isDone.
                Interop.Abort(m_Id);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;
                m_Done = true;
                m_Owner.Forget(m_Id);
                Interop.Release(m_Id);
                m_Buffer = Array.Empty<byte>();
            }

            /// <summary>A buffer of at least <paramref name="count"/> bytes. Reused across chunks of one transfer.</summary>
            internal byte[] Rent(int count)
            {
                if (m_Buffer.Length < count)
                {
                    // Grown in powers of two so a stream of 16 KB chunks allocates once, not once per chunk.
                    int size = m_Buffer.Length == 0 ? 16 * 1024 : m_Buffer.Length;
                    while (size < count)
                        size *= 2;

                    m_Buffer = new byte[size];
                }

                return m_Buffer;
            }

            /// <summary>Folds one chunk into the running content hash.</summary>
            internal void Absorb(byte[] buffer, int count)
            {
                ulong a = hashLow;
                ulong b = hashHigh;

                for (int i = 0; i < count; ++i)
                {
                    byte value = buffer[i];
                    a = (a ^ value) * k_FnvPrimeA;
                    b = (b ^ value) * k_FnvPrimeB;
                }

                hashLow = a;
                hashHigh = b;
                byteCount += count;
            }

            /// <summary>Reports the end of the transfer to the sink and marks the handle done, exactly once.</summary>
            internal void Settle(NowFetchOutcome outcome, string error)
            {
                if (m_Done)
                    return;

                m_Done = true;
                sink.OnComplete(outcome, error);
            }
        }

        // ---------------------------------------------------------------------------------------------- interop

        /// <summary>
        /// The JS functions <c>main.js</c> registered as the module <c>nowui-fetch.js</c>.
        /// </summary>
        /// <remarks>
        /// No <c>JSHost.ImportAsync</c>, unlike <see cref="WebGL2Backend"/> and <see cref="WebInput"/>: this bridge
        /// calls back INTO managed code, so <c>main.js</c> has to hand it the assembly exports anyway, and having
        /// main.js also register its imports makes it exactly one module instance instead of two URL resolutions
        /// that have to agree.
        /// </remarks>
        private static partial class Interop
        {
            private const string ModuleName = "nowui-fetch.js";

            /// <summary>Empty when the bridge is wired up, a reason when it is not.</summary>
            [JSImport("probe", ModuleName)]
            public static partial string Probe();

            [JSImport("start", ModuleName)]
            public static partial void Start(int id, string url, string method, string headerText,
                                             double timeoutSeconds, int redirectMode);

            [JSImport("abort", ModuleName)]
            public static partial void Abort(int id);

            [JSImport("release", ModuleName)]
            public static partial void Release(int id);

            /// <summary>Copies the chunk parked in JS into managed memory. Legal only inside an OnData call.</summary>
            [JSImport("readChunk", ModuleName)]
            public static partial void ReadChunk(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

            /// <summary>Copies the pre-decoded RGBA parked in JS into managed memory. Legal only inside an OnImage call.</summary>
            [JSImport("readImage", ModuleName)]
            public static partial void ReadImage(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);
        }
    }
}
