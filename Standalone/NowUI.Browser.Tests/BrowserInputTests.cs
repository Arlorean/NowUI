using NowUI.Browser;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Browser.Tests;

[NonParallelizable]
public sealed class BrowserInputTests
{
    BrowserInput input;
    NowFileResources resources;
    NowDrawList drawing;
    Key binding;
    readonly Vector2 size = new(400, 200);

    sealed class Host : INowHostServices
    {
        readonly DefaultHostServices defaults = new();
        public INowResourceProvider resources { get; set; }
        public INowClipboard clipboard { get; set; }
        public INowTouchKeyboard touchKeyboard => null;
        public INowImageDecoder imageDecoder => null;
        public INowClock clock => defaults.clock;
        public INowLogger logger => defaults.logger;
        public NowScreenInfo screen => new(400, 200, 96);
        public RuntimePlatform platform => defaults.platform;
        public string[] layerNames => defaults.layerNames;
        public INowFetchProvider fetch => null;
        public double realtimeSeconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        public int screenWidth => 400;
        public int screenHeight => 200;
        public float screenDpi => 96;
        public Rect safeArea => new(0, 0, 400, 200);
        public string dataPath => AppContext.BaseDirectory;
        public string persistentDataPath => Path.GetTempPath();
    }

    [SetUp]
    public void Setup()
    {
        NowInput.Reset(); NowFocus.Reset(); NowControlState.Reset(); NowKeyInput.Reset(); NowTextInput.Reset();
        resources = new NowFileResources(); input = new BrowserInput();
        NowRuntime.Initialize(new Host { resources = resources, clipboard = input }, new NullRenderBackend());
        drawing = new NowDrawList(); input.Install(); binding = Key.E;
    }
    [TearDown]
    public void Teardown() { input.Dispose(); drawing.Dispose(); resources.Dispose(); NowRuntime.Shutdown(); }

    [TestCase(Key.F24)]
    [TestCase(Key.NumpadEquals)]
    [TestCase(Key.RightCtrl)]
    [TestCase(Key.MediaPlayPause)]
    public void PackedPhysicalKeysDriveTheExistingBindingField(Key key)
    {
        Bind([2, 30, 30, 0, 1]); Bind([3, 30, 30, 0, 0]);
        Assert.That(Bind([8, (int)key, 0, 0, 0, 9, (int)key, 0, 0, 0]), Is.True);
        Assert.That(binding, Is.EqualTo(key));
        Assert.That(Bind([]), Is.False);
    }

    [Test]
    public void CompositionStaysOutOfTextUntilCommitAndOwnsTheCommitKey()
    {
        string value = "A";
        NowTextFieldResult result = default;
        DrawText([], null, null, true);
        DrawText([16, 1, 0, 0, 0], null, "にほん");
        Assert.That(value, Is.EqualTo("A"));
        DrawText([8, (int)Key.Enter, 0, 0, 0, 16, 0, 0, 0, 0], "日本", "");
        Assert.That(value, Is.EqualTo("A日本"));
        Assert.That(result.submitted, Is.False);
        DrawText([], null, null);
        Assert.That(value, Is.EqualTo("A日本"));
        void DrawText(double[] packet, string chars, string preedit, bool focus = false) => Frame(packet, () =>
        {
            if (focus) NowFocus.Focus(NowControls.GetControlId("text"));
            result = Now.TextField(new NowRect(10, 10, 270, 40), "text").Draw(ref value);
        }, chars, preedit);
    }

    [Test]
    public void CancellationAbortsAnActualDragWithoutClicking()
    {
        Assert.That(Interact([2, 30, 30, 0, 1]).pressed, Is.True);
        Assert.That(Interact([1, 80, 30, 1, 0]).dragging, Is.True);
        var cancelled = Interact([4, 0, 0, 0, 0]);
        Assert.That(cancelled.dragCancelled, Is.True);
        Assert.That(cancelled.clicked || cancelled.dragEnded, Is.False);
    }

    [TestCase(3, NowPointerButton.Back)]
    [TestCase(4, NowPointerButton.Forward)]
    public void FiveButtonMappingReachesExistingInteractions(int index, NowPointerButton button)
    {
        Assert.That(Interact([2, 30, 30, index, 1 << index], button).pressed, Is.True);
        Assert.That(Interact([3, 30, 30, index, 0], button).clicked, Is.True);
    }

    [Test]
    public void GamepadMovesFocusAndSubmitsActualControlsOnce()
    {
        int clicks = 0;
        NowResolvedId second = default;
        Buttons([], true);
        Buttons([20, 0, 1, 0, 0]);
        Assert.That(NowFocus.focusedResolvedId, Is.EqualTo(second));
        Buttons([20, 0, 0, 0, 1]); Buttons([]);
        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(Snapshot([21, 0, 0, 0, 0]).submitReleased, Is.True);
        void Buttons(double[] packet, bool focus = false) => Frame(packet, () =>
        {
            if (focus) NowFocus.Focus(NowControls.GetControlId("first"));
            second = NowControls.GetControlId("second");
            Now.Button(new NowRect(10, 70, 150, 40), "First").SetId("first").Draw();
            if (Now.Button(new NowRect(210, 70, 150, 40), "Second").SetId("second").Draw()) clicks++;
        });
    }

    [Test]
    public void BrowserPasteGestureUsesClipboardAndNoDuplicateTypedCharacters()
    {
        string value = "";
        Frame([], () => { NowFocus.Focus(NowControls.GetControlId("text")); DrawText(); });
        input.SetText("clipboard🙂");
        Frame([17, 0, 0, 0, 0], DrawText);
        Frame([], DrawText);
        Assert.That(value, Is.EqualTo("clipboard🙂"));
        void DrawText() => Now.TextField(new NowRect(10, 10, 270, 40), "text").Draw(ref value);
    }

    [Test]
    public void BlurDropsKeysAndSourceChangeDoesNotCreateADragDelta()
    {
        Assert.That(Snapshot([8, (int)Key.A, 0, 0, 0]).navigation, Is.EqualTo(Vector2.left));
        Assert.That(Snapshot([10, 0, 0, 0, 0]).navigation, Is.EqualTo(Vector2.zero));
        var source = Snapshot([10, 1, 0, 0, 0, 15, 0, 0, 0, 0, 2, 300, 80, 0, 1]);
        Assert.That(source.pointerDelta, Is.EqualTo(Vector2.zero));
    }

    bool Bind(double[] packet) { bool result = false; Frame(packet, () => result = Now.KeyBindingField(new NowRect(10, 10, 150, 40), "binding").Draw(ref binding)); return result; }
    NowInteraction Interact(double[] packet, NowPointerButton button = NowPointerButton.Primary)
    { NowInteraction result = default; Frame(packet, () => result = NowInput.Interact(NowControls.GetControlId("probe"), new Rect(0, 0, 200, 100), button)); return result; }
    NowInputSnapshot Snapshot(double[] packet) { NowInputSnapshot result = default; Frame(packet, () => result = NowInput.current); return result; }
    void Frame(double[] packet, Action draw, string characters = null, string composition = null)
    {
        NowRuntime.BeginFrame();
        try
        {
            input.DrainPacket(packet, characters, composition);
            using (NowInput.Begin(input, new NowInputSurface(size)))
            using (drawing.Begin(size)) draw();
        }
        finally { NowRuntime.EndFrame(); }
    }
}
