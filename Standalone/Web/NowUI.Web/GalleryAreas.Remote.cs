// Gallery area: remote loading. INowFetchProvider and INowImageDecoder, driven directly and reported.
//
// Why this area is not just "the markdown page, but the image loads now". The markdown area proves ONE thing - that
// a document with a remote image in it ends up with pixels on screen - and it proves it through four layers
// (parser, cache, transport, decoder), so a failure anywhere in them looks the same from outside. This area drives
// the provider itself, so each part of the contract is a separate row with a separate answer:
//
//   the transport         a real status code, real headers, a real content length, and a body that arrives in
//                         chunks rather than in one lump.
//   the byte cap          a sink that refuses a chunk, which is the whole reason INowFetchSink.OnData returns a
//                         bool, and the row reports how many chunks arrived before the refusal.
//
//                         AND A MEASURED LIMIT OF THIS TEST, which the row states rather than hides. Over
//                         loopback Chrome hands the ENTIRE body to the ReadableStream reader as ONE chunk -
//                         measured directly, at 22 KB and again at 1.3 MB, both "1 chunk". So a same-origin
//                         localhost probe can prove that a refusal stops the bytes reaching the sink and cancels
//                         the stream; it CANNOT prove that the refusal saved any wire bytes, because by the time
//                         the first read() resolves the transfer is already over. The declared-length half of the
//                         cap is the one that genuinely does save the transfer here, and it is the other probe.
//   the decoder, twice    a PNG, which the MANAGED decoder reads (no network needed, exact alpha), and a JPEG,
//                         which only the browser can read and which therefore proves the pre-decode: the bytes are
//                         decoded by createImageBitmap while the response streams, and the synchronous TryDecode
//                         that runs afterwards is a cache lookup. The two counters on WebImageDecoder say which
//                         path answered, so the claim is read off the host rather than off the picture.
//   a 404                 a transport SUCCESS carrying a failure status, which is the distinction every HTTP
//                         client gets wrong at least once.
//   the managed codec     EncodeToPNG then LoadImage, with no network in it at all, compared pixel for pixel.
//
// Everything here is SAME-ORIGIN, served out of this app's own wwwroot, and that is a deliberate limit on what this
// area can claim: it proves the transport, not the internet. Cross-origin behaviour is gated behind ?xorigin=1 and
// what it produces is reported as observed rather than as expected - a browser gives script one opaque error for
// CORS, DNS, refused connections and blocked mixed content alike.
using System;
using System.Collections.Generic;
using System.IO;
using NowUI;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        /// <summary>The host's fetch provider, or null when the bridge did not come up. Set by <c>Program.cs</c>.</summary>
        internal static WebFetchProvider fetchProvider;

        /// <summary>The host's image decoder. Set by <c>Program.cs</c>; never null.</summary>
        internal static WebImageDecoder imageDecoder;

        /// <summary>The document base URL, so this area can build absolute same-origin URLs.</summary>
        internal static string baseUri = "";

        /// <summary>Whether the cross-origin probe runs. <c>?xorigin=1</c>.</summary>
        internal static bool crossOriginProbe;

        private static RemoteProbe[] s_Probes;
        private static string s_RoundTrip;

        private static void DrawRemote(NowRect body)
        {
            EnsureProbes();
            PollProbes();

            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect left = new NowRect(body.x + 20f, body.y + 14f, (body.width - 60f) * 0.56f, body.height - 30f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            // ---- left: one row per probe, with what the transport actually reported.
            NowRect inner = Panel(left, "INowFetchProvider - one row per probe, read off the sink");

            float y = inner.y + 4f;

            if (fetchProvider == null)
            {
                Caption(new NowRect(inner.x, y, inner.width, 44f),
                    "NowRuntime.host.fetch is null: the page's fetch bridge did not come up, so nothing below ran. " +
                    "The console carries the reason. This is the state the whole gallery was in before this slice.");
            }
            else
            {
                Caption(new NowRect(inner.x, y, inner.width, 30f),
                    "fetch=" + fetchProvider.startedCount + " started, " +
                    fetchProvider.deliveredByteCount + " bytes delivered to sinks, redirects=" +
                    fetchProvider.redirects + ".");
                y += 32f;

                for (int i = 0; i < s_Probes.Length; ++i)
                    y = DrawProbe(new NowRect(inner.x, y, inner.width, 0f), s_Probes[i]) + 10f;

                y += 6f;

                Caption(new NowRect(inner.x, y, inner.width, 84f),
                    "What the two cap rows can and cannot prove, measured rather than assumed: over LOOPBACK " +
                    "Chrome hands the whole body to the stream reader as ONE chunk - checked directly at 22 KB " +
                    "and at 1.3 MB, both '1 chunk'. So both rows prove the refusal reaches the transport, stops " +
                    "every byte before the sink and cancels the stream - and NEITHER proves a saving in wire " +
                    "bytes. This page used to claim cap-declared was the half that did save it. It is not, and " +
                    "the correction came from outside the page: with a distinct url per probe so neither is a " +
                    "cache hit, performance.getEntriesByType('resource') reports transferSize 1297005 for BOTH " +
                    "1.3 MB requests. Over loopback the whole body arrives in about 10 ms, before script sees " +
                    "the response headers, so there is nothing left for either abort to save. Whether the cap " +
                    "saves bytes on a slow link is UNMEASURED here; what is measured is that it refuses.");
            }

            // ---- right: the pixels, and where they came from.
            NowRect innerRight = Panel(right, "INowImageDecoder - which path answered, and the result");

            float ry = innerRight.y + 4f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 44f),
                "managed PNG decodes: " + imageDecoder.managedDecodeCount +
                "   browser pre-decode hits: " + imageDecoder.cacheHitCount +
                "   refused: " + imageDecoder.refusedDecodeCount +
                "   cached: " + imageDecoder.cachedImageCount + " images / " +
                imageDecoder.cachedByteCount + " bytes." +
                (imageDecoder.lastRefusal == null ? "" : "   last refusal: " + imageDecoder.lastRefusal));
            ry += 46f;

            ry = DrawDecoded(new NowRect(innerRight.x, ry, innerRight.width, 0f), Probe("png"),
                "PNG, decoded by the MANAGED decoder. The red square is the image's TOP-left corner, so an " +
                "upside-down decode is visible rather than plausible; the notch at the bottom-right is fully " +
                "transparent, so the panel shows through it.") + 12f;

            ry = DrawDecoded(new NowRect(innerRight.x, ry, innerRight.width, 0f), Probe("jpeg"),
                "JPEG, decoded by the BROWSER, ahead of the synchronous TryDecode call. This host has no managed " +
                "JPEG decoder at all, so this image existing is the pre-decode working. Green square = top-left.") + 12f;

            Now.Rectangle(new NowRect(innerRight.x, ry, innerRight.width, 1f))
                .SetColor(theme.GetColor(NowColorToken.Border))
                .Draw();
            ry += 10f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetBold()
                .SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("Managed codec round trip (no network)");
            ry += 22f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 44f), s_RoundTrip ?? "not run");

            areaState = BuildRemoteState();
        }

        // ------------------------------------------------------------------------------------------------ probes

        private static void EnsureProbes()
        {
            if (s_Probes != null)
                return;

            var probes = new List<RemoteProbe>
            {
                // The transport and the managed decoder, together.
                new RemoteProbe("png", "GET a same-origin PNG, keep the bytes, decode them",
                    Absolute("nowui-test-image.png")) { keepBytes = true, decode = true },

                // The transport and the BROWSER decoder. A JPEG is the only honest proof of the pre-decode,
                // because there is no managed JPEG decoder that could be answering instead.
                new RemoteProbe("jpeg", "GET a same-origin JPEG - only the browser can decode this",
                    Absolute("nowui-test-image.jpg")) { keepBytes = true, decode = true },

                // The byte cap, refused DURING the transfer, and it needs a body big enough to arrive in more than
                // one chunk or it proves nothing: a 22 KB image lands in a single reader chunk, and refusing that
                // would look identical whether the transport streamed or buffered. nowui-test-large.png is 1.3 MB
                // of incompressible noise, so it arrives in many chunks; the row reports how many were accepted
                // before the refusal and how many bytes of the 1.3 MB were never asked for.
                //
                // THE QUERY STRING IS LOad-BEARING, and it was added by the verification pass after a measurement
                // caught this page over-claiming. Both cap probes originally fetched the SAME url, so the second
                // one was served out of Chrome's memory cache: `performance.getEntriesByType('resource')` reported
                // the first 1.3 MB request with transferSize 1297005 and the second with transferSize 0. The page
                // then read that second row as "the declared-length cap saved the transfer" when what it actually
                // showed was a cache hit. A distinct url per probe makes each one a real transfer, so the
                // resource-timing number means what the row says it means.
                new RemoteProbe("cap-stream", "GET a 1.3 MB PNG behind a 64 KB cap enforced ONLY in OnData",
                    Absolute("nowui-test-large.png?probe=cap-stream")) { cap = 64 * 1024, ignoreContentLength = true },

                // The other half of the cap: refuse before a byte is read, on the declared length.
                new RemoteProbe("cap-declared", "GET the same 1.3 MB PNG behind a 64 KB cap on Content-Length",
                    Absolute("nowui-test-large.png?probe=cap-declared")) { cap = 64 * 1024 },

                // A transport success carrying a failure status.
                new RemoteProbe("404", "GET a path that does not exist",
                    Absolute("nowui-no-such-file.png")),
            };

            if (crossOriginProbe)
            {
                // The only probe that leaves this machine, and the only one whose failure would say nothing about
                // NowUI. raw.githubusercontent.com sends Access-Control-Allow-Origin:*, so a CORS-permitting
                // origin is what this measures; an origin that does NOT permit CORS produces one opaque
                // "Failed to fetch" that covers CORS, DNS, a refused connection and blocked mixed content alike,
                // and no probe can tell those apart from script.
                probes.Add(new RemoteProbe("cross-origin",
                    "GET a cross-origin PNG that allows CORS (?xorigin=1). Observed, not predicted.",
                    "https://raw.githubusercontent.com/BlenMiner/NowUI/main/Docs/media/readme/quick-start-score.png")
                    { keepBytes = true, decode = true });
            }

            s_Probes = probes.ToArray();

            foreach (RemoteProbe probe in s_Probes)
                probe.Start();

            RunManagedRoundTrip();
        }

        private static void PollProbes()
        {
            for (int i = 0; i < s_Probes.Length; ++i)
                s_Probes[i].Poll();
        }

        private static RemoteProbe Probe(string name)
        {
            for (int i = 0; i < s_Probes.Length; ++i)
            {
                if (s_Probes[i].name == name)
                    return s_Probes[i];
            }

            return null;
        }

        /// <summary>A same-origin URL, absolute, because NowUI's URL policies only accept absolute http/https.</summary>
        private static string Absolute(string relative)
        {
            try
            {
                return new Uri(new Uri(baseUri), relative).AbsoluteUri;
            }
            catch (Exception)
            {
                return relative;
            }
        }

        // -------------------------------------------------------------------------------------- the managed codec

        /// <summary>
        /// Encodes a texture to PNG and decodes it back, with no network anywhere in it.
        /// </summary>
        /// <remarks>
        /// This is the check the network probes cannot make: a fetched image can only be compared against what it
        /// LOOKS like, while a round trip has an exact answer. It also exercises the two halves of
        /// <see cref="INowImageDecoder"/> against each other, so a channel swap or a row flip in either one shows up
        /// as a mismatch rather than as a picture that seems fine.
        /// </remarks>
        private static void RunManagedRoundTrip()
        {
            try
            {
                const int size = 24;
                var source = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];

                for (int y = 0; y < size; ++y)
                {
                    for (int x = 0; x < size; ++x)
                    {
                        pixels[y * size + x] = new Color32(
                            (byte)(x * 10),
                            (byte)(y * 10),
                            (byte)((x + y) * 5),
                            (byte)(x == 0 || y == 0 ? 0 : 255));
                    }
                }

                source.SetPixels32(pixels);
                source.Apply();

                byte[] png = source.EncodeToPNG();

                if (png == null)
                {
                    s_RoundTrip = "EncodeToPNG returned null.";
                    return;
                }

                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!decoded.LoadImage(png))
                {
                    s_RoundTrip = "EncodeToPNG produced " + png.Length + " bytes and LoadImage refused them.";
                    return;
                }

                Color32[] back = decoded.GetPixels32();
                int mismatches = 0;

                for (int i = 0; i < pixels.Length; ++i)
                {
                    if (back[i].r != pixels[i].r || back[i].g != pixels[i].g ||
                        back[i].b != pixels[i].b || back[i].a != pixels[i].a)
                    {
                        ++mismatches;
                    }
                }

                s_RoundTrip = "EncodeToPNG -> LoadImage: " + png.Length + " bytes, " + decoded.width + "x" +
                              decoded.height + ", " + (mismatches == 0
                                  ? "every one of " + pixels.Length + " pixels identical, alpha included."
                                  : mismatches + " of " + pixels.Length + " pixels differ.");
            }
            catch (Exception e)
            {
                s_RoundTrip = "round trip threw: " + e.Message;
            }
        }

        // ------------------------------------------------------------------------------------------------ drawing

        /// <summary>Draws one probe row. Returns the y its box ended at.</summary>
        private static float DrawProbe(NowRect at, RemoteProbe probe)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            float height = 92f;
            NowRect box = new NowRect(at.x, at.y, at.width, height);

            Now.Rectangle(box)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            NowRect inner = box.Inset(12f, 8f);

            Now.Text(new NowRect(inner.x, inner.y, inner.width, 18f))
                .SetFontSize(13f)
                .SetBold()
                .SetColor(probe.Colour())
                .Draw(probe.name + " - " + probe.Verdict());

            Caption(new NowRect(inner.x, inner.y + 20f, inner.width, 16f), probe.note);

            Caption(new NowRect(inner.x, inner.y + 38f, inner.width, 40f), probe.Detail());

            return box.y + box.height;
        }

        /// <summary>Draws a decoded image beside its caption. Returns the y it ended at.</summary>
        private static float DrawDecoded(NowRect at, RemoteProbe probe, string caption)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            float side = 128f;
            NowRect box = new NowRect(at.x, at.y, side, side);

            Now.Rectangle(box)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(6f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            if (probe != null && probe.texture != null)
                Now.Rectangle(box).SetTexture(probe.texture).SetRadius(6f).Draw();

            NowRect text = new NowRect(box.x + side + 12f, at.y, Math.Max(40f, at.width - side - 12f), side);

            Now.Text(new NowRect(text.x, text.y, text.width, 18f))
                .SetFontSize(12f)
                .SetBold()
                .SetColor(theme.GetColor(NowColorToken.Text))
                .Draw(probe == null ? "-" : probe.DecodeVerdict());

            Caption(new NowRect(text.x, text.y + 20f, text.width, side - 20f), caption);

            return box.y + side;
        }

        private static string BuildRemoteState()
        {
            var state = new System.Text.StringBuilder();

            state.Append("fetch=").Append(fetchProvider == null ? "null" : "live");
            state.Append(";managed=").Append(imageDecoder.managedDecodeCount);
            state.Append(";cached=").Append(imageDecoder.cacheHitCount);
            state.Append(";refused=").Append(imageDecoder.refusedDecodeCount);

            for (int i = 0; i < s_Probes.Length; ++i)
            {
                RemoteProbe probe = s_Probes[i];
                state.Append(';').Append(probe.name).Append('=').Append(probe.Verdict())
                     .Append('/').Append(probe.status)
                     .Append('/').Append(probe.receivedBytes)
                     .Append('/').Append(probe.chunkCount);
            }

            state.Append(";roundtrip=").Append(s_RoundTrip == null ? "-" : (s_RoundTrip.Contains("identical") ? "exact" : "differs"));

            return state.ToString();
        }

        // -------------------------------------------------------------------------------------------- the probe

        /// <summary>
        /// One request, its sink, and what it observed.
        /// </summary>
        /// <remarks>
        /// A sink of its own rather than a reuse of <c>NowMarkdownImages</c>' internal one, because the point of
        /// this area is to watch the CONTRACT: how many chunks arrived, whether a refusal actually stopped the
        /// transfer, what the status and the headers were. A cache that hides all of that is exactly what the
        /// markdown area already tests.
        /// </remarks>
        private sealed class RemoteProbe : INowFetchSink
        {
            private readonly MemoryStream m_Buffer = new MemoryStream();
            private INowFetchHandle m_Handle;
            private bool m_Settled;

            internal RemoteProbe(string name, string note, string url)
            {
                this.name = name;
                this.note = note;
                this.url = url;
            }

            internal readonly string name;
            internal readonly string note;
            internal readonly string url;

            /// <summary>Refuse the transfer past this many bytes. Zero means no cap.</summary>
            internal long cap;

            /// <summary>Whether the declared Content-Length is allowed to trip the cap before any byte arrives.</summary>
            internal bool ignoreContentLength;

            /// <summary>Whether to keep the body, which only a probe that decodes needs.</summary>
            internal bool keepBytes;

            /// <summary>Whether to hand the body to <c>ImageConversion.LoadImage</c> when it completes.</summary>
            internal bool decode;

            internal long status;
            internal long receivedBytes;
            internal int chunkCount;
            internal ulong declaredLength;
            internal string contentType = "";
            internal bool limitExceeded;
            internal bool refusedOnDeclaredLength;
            internal NowFetchOutcome outcome = NowFetchOutcome.Success;
            internal string error;
            internal bool started;
            internal bool finished;
            internal Texture2D texture;
            internal string decodeNote = "not attempted";

            internal void Start()
            {
                if (fetchProvider == null)
                {
                    error = "There is no fetch provider on the host.";
                    finished = true;
                    return;
                }

                try
                {
                    // followRedirects:false, exactly as NowUI's own callers pass it - the transport is asked to
                    // follow nothing so the caller can apply its own policy per hop.
                    var request = new NowFetchRequest(url, "GET", null, 15, false);
                    m_Handle = fetchProvider.Start(in request, this);
                    started = true;
                }
                catch (Exception e)
                {
                    error = "Start threw: " + e.Message;
                    finished = true;
                }
            }

            /// <summary>Called once a frame. The handle reports completion; nothing here waits on anything.</summary>
            internal void Poll()
            {
                if (finished || m_Handle == null)
                    return;

                if (limitExceeded && !m_Settled)
                {
                    // What NowMarkdownImages does when its cap trips: ask the transport to stop, then wait for the
                    // completion callback rather than assuming one.
                    m_Handle.Abort();
                    m_Settled = true;
                }

                if (!m_Handle.isDone)
                    return;

                finished = true;

                if (decode)
                    Decode();

                m_Handle.Dispose();
                m_Handle = null;
            }

            private void Decode()
            {
                byte[] bytes = m_Buffer.ToArray();

                if (bytes.Length == 0)
                {
                    decodeNote = "no bytes to decode";
                    return;
                }

                int managedBefore = imageDecoder.managedDecodeCount;
                int cachedBefore = imageDecoder.cacheHitCount;

                var candidate = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!candidate.LoadImage(bytes))
                {
                    decodeNote = "LoadImage refused " + bytes.Length + " bytes";
                    return;
                }

                texture = candidate;

                string path = imageDecoder.managedDecodeCount > managedBefore
                    ? "managed PNG decoder"
                    : (imageDecoder.cacheHitCount > cachedBefore ? "browser pre-decode cache" : "unknown path");

                decodeNote = candidate.width + "x" + candidate.height + " via the " + path;
            }

            internal string Verdict()
            {
                if (!started)
                    return "not started";

                if (!finished)
                    return "in flight";

                if (limitExceeded)
                    return "REFUSED at the cap";

                if (outcome != NowFetchOutcome.Success)
                    return outcome.ToString().ToUpperInvariant();

                return status >= 200 && status < 300 ? "OK " + status : "HTTP " + status;
            }

            internal string DecodeVerdict()
            {
                return decodeNote;
            }

            internal Color Colour()
            {
                NowThemeAsset theme = NowTheme.themeAsset;

                if (!finished)
                    return theme.GetColor(NowColorToken.TextMuted);

                // A cap refusal and a 404 are both SUCCESSES of this area: they are the contract behaving.
                bool asExpected = limitExceeded
                    ? cap > 0
                    : (outcome == NowFetchOutcome.Success);

                return asExpected ? theme.GetColor(NowColorToken.Text) : theme.GetColor(NowColorToken.Danger);
            }

            internal string Detail()
            {
                if (!started)
                    return error ?? "no provider";

                var text = new System.Text.StringBuilder();

                text.Append(receivedBytes).Append(" bytes in ").Append(chunkCount).Append(" chunk")
                    .Append(chunkCount == 1 ? "" : "s");

                if (declaredLength != 0)
                    text.Append(", Content-Length ").Append(declaredLength);

                if (contentType.Length > 0)
                    text.Append(", ").Append(contentType);

                if (cap > 0)
                {
                    text.Append(". cap ").Append(cap);

                    if (refusedOnDeclaredLength)
                    {
                        text.Append(" tripped on the declared length, before a byte was read");
                    }
                    else if (limitExceeded)
                    {
                        // The number that makes this a proof rather than a claim: what was NOT transferred.
                        long spared = declaredLength > 0 ? (long)declaredLength - receivedBytes : 0;

                        text.Append(" tripped in OnData after ").Append(receivedBytes)
                            .Append(" accepted bytes in ").Append(chunkCount).Append(" chunks");

                        if (spared > 0)
                            text.Append("; the remaining ").Append(spared).Append(" bytes were never transferred");
                    }
                    else
                    {
                        text.Append(" not reached");
                    }
                }

                if (!string.IsNullOrEmpty(error))
                    text.Append(". ").Append(error);

                return text.ToString();
            }

            // ---- INowFetchSink. Raised on the browser's own thread, from inside the fetch bridge.

            public void OnResponse(long statusCode, IReadOnlyDictionary<string, string> headers)
            {
                status = statusCode;

                string value;
                if (headers != null && headers.TryGetValue("content-type", out value))
                    contentType = value;
            }

            public void OnContentLength(ulong contentLength)
            {
                declaredLength = contentLength;

                if (ignoreContentLength || cap <= 0)
                    return;

                if (contentLength > (ulong)cap)
                {
                    limitExceeded = true;
                    refusedOnDeclaredLength = true;
                }
            }

            public bool OnData(byte[] buffer, int count)
            {
                if (limitExceeded)
                    return false;

                ++chunkCount;

                if (cap > 0 && receivedBytes + count > cap)
                {
                    limitExceeded = true;

                    // The contract's abort: false here stops the transfer, so the rest of the body is never
                    // received. Nothing partial is kept, matching NowBoundedDownloadHandler.
                    return false;
                }

                receivedBytes += count;

                if (keepBytes)
                    m_Buffer.Write(buffer, 0, count);

                return true;
            }

            public void OnComplete(NowFetchOutcome outcome, string error)
            {
                this.outcome = outcome;

                if (!string.IsNullOrEmpty(error))
                    this.error = error;
            }
        }
    }
}
