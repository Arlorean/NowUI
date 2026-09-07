#if !NOWUI_STANDALONE
// The UnityWebRequest and coroutine half of NowLottieCache (design Docs/Standalone/StandaloneCoreDesign.md 5.2, unit U17).
// Runner, Load and GetRunner were moved verbatim out of NowLottieCache.cs so the core half never names MonoBehaviour,
// Coroutine or UnityEngine.Networking; nothing in them was rewritten. The five small statics above them are the
// extraction the design calls for: they carry the transport-specific statements that used to sit inline in Reset,
// PumpLoads and RemoveEntry, in the same order, so the core half keeps the whole state machine and neither half owns
// a second copy of it. The engine-free counterpart is NowLottieCache.Standalone.cs.
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace NowUI
{
    public static partial class NowLottieCache
    {
        sealed partial class Entry
        {
            public Coroutine coroutine;
            public UnityWebRequest request;
        }

        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                Tick();
            }
        }

        static Runner _runner;

        /// <summary>
        /// Starts the load coroutine for one entry, or answers false when no runner could be created - which is the
        /// condition PumpLoads reports as "Could not create Lottie cache runner.". The two bookkeeping statements are
        /// kept here, ahead of StartCoroutine, because that is where they ran before the split: StartCoroutine runs the
        /// body up to its first yield, so moving them after it would let the coroutine observe a stale _activeLoads.
        /// </summary>
        static bool StartLoad(Entry entry)
        {
            var runner = GetRunner();

            if (runner == null)
                return false;

            entry.active = true;
            ++_activeLoads;
            entry.coroutine = runner.StartCoroutine(Load(entry));
            return true;
        }

        /// <summary>
        /// Aborts an entry's request without disposing it: the coroutine that owns the request is still running and
        /// its finally block does the dispose. This is the RemoveEntry site, and the first pass of Reset.
        /// </summary>
        static void AbortLoad(Entry entry)
        {
            if (entry.request != null)
                entry.request.Abort();
        }

        /// <summary>Stops every in-flight load coroutine, so no finally block will run and dispose its request.</summary>
        static void StopLoads()
        {
            if (_runner != null)
                _runner.StopAllCoroutines();
        }

        /// <summary>
        /// Disposes an entry's request after StopLoads has stopped the coroutine that would otherwise have done it.
        /// </summary>
        static void DisposeLoad(Entry entry)
        {
            if (entry.request != null)
            {
                entry.request.Dispose();
                entry.request = null;
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

        static IEnumerator Load(Entry entry)
        {
            NowLottieAsset loaded = null;
            string error = null;

            yield return NowLottieAsset.LoadFromUrlInternal(
                entry.url,
                asset => loaded = asset,
                value => error = value,
                request =>
                {
                    entry.request = request;

                    if (request != null &&
                        (!_entries.TryGetValue(entry.url, out var current) || !ReferenceEquals(current, entry)))
                    {
                        request.Abort();
                    }
                });

            entry.request = null;
            entry.coroutine = null;

            if (entry.active)
            {
                entry.active = false;
                _activeLoads = Mathf.Max(0, _activeLoads - 1);
            }

            if (!_entries.TryGetValue(entry.url, out var current) || !ReferenceEquals(current, entry))
            {
                NowLottieAsset.DestroyRuntimeAsset(loaded);
                PumpLoads();
                yield break;
            }

            if (error != null)
            {
                entry.state = NowLottieCacheState.Failed;
                entry.error = error;
                NowLottieAsset.DestroyRuntimeAsset(loaded);
                PumpLoads();
                yield break;
            }

            if (loaded == null)
            {
                entry.state = NowLottieCacheState.Failed;
                entry.error = $"Failed to load Lottie from '{entry.url}'.";
                PumpLoads();
                yield break;
            }

            entry.asset = loaded;
            entry.state = NowLottieCacheState.Loaded;
            entry.error = null;
            entry.ownsAsset = true;
            Touch(entry);
            TrimCache(entry);
            PumpLoads();
        }

        static Runner GetRunner()
        {
            if (_runner != null)
                return _runner;

            var go = new GameObject("Now Lottie Cache")
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
