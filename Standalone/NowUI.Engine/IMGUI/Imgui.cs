// Mirrors the UnityEngine IMGUI surface: Event, GUIUtility, GUILayoutUtility, GUILayoutOption and GUI.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.8 "Engine/IMGUI/Imgui.cs" member list; H.4 mechanism 2
// "compile the two IMGUI files whole"; §6.1 / §7 for the per-frame reset raised by NowRuntime.BeginFrame).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.GUIUtility", §B.4 rows for
// Runtime/NowGUI.cs and Runtime/Input/NowIMGUIInputProvider.cs, §F).
//
// Inert by construction: Event.current is null in the standalone build, so Assets/NowUI/Runtime/NowGUI.cs and
// Assets/NowUI/Runtime/Input/NowIMGUIInputProvider.cs compile unchanged and every entry point returns immediately -
// exactly what happens in a Unity player that never runs an IMGUI pass. Nothing here talks to the render backend;
// GUI.DrawTexture is a no-op and GUILayoutUtility has no layout engine behind it.
using System;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// The mutable state behind the four IMGUI facades, in one place so that touching <i>any</i> facade member runs
    /// this class's static constructor and therefore installs the per-frame hook.
    /// </summary>
    /// <remarks>
    /// <para>Design §3.8 requires the <c>GetControlID</c> counter to be reset by <c>NowRuntime.BeginFrame</c>, and
    /// NowRuntime.cs deliberately does not name this file: facades that own per-frame counters subscribe themselves
    /// to <c>NowRuntime.onEngineBeginFrame</c>. Subscribing from a static constructor rather than a
    /// <c>[ModuleInitializer]</c> keeps assembly load free of side effects, and loses nothing: the subscription is
    /// installed by the first IMGUI call ever made, and before that first call there is no counter state to reset.</para>
    /// <para>The explicit static constructor also costs a class-init check on each access, which is why design H
    /// rejects adding one to <i>existing</i> NowUI types. It is accepted here because the whole class is unreachable
    /// while <c>Event.current</c> is null, so it is never on a steady-state path.</para>
    /// </remarks>
    internal static class NowImguiState
    {
        /// <summary>Hands out <c>GUIUtility.GetControlID</c> ids; pre-incremented, so no control ever gets id 0.</summary>
        /// <remarks>
        /// Id 0 is IMGUI's "no control" sentinel - <c>NowIMGUIInputProvider</c> tests <c>_hostControlId != 0</c>
        /// before touching <c>hotControl</c> - so the counter must hand out 1 first.
        /// </remarks>
        internal static int controlIdCounter;

        internal static Rect lastRect;

        internal static int hotControl;

        internal static int keyboardControl;

        internal static bool changed;

        internal static Color color = Color.white;

        internal static Color contentColor = Color.white;

        internal static Event current;

        static NowImguiState()
        {
            NowRuntime.onEngineBeginFrame += BeginFrame;
            NowRuntime.onEngineReset += Reset;
        }

        /// <summary>
        /// Clears the state that a Unity IMGUI pass rebuilds from scratch every time <c>OnGUI</c> is entered. One
        /// standalone frame is one IMGUI pass, so the frame boundary is where that happens.
        /// </summary>
        /// <remarks>
        /// <see cref="hotControl"/> and <see cref="keyboardControl"/> are deliberately <b>not</b> cleared: in Unity
        /// they survive across frames (that is how a drag keeps its capture between the MouseDown and the MouseUp
        /// events), and <c>NowIMGUIInputProvider.ReleaseNativeCapture</c> relies on being the one to clear them.
        /// </remarks>
        private static void BeginFrame()
        {
            controlIdCounter = 0;
            lastRect = default;
            changed = false;
        }

        /// <summary>Full teardown for <c>NowRuntime.ResetAll</c>: back to the state of a freshly loaded assembly.</summary>
        private static void Reset()
        {
            BeginFrame();
            hotControl = 0;
            keyboardControl = 0;
            current = null;
            color = Color.white;
            contentColor = Color.white;
        }

        /// <summary>
        /// Forces the static constructor to run. Called by the facade members that would otherwise touch no field of
        /// this class, so that <i>every</i> route into the IMGUI shim installs the per-frame hook.
        /// </summary>
        internal static void Touch()
        {
        }
    }

    /// <summary>
    /// An IMGUI input event (design §3.8). <see cref="current"/> is null in the standalone build unless a host sets
    /// it, which is what makes <c>NowGUI</c> and <c>NowIMGUIInputProvider</c> dormant.
    /// </summary>
    /// <remarks>
    /// A plain class, not a <c>UnityEngine.Object</c>: <c>NowIMGUIInputProvider</c> compares events with
    /// <c>== null</c> and <c>??</c>, both of which must be ordinary reference semantics rather than the fake-null
    /// operator (see UnityValueTypeSemantics.md §13).
    /// </remarks>
    public sealed class Event
    {
        /// <summary>The type as it reads today, i.e. <c>Used</c> once <see cref="Use"/> has been called.</summary>
        private EventType m_Type;

        /// <summary>
        /// The type the event arrived with. Kept separately so <see cref="rawType"/> can still report
        /// <c>MouseUp</c> after some control called <see cref="Use"/> - which is precisely the case
        /// <c>NowIMGUIInputProvider</c> reads at <c>ownsCapture &amp;&amp; current.rawType == EventType.MouseUp</c>.
        /// </summary>
        private EventType m_RawType;

        /// <summary>
        /// Backs the <c>Event(int displayIndex)</c> constructor that design §3.8 lists. Unity exposes a public
        /// <c>displayIndex</c> property; §3's omission rule leaves it off the shim's public surface, so it is
        /// internal here - the constructor still has somewhere to put its argument, and a core reference to the
        /// public property is still a compile error rather than a silent no-op. (Reported to the lead.)
        /// </summary>
        internal int displayIndex { get; set; }

        /// <summary>The event being processed, or null when no IMGUI pass is running - the standalone default.</summary>
        /// <remarks>
        /// Settable, as design §3.8 requires, so a host or a test can drive the two IMGUI files. Cleared by
        /// <c>NowRuntime.ResetAll</c>.
        /// </remarks>
        public static Event current
        {
            get => NowImguiState.current;
            set => NowImguiState.current = value;
        }

        /// <summary>Creates an inert event.</summary>
        /// <remarks>
        /// The type starts at <see cref="EventType.Ignore"/>. Unity's own default is not pinned down by any spec in
        /// Docs/Standalone, and <c>Ignore</c> is the only value that cannot be mistaken for input: the zero value of
        /// <c>EventType</c> is <c>MouseDown</c>, so a default-constructed event that kept it would look like a click
        /// to every consumer. A host that drives IMGUI assigns <see cref="type"/> explicitly.
        /// </remarks>
        public Event()
        {
            NowImguiState.Touch();
            m_Type = EventType.Ignore;
            m_RawType = EventType.Ignore;
        }

        /// <summary>Copy constructor.</summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="other"/> is null. Unity throws <c>ArgumentException</c> here rather than
        /// <c>ArgumentNullException</c>, so the shim does too; note that catching <c>ArgumentException</c> catches
        /// either, so a caller written against Unity keeps working.
        /// </exception>
        public Event(Event other)
        {
            if (other == null)
                throw new ArgumentException("Event to copy from is null.");

            NowImguiState.Touch();
            m_Type = other.m_Type;
            m_RawType = other.m_RawType;
            displayIndex = other.displayIndex;
            mousePosition = other.mousePosition;
            delta = other.delta;
            button = other.button;
            clickCount = other.clickCount;
            keyCode = other.keyCode;
            character = other.character;
            modifiers = other.modifiers;
        }

        /// <summary>Creates an inert event bound to a display index (multi-display hosts).</summary>
        public Event(int displayIndex)
            : this()
        {
            this.displayIndex = displayIndex;
        }

        /// <summary>
        /// What this event is. Reads <c>Used</c> once <see cref="Use"/> has been called; assigning makes the event a
        /// live one of the assigned type again, so <see cref="rawType"/> follows the assignment.
        /// </summary>
        public EventType type
        {
            get => m_Type;
            set
            {
                m_Type = value;
                m_RawType = value;
            }
        }

        /// <summary>The type the event arrived with, unaffected by <see cref="Use"/>.</summary>
        /// <remarks>
        /// In Unity this is also the type before the current GUI clip/window filtering; the shim has neither, so the
        /// only difference between this and <see cref="type"/> is the used state.
        /// </remarks>
        public EventType rawType => m_RawType;

        /// <summary>Pointer position in GUI space (top-left origin), for the mouse events.</summary>
        public Vector2 mousePosition { get; set; }

        /// <summary>Pointer movement since the previous event, or the scroll amount for a scroll-wheel event.</summary>
        public Vector2 delta { get; set; }

        /// <summary>0 = left, 1 = right, 2 = middle.</summary>
        public int button { get; set; }

        /// <summary>1 for a single click, 2 for a double click, and so on.</summary>
        public int clickCount { get; set; }

        /// <summary>The key for a <see cref="EventType.KeyDown"/> / <see cref="EventType.KeyUp"/> event.</summary>
        public KeyCode keyCode { get; set; }

        /// <summary>The typed character for a text-input event; <c>'\0'</c> when the key produces no character.</summary>
        public char character { get; set; }

        /// <summary>The modifier keys held while the event was produced.</summary>
        public EventModifiers modifiers { get; set; }

        /// <summary>Shift held. A view over <see cref="modifiers"/>, exactly as in Unity - there is no second field.</summary>
        public bool shift
        {
            get => (modifiers & EventModifiers.Shift) != 0;
            set => SetModifier(EventModifiers.Shift, value);
        }

        /// <summary>Control held (the Windows/Linux chord key).</summary>
        public bool control
        {
            get => (modifiers & EventModifiers.Control) != 0;
            set => SetModifier(EventModifiers.Control, value);
        }

        /// <summary>Alt / Option held.</summary>
        public bool alt
        {
            get => (modifiers & EventModifiers.Alt) != 0;
            set => SetModifier(EventModifiers.Alt, value);
        }

        /// <summary>Command held (the macOS chord key; the Windows OS key also reports here in IMGUI).</summary>
        public bool command
        {
            get => (modifiers & EventModifiers.Command) != 0;
            set => SetModifier(EventModifiers.Command, value);
        }

        /// <summary>True for <see cref="EventType.KeyDown"/> and <see cref="EventType.KeyUp"/>.</summary>
        /// <remarks>
        /// Reads <see cref="type"/>, not <see cref="rawType"/>: a used key event answers false, which is the
        /// Unity-faithful behaviour and the reason <c>NowIMGUIInputProvider</c> classifies with its own
        /// <c>IsKeyboardEvent(rawType)</c> helper instead of asking the event.
        /// </remarks>
        public bool isKey => m_Type == EventType.KeyDown || m_Type == EventType.KeyUp;

        /// <summary>
        /// True for the four pointer events - <c>MouseDown</c>, <c>MouseUp</c>, <c>MouseMove</c>, <c>MouseDrag</c>.
        /// </summary>
        /// <remarks>
        /// <c>ScrollWheel</c>, <c>ContextClick</c> and the <c>MouseEnterWindow</c>/<c>MouseLeaveWindow</c> pair are
        /// <b>not</b> mouse events by this test, matching Unity; <see cref="isScrollWheel"/> covers the first.
        /// </remarks>
        public bool isMouse =>
            m_Type == EventType.MouseDown ||
            m_Type == EventType.MouseUp ||
            m_Type == EventType.MouseMove ||
            m_Type == EventType.MouseDrag;

        /// <summary>True for <see cref="EventType.ScrollWheel"/>.</summary>
        public bool isScrollWheel => m_Type == EventType.ScrollWheel;

        /// <summary>
        /// The type of this event as seen by the control with id <paramref name="controlID"/>: the routing rule that
        /// decides whether that control gets to react at all.
        /// </summary>
        /// <remarks>
        /// <para>Design §3.8 abbreviates this to "type, or Used when already used", but the core depends on the two
        /// filters as well - <c>NowIMGUIInputProvider.TryGetSnapshot</c> documents them in a comment: "A panel uses a
        /// passive native control ID because NowFocus owns keyboard focus internally. GetTypeForControl therefore
        /// filters KeyDown/KeyUp to Ignore even when this panel owns the focused NowUI field. Preserve native
        /// keyboard events [...]; pointer events still use Unity's routed type for hot-control capture." Returning a
        /// bare <c>type</c> would make that branch dead and silently change the provider's behaviour, so the filters
        /// are implemented:</para>
        /// <list type="bullet">
        /// <item><description>a used event reads <c>Used</c> for everyone;</description></item>
        /// <item><description>a key event reaches only <c>GUIUtility.keyboardControl</c> and is <c>Ignore</c>
        /// elsewhere - a passive control never has keyboard focus, hence the provider's comment;</description></item>
        /// <item><description>a pointer event reaches only <c>GUIUtility.hotControl</c> while some control holds the
        /// capture, and is <c>Ignore</c> elsewhere; with no capture (<c>hotControl == 0</c>) it reaches
        /// everyone.</description></item>
        /// </list>
        /// <para>Every other event type passes through unfiltered.</para>
        /// </remarks>
        public EventType GetTypeForControl(int controlID)
        {
            if (m_Type == EventType.Used)
                return EventType.Used;

            if (isKey)
                return NowImguiState.keyboardControl == controlID ? m_Type : EventType.Ignore;

            if (isMouse)
            {
                int hot = NowImguiState.hotControl;
                return hot == 0 || hot == controlID ? m_Type : EventType.Ignore;
            }

            return m_Type;
        }

        /// <summary>
        /// Consumes the event so no later control reacts to it: <see cref="type"/> becomes
        /// <see cref="EventType.Used"/> while <see cref="rawType"/> keeps reporting what arrived.
        /// </summary>
        public void Use()
        {
            m_Type = EventType.Used;
        }

        /// <summary>Unity's diagnostic form: keyboard events print their key, everything else its position.</summary>
        public override string ToString()
        {
            return isKey
                ? string.Concat(
                    "Event: ", m_Type.ToString(),
                    "   Character: ", ((int)character).ToString(),
                    "   KeyCode: ", keyCode.ToString(),
                    "   Modifiers: ", modifiers.ToString())
                : string.Concat(
                    "Event: ", m_Type.ToString(),
                    "   Position: ", mousePosition.ToString(),
                    "   Modifiers: ", modifiers.ToString());
        }

        private void SetModifier(EventModifiers modifier, bool value)
        {
            if (value)
                modifiers |= modifier;
            else
                modifiers &= ~modifier;
        }
    }

    /// <summary>
    /// IMGUI control-id and capture bookkeeping, plus the system clipboard (design §3.8, inventory §A.3).
    /// </summary>
    public static class GUIUtility
    {
        /// <summary>
        /// The control that owns the pointer capture, or 0 for none. Survives frame boundaries, because a drag's
        /// capture has to outlive the frame its MouseDown arrived on.
        /// </summary>
        public static int hotControl
        {
            get => NowImguiState.hotControl;
            set => NowImguiState.hotControl = value;
        }

        /// <summary>The control that has keyboard focus, or 0 for none. Read by <c>Event.GetTypeForControl</c>.</summary>
        public static int keyboardControl
        {
            get => NowImguiState.keyboardControl;
            set => NowImguiState.keyboardControl = value;
        }

        /// <summary>Allocates the id that identifies the calling control for the rest of the frame.</summary>
        /// <remarks>
        /// <para>Design §3.8: "a monotonically increasing per-frame id (reset by <c>NowRuntime.BeginFrame</c>)".
        /// Unity derives its ids from a per-window hash of the call site; the shim counts instead, which gives the
        /// property the core actually relies on - the same call, in the same order, gets the same id every frame, so
        /// <c>NowGUI</c>'s <c>(context, controlId)</c> render-texture cache keys stay stable across frames.</para>
        /// <para>The counter is pre-incremented so the first id of a frame is 1: 0 means "no control" in IMGUI, and
        /// <c>NowIMGUIInputProvider</c> tests <c>_hostControlId != 0</c> before it touches
        /// <see cref="hotControl"/>.</para>
        /// <para>The <c>hint</c>, <c>focus</c> and <c>position</c> arguments are accepted and ignored, as they are in
        /// Unity's non-keyboard path: <c>hint</c> only disambiguates Unity's hash, <c>focus</c> only matters to
        /// keyboard tab order (the shim has no tab order, and <c>NowGUI</c> asks for <c>FocusType.Passive</c>), and
        /// <c>position</c> only feeds the editor's keyboard-navigation geometry.</para>
        /// </remarks>
        public static int GetControlID(FocusType focus)
        {
            return ++NowImguiState.controlIdCounter;
        }

        /// <inheritdoc cref="GetControlID(FocusType)"/>
        public static int GetControlID(int hint, FocusType focus)
        {
            return ++NowImguiState.controlIdCounter;
        }

        /// <inheritdoc cref="GetControlID(FocusType)"/>
        public static int GetControlID(int hint, FocusType focus, Rect position)
        {
            return ++NowImguiState.controlIdCounter;
        }

        /// <summary>The system clipboard, backed by <c>NowRuntime.host.clipboard</c>.</summary>
        /// <remarks>
        /// Null-safe in both directions (design §3.8): a host with no clipboard reads <c>""</c> and swallows the
        /// write. That matters at type-load time, not only at runtime - <c>NowClipboard</c>'s static field
        /// initialisers wrap this property in delegates (inventory §B.4, NowClipboard 14/16), and a throwing getter
        /// there would surface as a <c>TypeInitializationException</c> from an unrelated call site. A null string
        /// from the host is normalised to <c>""</c>, which is what Unity reports for an empty clipboard.
        /// </remarks>
        public static string systemCopyBuffer
        {
            get
            {
                INowClipboard clipboard = NowRuntime.host?.clipboard;
                if (clipboard == null)
                    return string.Empty;

                return clipboard.GetText() ?? string.Empty;
            }
            set
            {
                INowClipboard clipboard = NowRuntime.host?.clipboard;
                clipboard?.SetText(value ?? string.Empty);
            }
        }

        /// <summary>Converts a point from GUI space to screen space.</summary>
        /// <remarks>
        /// The identity, because the shim has no GUI clip stack and no editor-window offset: standalone GUI space
        /// <i>is</i> screen space. Kept so a host driving IMGUI does not have to special-case the shim.
        /// </remarks>
        public static Vector2 GUIToScreenPoint(Vector2 guiPoint)
        {
            NowImguiState.Touch();
            return guiPoint;
        }

        /// <summary>Converts a point from screen space to GUI space; the identity, see <see cref="GUIToScreenPoint"/>.</summary>
        public static Vector2 ScreenToGUIPoint(Vector2 screenPoint)
        {
            NowImguiState.Touch();
            return screenPoint;
        }
    }

    /// <summary>
    /// Rectangle allocation for the IMGUI automatic-layout system (design §3.8).
    /// </summary>
    /// <remarks>
    /// <para>The shim has no layout engine: no layout groups, no width/height resolution pass, and no second
    /// (repaint) pass to hand out the resolved rects. Every overload therefore returns the <i>minimum</i> requested
    /// size at the origin and records it for <see cref="GetLastRect"/>. That is well-defined and deterministic,
    /// which is all §3.8 asks for - <c>NowGUI.Auto</c> discards the rect and returns an empty scope while
    /// <c>Event.current</c> is null, so no standalone caller can observe it. A future host that really drives IMGUI
    /// has to bring its own layout.</para>
    /// <para>Note the consequence for <c>NowGUI.Auto(float height, ...)</c>, which asks for
    /// <c>GetRect(0f, float.MaxValue, height, height)</c>, i.e. "as wide as the container": the shim answers with
    /// width 0, because it has no container to measure. Deliberate - guessing <c>Screen.width</c> would make the
    /// shim silently disagree with whatever layout a host later installs.</para>
    /// </remarks>
    public static class GUILayoutUtility
    {
        /// <summary>Allocates a fixed-size rect.</summary>
        public static Rect GetRect(float width, float height)
        {
            return Allocate(width, height);
        }

        /// <inheritdoc cref="GetRect(float,float)"/>
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options)
        {
            return Allocate(width, height);
        }

        /// <summary>
        /// Allocates a flexible rect. Returns the minimum size; see the class remarks for why the maxima are ignored.
        /// </summary>
        /// <remarks>
        /// Not listed in design §3.8, which stops at the two-float overloads - but <c>NowGUI.Auto</c> calls this form
        /// at NowGUI.cs 1007 and 1018, so the two IMGUI files cannot compile without it. Unity declares both the
        /// plain and the <c>params</c> variant, so both are here. (Reported to the lead.)
        /// </remarks>
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight)
        {
            return Allocate(minWidth, minHeight);
        }

        /// <inheritdoc cref="GetRect(float,float,float,float)"/>
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, params GUILayoutOption[] options)
        {
            return Allocate(minWidth, minHeight);
        }

        /// <summary>
        /// The rect handed out by the last <c>GetRect</c> call this frame, or <c>Rect.zero</c> before the first one.
        /// </summary>
        /// <remarks>
        /// Cleared at the frame boundary rather than kept, because in Unity it belongs to the layout state that each
        /// IMGUI pass rebuilds, and one standalone frame is one pass.
        /// </remarks>
        public static Rect GetLastRect()
        {
            return NowImguiState.lastRect;
        }

        private static Rect Allocate(float width, float height)
        {
            Rect rect = new Rect(0f, 0f, width, height);
            NowImguiState.lastRect = rect;
            return rect;
        }
    }

    /// <summary>
    /// One constraint on a laid-out control - what <c>GUILayout.Width</c> and friends return in Unity.
    /// </summary>
    /// <remarks>
    /// The constructor is internal, so standalone code can never produce one; the type exists only so the
    /// <c>params GUILayoutOption[]</c> parameters on <c>NowGUI.Auto</c>'s public overloads - part of NowUI's own API
    /// surface - keep their signatures. Callers pass an empty array. Sealed and empty because
    /// <c>GUILayoutUtility</c> has nothing to apply it to (see its remarks).
    /// </remarks>
    public sealed class GUILayoutOption
    {
        internal GUILayoutOption()
        {
        }
    }

    /// <summary>
    /// The IMGUI drawing facade (design §3.8). Drawing is a no-op in the standalone build.
    /// </summary>
    public static class GUI
    {
        /// <summary>Set by a control that changed a value; cleared at the start of every frame.</summary>
        /// <remarks>
        /// Unity clears it when an IMGUI pass begins, and one standalone frame is one pass, so
        /// <c>NowRuntime.BeginFrame</c> is where the clear happens. <c>NowIMGUIInputProvider</c> only ever sets it
        /// (to make its host repaint), so the clear is what keeps that request from sticking forever.
        /// </remarks>
        public static bool changed
        {
            get => NowImguiState.changed;
            set => NowImguiState.changed = value;
        }

        /// <summary>Tint applied to everything IMGUI draws. Inert; kept so a host can round-trip it.</summary>
        public static Color color
        {
            get => NowImguiState.color;
            set => NowImguiState.color = value;
        }

        /// <summary>Tint applied to IMGUI text and images only. Inert; see <see cref="color"/>.</summary>
        public static Color contentColor
        {
            get => NowImguiState.contentColor;
            set => NowImguiState.contentColor = value;
        }

        /// <summary>Draws a texture into <paramref name="position"/>. A no-op in the standalone build.</summary>
        /// <remarks>
        /// <para>Unreachable in M1: <c>NowGUI</c>'s only call (NowGUI.cs 539) sits behind an
        /// <c>Event.current != null</c> test, and the standalone renderer never goes through IMGUI - it submits
        /// through <c>INowRenderBackend</c>. Drawing here would need an IMGUI pass with a bound target and a
        /// screen-space projection, none of which the shim has.</para>
        /// <para>A null <paramref name="image"/> is accepted silently rather than logged, so a host cannot make this
        /// no-op noisy.</para>
        /// </remarks>
        public static void DrawTexture(Rect position, Texture image)
        {
            NowImguiState.Touch();
        }

        /// <inheritdoc cref="DrawTexture(Rect,Texture)"/>
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode)
        {
            NowImguiState.Touch();
        }

        /// <inheritdoc cref="DrawTexture(Rect,Texture)"/>
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend)
        {
            NowImguiState.Touch();
        }
    }
}
