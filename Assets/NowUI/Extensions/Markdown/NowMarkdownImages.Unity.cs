#if !NOWUI_STANDALONE
// The UnityWebRequest half of NowMarkdownImages (design Docs/Standalone/StandaloneCoreDesign.md §5.2, unit U18).
// Every member below except CompleteDownload was moved verbatim out of NowMarkdownImages.cs so the core half never
// names UnityEngine.Networking or MonoBehaviour; nothing in them was rewritten. CompleteDownload keeps the request
// bookkeeping that is specific to UnityWebRequest and hands the response to the core's FinishDownload, which carries
// the redirect, transport-error, decode and cache-state policy that both transports share.
// The engine-free counterpart is NowMarkdownImages.Standalone.cs.
using System;
using NowUI.Internal;
using UnityEngine;
using UnityEngine.Networking;

namespace NowUI.Markdown
{
    public static partial class NowMarkdownImages
    {
        sealed partial class Entry
        {
            public UnityWebRequest request;
            public NowBoundedDownloadHandler downloadHandler;
            public UnityWebRequestAsyncOperation operation;
        }

        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                Tick();
            }
        }

        static Runner _runner;

        static void PollDownloads()
        {
            long byteLimit = EffectiveLimit(maxDownloadBytes);

            for (int i = _active.Count - 1; i >= 0; --i)
            {
                var entry = _active[i];

                if (entry.completed || entry.request == null)
                    continue;

                long remaining = Math.Max(0L, byteLimit - entry.downloadedBytes);

                if ((entry.downloadHandler?.limitExceeded ?? false) ||
                    RequestExceedsLimit(entry.request, remaining))
                {
                    entry.forcedError =
                        $"Markdown image download from '{entry.url}' exceeds the configured limit of {byteLimit} bytes across redirects.";
                    entry.request.Abort();
                }

                if (entry.operation != null && entry.operation.isDone)
                    CompleteDownload(entry);
            }
        }

        static void StartDownload(Entry entry)
        {
            GetRunner();

            try
            {
                long byteLimit = EffectiveLimit(maxDownloadBytes);
                long remaining = Math.Max(0L, byteLimit - entry.downloadedBytes);
                var request = new UnityWebRequest(
                    entry.currentUri.AbsoluteUri,
                    UnityWebRequest.kHttpVerbGET);
                entry.request = request;
                var downloadHandler = new NowBoundedDownloadHandler(remaining);
                entry.downloadHandler = downloadHandler;
                request.downloadHandler = downloadHandler;
                request.disposeDownloadHandlerOnDispose = true;
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                // Follow redirects ourselves so each target is checked by the URL policy.
                request.redirectLimit = 0;
                entry.active = true;
                _active.Add(entry);
                entry.operation = request.SendWebRequest();
                entry.operation.completed += _ => CompleteDownload(entry);

                if (entry.operation.isDone)
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

            var request = entry.request;
            var downloadHandler = entry.downloadHandler;
            entry.request = null;
            entry.downloadHandler = null;
            entry.operation = null;

            bool current = _entries.TryGetValue(entry.url, out var cached) && ReferenceEquals(cached, entry);

            if (!current)
            {
                if (request != null)
                    request.Dispose();
                else
                    downloadHandler?.Dispose();

                PumpDownloads();
                return;
            }

            string error = entry.forcedError;
            long byteLimit = EffectiveLimit(maxDownloadBytes);
            long remaining = Math.Max(0L, byteLimit - entry.downloadedBytes);

            if (error == null && (downloadHandler?.limitExceeded ?? false))
            {
                error =
                    $"The image response exceeds the configured limit of {byteLimit} bytes across redirects.";
            }
            else if (error == null && request != null && RequestExceedsLimit(request, remaining))
            {
                error =
                    $"The image response exceeds the configured limit of {byteLimit} bytes across redirects.";
            }

            if (downloadHandler != null)
                entry.downloadedBytes += downloadHandler.receivedByteCount;

            // Every accessor below is read under the same condition that guarded it inside CompleteDownload before
            // the split, so no property is touched on a path that did not touch it before. `responseCode` and
            // `result` were both reached only through `error == null && request != null` (the redirect test and the
            // short-circuit in the transport test); `error` only when the result was not Success; `Location` only
            // inside the redirect branch. A non-null `error` here is the forced error or the byte-cap breach, and on
            // those paths the original read nothing off the request at all.
            long status = error == null && request != null ? request.responseCode : 0L;
            string location = error == null && request != null && IsRedirectStatus(status)
                ? request.GetResponseHeader("Location")
                : null;
            string requestError = error != null
                ? null
                : request == null
                    ? "The image request was not created."
                    : (request.result != UnityWebRequest.Result.Success ? request.error : null);

            // GetBytes stays as lazy as it was inside CompleteDownload: it is read only on the path that used to
            // reach `byte[] data = downloadHandler?.GetBytes();`, because on an aborted transfer the handler has no
            // consolidated payload yet and would copy the partial bytes for nothing. The condition is FinishDownload's
            // own precondition for the decode branch: no error so far, no transport error, and not a redirect.
            byte[] bytes = error == null && requestError == null && !IsRedirectStatus(status)
                ? downloadHandler?.GetBytes()
                : null;

            // The request is disposed before the policy runs rather than after the decode. The bytes have already
            // been taken from the handler, and on the redirect path this is the same `request.Dispose()` that used to
            // sit immediately before the re-queue.
            if (request != null)
                request.Dispose();
            else
                downloadHandler?.Dispose();

            FinishDownload(entry, bytes, status, location, error, requestError);
        }

        static bool RequestExceedsLimit(UnityWebRequest request, long limit)
        {
            if (request.downloadedBytes > (ulong)limit)
                return true;

            string contentLength = request.GetResponseHeader("Content-Length");
            return long.TryParse(contentLength, out long declaredLength) && declaredLength > limit;
        }

        static void AbortRequest(Entry entry)
        {
            var request = entry.request;
            var downloadHandler = entry.downloadHandler;
            entry.request = null;
            entry.downloadHandler = null;
            entry.operation = null;

            if (request != null)
            {
                request.Abort();
                request.Dispose();
            }
            else
            {
                downloadHandler?.Dispose();
            }
        }

        static void DestroyRunner()
        {
            if (_runner != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(_runner.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(_runner.gameObject);

                _runner = null;
            }
        }

        static Runner GetRunner()
        {
            if (_runner != null)
                return _runner;

            var go = new GameObject("Now Markdown Image Cache")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            return _runner;
        }
    }
}
#endif
