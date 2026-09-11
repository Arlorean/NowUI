#if NOWUI_STANDALONE
// The engine-free half of NowLottieAsset (design Docs/Standalone/StandaloneCoreDesign.md §4.4 and §5.2, unit U16).
// It replaces the UnityWebRequest members that live in NowLottieAsset.Unity.cs with the same members implemented over
// INowFetchProvider / INowFetchSink, so the public surface of NowLottieAsset is identical in both configurations: the
// coroutines still return IEnumerator and are driven by MoveNext from NowRuntime.onFrame instead of by StartCoroutine.
//
// The policy is deliberately the core half's, not a second copy of it: TryValidateRemoteUrl, IsRedirectStatus and
// TryResolveRedirect are called from NowLottieAsset.cs, and the byte cap is enforced during transfer by the sink's
// OnData returning false - the same contract NowBoundedDownloadHandler implements in the Unity build.
using NowUI.Engine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NowUI
{
    public sealed partial class NowLottieAsset
    {
        /// <summary>
        /// Downloads a Lottie document from an http/https URL and assigns it to this
        /// asset. The previous source remains active if download or parsing fails.
        /// </summary>
        public IEnumerator SetSourceFromUrl(string url, Action<string> onError = null)
        {
            byte[] bytes = null;
            string error = null;
            yield return DownloadSourceBytes(url, value => bytes = value, value => error = value);

            if (error != null)
            {
                onError?.Invoke(error);
                yield break;
            }

            try
            {
                SetSource(bytes);
            }
            catch (Exception exception)
            {
                onError?.Invoke($"Failed to parse Lottie from '{url}': {exception.Message}");
            }
        }

        /// <summary>
        /// Creates a transient runtime asset from an http/https URL. The caller owns
        /// the returned asset and should destroy it when no longer needed.
        /// </summary>
        public static IEnumerator LoadFromUrl(string url, Action<NowLottieAsset> onLoaded, Action<string> onError = null)
        {
            return LoadFromUrlInternal(url, onLoaded, onError, null);
        }

        /// <summary>
        /// The standalone counterpart of the Unity overload. The only difference is the observer parameter: the caller
        /// is handed the in-flight <see cref="INowFetchHandle"/> instead of a <c>UnityWebRequest</c>, which is what
        /// <c>NowLottieCache</c> aborts when an entry is evicted.
        /// </summary>
        internal static IEnumerator LoadFromUrlInternal(
            string url,
            Action<NowLottieAsset> onLoaded,
            Action<string> onError,
            Action<INowFetchHandle> onRequestChanged)
        {
            if (onLoaded == null)
                throw new ArgumentNullException(nameof(onLoaded));

            byte[] bytes = null;
            string error = null;
            yield return DownloadSourceBytes(
                url,
                value => bytes = value,
                value => error = value,
                onRequestChanged);

            if (error != null)
            {
                onError?.Invoke(error);
                yield break;
            }

            var asset = CreateInstance<NowLottieAsset>();
            asset.name = GetAssetNameFromUrl(url);

            try
            {
                asset.SetSource(bytes);
            }
            catch (Exception exception)
            {
                DestroyRuntimeAsset(asset);
                onError?.Invoke($"Failed to parse Lottie from '{url}': {exception.Message}");
                yield break;
            }

            onLoaded(asset);
        }

        static IEnumerator DownloadSourceBytes(
            string url,
            Action<byte[]> onLoaded,
            Action<string> onError,
            Action<INowFetchHandle> onRequestChanged = null)
        {
            if (!TryValidateRemoteUrl(url, out Uri currentUri, out string validationError))
            {
                onError?.Invoke(validationError);
                yield break;
            }

            var provider = NowRuntime.host?.fetch;

            if (provider == null)
            {
                onError?.Invoke(
                    $"Failed to download Lottie from '{url}': remote Lottie loading requires an INowFetchProvider.");
                yield break;
            }

            int redirects = 0;
            long totalDownloaded = 0L;
            long byteLimit = EffectiveLimit(maxDownloadBytes);

            while (true)
            {
                long remaining = Math.Max(0L, byteLimit - totalDownloaded);
                var sink = new NowLottieFetchSink(remaining);
                var request = new NowFetchRequest(
                    currentUri.AbsoluteUri,
                    "GET",
                    null,
                    Mathf.Max(1, requestTimeoutSeconds),
                    // Redirects are followed manually so every target runs through the
                    // same scheme/host policy as the original URL.
                    followRedirects: false);

                INowFetchHandle handle = provider.Start(in request, sink);

                if (handle == null)
                {
                    onError?.Invoke($"Failed to download Lottie from '{url}': the fetch provider returned no handle.");
                    yield break;
                }

                onRequestChanged?.Invoke(handle);

                try
                {
                    while (!handle.isDone)
                    {
                        if (sink.limitExceeded)
                        {
                            handle.Abort();
                            onError?.Invoke(
                                $"Lottie download from '{url}' exceeds the configured limit of {byteLimit} bytes across redirects.");
                            yield break;
                        }

                        yield return null;
                    }

                    if (sink.limitExceeded)
                    {
                        onError?.Invoke(
                            $"Lottie download from '{url}' exceeds the configured limit of {byteLimit} bytes across redirects.");
                        yield break;
                    }

                    totalDownloaded += sink.receivedByteCount;

                    if (IsRedirectStatus(sink.statusCode))
                    {
                        if (redirects >= Mathf.Max(0, maxRedirects))
                        {
                            onError?.Invoke(
                                $"Lottie download from '{url}' exceeded the configured redirect limit of {Mathf.Max(0, maxRedirects)}.");
                            yield break;
                        }

                        if (!TryResolveRedirect(
                            currentUri,
                            sink.location,
                            out var redirectUri,
                            out var redirectError))
                        {
                            onError?.Invoke($"Refused Lottie redirect from '{url}': {redirectError}");
                            yield break;
                        }

                        if (!TryValidateRemoteUrl(redirectUri.AbsoluteUri, out var validatedUri, out redirectError))
                        {
                            onError?.Invoke($"Refused Lottie redirect from '{url}': {redirectError}");
                            yield break;
                        }

                        currentUri = validatedUri;
                        ++redirects;

                        // Leaves the try (running the finally) and goes round the loop for the next hop.
                    }
                    else if (sink.outcome != NowFetchOutcome.Success)
                    {
                        onError?.Invoke($"Failed to download Lottie from '{url}': {sink.error}");
                        yield break;
                    }
                    else
                    {
                        byte[] data = sink.GetBytes();

                        if (data == null)
                        {
                            onError?.Invoke($"Lottie download from '{url}' returned no data.");
                            yield break;
                        }

                        onLoaded?.Invoke(data);
                        yield break;
                    }
                }
                finally
                {
                    onRequestChanged?.Invoke(null);
                    handle.Dispose();
                }
            }
        }

        /// <summary>
        /// Buffers one fetch and enforces the byte cap while the bytes arrive. Every callback may run on whatever
        /// thread the provider chooses while the coroutine polls from the frame thread, so the state sits behind one
        /// lock; the browser provider raises them all on the single JS thread and the lock is then uncontended.
        /// </summary>
        sealed class NowLottieFetchSink : INowFetchSink
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

            public NowLottieFetchSink(long limit)
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
                // already says the body is too large. UnityWebRequest's Content-Length check does the same.
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

            public byte[] GetBytes()
            {
                lock (_gate)
                    return _buffer.ToArray();
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
