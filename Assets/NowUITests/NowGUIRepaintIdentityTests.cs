using System;
using System.Reflection;
using NowUI;
using NowUI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises the real IMGUI dispatch and renderer boundary, including non-Repaint input passes.</summary>
public class NowGUIRepaintIdentityTests
{
    static readonly MethodInfo RepaintImmediately = typeof(EditorWindow).GetMethod(
        "RepaintImmediately", BindingFlags.Instance | BindingFlags.NonPublic);

    NowGUIRepaintIdentityTestWindow _owner;
    NowGUIRepaintIdentityTestWindow _other;

    [SetUp]
    public void SetUp()
    {
        NowEditorGUI.DisposeAll();
        NowInput.Reset();
        NowFocus.Reset();
        NowControls.Reset();
        NowControlState.Reset();
        NowLayout.Reset();
        NowOverlay.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        if (_other)
            _other.Close();
        if (_owner)
            _owner.Close();
        NowEditorGUI.DisposeAll();
        NowOverlay.Reset();
        NowLayout.Reset();
        NowControlState.Reset();
        NowControls.Reset();
        NowFocus.Reset();
        NowInput.Reset();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeWheelOffsetSurvivesRepaint(bool authoredId)
    {
        _owner = CreateWindow(authoredId);
        Prime(_owner);

        Send(_owner, EventType.ScrollWheel, new Vector2(60f, 60f), new Vector2(0f, 3f));
        var wheel = _owner.samples[0];
        Assert.Greater(wheel.offsetAfter.y, 0f, "The real ScrollView must handle the native wheel tick.");
        Assert.AreEqual(EventType.Used, _owner.eventAfter, "Owned wheel input must be consumed.");

        Repaint(_owner);
        var repaint = _owner.samples[0];
        TestContext.WriteLine($"Wheel: input offset {wheel.offsetAfter.y}, repaint offset {repaint.offsetBefore.y}, content y {repaint.contentY}.");
        Assert.AreEqual(wheel.offsetAfter.y, repaint.offsetBefore.y, 0.001f,
            "Repaint must read the scroll offset written in the preceding input pass.");
        Assert.AreEqual(-wheel.offsetAfter.y, repaint.contentY, 0.001f,
            "The rendered content must move by the stored wheel offset.");
        Assert.AreEqual(wheel.scrollId, repaint.scrollId,
            "The scroll view must have the same resolved identity during input and Repaint.");
        Assert.AreEqual(0f, _owner.samples[1].offsetAfter.y,
            "Scrolling one panel must leave its sibling untouched.");

        Send(_owner, EventType.Layout);
        Assert.AreEqual(wheel.offsetAfter.y, _owner.samples[0].offsetBefore.y, 0.001f);
        Repaint(_owner);
        Assert.AreEqual(wheel.offsetAfter.y, _owner.samples[0].offsetBefore.y, 0.001f,
            "Extra Layout/Repaint passes must retain the offset without applying the wheel twice.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeScrollbarDragSurvivesInterleavedRepaint(bool authoredId)
    {
        _owner = CreateWindow(authoredId);
        Prime(_owner);
        float thumbX = 10f + 200f - 2f - NowTheme.themeAsset.controlStyles.scrollbarWidth * 0.5f;
        var press = new Vector2(thumbX, 20f);
        var drag = new Vector2(thumbX, 90f);
        bool released = false;
        try
        {
            Send(_owner, EventType.MouseDown, press);
            Assert.AreEqual(EventType.Used, _owner.eventAfter,
                "The real scrollbar must acquire the native pointer press.");
            Repaint(_owner);
            Send(_owner, EventType.MouseDrag, drag, drag - press);
            float draggedOffset = _owner.samples[0].offsetAfter.y;
            Assert.Greater(draggedOffset, 100f, "Dragging the thumb must move the scroll content.");

            Repaint(_owner);
            TestContext.WriteLine($"Drag: input offset {draggedOffset}, repaint offset {_owner.samples[0].offsetBefore.y}, content y {_owner.samples[0].contentY}.");
            Assert.AreEqual(draggedOffset, _owner.samples[0].offsetBefore.y, 0.001f,
                "Repaint must preserve both the thumb gesture state and the resulting content offset.");
            Assert.AreEqual(-draggedOffset, _owner.samples[0].contentY, 0.001f);

            Send(_owner, EventType.MouseUp, drag);
            released = true;
            Assert.AreEqual(EventType.Used, _owner.eventAfter, "The captured release must be consumed.");
            Repaint(_owner);
            Assert.AreEqual(draggedOffset, _owner.samples[0].offsetBefore.y, 0.001f);
            Assert.AreEqual(0f, _owner.samples[1].offsetAfter.y);
        }
        finally
        {
            // Assertions may abort while Unity still routes the native capture
            // to this window. Release in its OnGUI context before closing it.
            if (!released && _owner)
                _owner.SendEvent(new Event { type = EventType.MouseUp, mousePosition = drag, button = 0 });
        }
    }

    [Test]
    public void IdenticalLocalIdsStayDistinctAcrossPanelsAndEditorWindows()
    {
        _owner = CreateWindow(authoredId: true);
        _other = CreateWindow(authoredId: true);
        Prime(_owner);
        Prime(_other);
        var ownerFirst = _owner.samples[0].scrollId;
        var ownerSecond = _owner.samples[1].scrollId;
        var otherFirst = _other.samples[0].scrollId;
        var otherSecond = _other.samples[1].scrollId;
        Assert.AreEqual(4, new System.Collections.Generic.HashSet<NowResolvedId>
            { ownerFirst, ownerSecond, otherFirst, otherSecond }.Count,
            "The same authored scroll key in four panels must resolve to four distinct owners.");

        Send(_owner, EventType.ScrollWheel, new Vector2(60f, 60f), new Vector2(0f, 3f));
        float offset = _owner.samples[0].offsetAfter.y;
        Assert.Greater(offset, 0f);
        Repaint(_other);
        Assert.AreEqual(0f, _other.samples[0].offsetAfter.y);
        Assert.AreEqual(0f, _other.samples[1].offsetAfter.y);
        Repaint(_owner);
        Assert.AreEqual(offset, _owner.samples[0].offsetBefore.y, 0.001f);
        Assert.AreEqual(0f, _owner.samples[1].offsetAfter.y);
        Assert.AreEqual(ownerFirst, _owner.samples[0].scrollId);
        Assert.AreEqual(otherFirst, _other.samples[0].scrollId);
    }

    static NowGUIRepaintIdentityTestWindow CreateWindow(bool authoredId)
    {
        var window = ScriptableObject.CreateInstance<NowGUIRepaintIdentityTestWindow>();
        window.authoredId = authoredId;
        window.titleContent = new GUIContent("NowUI repaint identity regression");
        window.position = new Rect(100f, 100f, 440f, 160f);
        window.ShowUtility();
        return window;
    }

    static void Prime(NowGUIRepaintIdentityTestWindow window)
    {
        // One-pass layout caches settle on subsequent declarations. Prime both
        // pass kinds so the regression measures persistence rather than cold layout.
        for (int i = 0; i < 3; ++i)
        {
            Send(window, EventType.Layout);
            Repaint(window);
        }
        Assert.Greater(window.samples[0].maxOffset.y, 500f);
        Assert.AreEqual(0f, window.samples[0].offsetAfter.y);
    }

    static void Send(NowGUIRepaintIdentityTestWindow window, EventType type,
        Vector2 position = default, Vector2 delta = default)
    {
        window.simulatedPointerPosition = position;
        int previous = window.dispatchCount;
        window.SendEvent(new Event { type = type, mousePosition = position, delta = delta, button = 0 });
        CheckDispatch(window, previous, type);
    }

    static void Repaint(NowGUIRepaintIdentityTestWindow window)
    {
        Assert.NotNull(RepaintImmediately, "Unity's synchronous EditorWindow repaint seam changed.");
        int previous = window.dispatchCount;
        RepaintImmediately.Invoke(window, null);
        CheckDispatch(window, previous, EventType.Repaint);
    }

    static void CheckDispatch(NowGUIRepaintIdentityTestWindow window, int previous, EventType expected)
    {
        Assert.IsNull(window.error, window.error?.ToString());
        Assert.Greater(window.dispatchCount, previous, "Unity must invoke the test window's real OnGUI.");
        Assert.AreEqual(expected, window.eventBefore);
    }
}

sealed class NowGUIRepaintIdentityTestWindow : EditorWindow
{
    internal struct Sample
    {
        public NowResolvedId scrollId;
        public Vector2 offsetBefore;
        public Vector2 offsetAfter;
        public Vector2 maxOffset;
        public float contentY;
    }

    internal bool authoredId;
    internal Vector2 simulatedPointerPosition;
    internal readonly Sample[] samples = new Sample[2];
    internal int dispatchCount;
    internal EventType eventBefore;
    internal EventType eventAfter;
    internal Exception error;

    void OnGUI()
    {
        ++dispatchCount;
        eventBefore = Event.current.type;
        Vector2 nativeMousePosition = Event.current.mousePosition;
        // SendEvent moves the simulated pointer, not the OS cursor. Unity's
        // synchronous repaint can emit Layout/Repaint at the OS cursor; keep
        // those passes in the same synthetic gesture as the injected events.
        if (eventBefore == EventType.Layout || eventBefore == EventType.Repaint)
            Event.current.mousePosition = simulatedPointerPosition;
        try
        {
            for (int panel = 0; panel < samples.Length; ++panel)
            {
                using (var ui = NowEditorGUI.Auto(new Rect(10f + panel * 220f, 10f, 200f, 120f)))
                {
                    var scroll = Now.ScrollView(new NowRect(0f, 0f, ui.width, ui.height),
                        authoredId ? new NowId("same-scroll") : default).Begin();
                    Sample sample;
                    using (scroll)
                    {
                        sample = new Sample
                        {
                            scrollId = NowFocus.currentScrollRegionResolvedId,
                            offsetBefore = scroll.scrollOffset,
                            maxOffset = scroll.maxScrollOffset
                        };
                        NowRect content = NowLayout.ReserveRect(width: 120f, height: 1000f);
                        sample.contentY = content.y;
                        Now.Rectangle(content).SetColor(Color.green).Draw();
                    }
                    // Wheel and scrollbar handling run when the scope is disposed.
                    sample.offsetAfter = scroll.scrollOffset;
                    samples[panel] = sample;
                }
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            Event.current.mousePosition = nativeMousePosition;
        }
        eventAfter = Event.current.type;
    }
}
