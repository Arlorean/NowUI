using NowUI.Desktop;
using NowUI.Engine;
using NUnit.Framework;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class DesktopInputTests
{
    DesktopInput input = null!;
    readonly NowInputSurface surface = new(new Vector2(200, 100));

    [SetUp]
    public void SetUp()
    {
        NowInput.Reset();
        NowTextInput.Reset();
        NowRuntime.Initialize(new DefaultHostServices(), new NullRenderBackend());
        input = new DesktopInput();
        input.SetViewportSize(200, 100, 400, 200);
        input.Install();
    }

    [TearDown]
    public void TearDown()
    {
        input.Dispose();
        NowRuntime.Shutdown();
    }

    [TestCase(1f, 60f, 40f)]
    [TestCase(2f, 30f, 20f)]
    public void MapsClientCoordinatesThroughFramebufferDensityAndUiScale(float scale, float x, float y)
    {
        input.PointerMove(30, 20);
        var frame = Frame(scale).Pointer;
        Assert.That(frame.hasPointer, Is.True);
        Assert.That(frame.pointerPosition, Is.EqualTo(new Vector2(x, y)));
    }

    [Test]
    public void ThreeButtonsPreserveIndependentPressHoldAndReleaseEdges()
    {
        input.PointerDown(20, 20, 0);
        input.PointerDown(20, 20, 1);
        input.PointerDown(20, 20, 2);
        var down = Frame().Pointer;
        foreach (NowPointerButton button in new[] { NowPointerButton.Primary, NowPointerButton.Secondary, NowPointerButton.Middle })
        {
            Assert.That(down.IsPointerDown(button), Is.True);
            Assert.That(down.WasPointerPressed(button), Is.True);
        }
        var held = Frame().Pointer;
        Assert.That(held.pointerButtonsDown, Is.EqualTo(down.pointerButtonsDown));
        Assert.That(held.pointerButtonsPressed, Is.EqualTo(NowPointerButtons.None));
        input.PointerUp(20, 20, 1);
        var up = Frame().Pointer;
        Assert.That(up.WasPointerReleased(NowPointerButton.Secondary), Is.True);
        Assert.That(up.IsPointerDown(NowPointerButton.Secondary), Is.False);
        Assert.That(up.IsPointerDown(NowPointerButton.Primary), Is.True);
        Assert.That(up.IsPointerDown(NowPointerButton.Middle), Is.True);
    }

    [Test]
    public void DragKeepsOffWindowCoordinatesUntilNormalRelease()
    {
        input.PointerDown(20, 20, 0);
        Frame();
        input.PointerLeave();
        input.PointerMove(240, 120);
        var drag = Frame().Pointer;
        Assert.That(drag.hasPointer, Is.True);
        Assert.That(drag.primaryDown, Is.True);
        Assert.That(drag.pointerPosition, Is.EqualTo(new Vector2(240, 120)));
        input.PointerUp(240, 120, 0);
        var release = Frame().Pointer;
        Assert.That(release.primaryReleased, Is.True);
        Assert.That(release.primaryDown, Is.False);
        Assert.That(release.hasPointer, Is.False);
        Assert.That(release.pointerCaptureCancelled, Is.False);
    }

    [Test]
    public void LostFocusCancelsActualInteractionAndDropsHeldKeyboardState()
    {
        input.PointerDown(20, 20, 0);
        Assert.That(Interaction().pressed, Is.True);
        input.PointerMove(60, 20);
        Assert.That(Interaction().dragging, Is.True);
        input.KeyDown(Keys.Left, KeyModifiers.Control);
        input.TextInput("stale");
        input.SetFocused(false);
        var cancelled = Interaction();
        Assert.That(cancelled.dragCancelled, Is.True);
        Assert.That(cancelled.clicked, Is.False);
        Assert.That(cancelled.dragEnded, Is.False);
        input.KeyUp(Keys.Left, KeyModifiers.Control); // Late OS key-up after focus cancellation.
        var blurred = Frame();
        Assert.That(blurred.Text.hasActivity, Is.False);
        Assert.That(blurred.Text.command, Is.False);
        Assert.That(blurred.Pointer.navigation, Is.EqualTo(Vector2.zero));
        input.SetFocused(true);
        var resumed = Frame();
        Assert.That(resumed.Pointer.primaryDown, Is.False);
        Assert.That(resumed.Text.characters, Is.Null);
    }

    [Test]
    public void ScrollUsesCanonicalNotchesAndClearsAfterOneFrame()
    {
        input.Scroll(.5f, -2);
        Assert.That(Frame().Pointer.scrollDelta, Is.EqualTo(new Vector2(.5f, -2)));
        Assert.That(Frame().Pointer.scrollDelta, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void NavigationAndEditingHaveHeldLevelsButOnlyOnePressedEdge()
    {
        input.KeyDown(Keys.Tab, KeyModifiers.Shift);
        input.KeyDown(Keys.Backspace, KeyModifiers.Shift);
        input.KeyDown(Keys.Left, KeyModifiers.Shift);
        var first = Frame();
        Assert.That(first.Pointer.focusPreviousPressed, Is.True);
        Assert.That(first.Pointer.focusNextPressed, Is.False);
        Assert.That(first.Pointer.navigation.x, Is.EqualTo(-1));
        Assert.That(first.Text.backspaceHeld && first.Text.leftHeld && first.Text.tabPressed, Is.True);
        input.KeyDown(Keys.Tab, KeyModifiers.Shift, isRepeat: true);
        input.KeyDown(Keys.Backspace, KeyModifiers.Shift, isRepeat: true);
        var repeat = Frame();
        Assert.That(repeat.Pointer.focusPreviousPressed, Is.False);
        Assert.That(repeat.Text.tabPressed, Is.False);
        Assert.That(repeat.Text.backspaceHeld, Is.True);
        input.KeyUp(Keys.Backspace);
        input.KeyUp(Keys.Left);
        input.KeyUp(Keys.Tab);
        var released = Frame();
        Assert.That(released.Text.backspaceHeld || released.Text.leftHeld || released.Text.tabHeld, Is.False);
        Assert.That(released.Pointer.navigation, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void FastEditingTapIsRetainedAndUnreadCharactersDoNotReplay()
    {
        input.KeyDown(Keys.Delete);
        input.KeyUp(Keys.Delete);
        input.TextInput("é🙂\n\t");
        var tap = Frame();
        Assert.That(tap.Text.deleteHeld, Is.True);
        Assert.That(tap.Text.characters, Is.EqualTo("é🙂"));
        Assert.That(Frame().Text.hasActivity, Is.False);
    }

    [Test]
    public void ShortcutsSuppressChordTextWithoutDroppingAltGrCharacters()
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Super : KeyModifiers.Control;
        input.KeyDown(Keys.V, command);
        input.TextInput("v");
        var paste = Frame();
        Assert.That(paste.Text.pastePressed, Is.True);
        Assert.That(paste.Text.characters, Is.Null);
        input.KeyUp(Keys.V);
        input.KeyDown(Keys.Q, KeyModifiers.Control | KeyModifiers.Alt);
        input.TextInput("@");
        var altGr = Frame();
        Assert.That(altGr.Text.characters, Is.EqualTo("@"));
        Assert.That(altGr.Text.pastePressed, Is.False);
        Assert.That(altGr.Text.option, Is.True);
    }

    [Test]
    public void DiscardAndRepeatedDrainDoNotDuplicateText()
    {
        input.TextInput("old");
        input.DiscardPendingText();
        input.TextInput("new");
        NowRuntime.BeginFrame();
        try
        {
            input.Drain(2);
            Assert.That(NowTextInput.current.characters, Is.EqualTo("new"));
            input.Drain(2);
            Assert.That(NowTextInput.current.characters, Is.EqualTo("new"));
            NowTextInput.DiscardPending();
            Assert.That(NowTextInput.current.characters, Is.Null);
        }
        finally { NowRuntime.EndFrame(); }
        Assert.That(Frame().Text.characters, Is.Null);
    }

    [Test]
    public void DisposePreservesSettingsReplacedByApplication()
    {
        var appProvider = new NowUIToolkitInputProvider();
        var appText = new EmptyTextSource();
        NowInput.defaultProvider = appProvider;
        NowTextInput.source = appText;
        NowInput.navigationKeys = NowNavigationKeys.None;
        input.Dispose();
        Assert.That(NowInput.defaultProvider, Is.SameAs(appProvider));
        Assert.That(NowTextInput.source, Is.SameAs(appText));
        Assert.That(NowInput.navigationKeys, Is.EqualTo(NowNavigationKeys.None));
    }

    sealed class EmptyTextSource : INowTextInputSource
    { public bool TryGetFrame(out NowTextInputFrame frame) { frame = default; return true; } }

    (NowInputSnapshot Pointer, NowTextInputFrame Text) Frame(float scale = 2)
    {
        NowRuntime.BeginFrame();
        try
        {
            input.Drain(scale);
            input.TryGetSnapshot(surface, out var pointer);
            input.TryGetFrame(out var text);
            return (pointer, text);
        }
        finally { NowRuntime.EndFrame(); }
    }

    NowInteraction Interaction()
    {
        NowRuntime.BeginFrame();
        try
        {
            input.Drain(2);
            using (NowInput.Begin(input, surface))
                return NowInput.Interact(NowControls.GetControlId("input-probe"), new Rect(0, 0, 200, 100));
        }
        finally { NowRuntime.EndFrame(); }
    }
}
