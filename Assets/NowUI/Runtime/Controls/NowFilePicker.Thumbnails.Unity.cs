#if !NOWUI_STANDALONE
// The UnityWebRequest-driven half of the NowFilePicker thumbnail pipeline.
// Moved verbatim out of NowFilePicker.cs so the standalone (engine-free) build can
// supply its own half; see Docs/Standalone/StandaloneCoreDesign.md sections 5.2 and 12.8.
// Unity compiles this file; the standalone build compiles NowFilePicker.Thumbnails.Standalone.cs instead.

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace NowUI
{
    public partial struct NowFilePicker
    {
        sealed partial class ThumbnailEntry
        {
            public UnityWebRequest request;
            public UnityWebRequestAsyncOperation operation;
        }

        static void StartThumbnailRequest(PopupState state, ThumbnailEntry entry)
        {
            if (entry == null || entry.state != ThumbnailState.Pending)
                return;

            if (state.activeThumbnailRequests >= MaxThumbnailRequests)
            {
                NowControlState.RequestRepaint();
                return;
            }

            try
            {
                var file = new FileInfo(entry.path);

                if (!file.Exists || file.Length <= 0L || file.Length > MaxThumbnailFileBytes)
                {
                    entry.state = ThumbnailState.Failed;
                    return;
                }

                if (!TryReadEncodedImageSize(file.FullName, out int width, out int height) ||
                    !IsThumbnailSourceSizeAllowed(width, height))
                {
                    entry.state = ThumbnailState.Failed;
                    return;
                }

                var uri = new Uri(file.FullName);
                var parameters = DownloadedTextureParams.Default;
                parameters.readable = false;
                parameters.mipmapChain = false;
                parameters.linearColorSpace = false;
                var request = UnityWebRequestTexture.GetTexture(uri, parameters);
                request.timeout = 15;
                entry.request = request;
                entry.operation = request.SendWebRequest();
                entry.state = ThumbnailState.Loading;
                ++state.activeThumbnailRequests;
                NowControlState.RequestRepaint();
            }
            catch (Exception)
            {
                entry.request?.Dispose();
                entry.request = null;
                entry.operation = null;
                entry.state = ThumbnailState.Failed;
            }
        }

        static void PollThumbnailRequests(PopupState state)
        {
            if (state.thumbnails.Count == 0)
                return;

            foreach (var pair in state.thumbnails)
            {
                var entry = pair.Value;

                if (entry.state == ThumbnailState.Loading &&
                    entry.operation != null &&
                    entry.operation.isDone)
                {
                    CompleteThumbnailRequest(state, entry);
                }
            }

            if (state.activeThumbnailRequests > 0)
                NowControlState.RequestRepaint();

            TrimThumbnailCache(state, null);
        }

        static void CompleteThumbnailRequest(PopupState state, ThumbnailEntry entry)
        {
            var request = entry.request;
            entry.request = null;
            entry.operation = null;
            state.activeThumbnailRequests = Mathf.Max(0, state.activeThumbnailRequests - 1);
            Texture2D source = null;

            try
            {
                if (request == null || request.result != UnityWebRequest.Result.Success)
                {
                    entry.state = ThumbnailState.Failed;
                    return;
                }

                source = DownloadHandlerTexture.GetContent(request);

                if (source == null ||
                    source.width < 1 ||
                    source.height < 1 ||
                    !IsThumbnailSourceSizeAllowed(source.width, source.height))
                {
                    entry.state = ThumbnailState.Failed;
                    return;
                }

                entry.dimensions = source.width + " × " + source.height;
                Texture thumbnail = CreateThumbnailTexture(source, ThumbnailDimension);

                if (thumbnail == null)
                {
                    entry.state = ThumbnailState.Failed;
                    return;
                }

                if (!ReferenceEquals(thumbnail, source))
                {
                    DestroyThumbnailTexture(source);
                    source = null;
                }

                entry.texture = thumbnail;
                entry.state = ThumbnailState.Loaded;
            }
            catch (Exception)
            {
                entry.state = ThumbnailState.Failed;
            }
            finally
            {
                request?.Dispose();

                if (entry.state != ThumbnailState.Loaded && source != null)
                    DestroyThumbnailTexture(source);

                NowControlState.RequestRepaint();
            }
        }

        // Extracted (not moved): the shared abort/dispose sequence that TrimThumbnailCache and
        // CancelThumbnailRequests used to perform inline. See design section 12.8.
        static void AbortThumbnailRequest(ThumbnailEntry entry)
        {
            entry.request?.Abort();
            entry.request?.Dispose();
            entry.request = null;
            entry.operation = null;
        }
    }
}
#endif
