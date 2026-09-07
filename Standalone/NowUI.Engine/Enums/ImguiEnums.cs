// Mirrors the UnityEngine IMGUI and legacy-input enums for the NowUI standalone build:
// EventType, FocusType, ScaleMode, EventModifiers, TouchPhase and IMECompositionMode.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/ImguiEnums.cs") and inventory §A.5;
// values verified against UnityEngine.IMGUIModule.dll / UnityEngine.InputLegacyModule.dll from Unity 6000.4.0f1.

using System;

namespace UnityEngine
{
    /// <summary>
    /// The kind of an IMGUI <c>Event</c> (design §3.2, inventory §A.5).
    /// </summary>
    /// <remarks>
    /// <para>Note the ordering oddity: <c>DragExited = 15</c> sits after <c>ContextClick</c>'s neighbours rather
    /// than next to <c>DragUpdated</c>/<c>DragPerform</c> (9/10), because it was added later and took the next free
    /// slot. Slots 17-19 and 22-29 are unused.</para>
    /// <para><b>Divergence from design §3.2, deliberate:</b> the design lists the six touch events as 80..85.
    /// Unity 6000.4 actually numbers them <c>TouchDown = 30</c> .. <c>TouchStationary = 35</c> (read out of
    /// UnityEngine.IMGUIModule.dll). §3.2's own rule is that "values are Unity's and are normative", and rule 4 of
    /// the unit brief requires the public API to match Unity exactly, so the verified values win over the listed
    /// ones — a wrong number is a silent bug, whereas the design's stated numbers have no consumer (inventory §A.5
    /// shows NowUI touches only Repaint, Layout, Used, Ignore, MouseDown/Up/Drag, MouseLeaveWindow, ScrollWheel,
    /// KeyDown and KeyUp). Reported to the lead.</para>
    /// <para>Unity also declares lower-case obsolete aliases (<c>mouseDown</c>, <c>repaint</c>, …). They are omitted
    /// per the design §3 omission rule, so a reference to one is a compile error rather than a surprise.</para>
    /// </remarks>
    public enum EventType
    {
        MouseDown = 0,
        MouseUp = 1,
        MouseMove = 2,
        MouseDrag = 3,
        KeyDown = 4,
        KeyUp = 5,
        ScrollWheel = 6,
        Repaint = 7,
        Layout = 8,
        DragUpdated = 9,
        DragPerform = 10,
        DragExited = 15,
        Ignore = 11,
        Used = 12,
        ValidateCommand = 13,
        ExecuteCommand = 14,
        ContextClick = 16,
        MouseEnterWindow = 20,
        MouseLeaveWindow = 21,
        TouchDown = 30,
        TouchUp = 31,
        TouchMove = 32,
        TouchEnter = 33,
        TouchLeave = 34,
        TouchStationary = 35,
    }

    /// <summary>How an IMGUI control participates in keyboard focus (design §3.2).</summary>
    /// <remarks>NowGUI passes <see cref="Passive"/> only (inventory §A.5).</remarks>
    public enum FocusType
    {
        /// <summary>Obsolete in Unity; kept because it holds slot 0 and <c>default(FocusType)</c> lands on it.</summary>
        Native = 0,
        Keyboard = 1,
        Passive = 2,
    }

    /// <summary>How <c>GUI.DrawTexture</c> fits a texture into a rect (design §3.2).</summary>
    public enum ScaleMode
    {
        StretchToFill = 0,
        ScaleAndCrop = 1,
        ScaleToFit = 2,
    }

    /// <summary>Modifier keys held during an IMGUI event (design §3.2).</summary>
    /// <remarks>
    /// <c>Command</c> is the macOS command key; on Windows the OS key reports as <c>Command</c> too in IMGUI, which
    /// is why NowUI's chord handling checks both <c>control</c> and <c>command</c>.
    /// </remarks>
    [Flags]
    public enum EventModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Command = 8,
        Numeric = 16,
        CapsLock = 32,
        FunctionKey = 64,
    }

    /// <summary>
    /// Phase of a legacy-input <c>Touch</c> (inventory §A.5 — NowDeviceInput reads Began / Ended / Canceled).
    /// </summary>
    /// <remarks>
    /// Spelled <c>Canceled</c> with one 'l', matching Unity; the InputSystem package spells the equivalent state
    /// the same way, so the two never disagree on spelling even though they disagree on numbering.
    /// </remarks>
    public enum TouchPhase
    {
        Began = 0,
        Moved = 1,
        Stationary = 2,
        Ended = 3,
        Canceled = 4,
    }

    /// <summary>
    /// IME composition mode for <c>Input.imeCompositionMode</c> (inventory §A.5 — NowTextInput sets On / Auto).
    /// </summary>
    /// <remarks>
    /// <c>Auto</c> is 0, so <c>default(IMECompositionMode)</c> means "let the platform decide", not "off".
    /// </remarks>
    public enum IMECompositionMode
    {
        Auto = 0,
        On = 1,
        Off = 2,
    }
}
