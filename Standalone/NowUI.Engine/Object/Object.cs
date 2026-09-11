// Mirrors UnityEngine.Object for the NowUI standalone build.
// Spec: Docs/Standalone/UnityValueTypeSemantics.md (VT §13, the fake-null table).
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.4 member list, §6.3 destroy pipeline, §6.6 shutdown).
using System;
using System.Collections.Generic;
using System.Threading;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// Base of every Unity engine object. Unity's real <c>Object</c> is a managed wrapper around a native pointer, and
    /// "the native object is gone" is what produces its famous fake-null behaviour. The shim models that native pointer
    /// with a single <see cref="isDestroyed"/> flag, and reproduces every row of VT §13 from it.
    /// </summary>
    public class Object
    {
        // Unity hands runtime-created objects negative, decreasing instance ids (VT §13). Starting at -1 and
        // decrementing makes the first object -1, which is what the editor does for a freshly created instance.
        private static int s_nextInstanceId = -1;

        // Deferred destruction (design §6.3). A list rather than a Dictionary because it is drained in order once per
        // frame and is empty in the steady state.
        private static readonly List<PendingDestroy> s_DestroyQueue = new List<PendingDestroy>();
        private static readonly List<PendingDestroy> s_DrainBuffer = new List<PendingDestroy>();

        // The weak live list NowRuntime.Shutdown drains (design §6.6). Weak so that tracking never keeps a texture or a
        // mesh alive: a host that drops its last reference must still be collectable without calling Destroy.
        private static readonly List<WeakReference<Object>> s_Live = new List<WeakReference<Object>>();
        private static int s_LiveHighWater;

        private readonly int m_InstanceID;
        private string m_Name = "";
        private HideFlags m_HideFlags;

        /// <summary>Stands in for "the native object has been freed". Internal: not a Unity member.</summary>
        internal bool isDestroyed;

        protected Object()
        {
            // Interlocked rather than a bare decrement: a host may create resources from a loader thread. Unity itself
            // is main-thread-only here, so the only observable contract is uniqueness and the decreasing negative run.
            m_InstanceID = Interlocked.Decrement(ref s_nextInstanceId) + 1;
            TrackLive(this);
        }

        /// <summary>
        /// The object's name. Throws <see cref="MissingReferenceException"/> once destroyed (VT §13) - including
        /// through <c>?.</c>, because the null-conditional operator sees a non-null managed reference.
        /// </summary>
        public string name
        {
            get
            {
                ThrowIfDestroyed();
                return m_Name;
            }
            set
            {
                ThrowIfDestroyed();
                // Unity stores a null name as the empty string; nothing in the object model observes a null name.
                m_Name = value ?? "";
            }
        }

        /// <summary>Editor/serialisation flags. Throws once destroyed, exactly like <see cref="name"/> (VT §13).</summary>
        public HideFlags hideFlags
        {
            get
            {
                ThrowIfDestroyed();
                return m_HideFlags;
            }
            set
            {
                ThrowIfDestroyed();
                m_HideFlags = value;
            }
        }

        /// <summary>The instance id. Stable across destruction (VT §13), which is what keeps hash codes stable.</summary>
        public int GetInstanceID()
        {
            return m_InstanceID;
        }

        /// <summary>6000.4's newer spelling of <see cref="GetInstanceID"/>; both return the same id.</summary>
        public int GetEntityId()
        {
            return m_InstanceID;
        }

        /// <summary>The instance id, so a destroyed object hashes to what it hashed to when alive (VT §13).</summary>
        public override int GetHashCode()
        {
            return m_InstanceID;
        }

        /// <summary>
        /// Unity's <c>CompareBaseObjects</c> semantics: a non-<c>Object</c>, non-null argument is never equal, and a
        /// destroyed object equals <c>null</c> (VT §13).
        /// </summary>
        public override bool Equals(object other)
        {
            Object rhs = other as Object;
            // "not null and not an Object" -> false. A boxed struct or a string can never equal an engine object.
            //
            // ReferenceEquals, NOT `rhs == null`: the overloaded operator reports true for a *destroyed* Object, so
            // writing it the natural way would classify `destroyed.Equals(destroyed)` as "not an Object" and answer
            // false, where Unity answers true. This is the fake-null trap the whole file exists to reproduce, and it
            // bites the file that implements it just as easily as it bites core code.
            if (ReferenceEquals(rhs, null) && !ReferenceEquals(other, null))
                return false;
            return CompareBaseObjects(this, rhs);
        }

        /// <summary>
        /// Fake-null equality (VT §13). Two <i>distinct</i> destroyed objects compare unequal even though each compares
        /// equal to <c>null</c>, because the comparison falls through to their (differing) instance ids.
        /// </summary>
        public static bool operator ==(Object x, Object y)
        {
            return CompareBaseObjects(x, y);
        }

        public static bool operator !=(Object x, Object y)
        {
            return !CompareBaseObjects(x, y);
        }

        /// <summary>
        /// Truthiness: <c>if (obj)</c> is false for a real null and for a destroyed object (VT §13). Defined as
        /// <c>!(exists == null)</c> so there is exactly one place where "alive" is decided.
        /// </summary>
        public static implicit operator bool(Object exists)
        {
            return !CompareBaseObjects(exists, null);
        }

        /// <summary>
        /// <c>"{name} ({FullTypeName})"</c> while alive - the space belongs to the suffix, so an empty name gives
        /// <c>" (UnityEngine.Texture2D)"</c> - and the literal string <c>"null"</c> once destroyed (VT §13).
        /// </summary>
        public override string ToString()
        {
            if (isDestroyed)
                return "null";
            // Reads the field, not the property: ToString must never throw, and the destroyed case is handled above.
            return m_Name + " (" + GetType().FullName + ")";
        }

        /// <summary>
        /// Destroys <paramref name="obj"/>. During play the destruction is deferred to the end of the frame exactly as
        /// Unity defers it (design §6.3); outside play it happens now. <c>null</c> and already-destroyed are silent.
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null) || obj.isDestroyed)
                return;

            if (NowRuntime.isPlaying && NowRuntime.deferDestroyToEndOfFrame)
            {
                // NegativeInfinity means "the next EndFrame", with no dependence on the clock.
                Enqueue(obj, double.NegativeInfinity);
                return;
            }

            DestroyNow(obj);
        }

        /// <summary>
        /// Destroys <paramref name="obj"/> after <paramref name="t"/> seconds. Always queued, with a deadline measured
        /// on the frame clock, and drained by <c>NowRuntime.EndFrame</c> once the deadline has passed (design §6.3).
        /// </summary>
        public static void Destroy(Object obj, float t)
        {
            if (ReferenceEquals(obj, null) || obj.isDestroyed)
                return;

            Enqueue(obj, NowRuntime.timeSeconds + t);
        }

        /// <summary>Destroys now, whatever the play state is (design §6.3). <c>null</c> is silent.</summary>
        public static void DestroyImmediate(Object obj)
        {
            if (ReferenceEquals(obj, null) || obj.isDestroyed)
                return;

            DestroyNow(obj);
        }

        /// <summary>
        /// Destroys now. <paramref name="allowDestroyingAssets"/> is accepted for source compatibility and ignored:
        /// the standalone build has no asset database, so nothing is an asset.
        /// </summary>
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets)
        {
            DestroyImmediate(obj);
        }

        /// <summary>
        /// No-op. The standalone build has no scenes, so there is nothing for an object to survive (design §3.4).
        /// </summary>
        public static void DontDestroyOnLoad(Object target)
        {
        }

        /// <summary>Clones <paramref name="original"/> through its <c>CloneForInstantiate</c> override.</summary>
        public static T Instantiate<T>(T original) where T : Object
        {
            return (T)Instantiate((Object)original);
        }

        /// <summary>
        /// Clones <paramref name="original"/>. The fake-null test is deliberate: Unity refuses a destroyed original the
        /// same way it refuses a null one.
        /// </summary>
        public static Object Instantiate(Object original)
        {
            if (CompareBaseObjects(original, null))
                throw new ArgumentNullException(nameof(original), "The Object you want to instantiate is null.");

            Object clone = original.CloneForInstantiate();
            // Unity appends "(Clone)" with no separating space.
            clone.m_Name = original.m_Name + "(Clone)";
            return clone;
        }

        /// <summary>
        /// Produces the copy <see cref="Instantiate(Object)"/> returns. Overridden by <c>Material</c>, <c>Texture2D</c>
        /// and <c>Mesh</c>; every other type refuses, because Unity cannot clone it without a scene either.
        /// </summary>
        internal virtual Object CloneForInstantiate()
        {
            throw new NotSupportedException(
                "Instantiate is not supported for " + GetType().FullName + " in the NowUI standalone build.");
        }

        /// <summary>
        /// Releases backend-owned resources. Called once, before the destroyed flag is set, so an override can still
        /// read its own CPU-side state (design §6.3).
        /// </summary>
        internal virtual void OnDestroyResources()
        {
        }

        /// <summary>
        /// Adds <paramref name="o"/> to the weak list <c>NowRuntime.Shutdown</c> drains. Called automatically from the
        /// base constructor, so no subclass has to remember to call it.
        /// </summary>
        internal static void TrackLive(Object o)
        {
            if (ReferenceEquals(o, null))
                return;

            lock (s_Live)
            {
                // Amortised compaction: dead weak references are dropped once the list has doubled since the last
                // sweep, so a long session that creates and drops many textures does not grow the list without bound.
                if (s_Live.Count >= 64 && s_Live.Count >= s_LiveHighWater * 2)
                    CompactLiveNoLock();

                s_Live.Add(new WeakReference<Object>(o));
            }
        }

        /// <summary>
        /// Destroys every object still tracked, newest first. Used by <c>NowRuntime.Shutdown</c> (design §6.6).
        /// </summary>
        internal static void DestroyAllLive()
        {
            Object[] snapshot;
            lock (s_Live)
            {
                List<Object> alive = new List<Object>(s_Live.Count);
                for (int i = 0; i < s_Live.Count; i++)
                {
                    Object o;
                    if (s_Live[i].TryGetTarget(out o) && !o.isDestroyed)
                        alive.Add(o);
                }

                s_Live.Clear();
                s_LiveHighWater = 0;
                snapshot = alive.ToArray();
            }

            // Newest first: a resource created late is the most likely to depend on an earlier one.
            for (int i = snapshot.Length - 1; i >= 0; i--)
                DestroyNow(snapshot[i]);
        }

        /// <summary>
        /// Drains the deferred-destroy queue. Entries with no deadline go now; timed entries go once
        /// <paramref name="now"/> has reached their deadline. Called by <c>NowRuntime.EndFrame</c> (design §6.3).
        /// </summary>
        internal static void DrainDestroyQueue(double now)
        {
            lock (s_DestroyQueue)
            {
                if (s_DestroyQueue.Count == 0)
                    return;

                s_DrainBuffer.Clear();
                for (int i = s_DestroyQueue.Count - 1; i >= 0; i--)
                {
                    PendingDestroy entry = s_DestroyQueue[i];
                    if (entry.deadline > now)
                        continue;

                    s_DrainBuffer.Add(entry);
                    s_DestroyQueue.RemoveAt(i);
                }
            }

            // Destroyed outside the lock, and in enqueue order (the scan above walked backwards): an OnDestroy handler
            // is free to call Destroy again, and re-entering the lock from a handler would deadlock a threaded host.
            for (int i = s_DrainBuffer.Count - 1; i >= 0; i--)
            {
                Object o = s_DrainBuffer[i].target;
                if (!ReferenceEquals(o, null) && !o.isDestroyed)
                    DestroyNow(o);
            }

            s_DrainBuffer.Clear();
        }

        /// <summary>Empties the queue without destroying anything. Used by <c>NowRuntime.ResetAll</c>.</summary>
        internal static void ClearDestroyQueue()
        {
            lock (s_DestroyQueue)
            {
                s_DestroyQueue.Clear();
            }
        }

        /// <summary>How many objects are waiting to be destroyed; exposed for the shim's own tests.</summary>
        internal static int pendingDestroyCount
        {
            get
            {
                lock (s_DestroyQueue)
                {
                    return s_DestroyQueue.Count;
                }
            }
        }

        private static void CompactLiveNoLock()
        {
            int write = 0;
            for (int read = 0; read < s_Live.Count; read++)
            {
                Object o;
                if (!s_Live[read].TryGetTarget(out o))
                    continue;
                s_Live[write++] = s_Live[read];
            }

            s_Live.RemoveRange(write, s_Live.Count - write);
            s_LiveHighWater = s_Live.Count;
        }

        private static void Enqueue(Object obj, double deadline)
        {
            lock (s_DestroyQueue)
            {
                for (int i = 0; i < s_DestroyQueue.Count; i++)
                {
                    if (!ReferenceEquals(s_DestroyQueue[i].target, obj))
                        continue;

                    // Scheduling the same object twice keeps the earliest deadline, so a later Destroy(obj, 10f)
                    // cannot postpone a Destroy(obj) that is already due this frame.
                    if (deadline < s_DestroyQueue[i].deadline)
                        s_DestroyQueue[i] = new PendingDestroy(obj, deadline);
                    return;
                }

                s_DestroyQueue.Add(new PendingDestroy(obj, deadline));
            }
        }

        private static void DestroyNow(Object obj)
        {
            if (ReferenceEquals(obj, null) || obj.isDestroyed)
                return;

            obj.OnDestroyResources();

            // Only ScriptableObject has lifecycle messages in the standalone build; MonoBehaviour is deliberately not
            // provided (design §3.4), so there is no other message receiver to dispatch to.
            ScriptableObject so = obj as ScriptableObject;
            if (so != null)
                so.DispatchDestroyMessages();

            // Set last: OnDestroyResources and the messages above must still see a live object, exactly as in Unity.
            obj.isDestroyed = true;
        }

        private static bool CompareBaseObjects(Object x, Object y)
        {
            bool lhsNull = ReferenceEquals(x, null);
            bool rhsNull = ReferenceEquals(y, null);

            if (lhsNull && rhsNull)
                return true;
            if (rhsNull)
                return !IsAlive(x);
            if (lhsNull)
                return !IsAlive(y);

            return x.m_InstanceID == y.m_InstanceID;
        }

        private static bool IsAlive(Object o)
        {
            // Unity additionally asks the native side whether an object with this id still exists (editor asset
            // resurrection). There is no native side here, so the destroyed flag is the whole answer (VT §13).
            return !o.isDestroyed;
        }

        private void ThrowIfDestroyed()
        {
            if (isDestroyed)
                throw MissingReferenceException.For(GetType());
        }

        private readonly struct PendingDestroy
        {
            public readonly Object target;
            public readonly double deadline;

            public PendingDestroy(Object target, double deadline)
            {
                this.target = target;
                this.deadline = deadline;
            }
        }
    }
}
