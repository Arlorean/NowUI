#if NOWUI_STANDALONE
// The engine-free half of NowMarkdownImages (design Docs/Standalone/StandaloneCoreDesign.md §4.4 and §5.2, unit U18).
// It replaces the UnityWebRequest members that live in NowMarkdownImages.Unity.cs with the same members implemented
// over INowFetchProvider / INowFetchSink, so the public surface of NowMarkdownImages is identical in both
// configurations: the cache still queues, polls and settles entries the same way, driven by NowRuntime.onFrame
// instead of by a MonoBehaviour Update.
//
// The policy is the core half's, not a second copy of it: FinishDownload in NowMarkdownImages.cs owns the redirect
// hop, the transport error, the decode and the resulting cache state, and the byte cap is enforced during transfer by
// the sink's OnData returning false - the same contract NowBoundedDownloadHandler implements in the Unity build.
// With no INowImageDecoder installed, ImageConversion.LoadImage returns false and the entry reaches Failed, which is
// the error path the cache already had for a corrupt download.
using NowUI.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NowUI.Markdown
{
    public static partial class NowMarkdownImages
    {
        sealed partial class Entry
        {
            public INowFetchHandle handle;
            public NowMarkdownFetchSink sink;
        }

        static void PollDownloads()
        {
            long byteLimit = EffectiveLimit(maxDownloadBytes);

            for (int i = _active.Count - 1; i >= 0; --i)
            {
                var entry = _active[i];

                if (entry.completed || entry.handle == null)
                    continue;

                if (entry.sink?.limitExceeded ?? false)
                {
                    entry.forcedError =
                        $"Markdown image download from '{entry.url}' exceeds the configured limit of {byteLimit} bytes across redirects.";
                    entry.handle.Abort();
                }

                if (entry.handle.isDone)
                    CompleteDownload(entry);
            }
        }

        static void StartDownload(Entry entry)
        {
            EnsureTicking();

            try
            {
                var provider = NowRuntime.host?.fetch;

                if (provider == null)
                {
                    entry.forcedError =
                        $"Failed to start markdown image download from '{entry.url}': remote image loading requires an INowFetchProvider.";
                    CompleteDownload(entry);
                    return;
                }

                long byteLimit = EffectiveLimit(maxDownloadBytes);
                long remaining = Math.Max(0L, byteLimit - entry.downloadedBytes);
                var sink = new NowMarkdownFetchSink(remaining);
                entry.sink = sink;
                var request = new NowFetchRequest(
                    entry.currentUri.AbsoluteUri,
                    "GET",
                    null,
                    Mathf.Max(1, requestTimeoutSeconds),
                    // Follow redirects ourselves so each target is checked by the URL policy.
                    followRedirects: false);
                entry.active = true;
                _active.Add(entry);
                entry.handle = provider.Start(in request, sink);

                if (entry.handle == null)
                {
                    entry.forcedError =
                        $"Failed to start markdown image download from '{entry.url}': the fetch provider returned no handle.";
                    CompleteDownload(entry);
                    return;
                }

                if (entry.handle.isDone)
                    CompleteDownload(entry);
            }
            catch (Exception exception)
            {
                entry.forcedError = $"Failed to start markdown image download from '{entry.url}': {exception.Message}";
                CompleteDownload(entry);
            }
        }

        static void CompleteDownload(Entry entry)
        {
            if (entry == null || entry.completed)
                return;

            entry.completed = true;
            entry.active = false;
            _active.Remove(entry);

            var handle = entry.handle;
            var sink = entry.sink;
            entry.handle = null;
            entry.sink = null;

            bool current = _entries.TryGetValue(entry.url, out var cached) && ReferenceEquals(cached, entry);

            if (!current)
            {
                handle?.Dispose();
                PumpDownloads();
                return;
            }

            string error = entry.forcedError;
            long byteLimit = EffectiveLimit(maxDownloadBytes);

            if (error == null && (sink?.limitExceeded ?? false))
            {
                error =
                    $"The image response exceeds the configured limit of {byteLimit} bytes across redirects.";
            }

            if (sink != null)
                entry.downloadedBytes += sink.receivedByteCount;

            long status = sink?.statusCode ?? 0L;
            string location = sink?.location;
            string requestError = sink == null
                ? "The image request was not created."
                : (sink.outcome != NowFetchOutcome.Success
                    ? (sink.error ?? $"The image request failed ({sink.outcome}).")
                    : null);

            // Read the buffer only on the path that actually decodes it, which is what the Unity half does with
            // NowBoundedDownloadHandler.GetBytes: an aborted transfer would otherwise copy its partial bytes for
            // nothing. The condition is FinishDownload's own precondition for the decode branch.
            byte[] bytes = error == null && requestError == null && !IsRedirectStatus(status)
                ? sink?.GetBytes()
                : null;

            handle?.Dispose();

            FinishDownload(entry, bytes, status, location, error, requestError);
        }

        static void AbortRequest(Entry entry)
        {
            var handle = entry.handle;
            entry.handle = null;
            entry.sink = null;

            if (handle != null)
            {
                handle.Abort();
                handle.Dispose();
            }
        }

        /// <summary>
        /// Subscribes <see cref="Tick"/> to the frame loop, taking the place of the Unity half's runner GameObject.
        /// The unsubscribe-then-subscribe pair is what keeps it idempotent and what re-arms the cache after
        /// <c>NowRuntime.Shutdown</c> has cleared every <c>onFrame</c> handler.
        /// </summary>
        static void EnsureTicking()
        {
            NowRuntime.onFrame -= Tick;
            NowRuntime.onFrame += Tick;
        }

        static void DestroyRunner()
        {
            NowRuntime.onFrame -= Tick;
        }

        /// <summary>
        /// Buffers one image response and enforces the byte cap while the bytes arrive. Every callback may run on
        /// whatever thread the provider chooses while the cache polls from the frame thread, so the state sits behind
        /// one lock; the browser provider raises them all on the single JS thread and the lock is then uncontended.
        /// </summary>
        sealed class NowMarkdownFetchSink : INowFetchSink
        {
            readonly object _gate = new object();
            readonly MemoryStream _buffer = new MemoryStream();
            readonly long _limit;

            long _statusCode;
            long _receivedByteCount;
            bool _limitExceeded;
            string _location;
            NowFetchOutcome _outcome = NowFetchOutcome.Success;
            string _error;

            public NowMarkdownFetchSink(long limit)
            {
                _limit = Math.Max(0L, limit);
            }

            public long statusCode
            {
                get { lock (_gate) return _statusCode; }
            }

            public long receivedByteCount
            {
                get { lock (_gate) return _receivedByteCount; }
            }

            public bool limitExceeded
            {
                get { lock (_gate) return _limitExceeded; }
            }

            public string location
            {
                get { lock (_gate) return _location; }
            }

            public NowFetchOutcome outcome
            {
                get { lock (_gate) return _outcome; }
            }

            public string error
            {
                get { lock (_gate) return _error; }
            }

            public void OnResponse(long statusCode, IReadOnlyDictionary<string, string> headers)
            {
                string location = TryGetHeader(headers, "Location");

                lock (_gate)
                {
                    _statusCode = statusCode;
                    _location = location;
                }
            }

            public void OnContentLength(ulong contentLength)
            {
                // The declared length is the cheap half of the cap: refuse before a byte is read when the server
                // already says the body is too large. RequestExceedsLimit does the same with Content-Length.
                if (contentLength <= (ulong)_limit)
                    return;

                lock (_gate)
                    _limitExceeded = true;
            }

            public bool OnData(byte[] buffer, int count)
            {
                lock (_gate)
                {
                    if (_limitExceeded)
                        return false;

                    if (buffer == null || count <= 0)
                        return true;

                    if (count > _limit - _receivedByteCount)
                    {
                        _limitExceeded = true;
                        return false;
                    }

                    _receivedByteCount += count;
                    _buffer.Write(buffer, 0, count);
                    return true;
                }
            }

            public void OnComplete(NowFetchOutcome outcome, string error)
            {
                lock (_gate)
                {
                    _outcome = outcome;
                    _error = error;
                }
            }

            /// <summary>
            /// The received bytes, or null once the cap was breached - matching NowBoundedDownloadHandler.GetBytes,
            /// which returns null rather than a truncated image.
            /// </summary>
            public byte[] GetBytes()
            {
                lock (_gate)
                    return _limitExceeded ? null : _buffer.ToArray();
            }

            static string TryGetHeader(IReadOnlyDictionary<string, string> headers, string name)
            {
                if (headers == null)
                    return null;

                if (headers.TryGetValue(name, out string value))
                    return value;

                foreach (var pair in headers)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        return pair.Value;
                }

                return null;
            }
        }
    }
}
#endif
