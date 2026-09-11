#if !NOWUI_STANDALONE
// The UnityWebRequest half of NowLottieAsset (design Docs/Standalone/StandaloneCoreDesign.md §5.2, unit U16).
// Every member below was moved verbatim out of NowLottieAsset.cs so the core half never names UnityEngine.Networking;
// nothing in it was rewritten. The engine-free counterpart is NowLottieAsset.Standalone.cs.
using NowUI.Internal;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

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

        internal static IEnumerator LoadFromUrlInternal(
            string url,
            Action<NowLottieAsset> onLoaded,
            Action<string> onError,
            Action<UnityWebRequest> onRequestChanged)
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
            Action<UnityWebRequest> onRequestChanged = null)
        {
            if (!TryValidateRemoteUrl(url, out Uri currentUri, out string validationError))
            {
                onError?.Invoke(validationError);
                yield break;
            }

            int redirects = 0;
            long totalDownloaded = 0L;
            long byteLimit = EffectiveLimit(maxDownloadBytes);

            while (true)
            {
                long remaining = Math.Max(0L, byteLimit - totalDownloaded);
                var request = new UnityWebRequest(
                    currentUri.AbsoluteUri,
                    UnityWebRequest.kHttpVerbGET);
                var downloadHandler = new NowBoundedDownloadHandler(remaining);
                request.downloadHandler = downloadHandler;
                request.disposeDownloadHandlerOnDispose = true;
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                // Redirects are followed manually so every target runs through the
                // same scheme/host policy as the original URL.
                request.redirectLimit = 0;
                onRequestChanged?.Invoke(request);

                try
                {
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        if (downloadHandler.limitExceeded ||
                            RequestExceedsLimit(request, remaining))
                        {
                            request.Abort();
                            onError?.Invoke(
                                $"Lottie download from '{url}' exceeds the configured limit of {byteLimit} bytes across redirects.");
                            yield break;
                        }

                        yield return null;
                    }

                    if (downloadHandler.limitExceeded ||
                        RequestExceedsLimit(request, remaining))
                    {
                        onError?.Invoke(
                            $"Lottie download from '{url}' exceeds the configured limit of {byteLimit} bytes across redirects.");
                        yield break;
                    }

                    totalDownloaded += downloadHandler.receivedByteCount;

                    if (IsRedirectStatus(request.responseCode))
                    {
                        if (redirects >= Mathf.Max(0, maxRedirects))
                        {
                            onError?.Invoke(
                                $"Lottie download from '{url}' exceeded the configured redirect limit of {Mathf.Max(0, maxRedirects)}.");
                            yield break;
                        }

                        if (!TryResolveRedirect(
                            currentUri,
                            request.GetResponseHeader("Location"),
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
                        continue;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onError?.Invoke($"Failed to download Lottie from '{url}': {request.error}");
                        yield break;
                    }

                    byte[] data = downloadHandler.GetBytes();

                    if (data == null)
                    {
                        onError?.Invoke($"Lottie download from '{url}' returned no data.");
                        yield break;
                    }

                    onLoaded?.Invoke(data);
                    yield break;
                }
                finally
                {
                    onRequestChanged?.Invoke(null);
                    request.Dispose();
                }
            }
        }

        static bool RequestExceedsLimit(UnityWebRequest request, long limit)
        {
            if (request.downloadedBytes > (ulong)limit)
                return true;

            string contentLength = request.GetResponseHeader("Content-Length");
            return long.TryParse(contentLength, out long declaredLength) && declaredLength > limit;
        }
    }
}
#endif
