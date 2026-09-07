// Streaming HTTP fetch contract for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.4).
//
// The shape maps 1:1 onto fetch() + ReadableStream in a browser, and preserves the byte-cap-during-transfer contract
// that NowBoundedDownloadHandler implements in the Unity build: the sink sees each chunk as it arrives and can abort
// mid-transfer by returning false, so an over-large response is never fully buffered.
using System;
using System.Collections.Generic;

namespace NowUI.Engine
{
    /// <summary>One HTTP request. A readonly struct so a host can pass it by <c>in</c> without copying or allocating.</summary>
    public readonly struct NowFetchRequest
    {
        public readonly string url;
        public readonly string method;
        public readonly IReadOnlyDictionary<string, string> headers;
        public readonly int timeoutSeconds;

        /// <summary>
        /// Whether the transport may follow redirects itself. NowUI applies its own per-hop policy (scheme, host and
        /// hop-count checks between hops), so this is false for every request the core makes.
        /// </summary>
        public readonly bool followRedirects;

        public NowFetchRequest(
            string url,
            string method,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds,
            bool followRedirects)
        {
            this.url = url;
            this.method = method;
            this.headers = headers;
            this.timeoutSeconds = timeoutSeconds;
            this.followRedirects = followRedirects;
        }
    }

    /// <summary>How a fetch ended. Mirrors the outcomes NowUI's UnityWebRequest wrapper distinguishes.</summary>
    public enum NowFetchOutcome
    {
        Success = 0,
        ConnectionError = 1,
        ProtocolError = 2,
        Aborted = 3,
        Timeout = 4,

        /// <summary>The sink refused a chunk because the response exceeded its byte cap.</summary>
        LimitExceeded = 5,
    }

    /// <summary>
    /// Receives a fetch's progress. Every callback runs on the thread the provider chooses; the browser provider runs
    /// them on the single JS thread, so the core's handlers must stay non-blocking.
    /// </summary>
    public interface INowFetchSink
    {
        void OnResponse(long statusCode, IReadOnlyDictionary<string, string> headers);

        void OnContentLength(ulong contentLength);

        /// <summary>
        /// Consumes <paramref name="count"/> bytes of <paramref name="buffer"/>. Returning false aborts the transfer,
        /// which is UnityWebRequest's own <c>ReceiveData</c> rule and the hook the byte cap uses.
        /// </summary>
        bool OnData(byte[] buffer, int count);

        void OnComplete(NowFetchOutcome outcome, string error);
    }

    /// <summary>A fetch in flight. Disposing it releases the transport; it does not by itself report an abort.</summary>
    public interface INowFetchHandle : IDisposable
    {
        bool isDone { get; }

        void Abort();
    }

    /// <summary>Starts fetches. A null provider on the host means remote loads fail fast with a named error.</summary>
    public interface INowFetchProvider
    {
        INowFetchHandle Start(in NowFetchRequest request, INowFetchSink sink);
    }
}
