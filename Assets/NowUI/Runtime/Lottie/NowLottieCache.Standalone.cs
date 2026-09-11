#if NOWUI_STANDALONE
// The engine-free half of NowLottieCache (design Docs/Standalone/StandaloneCoreDesign.md 4.4 and 5.2, unit U17).
// It replaces the MonoBehaviour runner, the Coroutine handle and the UnityWebRequest of NowLottieCache.Unity.cs with
// the same five statics implemented over NowRuntime.onFrame and INowFetchHandle, so the cache in NowLottieCache.cs is
// the only copy of the state machine: it still queues, trims, evicts and counts loads exactly as it does in Unity.
//
// The load itself is the standalone NowLottieAsset.LoadFromUrlInternal, which is an IEnumerator for the same reason
// the Unity one is - it is the same coroutine - so this half carries the small scheduler that Unity supplies:
// Routine drives one enumerator per frame, and PollLoads steps every live routine from the frame event before the
// core Tick trims and pumps.
using NowUI.Engine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NowUI
{
    public static partial class NowLottieCache
    {
        sealed partial class Entry
        {
            public Routine routine;
            public INowFetchHandle handle;
        }

        static readonly List<Entry> _running = new List<Entry>(4);

        /// <summary>
        /// Reused snapshot of <see cref="_running"/>. A routine that finishes calls back into PumpLoads, which can
        /// start another load and so append to _running while it is being walked; stepping a copy is what keeps that
        /// safe without allocating per frame.
        /// </summary>
        static readonly List<Entry> _pumping = new List<Entry>(4);

        /// <summary>
        /// Starts the load routine for one entry. Unlike the Unity half this cannot fail, so PumpLoads never reports
        /// "Could not create Lottie cache runner." here. The routine is deliberately not stepped now: StartLoad runs
        /// inside PumpLoads' own loop, and a routine that failed fast would re-enter PumpLoads from within it.
        /// </summary>
        static bool StartLoad(Entry entry)
        {
            EnsureTicking();

            entry.active = true;
            ++_activeLoads;
            entry.routine = new Routine(Load(entry));
            _running.Add(entry);
            return true;
        }

        /// <summary>
        /// Aborts an entry's fetch without disposing it: the routine that owns the handle is still running and its
        /// finally block does the dispose. This is the RemoveEntry site, and the first pass of Reset.
        /// </summary>
        static void AbortLoad(Entry entry)
        {
            if (entry.handle != null)
                entry.handle.Abort();
        }

        /// <summary>
        /// Drops every live routine, the counterpart of StopAllCoroutines: no finally block runs, which is why Reset
        /// disposes the handles itself in the pass that follows.
        /// </summary>
        static void StopLoads()
        {
            for (int i = 0; i < _running.Count; ++i)
                _running[i].routine = null;

            _running.Clear();
        }

        /// <summary>Disposes an entry's fetch after StopLoads dropped the routine that would otherwise have done it.</summary>
        static void DisposeLoad(Entry entry)
        {
            if (entry.handle != null)
            {
                entry.handle.Dispose();
                entry.handle = null;
            }
        }

        static void DestroyRunner()
        {
            NowRuntime.onFrame -= FrameTick;
        }

        /// <summary>
        /// Subscribes the frame handler that takes the place of the Unity half's runner GameObject. The
        /// unsubscribe-then-subscribe pair is what keeps it idempotent and what re-arms the cache after
        /// <c>NowRuntime.Shutdown</c> has cleared every <c>onFrame</c> handler.
        /// </summary>
        static void EnsureTicking()
        {
            NowRuntime.onFrame -= FrameTick;
            NowRuntime.onFrame += FrameTick;
        }

        /// <summary>
        /// One frame of the cache: step the live routines, then run the core <see cref="Tick"/> that the Unity half's
        /// Runner.Update calls. Tick itself is untouched by this split, which is why it is called rather than inlined.
        /// </summary>
        static void FrameTick()
        {
            PollLoads();
            Tick();
        }

        static void PollLoads()
        {
            if (_running.Count == 0)
                return;

            _pumping.Clear();
            _pumping.AddRange(_running);

            for (int i = 0; i < _pumping.Count; ++i)
            {
                var entry = _pumping[i];
                // Re-read per iteration: an earlier routine finishing can have run Reset, which drops every routine.
                var routine = entry.routine;

                if (routine == null)
                {
                    _running.Remove(entry);
                    continue;
                }

                if (routine.MoveNext())
                    continue;

                if (ReferenceEquals(entry.routine, routine))
                    entry.routine = null;

                _running.Remove(entry);
            }

            _pumping.Clear();
        }

        /// <summary>
        /// The standalone counterpart of the Unity half's Load coroutine, statement for statement: the only
        /// differences are the handle type the observer is handed and the routine field it clears.
        /// </summary>
        static IEnumerator Load(Entry entry)
        {
            NowLottieAsset loaded = null;
            string error = null;

            yield return NowLottieAsset.LoadFromUrlInternal(
                entry.url,
                asset => loaded = asset,
                value => error = value,
                handle =>
                {
                    entry.handle = handle;

                    if (handle != null &&
                        (!_entries.TryGetValue(entry.url, out var current) || !ReferenceEquals(current, entry)))
                    {
                        handle.Abort();
                    }
                });

            entry.handle = null;
            entry.routine = null;

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

        /// <summary>
        /// Drives one <see cref="IEnumerator"/> the way Unity's coroutine scheduler does: a nested enumerator yielded
        /// by the body is run to completion before the body resumes, and any other yielded value suspends the routine
        /// until the next frame. It is the stand-in for the <c>Coroutine</c> handle the Unity half stores on the entry.
        /// </summary>
        sealed class Routine
        {
            readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

            public Routine(IEnumerator root)
            {
                _stack.Push(root);
            }

            /// <summary>Advances the routine by one frame. False once it has finished.</summary>
            public bool MoveNext()
            {
                while (_stack.Count > 0)
                {
                    IEnumerator top = _stack.Peek();

                    if (!top.MoveNext())
                    {
                        _stack.Pop();
                        continue;
                    }

                    if (top.Current is IEnumerator nested)
                    {
                        _stack.Push(nested);
                        continue;
                    }

                    return true;
                }

                return false;
            }
        }
    }
}
#endif
