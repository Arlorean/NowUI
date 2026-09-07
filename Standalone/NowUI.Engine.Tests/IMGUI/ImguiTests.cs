// Tests for the IMGUI shim (Standalone/NowUI.Engine/IMGUI/Imgui.cs, unit U13).
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.8 (member list and the "monotonically increasing per-frame id"
// / "null-safe systemCopyBuffer" rules) and H.4 (why the surface exists at all).
//
// The acceptance criterion for U13 is that Assets/NowUI/Runtime/NowGUI.cs and
// Assets/NowUI/Runtime/Input/NowIMGUIInputProvider.cs compile whole against this surface and take their
// Event.current == null branches; those two files are not part of this project, so what is asserted here is the
// behaviour they would see if a host ever drove them - every member design §3.8 lists, plus the routing rules
// NowIMGUIInputProvider's own comments depend on.
using System;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests.IMGUI
{
    [TestFixture]
    public sealed class ImguiTests
    {
        /// <summary>A host that is the default one in every respect except that it has a clipboard.</summary>
        private sealed class ClipboardHost : INowHostServices
        {
            private readonly INowHostServices m_Inner = new DefaultHostServices();

            public ClipboardHost(INowClipboard clipboard)
            {
                this.clipboard = clipboard;
            }

            public INowClipboard clipboard { get; }

            public INowClock clock => m_Inner.clock;

            public NowScreenInfo screen => m_Inner.screen;

            public INowLogger logger => m_Inner.logger;

            public INowTouchKeyboard touchKeyboard => m_Inner.touchKeyboard;

            public INowResourceProvider resources => m_Inner.resources;

            public INowImageDecoder imageDecoder => m_Inner.imageDecoder;

            public INowFetchProvider fetch => m_Inner.fetch;

            public UnityEngine.RuntimePlatform platform => m_Inner.platform;

            public string persistentDataPath => m_Inner.persistentDataPath;

            public string dataPath => m_Inner.dataPath;

            public string[] layerNames => m_Inner.layerNames;
        }

        /// <summary>A clipboard whose getter can be made to return null, which the shim has to normalise.</summary>
        private sealed class NullTextClipboard : INowClipboard
        {
            public string lastSet = "unset";

            public string GetText()
            {
                return null;
            }

            public void SetText(string text)
            {
                lastSet = text;
            }
        }

        /// <summary>Set by the tests that swap the host, so TearDown only restores the default when it has to.</summary>
        private bool m_HostReplaced;

        private void UseHost(INowHostServices host)
        {
            m_HostReplaced = true;
            NowRuntime.Initialize(host, null);
        }

        [SetUp]
        public void SetUp()
        {
            // Touching any facade member installs the per-frame hook; BeginFrame then puts the counter, GUI.changed
            // and the last rect into a known state so tests do not depend on each other's control ids.
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            Event.current = null;
            NowRuntime.BeginFrame();
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            Event.current = null;
            GUI.changed = false;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;

            // Only the clipboard tests swap the host; putting the default back unconditionally would also reset the
            // frame timing that other fixtures in this assembly rely on.
            if (m_HostReplaced)
            {
                m_HostReplaced = false;
                NowRuntime.Initialize(null, null);
            }
        }

        // ---------------------------------------------------------------- Event.current

        [Test]
        public void EventCurrent_IsNullByDefault()
        {
            // The whole H.4 mechanism rests on this: with no current event the two IMGUI files are dormant.
            Assert.That(Event.current, Is.Null);
        }

        [Test]
        public void EventCurrent_RoundTripsAndIsPlainReferenceNull()
        {
            Event e = new Event();
            Event.current = e;

            Assert.That(Event.current, Is.SameAs(e));
            Assert.That(e == null, Is.False);
            Assert.That(e != null, Is.True);

            Event.current = null;
            Assert.That(Event.current, Is.Null);
        }

        // ---------------------------------------------------------------- Event construction

        [Test]
        public void NewEvent_IsIgnoreRatherThanTheZeroValueMouseDown()
        {
            Event e = new Event();

            // EventType's zero value is MouseDown, so a default-constructed event that kept it would read as a
            // click; the shim starts at Ignore instead (Imgui.cs documents the choice).
            Assert.That(e.type, Is.EqualTo(EventType.Ignore));
            Assert.That(e.rawType, Is.EqualTo(EventType.Ignore));
            Assert.That(e.mousePosition, Is.EqualTo(Vector2.zero));
            Assert.That(e.delta, Is.EqualTo(Vector2.zero));
            Assert.That(e.button, Is.EqualTo(0));
            Assert.That(e.clickCount, Is.EqualTo(0));
            Assert.That(e.keyCode, Is.EqualTo(KeyCode.None));
            Assert.That(e.character, Is.EqualTo('\0'));
            Assert.That(e.modifiers, Is.EqualTo(EventModifiers.None));
        }

        [Test]
        public void CopyConstructor_CopiesEveryField()
        {
            Event source = new Event(3)
            {
                type = EventType.MouseDrag,
                mousePosition = new Vector2(12f, 34f),
                delta = new Vector2(-1f, 2f),
                button = 2,
                clickCount = 2,
                keyCode = KeyCode.Return,
                character = 'q',
                modifiers = EventModifiers.Shift | EventModifiers.Alt,
            };

            Event copy = new Event(source);

            Assert.That(copy.type, Is.EqualTo(EventType.MouseDrag));
            Assert.That(copy.rawType, Is.EqualTo(EventType.MouseDrag));
            Assert.That(copy.mousePosition, Is.EqualTo(new Vector2(12f, 34f)));
            Assert.That(copy.delta, Is.EqualTo(new Vector2(-1f, 2f)));
            Assert.That(copy.button, Is.EqualTo(2));
            Assert.That(copy.clickCount, Is.EqualTo(2));
            Assert.That(copy.keyCode, Is.EqualTo(KeyCode.Return));
            Assert.That(copy.character, Is.EqualTo('q'));
            Assert.That(copy.modifiers, Is.EqualTo(EventModifiers.Shift | EventModifiers.Alt));
            Assert.That(copy, Is.Not.SameAs(source));
        }

        [Test]
        public void CopyConstructor_CopiesTheUsedStateAndTheRawTypeSeparately()
        {
            Event source = new Event { type = EventType.MouseUp };
            source.Use();

            Event copy = new Event(source);

            Assert.That(copy.type, Is.EqualTo(EventType.Used));
            Assert.That(copy.rawType, Is.EqualTo(EventType.MouseUp));
        }

        [Test]
        public void CopyConstructor_RejectsNull()
        {
            // Unity throws ArgumentException here, not ArgumentNullException.
            Assert.That(() => new Event(null), Throws.ArgumentException);
        }

        [Test]
        public void DisplayIndexConstructor_ProducesAnInertEvent()
        {
            Event e = new Event(7);

            Assert.That(e.type, Is.EqualTo(EventType.Ignore));
            Assert.That(e.displayIndex, Is.EqualTo(7));
        }

        // ---------------------------------------------------------------- type / rawType / Use

        [Test]
        public void Use_MarksTheEventUsedButLeavesRawType()
        {
            // NowIMGUIInputProvider reads exactly this pair: "ownsCapture && current.rawType == EventType.MouseUp"
            // has to stay true after some other control consumed the event.
            Event e = new Event { type = EventType.MouseUp };

            e.Use();

            Assert.That(e.type, Is.EqualTo(EventType.Used));
            Assert.That(e.rawType, Is.EqualTo(EventType.MouseUp));
        }

        [Test]
        public void AssigningType_RevivesTheEventSoRawTypeFollows()
        {
            Event e = new Event { type = EventType.MouseDown };
            e.Use();

            e.type = EventType.KeyDown;

            Assert.That(e.type, Is.EqualTo(EventType.KeyDown));
            Assert.That(e.rawType, Is.EqualTo(EventType.KeyDown));
        }

        // ---------------------------------------------------------------- modifier views

        [Test]
        public void ModifierProperties_AreAViewOverModifiers()
        {
            Event e = new Event { modifiers = EventModifiers.Shift | EventModifiers.Command };

            Assert.That(e.shift, Is.True);
            Assert.That(e.command, Is.True);
            Assert.That(e.control, Is.False);
            Assert.That(e.alt, Is.False);
        }

        [Test]
        public void SettingAModifier_LeavesTheOthersAlone()
        {
            Event e = new Event { modifiers = EventModifiers.Shift | EventModifiers.CapsLock };

            e.control = true;
            Assert.That(e.modifiers,
                Is.EqualTo(EventModifiers.Shift | EventModifiers.CapsLock | EventModifiers.Control));

            e.shift = false;
            Assert.That(e.modifiers, Is.EqualTo(EventModifiers.CapsLock | EventModifiers.Control));

            e.alt = true;
            e.command = true;
            Assert.That(e.alt, Is.True);
            Assert.That(e.command, Is.True);

            e.alt = false;
            e.command = false;
            e.control = false;
            Assert.That(e.modifiers, Is.EqualTo(EventModifiers.CapsLock));
        }

        [Test]
        public void ClearingAModifierThatIsNotSet_IsANoOp()
        {
            Event e = new Event { modifiers = EventModifiers.Alt };

            e.shift = false;

            Assert.That(e.modifiers, Is.EqualTo(EventModifiers.Alt));
        }

        // ---------------------------------------------------------------- isKey / isMouse / isScrollWheel

        [TestCase(EventType.KeyDown, true)]
        [TestCase(EventType.KeyUp, true)]
        [TestCase(EventType.MouseDown, false)]
        [TestCase(EventType.Used, false)]
        [TestCase(EventType.Ignore, false)]
        [TestCase(EventType.Layout, false)]
        public void IsKey_IsTrueOnlyForTheTwoKeyTypes(EventType type, bool expected)
        {
            Assert.That(new Event { type = type }.isKey, Is.EqualTo(expected));
        }

        [TestCase(EventType.MouseDown, true)]
        [TestCase(EventType.MouseUp, true)]
        [TestCase(EventType.MouseMove, true)]
        [TestCase(EventType.MouseDrag, true)]
        [TestCase(EventType.ScrollWheel, false)]
        [TestCase(EventType.ContextClick, false)]
        [TestCase(EventType.MouseEnterWindow, false)]
        [TestCase(EventType.MouseLeaveWindow, false)]
        [TestCase(EventType.KeyDown, false)]
        [TestCase(EventType.Used, false)]
        public void IsMouse_CoversTheFourPointerTypesOnly(EventType type, bool expected)
        {
            Assert.That(new Event { type = type }.isMouse, Is.EqualTo(expected));
        }

        [TestCase(EventType.ScrollWheel, true)]
        [TestCase(EventType.MouseDrag, false)]
        [TestCase(EventType.Used, false)]
        public void IsScrollWheel_IsTrueOnlyForScrollWheel(EventType type, bool expected)
        {
            Assert.That(new Event { type = type }.isScrollWheel, Is.EqualTo(expected));
        }

        [Test]
        public void IsMouse_ReadsTypeSoAUsedEventIsNoLongerAMouseEvent()
        {
            Event e = new Event { type = EventType.MouseDown };
            e.Use();

            Assert.That(e.isMouse, Is.False);
            Assert.That(e.rawType, Is.EqualTo(EventType.MouseDown));
        }

        // ---------------------------------------------------------------- GetTypeForControl

        [Test]
        public void GetTypeForControl_ReportsUsedToEveryone()
        {
            Event e = new Event { type = EventType.MouseDown };
            e.Use();

            Assert.That(e.GetTypeForControl(1), Is.EqualTo(EventType.Used));
            Assert.That(e.GetTypeForControl(2), Is.EqualTo(EventType.Used));
        }

        [Test]
        public void GetTypeForControl_FiltersKeyEventsToTheKeyboardControl()
        {
            // This is the case NowIMGUIInputProvider documents: a panel takes a passive control id, so it never owns
            // keyboardControl and its key events come back as Ignore - which is why the provider re-routes them from
            // rawType instead.
            Event e = new Event { type = EventType.KeyDown, keyCode = KeyCode.A };
            GUIUtility.keyboardControl = 0;

            Assert.That(e.GetTypeForControl(42), Is.EqualTo(EventType.Ignore));

            GUIUtility.keyboardControl = 42;
            Assert.That(e.GetTypeForControl(42), Is.EqualTo(EventType.KeyDown));
            Assert.That(e.GetTypeForControl(43), Is.EqualTo(EventType.Ignore));
        }

        [Test]
        public void GetTypeForControl_RoutesPointerEventsByHotControl()
        {
            Event e = new Event { type = EventType.MouseDrag };

            // No capture: everyone sees the event.
            GUIUtility.hotControl = 0;
            Assert.That(e.GetTypeForControl(11), Is.EqualTo(EventType.MouseDrag));
            Assert.That(e.GetTypeForControl(12), Is.EqualTo(EventType.MouseDrag));

            // Captured: only the capturing control does.
            GUIUtility.hotControl = 11;
            Assert.That(e.GetTypeForControl(11), Is.EqualTo(EventType.MouseDrag));
            Assert.That(e.GetTypeForControl(12), Is.EqualTo(EventType.Ignore));
        }

        [Test]
        public void GetTypeForControl_PassesEverythingElseThrough()
        {
            GUIUtility.hotControl = 99;
            GUIUtility.keyboardControl = 99;

            Assert.That(new Event { type = EventType.Repaint }.GetTypeForControl(1), Is.EqualTo(EventType.Repaint));
            Assert.That(new Event { type = EventType.Layout }.GetTypeForControl(1), Is.EqualTo(EventType.Layout));
            Assert.That(new Event { type = EventType.ScrollWheel }.GetTypeForControl(1),
                Is.EqualTo(EventType.ScrollWheel));
            Assert.That(new Event { type = EventType.MouseLeaveWindow }.GetTypeForControl(1),
                Is.EqualTo(EventType.MouseLeaveWindow));
            Assert.That(new Event { type = EventType.Ignore }.GetTypeForControl(1), Is.EqualTo(EventType.Ignore));
        }

        // ---------------------------------------------------------------- GUIUtility.GetControlID

        [Test]
        public void GetControlID_CountsFromOneSoNoControlGetsTheNoControlSentinel()
        {
            NowRuntime.BeginFrame();

            Assert.That(GUIUtility.GetControlID(FocusType.Passive), Is.EqualTo(1));
            Assert.That(GUIUtility.GetControlID(0x4e6f7747, FocusType.Passive), Is.EqualTo(2));
            Assert.That(GUIUtility.GetControlID(0x4e6f7747, FocusType.Passive, new Rect(1f, 2f, 3f, 4f)),
                Is.EqualTo(3));
        }

        [Test]
        public void GetControlID_IsResetByBeginFrameSoTheSameCallOrderYieldsTheSameIds()
        {
            // NowGUI keys its render-texture cache on (context, controlId), so the id has to be stable frame over
            // frame for an unchanged call order.
            NowRuntime.BeginFrame();
            int firstFrameA = GUIUtility.GetControlID(1, FocusType.Passive);
            int firstFrameB = GUIUtility.GetControlID(2, FocusType.Passive);

            NowRuntime.BeginFrame();
            int secondFrameA = GUIUtility.GetControlID(1, FocusType.Passive);
            int secondFrameB = GUIUtility.GetControlID(2, FocusType.Passive);

            Assert.That(secondFrameA, Is.EqualTo(firstFrameA));
            Assert.That(secondFrameB, Is.EqualTo(firstFrameB));
            Assert.That(firstFrameB, Is.GreaterThan(firstFrameA));
        }

        [Test]
        public void GetControlID_IsMonotonicWithinAFrame()
        {
            NowRuntime.BeginFrame();

            int previous = 0;
            for (int i = 0; i < 64; i++)
            {
                int id = GUIUtility.GetControlID(FocusType.Passive);
                Assert.That(id, Is.GreaterThan(previous));
                previous = id;
            }
        }

        // ---------------------------------------------------------------- hotControl / keyboardControl

        [Test]
        public void HotControl_RoundTripsAndSurvivesTheFrameBoundary()
        {
            // A drag's capture is taken on MouseDown and released on MouseUp, which is a later frame.
            GUIUtility.hotControl = 17;

            NowRuntime.BeginFrame();

            Assert.That(GUIUtility.hotControl, Is.EqualTo(17));

            GUIUtility.hotControl = 0;
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [Test]
        public void KeyboardControl_RoundTripsAndSurvivesTheFrameBoundary()
        {
            GUIUtility.keyboardControl = 23;

            NowRuntime.BeginFrame();

            Assert.That(GUIUtility.keyboardControl, Is.EqualTo(23));
        }

        // ---------------------------------------------------------------- systemCopyBuffer

        [Test]
        public void SystemCopyBuffer_WithNoClipboardReadsEmptyAndSwallowsTheWrite()
        {
            // NowClipboard wraps this property in static field initialisers, so a throw here would surface as a
            // TypeInitializationException from an unrelated call site.
            UseHost(null);

            Assert.That(NowRuntime.host.clipboard, Is.Null, "the default host is supposed to have no clipboard");
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(string.Empty));
            Assert.That(() => GUIUtility.systemCopyBuffer = "dropped", Throws.Nothing);
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SystemCopyBuffer_RoundTripsThroughTheHostClipboard()
        {
            UseHost(new ClipboardHost(new NowMemoryClipboard()));

            GUIUtility.systemCopyBuffer = "copied text";

            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("copied text"));
        }

        [Test]
        public void SystemCopyBuffer_NormalisesNullInBothDirections()
        {
            NullTextClipboard clipboard = new NullTextClipboard();
            UseHost(new ClipboardHost(clipboard));

            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(string.Empty));

            GUIUtility.systemCopyBuffer = null;

            Assert.That(clipboard.lastSet, Is.EqualTo(string.Empty));
        }

        // ---------------------------------------------------------------- GUI-space conversions

        [Test]
        public void GuiAndScreenPointConversions_AreTheIdentity()
        {
            Vector2 point = new Vector2(3.5f, -7.25f);

            Assert.That(GUIUtility.GUIToScreenPoint(point), Is.EqualTo(point));
            Assert.That(GUIUtility.ScreenToGUIPoint(point), Is.EqualTo(point));
        }

        // ---------------------------------------------------------------- GUILayoutUtility

        [Test]
        public void GetRect_ReturnsTheRequestedSizeAtTheOrigin()
        {
            Assert.That(GUILayoutUtility.GetRect(120f, 30f), Is.EqualTo(new Rect(0f, 0f, 120f, 30f)));
            Assert.That(GUILayoutUtility.GetRect(120f, 30f, new GUILayoutOption[0]),
                Is.EqualTo(new Rect(0f, 0f, 120f, 30f)));
        }

        [Test]
        public void GetRect_FlexibleOverloadReturnsTheMinimumSize()
        {
            // NowGUI.Auto(height) asks for GetRect(0, float.MaxValue, height, height): "as wide as the container".
            // The shim has no container, so it answers with the minimum rather than guessing a width.
            Assert.That(GUILayoutUtility.GetRect(0f, float.MaxValue, 48f, 48f),
                Is.EqualTo(new Rect(0f, 0f, 0f, 48f)));
            Assert.That(GUILayoutUtility.GetRect(64f, 512f, 48f, 96f, new GUILayoutOption[0]),
                Is.EqualTo(new Rect(0f, 0f, 64f, 48f)));
        }

        [Test]
        public void GetLastRect_TracksTheLastAllocationAndIsClearedEachFrame()
        {
            NowRuntime.BeginFrame();
            Assert.That(GUILayoutUtility.GetLastRect(), Is.EqualTo(Rect.zero));

            GUILayoutUtility.GetRect(10f, 20f);
            GUILayoutUtility.GetRect(30f, 40f);
            Assert.That(GUILayoutUtility.GetLastRect(), Is.EqualTo(new Rect(0f, 0f, 30f, 40f)));

            NowRuntime.BeginFrame();
            Assert.That(GUILayoutUtility.GetLastRect(), Is.EqualTo(Rect.zero));
        }

        // ---------------------------------------------------------------- GUI

        [Test]
        public void GuiChanged_RoundTripsAndIsClearedAtTheFrameBoundary()
        {
            // NowIMGUIInputProvider only ever sets this (to request a host repaint); the frame boundary is what stops
            // the request from sticking forever.
            GUI.changed = true;
            Assert.That(GUI.changed, Is.True);

            NowRuntime.BeginFrame();

            Assert.That(GUI.changed, Is.False);
        }

        [Test]
        public void GuiColors_DefaultToWhiteAndRoundTrip()
        {
            Assert.That(GUI.color, Is.EqualTo(Color.white));
            Assert.That(GUI.contentColor, Is.EqualTo(Color.white));

            GUI.color = Color.red;
            GUI.contentColor = Color.green;

            Assert.That(GUI.color, Is.EqualTo(Color.red));
            Assert.That(GUI.contentColor, Is.EqualTo(Color.green));
        }

        [Test]
        public void DrawTexture_IsANoOpAndAcceptsANullTexture()
        {
            Rect rect = new Rect(0f, 0f, 32f, 32f);

            Assert.That(() => GUI.DrawTexture(rect, null), Throws.Nothing);
            Assert.That(() => GUI.DrawTexture(rect, null, ScaleMode.StretchToFill), Throws.Nothing);
            Assert.That(() => GUI.DrawTexture(rect, null, ScaleMode.StretchToFill, true), Throws.Nothing);
            Assert.That(() => GUI.DrawTexture(rect, null, ScaleMode.ScaleAndCrop, false), Throws.Nothing);
        }

        // ---------------------------------------------------------------- ResetAll

        [Test]
        public void ResetAll_ReturnsTheImguiStateToItsFreshlyLoadedValues()
        {
            Event.current = new Event { type = EventType.MouseDown };
            GUIUtility.hotControl = 5;
            GUIUtility.keyboardControl = 6;
            GUI.changed = true;
            GUI.color = Color.red;
            GUI.contentColor = Color.red;
            GUILayoutUtility.GetRect(10f, 10f);
            GUIUtility.GetControlID(FocusType.Passive);

            NowRuntime.ResetAll();

            Assert.That(Event.current, Is.Null);
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Assert.That(GUIUtility.keyboardControl, Is.Zero);
            Assert.That(GUI.changed, Is.False);
            Assert.That(GUI.color, Is.EqualTo(Color.white));
            Assert.That(GUI.contentColor, Is.EqualTo(Color.white));
            Assert.That(GUILayoutUtility.GetLastRect(), Is.EqualTo(Rect.zero));
            Assert.That(GUIUtility.GetControlID(FocusType.Passive), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- allocation behaviour

        [Test]
        public void SteadyStatePath_DoesNotAllocate()
        {
            // Design §1.2: nothing on a per-frame path may allocate. The IMGUI shim's per-frame work is the counter
            // reset plus whatever a dormant host asks for, and none of it may produce garbage.
            Event probe = new Event { type = EventType.MouseMove };
            Event.current = probe;
            GUIUtility.hotControl = 0;

            // Warm up: first touch of each path runs its class initialiser, which does allocate.
            for (int i = 0; i < 8; i++)
                Exercise(probe);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                Exercise(probe);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        /// <summary>
        /// The per-frame IMGUI work a dormant host does. Deliberately does not call <c>NowRuntime.BeginFrame</c>:
        /// that would measure other units' allocations rather than this one's.
        /// </summary>
        private static void Exercise(Event probe)
        {
            int id = GUIUtility.GetControlID(1, FocusType.Passive, new Rect(0f, 0f, 1f, 1f));
            GUIUtility.hotControl = id;
            EventType routed = probe.GetTypeForControl(id);
            if (routed == EventType.Used)
                GUI.changed = true;

            GUILayoutUtility.GetRect(0f, float.MaxValue, 24f, 24f);
            GUILayoutUtility.GetLastRect();
            GUIUtility.hotControl = 0;
        }
    }
}
