using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NowUI.Engine;

namespace NowUI.Hosting
{
    /// <summary>Streaming HTTP transport for native NowUI remote Lottie and Markdown resources.</summary>
    /// <remarks>
    /// Each request performs exactly one hop. NowUI's existing loaders validate every redirect target and enforce
    /// their own byte limits through the sink. Callbacks run on a worker thread; disposal cancels outstanding IO
    /// and prevents further callbacks. This provider does not replace the application's URL policy.
    /// </remarks>
    public sealed class NowHttpFetchProvider : INowFetchProvider, IDisposable
    {
        readonly HttpClient client;
        readonly ConcurrentDictionary<Fetch, byte> active = new();
        readonly object gate = new();
        readonly bool browserTransport;
        bool disposed;
        long requests;

        public NowHttpFetchProvider()
        {
            client = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
                MaxResponseHeadersLength = 64
            }) { Timeout = Timeout.InfiniteTimeSpan };
        }

        // BrowserHttpHandler has streaming fetch support, but no sockets or visible redirect headers.
        // The wrapper supplies a client with automatic redirects disabled and transfers its ownership here.
        internal NowHttpFetchProvider(HttpClient client, bool browserTransport)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.client.Timeout = Timeout.InfiniteTimeSpan;
            this.browserTransport = browserTransport;
        }

        /// <summary>Requests whose transport has not yet completed. No new request is started by reading this.</summary>
        public int pendingCount => active.Count;

        /// <summary>Total requests started, including completed requests waiting for a cache frame to consume them.</summary>
        public long requestCount => Interlocked.Read(ref requests);

        public INowFetchHandle Start(in NowFetchRequest request, INowFetchSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (!Uri.TryCreate(request.url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new ArgumentException("Native remote resources require an absolute http/https URL.", nameof(request));
            if (request.followRedirects)
                throw new ArgumentException("Automatic redirects are disabled; the NowUI loader must validate every redirect target.", nameof(request));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(NowHttpFetchProvider));
                var fetch = new Fetch(this, sink);
                active.TryAdd(fetch, 0);
                Interlocked.Increment(ref requests);
                fetch.Start(request);
                return fetch;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }
            // Never hold the provider gate while waiting for a sink callback to finish.
            foreach (var fetch in active.Keys) fetch.Dispose();
            client.Dispose();
        }

        sealed class Fetch : INowFetchHandle
        {
            readonly NowHttpFetchProvider owner;
            readonly INowFetchSink sink;
            readonly CancellationTokenSource cancellation = new();
            readonly object callbacks = new();
            bool disposed, aborted;
            int done;
            public bool isDone => Volatile.Read(ref done) != 0;

            internal Fetch(NowHttpFetchProvider owner, INowFetchSink sink) { this.owner = owner; this.sink = sink; }
            internal void Start(NowFetchRequest request) { _ = Run(request); }

            async Task Run(NowFetchRequest request)
            {
                byte[] buffer = null;
                try
                {
                    cancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.timeoutSeconds)));
                    using var message = new HttpRequestMessage(new HttpMethod(request.method ?? "GET"), request.url);
                    if (owner.browserTransport)
                    {
                        // These are the public browser request options consumed by .NET 9 BrowserHttpHandler.
                        message.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);
                        message.Options.Set(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
                            new Dictionary<string, object> { ["redirect"] = "manual", ["credentials"] = "omit" });
                    }
                    if (request.headers != null)
                        foreach (var header in request.headers)
                            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                                throw new ArgumentException("Unsupported request header: " + header.Key);
                    using var response = await owner.client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(owner.browserTransport);
                    if (owner.browserTransport && (int)response.StatusCode == 0)
                    {
                        Complete(NowFetchOutcome.ProtocolError,
                            "The browser returned an opaque response or redirect. Its redirect Location is hidden, so NowUI cannot validate the next URL. Use a direct URL with CORS access.");
                        return;
                    }
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
                    foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
                    lock (callbacks)
                    {
                        if (disposed) return;
                        sink.OnResponse((int)response.StatusCode, headers);
                        if (response.Content.Headers.ContentLength is long length && length >= 0) sink.OnContentLength((ulong)length);
                        // A sink can reject a declared oversized response before any body is transferred.
                        if (!sink.OnData(Array.Empty<byte>(), 0))
                        {
                            Complete(NowFetchOutcome.LimitExceeded, "The response exceeds the loader's byte limit.");
                            return;
                        }
                    }
                    using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(owner.browserTransport);
                    buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
                    while (true)
                    {
                        int count = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token).ConfigureAwait(owner.browserTransport);
                        if (count == 0) break;
                        lock (callbacks)
                        {
                            if (disposed) return;
                            if (!sink.OnData(buffer, count))
                            {
                                Complete(NowFetchOutcome.LimitExceeded, "The response exceeds the loader's byte limit.");
                                return;
                            }
                        }
                    }
                    int status = (int)response.StatusCode;
                    Complete(status < 400 ? NowFetchOutcome.Success : NowFetchOutcome.ProtocolError,
                        status < 400 ? null : $"HTTP {status} {response.ReasonPhrase}");
                }
                catch (OperationCanceledException)
                {
                    lock (callbacks) Complete(aborted ? NowFetchOutcome.Aborted : NowFetchOutcome.Timeout,
                        aborted ? "The request was aborted." : "The request timed out.");
                }
                catch (Exception error)
                {
                    Complete(NowFetchOutcome.ConnectionError, error.Message + (owner.browserTransport
                        ? " Browser fetch cannot distinguish CORS refusal, blocked mixed content, DNS failure, and an unavailable server. Cross-origin URLs require server CORS access."
                        : ""));
                }
                finally
                {
                    if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
                    Volatile.Write(ref done, 1);
                    owner.active.TryRemove(this, out _);
                    lock (callbacks) cancellation.Dispose();
                }
            }

            void Complete(NowFetchOutcome outcome, string error)
            {
                lock (callbacks)
                {
                    if (disposed || isDone) return;
                    try { sink.OnComplete(outcome, error); }
                    finally { Volatile.Write(ref done, 1); }
                }
            }

            public void Abort()
            {
                lock (callbacks)
                {
                    if (disposed || isDone) return;
                    aborted = true;
                    cancellation.Cancel();
                }
            }

            public void Dispose()
            {
                lock (callbacks)
                {
                    if (disposed) return;
                    disposed = true;
                    if (!isDone) cancellation.Cancel();
                }
            }
        }
    }
}
