using NowUI.Desktop;
using NowUI.Cli;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class DesktopCompositionTests
{
    DesktopInput input = null!;
    NowFileResources resources = null!;

    [SetUp]
    public void SetUp()
    {
        NowInput.Reset();
        NowTextInput.Reset();
        NowFocus.Reset();
        NowControlState.Reset();
        resources = new NowFileResources();
        NowRuntime.Initialize(new CaptureHost(600, 400, resources, AppContext.BaseDirectory), new NullRenderBackend());
        input = new DesktopInput();
        input.SetViewportSize(300, 200, 600, 400);
        input.Install();
        NowTextInput.setImeEnabled(true);
    }

    [TearDown]
    public void TearDown() { input.Dispose(); resources.Dispose(); NowRuntime.Shutdown(); }

    [Test]
    public void PreeditPersistsAcrossFramesAndCommitArrivesExactlyOnce()
    {
        input.CompositionStart();
        input.CompositionUpdate("にほん");
        var first = Frame();
        Assert.That(first.composition, Is.EqualTo("にほん"));
        Assert.That(first.characters, Is.Null);
        Assert.That(Frame().composition, Is.EqualTo("にほん"));
        input.CompositionCommit("日本");
        input.CompositionEnd();
        var commit = Frame();
        Assert.That(commit.characters, Is.EqualTo("日本"));
        Assert.That(commit.composition, Is.Null);
        Assert.That(Frame().characters, Is.Null);
    }

    [Test]
    public void CompositionOwnsEditingNavigationAndCommitEnterForTheEntireFrame()
    {
        input.CompositionStart();
        input.CompositionUpdate("test");
        input.KeyDown(Keys.Backspace);
        input.KeyDown(Keys.Tab);
        input.KeyDown(Keys.Left);
        input.KeyDown(Keys.Enter);
        input.CompositionCommit("語");
        input.CompositionEnd();
        var text = Frame();
        input.TryGetSnapshot(new NowInputSurface(new Vector2(300, 200)), out var pointer);
        Assert.That(text.characters, Is.EqualTo("語"));
        Assert.That(text.enterPressed || text.enterHeld || text.backspaceHeld || text.leftHeld || text.tabHeld, Is.False);
        Assert.That(pointer.navigation, Is.EqualTo(Vector2.zero));
        Assert.That(pointer.submitPressed || pointer.focusNextPressed, Is.False);
        Assert.That(Frame().enterHeld, Is.False, "A held IME acceptance key must not submit in the following frame.");
    }

    [Test]
    public void CancelFocusLossDisableAndDiscardNeverCommitPreedit()
    {
        input.CompositionUpdate("pending");
        input.CompositionEnd();
        Assert.That(Frame().composition, Is.Null);
        input.CompositionUpdate("pending");
        input.SetFocused(false);
        input.CompositionCommit("must not leak");
        Assert.That(Frame().characters, Is.Null);
        input.SetFocused(true);
        input.CompositionUpdate("pending");
        NowTextInput.setImeEnabled(false);
        Assert.That(Frame().composition, Is.Null);
        NowTextInput.setImeEnabled(true);
        input.CompositionUpdate("pending");
        input.DiscardPendingText();
        Assert.That(Frame().composition, Is.Null);
    }

    [Test]
    public void CandidatePositionConvertsUiUnitsToClientPixels()
    {
        Frame(1.5f);
        NowTextInput.setCompositionCursor(new Vector2(120, 60));
        Assert.That(input.CompositionCursorClient, Is.EqualTo(new Vector2(90, 45)));
    }

    [Test]
    public void DisposeRestoresOnlyCompositionCallbacksItStillOwns()
    {
        Action<bool> enabled = _ => { };
        Action<Vector2> cursor = _ => { };
        NowTextInput.setImeEnabled = enabled;
        NowTextInput.setCompositionCursor = cursor;
        input.Dispose();
        Assert.That(NowTextInput.setImeEnabled, Is.SameAs(enabled));
        Assert.That(NowTextInput.setCompositionCursor, Is.SameAs(cursor));
    }

    [Test]
    public void ActualTextFieldKeepsPreeditOutOfValueAndDoesNotSubmitTheCommitKey()
    {
        using var drawing = new NowDrawList();
        string value = "A";
        NowResolvedId id = default;
        Draw(focus: true);
        input.CompositionStart();
        input.CompositionUpdate("にほん");
        var preedit = Draw();
        Assert.That(value, Is.EqualTo("A"));
        Assert.That(preedit.changed, Is.False);
        input.KeyDown(Keys.Enter);
        input.CompositionCommit("日本");
        input.CompositionEnd();
        var commit = Draw();
        Assert.That(value, Is.EqualTo("A日本"));
        Assert.That(commit.changed, Is.True);
        Assert.That(commit.submitted, Is.False);
        Assert.That(NowFocus.focusedResolvedId, Is.EqualTo(id));
        Draw();
        Assert.That(value, Is.EqualTo("A日本"));

        NowTextFieldResult Draw(bool focus = false)
        {
            NowRuntime.BeginFrame();
            try
            {
                input.Drain(2);
                using (NowInput.Begin(input, new NowInputSurface(new Vector2(300, 200))))
                using (drawing.Begin(new Vector2(300, 200)))
                {
                    id = NowControls.GetControlId("ime-field");
                    if (focus) NowFocus.Focus(id);
                    return Now.TextField(new NowRect(10, 10, 270, 44), "ime-field").Draw(ref value);
                }
            }
            finally { NowRuntime.EndFrame(); }
        }
    }

    NowTextInputFrame Frame(float scale = 2)
    {
        NowRuntime.BeginFrame();
        try { input.Drain(scale); input.TryGetFrame(out var frame); return frame; }
        finally { NowRuntime.EndFrame(); }
    }
}
