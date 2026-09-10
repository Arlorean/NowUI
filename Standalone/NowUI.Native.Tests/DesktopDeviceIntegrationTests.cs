using NowUI.Cli;
using NowUI.Desktop;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class DesktopDeviceIntegrationTests
{
    DesktopInput input = null!;
    NowFileResources resources = null!;
    NowDrawList drawing = null!;
    readonly Vector2 size = new(400, 200);
    Key binding;

    [SetUp]
    public void SetUp()
    {
        NowInput.Reset(); NowTextInput.Reset(); NowKeyInput.Reset(); NowFocus.Reset(); NowControlState.Reset();
        resources = new NowFileResources();
        NowRuntime.Initialize(new CaptureHost(400, 200, resources, AppContext.BaseDirectory), new NullRenderBackend());
        drawing = new NowDrawList();
        input = new DesktopInput();
        input.SetViewportSize(400, 200, 400, 200);
        input.Install();
        binding = Key.E;
    }

    [TearDown]
    public void TearDown() { input.Dispose(); drawing.Dispose(); resources.Dispose(); NowRuntime.Shutdown(); }

    [TestCase(Keys.A, Key.A)]
    [TestCase(Keys.D0, Key.Digit0)]
    [TestCase(Keys.D7, Key.Digit7)]
    [TestCase(Keys.F12, Key.F12)]
    [TestCase(Keys.F13, Key.F13)]
    [TestCase(Keys.F24, Key.F24)]
    [TestCase(Keys.KeyPad0, Key.Numpad0)]
    [TestCase(Keys.KeyPadEqual, Key.NumpadEquals)]
    [TestCase(Keys.RightControl, Key.RightCtrl)]
    [TestCase(Keys.LeftSuper, Key.LeftMeta)]
    [TestCase(Keys.PrintScreen, Key.PrintScreen)]
    [TestCase(Keys.Apostrophe, Key.Quote)]
    public void ExistingKeyBindingFieldCapturesPhysicalNativeKey(Keys native, Key expected)
    {
        ClickBinding();
        input.KeyDown(native);
        input.KeyUp(native); // A quick tap between frames must still be capturable.
        Assert.That(DrawBinding(), Is.True);
        Assert.That(binding, Is.EqualTo(expected));
        Assert.That(DrawBinding(), Is.False);
    }

    [Test]
    public void KeyCaptureHonorsLowestIdentityRepeatCancelClearAndImeOwnership()
    {
        ClickBinding();
        input.KeyDown(Keys.Z); input.KeyDown(Keys.A);
        Assert.That(DrawBinding(), Is.True);
        Assert.That(binding, Is.EqualTo(Key.A));
        ClickBinding();
        input.KeyDown(Keys.A, isRepeat: true);
        Assert.That(DrawBinding(), Is.False);
        input.KeyDown(Keys.Escape);
        Assert.That(DrawBinding(), Is.False);
        input.KeyUp(Keys.Escape);
        ClickBinding();
        input.KeyDown(Keys.Delete);
        Assert.That(DrawBinding(), Is.True);
        Assert.That(binding, Is.EqualTo(Key.None));
        input.KeyUp(Keys.Delete);
        ClickBinding();
        NowTextInput.setImeEnabled(true);
        input.CompositionStart(); input.KeyDown(Keys.Enter); input.CompositionCommit("字"); input.CompositionEnd();
        Assert.That(DrawBinding(), Is.False, "The IME's commit key cannot become a binding.");
        Assert.That(binding, Is.EqualTo(Key.None));
    }

    [Test]
    public void EnterArmsExistingBindingFieldButItsRepeatCannotBindItself()
    {
        Frame(() =>
        {
            NowFocus.Focus(NowControls.GetControlId("binding"));
            Now.KeyBindingField(new NowRect(10, 10, 150, 40), "binding").Draw(ref binding);
        });
        input.KeyDown(Keys.Enter);
        Assert.That(DrawBinding(), Is.False);
        input.KeyDown(Keys.Enter, isRepeat: true);
        Assert.That(DrawBinding(), Is.False);
        input.KeyUp(Keys.Enter); input.KeyDown(Keys.F24);
        Assert.That(DrawBinding(), Is.True);
        Assert.That(binding, Is.EqualTo(Key.F24));
    }

    [TestCase(3, NowPointerButton.Back)]
    [TestCase(4, NowPointerButton.Forward)]
    public void ExtraMouseButtonsActivateTheExistingInteractionContract(int native, NowPointerButton button)
    {
        input.PointerDown(30, 30, native);
        Assert.That(Interact(button).pressed, Is.True);
        input.PointerUp(30, 30, native);
        Assert.That(Interact(button).clicked, Is.True);
    }

    [Test]
    public void KeyboardNavigationPreservesApplicationSettingsAndOverlappingHeldKeys()
    {
        Assert.That(NowInput.navigationKeys, Is.EqualTo(NowNavigationKeys.All));
        input.KeyDown(Keys.Left); input.KeyDown(Keys.A);
        Assert.That(Snapshot().navigation, Is.EqualTo(Vector2.left));
        input.KeyUp(Keys.Left);
        Assert.That(Snapshot().navigation, Is.EqualTo(Vector2.left));
        NowInput.navigationKeys = NowNavigationKeys.Arrows;
        Assert.That(Snapshot().navigation, Is.EqualTo(Vector2.zero));
        input.KeyDown(Keys.Space);
        Assert.That(Snapshot().submitPressed, Is.False);
        input.KeyUp(Keys.Space);
        Assert.That(Snapshot().submitReleased, Is.False);
    }

    [Test]
    public void GamepadNavigatesAndActivatesActualButtonsOncePerPress()
    {
        NowResolvedId first = default, second = default;
        int clicks = 0;
        DrawButtons(focus: true);
        input.SetGamepadState(0, Vector2.right, Vector2.zero);
        DrawButtons();
        Assert.That(NowFocus.focusedResolvedId, Is.EqualTo(second));
        input.SetGamepadState(0, Vector2.zero, Vector2.zero, south: true);
        DrawButtons(); DrawButtons();
        Assert.That(clicks, Is.EqualTo(1));
        input.SetGamepadState(0, Vector2.zero, Vector2.zero, connected: false);
        Assert.That(Snapshot().submitReleased, Is.True);
        Assert.That(Snapshot().submitDown, Is.False);

        void DrawButtons(bool focus = false) => Frame(() =>
        {
            first = NowControls.GetControlId("first"); second = NowControls.GetControlId("second");
            if (focus) NowFocus.Focus(first);
            Now.Button(new NowRect(10, 70, 150, 40), "First").SetId("first").Draw();
            if (Now.Button(new NowRect(210, 70, 150, 40), "Second").SetId("second").Draw()) clicks++;
        });
    }

    [Test]
    public void GamepadDeadzoneCombinedNavigationIndependentButtonsAndFocusAreStable()
    {
        input.SetGamepadState(0, new Vector2(.1f, 0), Vector2.zero);
        Assert.That(Snapshot().navigation, Is.EqualTo(Vector2.zero));
        input.SetGamepadState(0, Vector2.right, Vector2.up, south: true, start: true, east: true, select: true);
        var initial = Snapshot();
        Assert.That(initial.navigation.magnitude, Is.EqualTo(1).Within(.0001));
        Assert.That(initial.submitPressed && initial.cancelPressed, Is.True);
        input.SetGamepadState(0, Vector2.zero, Vector2.zero, start: true, select: true);
        var partial = Snapshot();
        Assert.That(partial.submitDown && partial.submitReleased && partial.cancelDown && partial.cancelReleased, Is.True);
        input.SetFocused(false);
        Assert.That(Snapshot().submitDown, Is.False);
        input.SetGamepadState(0, Vector2.zero, Vector2.zero, south: true);
        Snapshot();
        input.SetFocused(true);
        Assert.That(Snapshot().submitPressed, Is.False, "An edge consumed by an unfocused window cannot activate a control later.");
    }

    [Test]
    public void PrimaryTouchKeepsDragOwnershipAndPublishesReleaseBeforeNextContact()
    {
        input.TouchDown(11, 30, 30);
        Assert.That(Interact().pressed, Is.True);
        input.TouchDown(22, 250, 100); input.TouchMove(22, 300, 100);
        Assert.That(Snapshot().pointerPosition, Is.EqualTo(new Vector2(30, 30)));
        input.TouchMove(11, 70, 30);
        Assert.That(Interact().dragging, Is.True);
        input.TouchUp(11, 70, 30);
        var released = Interact();
        Assert.That(released.dragEnded, Is.True);
        Assert.That(released.dragCancelled, Is.False);
        var next = Snapshot();
        Assert.That(next.primaryPressed, Is.True);
        Assert.That(next.pointerPosition, Is.EqualTo(new Vector2(300, 100)));
        Assert.That(next.pointerDelta, Is.EqualTo(Vector2.zero));
        input.TouchUp(22, 300, 100);
        Snapshot();
        Assert.That(Snapshot().hasPointer, Is.False, "Touch has no lingering hover after release.");
    }

    [Test]
    public void CancelledTouchCannotClickAndMouseCanResumeWithoutDeltaJump()
    {
        input.TouchDown(1, 30, 30); Interact();
        input.TouchMove(1, 70, 30); Interact();
        input.TouchUp(1, 70, 30, cancelled: true);
        var cancelled = Interact();
        Assert.That(cancelled.dragCancelled, Is.True);
        Assert.That(cancelled.clicked || cancelled.dragEnded, Is.False);
        input.PointerMove(150, 60);
        var mouse = Snapshot();
        Assert.That(mouse.hasPointer, Is.True);
        Assert.That(mouse.pointerDelta, Is.EqualTo(Vector2.zero));
        input.PointerDown(30, 30, 0); Interact();
        input.PointerUp(30, 30, 0);
        Assert.That(Interact().clicked, Is.True);
    }

    [Test]
    public void TouchActivatesTheSharedTextFieldAndUnicodeIsEditedOnce()
    {
        string text = "";
        input.TouchDown(1, 30, 30); DrawText();
        input.TouchUp(1, 30, 30); DrawText();
        input.TextInput("hé🙂"); DrawText(); DrawText();
        Assert.That(text, Is.EqualTo("hé🙂"));
        var command = OperatingSystem.IsMacOS() ? Keys.LeftSuper : Keys.LeftControl;
        input.KeyDown(command); input.KeyDown(Keys.A); DrawText();
        input.KeyUp(Keys.A); input.KeyUp(command);
        input.TextInput("replacement"); DrawText();
        Assert.That(text, Is.EqualTo("replacement"), "Held physical modifiers must also drive text-editing shortcuts.");
        void DrawText() => Frame(() => Now.TextField(new NowRect(10, 10, 270, 44), "text").Draw(ref text));
    }

    [Test]
    public void QuickTouchTapClicksAndTouchCannotStealAnExistingMouseDrag()
    {
        input.TouchDown(1, 30, 30); input.TouchUp(1, 30, 30);
        Assert.That(Interact().clicked, Is.True);
        Snapshot();
        input.PointerDown(30, 30, 0); Interact();
        input.TouchDown(2, 250, 100);
        input.PointerMove(70, 30);
        Assert.That(Interact().dragging, Is.True);
        Assert.That(Snapshot().pointerPosition, Is.EqualTo(new Vector2(70, 30)));
        input.PointerUp(70, 30, 0);
        Assert.That(Interact().dragEnded, Is.True);
        Assert.That(Snapshot().pointerPosition, Is.EqualTo(new Vector2(250, 100)));
    }

    [TestCase(3, NowPointerButton.Back)]
    [TestCase(4, NowPointerButton.Forward)]
    public void ReplaySupportsExtraButtonsAndModifierChords(int native, NowPointerButton button)
    {
        var replay = new InputReplay([
            new(0, "click", 30, 30, Button: native),
            new(.2, "keyDown", Key: "LeftShift"), new(.2, "keyDown", Key: "Tab"),
            new(.3, "keyUp", Key: "Tab"), new(.3, "keyUp", Key: "LeftShift")], 10, 1);
        replay.Apply(0, input);
        Assert.That(Interact(button).pressed, Is.True);
        replay.Apply(1, input);
        Assert.That(Interact(button).clicked, Is.True);
        replay.Apply(2, input);
        Assert.That(Snapshot().focusPreviousPressed, Is.True);
        replay.Apply(3, input);
        Assert.That(Snapshot().focusPreviousPressed, Is.False);
        Assert.Throws<ArgumentException>(() => new InputReplay([new(0, "down", Button: 5)], 10, 1));
    }

    bool DrawBinding()
    {
        bool changed = false;
        Frame(() => changed = Now.KeyBindingField(new NowRect(10, 10, 150, 40), "binding").Draw(ref binding));
        return changed;
    }

    void ClickBinding()
    { input.PointerDown(30, 30, 0); DrawBinding(); input.PointerUp(30, 30, 0); DrawBinding(); }

    NowInteraction Interact(NowPointerButton button = NowPointerButton.Primary)
    {
        NowInteraction result = default;
        Frame(() => result = NowInput.Interact(NowControls.GetControlId("interaction"), new Rect(0, 0, 200, 100), button));
        return result;
    }

    NowInputSnapshot Snapshot()
    {
        NowInputSnapshot result = default;
        Frame(() => result = NowInput.current);
        return result;
    }

    void Frame(Action draw)
    {
        NowRuntime.BeginFrame();
        try
        {
            input.Drain();
            using (NowInput.Begin(input, new NowInputSurface(size)))
            using (drawing.Begin(size)) draw();
        }
        finally { NowRuntime.EndFrame(); }
    }
}
