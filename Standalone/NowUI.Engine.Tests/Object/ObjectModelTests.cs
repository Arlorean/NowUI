// Tests for U3 (object model): UnityEngine.Object's fake-null semantics, ScriptableObject message dispatch, the inert
// scene stubs, the default host services and the NowRuntime skeleton.
// Every row of Docs/Standalone/UnityValueTypeSemantics.md §13 is asserted here, plus the design's destroy pipeline
// (Docs/Standalone/StandaloneCoreDesign.md §6.3), CreateInstance dispatch (§6.5) and the frame contract (§6.1).
using System;
using System.Collections.Generic;
using System.Reflection;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NowUI.Engine.Tests
{
    /// <summary>
    /// A ScriptableObject that records the lifecycle messages it receives, in order. Declared in the <i>test</i>
    /// assembly on purpose: design §3.4 requires CreateInstance to work through Activator rather than a registry
    /// precisely so subclasses outside NowUI.Runtime work.
    /// </summary>
    public class RecordingAsset : ScriptableObject
    {
        public readonly List<string> messages = new List<string>();

        private void Awake()
        {
            messages.Add("Awake");
        }

        private void OnEnable()
        {
            messages.Add("OnEnable");
        }

        private void OnDisable()
        {
            messages.Add("OnDisable");
        }

        private void OnDestroy()
        {
            messages.Add("OnDestroy");
        }
    }

    /// <summary>Subclass of <see cref="RecordingAsset"/>: its private messages must run after the base's.</summary>
    public class DerivedRecordingAsset : RecordingAsset
    {
        private void Awake()
        {
            messages.Add("DerivedAwake");
        }

        private void OnEnable()
        {
            messages.Add("DerivedOnEnable");
        }
    }

    /// <summary>Base with a virtual message, to prove an override is dispatched exactly once.</summary>
    public class VirtualMessageAsset : ScriptableObject
    {
        public readonly List<string> messages = new List<string>();

        protected virtual void OnEnable()
        {
            messages.Add("Base");
        }
    }

    public class OverridingMessageAsset : VirtualMessageAsset
    {
        protected override void OnEnable()
        {
            messages.Add("Override");
        }
    }

    /// <summary>A ScriptableObject with only a private constructor, which is the usual shape of a Unity asset class.</summary>
    public class PrivateConstructorAsset : ScriptableObject
    {
        private PrivateConstructorAsset()
        {
        }
    }

    /// <summary>A ScriptableObject whose OnEnable throws; Unity logs and carries on.</summary>
    public class ThrowingAsset : ScriptableObject
    {
        private void OnEnable()
        {
            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>Records what a ResetAll's [RuntimeInitializeOnLoadMethod] scan invoked.</summary>
    public static class ResetProbe
    {
        public static readonly List<string> calls = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Late()
        {
            calls.Add("Late");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Early()
        {
            calls.Add("Early");
        }

        [RuntimeInitializeOnLoadMethod]
        public static void Default()
        {
            calls.Add("Default");
        }
    }

    /// <summary>
    /// A mis-declared initializer: the attribute is on an instance method. ResetAll must warn and skip rather than
    /// throw (design §6.2 step 2).
    /// </summary>
    public class InstanceResetProbe
    {
        [RuntimeInitializeOnLoadMethod]
        public void NotStatic()
        {
            ResetProbe.calls.Add("NotStatic");
        }
    }

    /// <summary>Collects log lines so the tests can assert what the shim reported.</summary>
    public sealed class RecordingLogger : INowLogger
    {
        public readonly List<string> lines = new List<string>();

        public void Log(LogType type, string message, Exception exception, Object context)
        {
            lines.Add(type + "|" + message);
        }
    }

    /// <summary>A host whose clock the test drives by hand.</summary>
    public sealed class ManualClock : INowClock
    {
        public double seconds;

        public double realtimeSeconds => seconds;
    }

    public sealed class TestHost : INowHostServices
    {
        private readonly DefaultHostServices m_Fallback = new DefaultHostServices();

        public ManualClock manualClock = new ManualClock();
        public RecordingLogger recordingLogger = new RecordingLogger();

        public INowClock clock => manualClock;

        public NowScreenInfo screen => m_Fallback.screen;

        public INowLogger logger => recordingLogger;

        public INowClipboard clipboard => null;

        public INowTouchKeyboard touchKeyboard => null;

        public INowResourceProvider resources => NowEmptyResourceProvider.instance;

        public INowImageDecoder imageDecoder => null;

        public INowFetchProvider fetch => null;

        public RuntimePlatform platform => m_Fallback.platform;

        public string persistentDataPath => m_Fallback.persistentDataPath;

        public string dataPath => m_Fallback.dataPath;

        public string[] layerNames => m_Fallback.layerNames;
    }

    /// <summary>
    /// VT §13's table, row by row. The fixture leaves NowRuntime's play state exactly as it found it: the flag is
    /// process-wide and other fixtures in this assembly read it.
    /// </summary>
    [TestFixture]
    public class ObjectFakeNullTests
    {
        private bool m_SavedIsPlaying;
        private bool m_SavedDefer;

        [SetUp]
        public void SetUp()
        {
            m_SavedIsPlaying = NowRuntime.isPlaying;
            m_SavedDefer = NowRuntime.deferDestroyToEndOfFrame;

            // Immediate destruction unless a test says otherwise: it keeps the fake-null rows independent of the
            // frame loop, and it is the mode the Tests host runs in.
            NowRuntime.isPlaying = false;
            NowRuntime.deferDestroyToEndOfFrame = true;
        }

        [TearDown]
        public void TearDown()
        {
            NowRuntime.isPlaying = m_SavedIsPlaying;
            NowRuntime.deferDestroyToEndOfFrame = m_SavedDefer;
            Object.ClearDestroyQueue();
        }

        private static ScriptableObject Alive(string name)
        {
            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            o.name = name;
            return o;
        }

        private static ScriptableObject Destroyed(string name)
        {
            ScriptableObject o = Alive(name);
            Object.DestroyImmediate(o);
            return o;
        }

        [Test]
        public void EqualsNull_IsTrueOnlyWhenDestroyedOrNull()
        {
            ScriptableObject alive = Alive("a");
            ScriptableObject dead = Destroyed("d");
            ScriptableObject none = null;

            Assert.That(alive == null, Is.False);
            Assert.That(dead == null, Is.True);
            Assert.That(none == null, Is.True);

            // The reversed operand order goes through the same CompareBaseObjects path.
            Assert.That(null == alive, Is.False);
            Assert.That(null == dead, Is.True);
        }

        [Test]
        public void NotEqualsNull_IsTheNegation()
        {
            ScriptableObject alive = Alive("a");
            ScriptableObject dead = Destroyed("d");

            Assert.That(alive != null, Is.True);
            Assert.That(dead != null, Is.False);
        }

        [Test]
        public void ReferenceEqualsNull_IsFalseForADestroyedObject()
        {
            ScriptableObject dead = Destroyed("d");

            // This is the row that makes fake-null "fake": the managed reference is still there.
            Assert.That((object)dead == null, Is.False);
            Assert.That(ReferenceEquals(dead, null), Is.False);
        }

        [Test]
        public void ImplicitBool_IsFalseForDestroyedAndForNull()
        {
            ScriptableObject alive = Alive("a");
            ScriptableObject dead = Destroyed("d");
            ScriptableObject none = null;

            Assert.That((bool)alive, Is.True);
            Assert.That((bool)dead, Is.False);
            Assert.That((bool)none, Is.False);

            // The ternary/if form core code actually writes.
            Assert.That(dead ? 1 : 0, Is.EqualTo(0));
            Assert.That(alive ? 1 : 0, Is.EqualTo(1));
        }

        [Test]
        public void Equals_FollowsCompareBaseObjects()
        {
            ScriptableObject alive = Alive("a");
            ScriptableObject other = Alive("b");
            ScriptableObject dead = Destroyed("d");

            Assert.That(alive.Equals(null), Is.False);
            Assert.That(dead.Equals(null), Is.True);
            Assert.That(alive.Equals(other), Is.False);
            Assert.That(alive.Equals(alive), Is.True);

            // A destroyed object still equals itself: both sides are non-null so the ids are compared.
            Assert.That(dead.Equals(dead), Is.True);

            // Not an Object -> never equal, whatever the state.
            Assert.That(alive.Equals("a"), Is.False);
            Assert.That(dead.Equals("d"), Is.False);
            Assert.That(alive.Equals(7), Is.False);
        }

        [Test]
        public void TwoDistinctDestroyedObjects_AreNotEqualToEachOther()
        {
            ScriptableObject a = Destroyed("a");
            ScriptableObject b = Destroyed("b");

            // Each equals null, but they do not equal each other: the comparison never reaches the "both null" case
            // and falls through to their differing instance ids.
            Assert.That(a == null, Is.True);
            Assert.That(b == null, Is.True);
            Assert.That(a == b, Is.False);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void SameObject_ComparesEqualAliveAndDestroyed()
        {
            ScriptableObject o = Alive("o");

            // CS1718 ("comparison made to same variable") is the point: `obj == obj` runs the overloaded operator,
            // and VT §13 says it stays true after destruction because both sides carry the same instance id.
#pragma warning disable CS1718
            Assert.That(o == o, Is.True);

            Object.DestroyImmediate(o);
            Assert.That(o == o, Is.True);
#pragma warning restore CS1718
        }

        [Test]
        public void NullConditionalName_ThrowsBecauseTheReferenceIsNotNull()
        {
            ScriptableObject dead = Destroyed("d");

            // ?. tests the managed reference, which is non-null, so the getter runs and throws. This is exactly the
            // Unity behaviour that makes `obj?.name` a fake-null bypass.
            Assert.Throws<MissingReferenceException>(() => { string _ = dead?.name; });
        }

        [Test]
        public void NullCoalescing_ReturnsTheDestroyedObject()
        {
            ScriptableObject dead = Destroyed("d");
            ScriptableObject fallback = Alive("fallback");

            ScriptableObject result = dead ?? fallback;

            // ?? also tests the managed reference, so the destroyed object wins over the fallback.
            Assert.That(ReferenceEquals(result, dead), Is.True);
        }

        [Test]
        public void TypeTests_SurviveDestruction()
        {
            ScriptableObject dead = Destroyed("d");

            Assert.That(dead is ScriptableObject, Is.True);
            Assert.That(dead is Object, Is.True);
            Assert.That(dead as ScriptableObject, Is.Not.Null);
        }

        [Test]
        public void GetHashCode_AndInstanceId_AreStableAcrossDestruction()
        {
            ScriptableObject o = Alive("o");
            int hash = o.GetHashCode();
            int id = o.GetInstanceID();

            Assert.That(hash, Is.EqualTo(id));
            Assert.That(id, Is.LessThan(0), "runtime-created instance ids are negative");
            Assert.That(o.GetEntityId(), Is.EqualTo(id));

            Object.DestroyImmediate(o);

            Assert.That(o.GetHashCode(), Is.EqualTo(hash));
            Assert.That(o.GetInstanceID(), Is.EqualTo(id));
            Assert.That(o.GetEntityId(), Is.EqualTo(id));
        }

        [Test]
        public void InstanceIds_AreUnique()
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < 32; i++)
                Assert.That(ids.Add(Alive("o" + i).GetInstanceID()), Is.True);
        }

        [Test]
        public void ToString_IsNameSpaceParenTypeAliveAndTheStringNullWhenDestroyed()
        {
            ScriptableObject named = Alive("Probe");
            Assert.That(named.ToString(), Is.EqualTo("Probe (NowUI.Engine.Tests.PrivateConstructorAsset)"));

            ScriptableObject empty = Alive("");
            // An empty name keeps the separating space: " (Type)". VT §13 records exactly this.
            Assert.That(empty.ToString(), Is.EqualTo(" (NowUI.Engine.Tests.PrivateConstructorAsset)"));

            Object.DestroyImmediate(named);
            Assert.That(named.ToString(), Is.EqualTo("null"));
        }

        [Test]
        public void NameAndHideFlags_ThrowMissingReferenceExceptionWhenDestroyed()
        {
            ScriptableObject o = Alive("o");
            o.hideFlags = HideFlags.DontSave;
            Assert.That(o.hideFlags, Is.EqualTo(HideFlags.DontSave));

            Object.DestroyImmediate(o);

            Assert.Throws<MissingReferenceException>(() => { string _ = o.name; });
            Assert.Throws<MissingReferenceException>(() => { o.name = "x"; });
            Assert.Throws<MissingReferenceException>(() => { HideFlags _ = o.hideFlags; });
            Assert.Throws<MissingReferenceException>(() => { o.hideFlags = HideFlags.None; });
        }

        [Test]
        public void MissingReferenceException_CarriesUnitysMessage()
        {
            ScriptableObject o = Destroyed("o");

            MissingReferenceException e = Assert.Throws<MissingReferenceException>(() => { string _ = o.name; });
            Assert.That(
                e.Message,
                Is.EqualTo(
                    "The object of type 'NowUI.Engine.Tests.PrivateConstructorAsset' has been destroyed but you are " +
                    "still trying to access it.\nYour script should either check if it is null or you should not " +
                    "destroy the object."));

            // Unity's exception derives from SystemException, so core `catch (SystemException)` sites behave the same.
            Assert.That(e, Is.InstanceOf<SystemException>());
        }

        [Test]
        public void NameSetter_TurnsNullIntoTheEmptyString()
        {
            ScriptableObject o = Alive("o");
            o.name = null;
            Assert.That(o.name, Is.EqualTo(string.Empty));
        }

        [Test]
        public void DestroyingNullOrAnAlreadyDestroyedObject_IsSilent()
        {
            Assert.DoesNotThrow(() => Object.Destroy(null));
            Assert.DoesNotThrow(() => Object.Destroy(null, 1f));
            Assert.DoesNotThrow(() => Object.DestroyImmediate(null));
            Assert.DoesNotThrow(() => Object.DestroyImmediate(null, true));

            ScriptableObject o = Alive("o");
            Object.DestroyImmediate(o);
            Assert.DoesNotThrow(() => Object.DestroyImmediate(o));
            Assert.DoesNotThrow(() => Object.Destroy(o));
        }

        [Test]
        public void DoubleDestroy_DispatchesTheMessagesOnlyOnce()
        {
            RecordingAsset o = ScriptableObject.CreateInstance<RecordingAsset>();
            o.messages.Clear();

            Object.DestroyImmediate(o);
            Object.DestroyImmediate(o);

            Assert.That(o.messages, Is.EqualTo(new[] { "OnDisable", "OnDestroy" }));
        }

        [Test]
        public void DontDestroyOnLoad_IsANoOp()
        {
            ScriptableObject o = Alive("o");
            Assert.DoesNotThrow(() => Object.DontDestroyOnLoad(o));
            Assert.DoesNotThrow(() => Object.DontDestroyOnLoad(null));
            Assert.That(o == null, Is.False);
        }

        [Test]
        public void Instantiate_RefusesNullAndDestroyedOriginals()
        {
            Assert.Throws<ArgumentNullException>(() => Object.Instantiate((Object)null));

            ScriptableObject dead = Destroyed("d");
            // A destroyed original is refused like a null one: Instantiate uses the fake-null test, not ReferenceEquals.
            Assert.Throws<ArgumentNullException>(() => Object.Instantiate(dead));
        }

        [Test]
        public void Instantiate_RefusesTypesThatCannotClone()
        {
            ScriptableObject o = Alive("o");
            // Only Material/Texture2D/Mesh override CloneForInstantiate (design §3.4).
            Assert.Throws<NotSupportedException>(() => Object.Instantiate(o));
        }
    }

    /// <summary>The design §6.3 destroy pipeline: deferred while playing, immediate otherwise, timed by the clock.</summary>
    [TestFixture]
    public class ObjectDestroyPipelineTests
    {
        private bool m_SavedIsPlaying;
        private bool m_SavedDefer;
        private INowHostServices m_SavedHost;
        private INowRenderBackend m_SavedBackend;
        private TestHost m_Host;

        [SetUp]
        public void SetUp()
        {
            m_SavedIsPlaying = NowRuntime.isPlaying;
            m_SavedDefer = NowRuntime.deferDestroyToEndOfFrame;
            m_SavedHost = NowRuntime.host;
            m_SavedBackend = NowRuntime.backend;

            m_Host = new TestHost();
            NowRuntime.Initialize(m_Host, null);
            NowRuntime.deferDestroyToEndOfFrame = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.ClearDestroyQueue();
            NowRuntime.isPlaying = m_SavedIsPlaying;
            NowRuntime.deferDestroyToEndOfFrame = m_SavedDefer;
            NowRuntime.Initialize(m_SavedHost, m_SavedBackend);
        }

        [Test]
        public void Destroy_WhilePlaying_BecomesVisibleOnlyAfterEndFrame()
        {
            NowRuntime.isPlaying = true;

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o);

            // Still alive within the frame, exactly as in a Unity player.
            Assert.That(o == null, Is.False);
            Assert.That(Object.pendingDestroyCount, Is.EqualTo(1));

            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();

            Assert.That(o == null, Is.True);
            Assert.That(Object.pendingDestroyCount, Is.EqualTo(0));
        }

        [Test]
        public void Destroy_WhenNotPlaying_IsImmediate()
        {
            NowRuntime.isPlaying = false;

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o);

            Assert.That(o == null, Is.True);
            Assert.That(Object.pendingDestroyCount, Is.EqualTo(0));
        }

        [Test]
        public void Destroy_WithDeferralOff_IsImmediateEvenWhilePlaying()
        {
            NowRuntime.isPlaying = true;
            NowRuntime.deferDestroyToEndOfFrame = false;

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o);

            Assert.That(o == null, Is.True);
        }

        [Test]
        public void DestroyImmediate_IsImmediateWhilePlaying()
        {
            NowRuntime.isPlaying = true;

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.DestroyImmediate(o);

            Assert.That(o == null, Is.True);
            Assert.That(Object.pendingDestroyCount, Is.EqualTo(0));
        }

        [Test]
        public void DestroyWithDelay_WaitsForTheDeadlineOnTheFrameClock()
        {
            NowRuntime.isPlaying = true;

            // Frame 1 at t = 0 establishes the time origin.
            m_Host.manualClock.seconds = 100d;
            NowRuntime.BeginFrame();

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o, 0.5f);
            NowRuntime.EndFrame();
            Assert.That(o == null, Is.False, "the deadline has not passed yet");

            // t = 0.25: still early.
            m_Host.manualClock.seconds = 100.25d;
            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();
            Assert.That(o == null, Is.False);

            // t = 0.5: due.
            m_Host.manualClock.seconds = 100.5d;
            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();
            Assert.That(o == null, Is.True);
        }

        [Test]
        public void DestroyWithDelay_KeepsTheEarliestDeadlineWhenScheduledTwice()
        {
            NowRuntime.isPlaying = true;
            m_Host.manualClock.seconds = 10d;
            NowRuntime.BeginFrame();

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o, 100f);
            Object.Destroy(o);          // no deadline at all: due at the next EndFrame

            Assert.That(Object.pendingDestroyCount, Is.EqualTo(1), "the object is queued once, not twice");

            NowRuntime.EndFrame();
            Assert.That(o == null, Is.True);
        }

        [Test]
        public void DestroyDuringDrain_IsHandledOnTheNextFrame()
        {
            NowRuntime.isPlaying = true;
            NowRuntime.BeginFrame();

            ScriptableObject victim = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            CascadingAsset cascade = ScriptableObject.CreateInstance<CascadingAsset>();
            cascade.victim = victim;

            Object.Destroy(cascade);
            NowRuntime.EndFrame();

            Assert.That(cascade == null, Is.True);
            // The re-entrant Destroy was queued while draining; a second frame retires it without deadlocking.
            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();
            Assert.That(victim == null, Is.True);
        }

        /// <summary>Destroys another object from its own OnDestroy, re-entering the queue while it is draining.</summary>
        public class CascadingAsset : ScriptableObject
        {
            public ScriptableObject victim;

            private void OnDestroy()
            {
                Object.Destroy(victim);
            }
        }
    }

    /// <summary>ScriptableObject creation and message dispatch (design §3.4, §6.5).</summary>
    [TestFixture]
    public class ScriptableObjectMessageTests
    {
        private bool m_SavedIsPlaying;

        [SetUp]
        public void SetUp()
        {
            m_SavedIsPlaying = NowRuntime.isPlaying;
            NowRuntime.isPlaying = false;
        }

        [TearDown]
        public void TearDown()
        {
            NowRuntime.isPlaying = m_SavedIsPlaying;
        }

        [Test]
        public void CreateInstance_DispatchesAwakeThenOnEnable()
        {
            RecordingAsset asset = ScriptableObject.CreateInstance<RecordingAsset>();

            Assert.That(asset.messages, Is.EqualTo(new[] { "Awake", "OnEnable" }));
        }

        [Test]
        public void Destroy_DispatchesOnDisableThenOnDestroy()
        {
            RecordingAsset asset = ScriptableObject.CreateInstance<RecordingAsset>();
            asset.messages.Clear();

            Object.DestroyImmediate(asset);

            Assert.That(asset.messages, Is.EqualTo(new[] { "OnDisable", "OnDestroy" }));
        }

        [Test]
        public void MessagesSeeALiveObject()
        {
            // The destroyed flag is set only after the messages have run, so a handler can still read `name`.
            NameReadingAsset asset = ScriptableObject.CreateInstance<NameReadingAsset>();
            asset.name = "Themed";

            Object.DestroyImmediate(asset);

            Assert.That(asset.nameSeenInOnDestroy, Is.EqualTo("Themed"));
        }

        [Test]
        public void PrivateMessagesRunBaseFirst()
        {
            DerivedRecordingAsset asset = ScriptableObject.CreateInstance<DerivedRecordingAsset>();

            Assert.That(
                asset.messages,
                Is.EqualTo(new[] { "Awake", "DerivedAwake", "OnEnable", "DerivedOnEnable" }));
        }

        [Test]
        public void AnOverriddenVirtualMessageRunsExactlyOnce()
        {
            OverridingMessageAsset asset = ScriptableObject.CreateInstance<OverridingMessageAsset>();

            // The base declaration and the override are two DeclaredOnly MethodInfos, but invoking either dispatches
            // virtually to the override; collecting one entry per base definition is what keeps this from doubling.
            Assert.That(asset.messages, Is.EqualTo(new[] { "Override" }));
        }

        [Test]
        public void CreateInstance_WorksThroughAPrivateConstructor()
        {
            Assert.That(ScriptableObject.CreateInstance<PrivateConstructorAsset>(), Is.Not.Null);
        }

        [Test]
        public void CreateInstance_NamesTheInstanceAfterItsType()
        {
            Assert.That(ScriptableObject.CreateInstance<RecordingAsset>().name, Is.EqualTo("RecordingAsset"));
        }

        [Test]
        public void CreateInstance_NonGenericFormReturnsTheSameThing()
        {
            ScriptableObject asset = ScriptableObject.CreateInstance(typeof(RecordingAsset));

            Assert.That(asset, Is.InstanceOf<RecordingAsset>());
            Assert.That(((RecordingAsset)asset).messages, Is.EqualTo(new[] { "Awake", "OnEnable" }));
        }

        [Test]
        public void CreateInstance_RejectsNullAndNonScriptableObjectTypes()
        {
            Assert.Throws<ArgumentNullException>(() => ScriptableObject.CreateInstance(null));
            Assert.Throws<ArgumentException>(() => ScriptableObject.CreateInstance(typeof(string)));
            Assert.Throws<ArgumentException>(() => ScriptableObject.CreateInstance(typeof(AbstractAsset)));
        }

        [Test]
        public void AThrowingMessageIsLoggedAndTheObjectIsStillUsable()
        {
            INowHostServices savedHost = NowRuntime.host;
            INowRenderBackend savedBackend = NowRuntime.backend;
            TestHost host = new TestHost();
            NowRuntime.Initialize(host, null);
            try
            {
                ThrowingAsset asset = null;
                Assert.DoesNotThrow(() => { asset = ScriptableObject.CreateInstance<ThrowingAsset>(); });

                Assert.That(asset == null, Is.False);
                Assert.That(host.recordingLogger.lines.Count, Is.EqualTo(1));
                Assert.That(host.recordingLogger.lines[0], Does.StartWith("Exception|"));
                Assert.That(host.recordingLogger.lines[0], Does.Contain("OnEnable"));
            }
            finally
            {
                NowRuntime.Initialize(savedHost, savedBackend);
            }
        }

        public abstract class AbstractAsset : ScriptableObject
        {
        }

        public class NameReadingAsset : ScriptableObject
        {
            public string nameSeenInOnDestroy;

            private void OnDestroy()
            {
                nameSeenInOnDestroy = name;
            }
        }
    }

    /// <summary>The inert scene stubs (design §3.4).</summary>
    [TestFixture]
    public class SceneStubTests
    {
        [Test]
        public void NoSceneStubHasAPublicConstructor()
        {
            Type[] stubs =
            {
                typeof(GameObject), typeof(Component), typeof(Behaviour),
                typeof(Transform), typeof(RectTransform), typeof(Camera),
            };

            foreach (Type type in stubs)
            {
                // A public constructor would let standalone code produce an instance, and every host-identity branch
                // would stop provably taking its null path.
                Assert.That(
                    type.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
                    Is.Empty,
                    type.Name + " must not be constructible from outside NowUI.Engine");
            }
        }

        [Test]
        public void CameraCurrentAndMainAreNull()
        {
            Assert.That(Camera.current, Is.Null);
            Assert.That(Camera.main, Is.Null);
            Assert.That(Camera.current == null, Is.True);
        }

        [Test]
        public void SceneStubsExposeTheDesignedIdentityValues()
        {
            // Read through reflection because nothing can construct one; the point is the declared shape.
            Assert.That(typeof(GameObject).GetProperty("activeInHierarchy"), Is.Not.Null);
            Assert.That(typeof(Behaviour).GetProperty("enabled").CanWrite, Is.True);
            Assert.That(typeof(RectTransform).IsSealed, Is.True);
            Assert.That(typeof(Camera).IsSealed, Is.True);
            Assert.That(typeof(Transform).IsSubclassOf(typeof(Component)), Is.True);
            Assert.That(typeof(Behaviour).IsSubclassOf(typeof(Component)), Is.True);
            Assert.That(typeof(Camera).IsSubclassOf(typeof(Behaviour)), Is.True);
        }

        [Test]
        public void MonoBehaviourAndCoroutineAreNotProvided()
        {
            // Deliberately absent (design §3.4): their core users move to host halves, so a compile error naming them
            // is the intended signal rather than an inert stub that hides the split.
            Assembly engine = typeof(Object).Assembly;
            Assert.That(engine.GetType("UnityEngine.MonoBehaviour", throwOnError: false), Is.Null);
            Assert.That(engine.GetType("UnityEngine.Coroutine", throwOnError: false), Is.Null);
        }

        [Test]
        public void SceneIsAlwaysInvalid()
        {
            UnityEngine.SceneManagement.Scene scene = default;

            Assert.That(scene.IsValid(), Is.False);
            Assert.That(scene.name, Is.EqualTo(""));
            Assert.That(scene.buildIndex, Is.EqualTo(-1));
        }
    }

    /// <summary>The default host services (design §4.4).</summary>
    [TestFixture]
    public class DefaultHostServicesTests
    {
        [Test]
        public void ScreenIs1920x1080At96DpiWithAFullRectSafeArea()
        {
            NowScreenInfo screen = new DefaultHostServices().screen;

            Assert.That(screen.width, Is.EqualTo(1920));
            Assert.That(screen.height, Is.EqualTo(1080));
            Assert.That(screen.dpi, Is.EqualTo(96f));
            Assert.That(screen.safeArea, Is.EqualTo(new Rect(0f, 0f, 1920f, 1080f)));
        }

        [Test]
        public void OptionalServicesAreNullAndRequiredOnesAreNot()
        {
            DefaultHostServices host = new DefaultHostServices();

            Assert.That(host.clock, Is.Not.Null);
            Assert.That(host.logger, Is.Not.Null);
            Assert.That(host.resources, Is.Not.Null);

            Assert.That(host.clipboard, Is.Null);
            Assert.That(host.touchKeyboard, Is.Null);
            Assert.That(host.imageDecoder, Is.Null);
            Assert.That(host.fetch, Is.Null);
        }

        [Test]
        public void TheEmptyResourceProviderReturnsNullForEverything()
        {
            DefaultHostServices host = new DefaultHostServices();

            Assert.That(host.resources.Load("NowUI/UIMaterial", typeof(Object)), Is.Null);
            Assert.That(host.resources.FindShader("NowUI/UI Rectangle"), Is.Null);
        }

        [Test]
        public void LayerNamesAreUnitysDefaultTableOf32()
        {
            string[] layers = new DefaultHostServices().layerNames;

            Assert.That(layers.Length, Is.EqualTo(32));
            Assert.That(layers[0], Is.EqualTo("Default"));
            Assert.That(layers[1], Is.EqualTo("TransparentFX"));
            Assert.That(layers[2], Is.EqualTo("Ignore Raycast"));
            Assert.That(layers[3], Is.EqualTo(""), "index 3 is the first empty layer, which NameToLayer(\"\") returns");
            Assert.That(layers[4], Is.EqualTo("Water"));
            Assert.That(layers[5], Is.EqualTo("UI"));
            for (int i = 6; i < 32; i++)
                Assert.That(layers[i], Is.EqualTo(""), "layer " + i);
        }

        [Test]
        public void EachHostGetsItsOwnLayerTable()
        {
            DefaultHostServices a = new DefaultHostServices();
            DefaultHostServices b = new DefaultHostServices();

            a.layerNames[0] = "Mutated";

            Assert.That(b.layerNames[0], Is.EqualTo("Default"));
        }

        [Test]
        public void PlatformIsTheOsMappedDesktopPlayer()
        {
            RuntimePlatform platform = new DefaultHostServices().platform;

            // Never WebGLPlayer by default: a browser host sets that explicitly (design §3.2).
            Assert.That(
                platform,
                Is.EqualTo(RuntimePlatform.WindowsPlayer)
                    .Or.EqualTo(RuntimePlatform.OSXPlayer)
                    .Or.EqualTo(RuntimePlatform.LinuxPlayer));
        }

        [Test]
        public void PersistentDataPathIsUnderTheTempDirectory()
        {
            string path = new DefaultHostServices().persistentDataPath;

            Assert.That(path, Does.EndWith("NowUI"));
            Assert.That(path, Does.StartWith(System.IO.Path.GetTempPath()));
            Assert.That(new DefaultHostServices().dataPath, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TheStopwatchClockIsMonotonic()
        {
            NowStopwatchClock clock = new NowStopwatchClock();
            double first = clock.realtimeSeconds;
            double second = clock.realtimeSeconds;

            Assert.That(second, Is.GreaterThanOrEqualTo(first));
            Assert.That(first, Is.GreaterThanOrEqualTo(0d));
        }

        [Test]
        public void TheMemoryClipboardRoundTripsAndNeverReportsNull()
        {
            NowMemoryClipboard clipboard = new NowMemoryClipboard();

            Assert.That(clipboard.GetText(), Is.EqualTo(""));
            clipboard.SetText("copied");
            Assert.That(clipboard.GetText(), Is.EqualTo("copied"));
            clipboard.SetText(null);
            Assert.That(clipboard.GetText(), Is.EqualTo(""));
        }
    }

    /// <summary>NowRuntime's frame contract (design §6.1) and ResetAll (design §6.2).</summary>
    [TestFixture]
    public class NowRuntimeTests
    {
        private INowHostServices m_SavedHost;
        private INowRenderBackend m_SavedBackend;
        private bool m_SavedIsPlaying;
        private TestHost m_Host;

        [SetUp]
        public void SetUp()
        {
            m_SavedHost = NowRuntime.host;
            m_SavedBackend = NowRuntime.backend;
            m_SavedIsPlaying = NowRuntime.isPlaying;

            m_Host = new TestHost();
            NowRuntime.Initialize(m_Host, null);
        }

        [TearDown]
        public void TearDown()
        {
            NowRuntime.Initialize(m_SavedHost, m_SavedBackend);
            NowRuntime.isPlaying = m_SavedIsPlaying;
            ResetProbe.calls.Clear();
        }

        [Test]
        public void HostAndBackendAreNeverNull()
        {
            NowRuntime.Initialize(null, null);

            Assert.That(NowRuntime.host, Is.Not.Null);
            Assert.That(NowRuntime.backend, Is.Not.Null);
            Assert.That(NowRuntime.globals, Is.Not.Null);
        }

        [Test]
        public void InitializeInstallsTheGivenHost()
        {
            Assert.That(NowRuntime.host, Is.SameAs(m_Host));
        }

        [Test]
        public void DefaultsMatchTheDesign()
        {
            // isPlaying is process-wide and other fixtures move it, so it is checked through a fresh read of the
            // documented default rather than by trusting the current value.
            Assert.That(NowRuntime.deferDestroyToEndOfFrame, Is.True);
            Assert.That(NowRuntime.colorSpace, Is.EqualTo(ColorSpace.Gamma));
            Assert.That(NowRuntime.releaseCpuCopiesOnSeal, Is.False);
        }

        [Test]
        public void FirstFrameHasZeroDeltaAndLaterFramesMeasureTheClock()
        {
            m_Host.manualClock.seconds = 1000d;
            NowRuntime.BeginFrame();
            Assert.That(NowRuntime.deltaTime, Is.EqualTo(0f), "the first frame has nothing to measure against");
            Assert.That(NowRuntime.timeSeconds, Is.EqualTo(0d));

            m_Host.manualClock.seconds = 1000.5d;
            NowRuntime.BeginFrame();
            Assert.That(NowRuntime.deltaTime, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(NowRuntime.timeSeconds, Is.EqualTo(0.5d).Within(1e-9d));
        }

        [Test]
        public void ABackwardsClockClampsDeltaTimeToZero()
        {
            m_Host.manualClock.seconds = 10d;
            NowRuntime.BeginFrame();
            m_Host.manualClock.seconds = 9d;
            NowRuntime.BeginFrame();

            // Core animation code multiplies by deltaTime; a negative value would run animations backwards.
            Assert.That(NowRuntime.deltaTime, Is.EqualTo(0f));
        }

        [Test]
        public void BeginFrameAdvancesTheFrameCounterAndPushesEngineClock()
        {
            int before = NowRuntime.frameCount;

            m_Host.manualClock.seconds = 0d;
            NowRuntime.BeginFrame();
            m_Host.manualClock.seconds = 0.25d;
            NowRuntime.BeginFrame();

            Assert.That(NowRuntime.frameCount, Is.EqualTo(before + 2));
            // The SmoothDamp overloads that omit deltaTime read EngineClock, so BeginFrame has to push it.
            Assert.That(EngineClock.deltaTime, Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void OnFrameIsRaisedOncePerBeginFrame()
        {
            int count = 0;
            Action handler = () => count++;
            NowRuntime.onFrame += handler;
            try
            {
                NowRuntime.BeginFrame();
                NowRuntime.BeginFrame();
            }
            finally
            {
                NowRuntime.onFrame -= handler;
            }

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void ResetAllRunsRuntimeInitializeOnLoadMethodsInUnitysOrder()
        {
            NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly);
            ResetProbe.calls.Clear();

            NowRuntime.ResetAll();

            // SubsystemRegistration before AfterSceneLoad, whatever the enum's numeric values are.
            Assert.That(ResetProbe.calls.IndexOf("Early"), Is.GreaterThanOrEqualTo(0));
            Assert.That(ResetProbe.calls.IndexOf("Late"), Is.GreaterThan(ResetProbe.calls.IndexOf("Early")));
        }

        [Test]
        public void ResetAllWarnsAboutAndSkipsAMisDeclaredInitializer()
        {
            NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly);
            ResetProbe.calls.Clear();
            m_Host.recordingLogger.lines.Clear();

            NowRuntime.ResetAll();

            Assert.That(ResetProbe.calls, Does.Not.Contain("NotStatic"));
            string warning = m_Host.recordingLogger.lines.Find(l => l.StartsWith("Warning|"));
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning, Does.Contain("InstanceResetProbe.NotStatic"));
        }

        [Test]
        public void ResetAllRunsTheDefaultLoadTypeLast()
        {
            NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly);
            ResetProbe.calls.Clear();

            NowRuntime.ResetAll();

            // The parameterless attribute means AfterSceneLoad, which is the last of Unity's five phases.
            Assert.That(ResetProbe.calls, Does.Contain("Default"));
            Assert.That(ResetProbe.calls.IndexOf("Default"), Is.GreaterThan(ResetProbe.calls.IndexOf("Early")));
        }

        [Test]
        public void ResetAllIsRepeatable()
        {
            NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly);

            NowRuntime.ResetAll();
            int first = ResetProbe.calls.Count;
            ResetProbe.calls.Clear();
            NowRuntime.ResetAll();

            Assert.That(ResetProbe.calls.Count, Is.EqualTo(first));
        }

        [Test]
        public void ResetAllEmptiesThePendingDestroyQueue()
        {
            NowRuntime.isPlaying = true;
            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            Object.Destroy(o);
            Assert.That(Object.pendingDestroyCount, Is.EqualTo(1));

            NowRuntime.ResetAll();

            Assert.That(Object.pendingDestroyCount, Is.EqualTo(0));
            // Dropping the queue does not destroy what was in it, exactly like a domain reload dropping the schedule.
            Assert.That(o == null, Is.False);
        }

        [Test]
        public void ShutdownRaisesQuitting_DestroysLiveObjects_AndRestoresTheDefaults()
        {
            // Shutdown is process-wide by design (§6.6): it destroys every tracked object, which is exactly what the
            // Tests fixture wants from OneTimeTearDown. Nothing in this assembly caches a live UnityEngine.Object in a
            // static, and ResetAll drops the shim's own lazily built ones, so running it mid-suite is safe.
            int quits = 0;
            Action handler = () => quits++;
            NowRuntime.onEngineQuitting += handler;

            ScriptableObject o = ScriptableObject.CreateInstance<PrivateConstructorAsset>();
            NowRuntime.BeginFrame();

            try
            {
                NowRuntime.Shutdown();
            }
            finally
            {
                NowRuntime.onEngineQuitting -= handler;
            }

            Assert.That(quits, Is.EqualTo(1));
            Assert.That(o == null, Is.True, "a live object is destroyed so its backend resources are released");
            Assert.That(NowRuntime.frameCount, Is.EqualTo(0));
            Assert.That(NowRuntime.host, Is.Not.SameAs(m_Host), "the default host is restored");
            Assert.That(NowRuntime.host, Is.InstanceOf<DefaultHostServices>());
            Assert.That(NowRuntime.backend, Is.Not.Null);
        }

        [Test]
        public void RegisterAssemblyRejectsNullAndIgnoresDuplicates()
        {
            Assert.Throws<ArgumentNullException>(() => NowRuntime.RegisterAssembly(null));
            Assert.DoesNotThrow(() => NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly));
            Assert.DoesNotThrow(() => NowRuntime.RegisterAssembly(typeof(ResetProbe).Assembly));
        }
    }
}
