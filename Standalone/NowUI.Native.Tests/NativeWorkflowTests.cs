using System.Diagnostics;
using NowUI.Cli;
using NowUI.Desktop;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class NativeWorkflowTests
{
    private string scratch = null!;
    [SetUp] public void SetUp() => scratch = Path.Combine(Path.GetTempPath(), "nowui-workflow-tests-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { LoadedScene.Cleanup(); if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }

    [Test]
    public void OptionsSeparateLiveAndDeterministicWorkflows()
    {
        var animate = RenderOptions.Parse(["animate", "Scene.csproj", "--output", scratch, "--duration", "1.5", "--fps", "24", "--color-space", "linear"]);
        Assert.That((animate.Animate, animate.Duration, animate.Fps, animate.ColorSpace), Is.EqualTo((true, 1.5, 24, ColorSpace.Linear)));
        Assert.That(RenderOptions.Parse(["preview", "Scene.csproj"]).Watch, Is.True);
        Assert.That(RenderOptions.Parse(["preview", "Scene.csproj", "--no-watch"]).Watch, Is.False);
        Assert.Throws<ArgumentException>(() => RenderOptions.Parse(["animate", "Scene.csproj", "--output", scratch, "--duration", "NaN"]));
        Assert.Throws<ArgumentException>(() => RenderOptions.Parse(["animate", "Scene.csproj", "--output", scratch, "--fps", "121"]));
        Assert.Throws<ArgumentException>(() => RenderOptions.Parse(["preview", "Scene.csproj", "--input", "events.json"]));
        Assert.Throws<ArgumentException>(() => new InputReplay([new(0, "click", 5, 5)], 60, 0));
        Assert.Throws<ArgumentException>(() => new InputReplay([new(0, "type")], 60, 1));
        Directory.CreateDirectory(scratch);
        Assert.Throws<ArgumentException>(() => RenderOptions.Parse(["animate", "Scene.csproj", "--output", scratch]));
    }

    [Test]
    public void ReplaySeparatesClickEdgesAndDeliversTextAndScrollOnce()
    {
        NowRuntime.Initialize(new DefaultHostServices(), new NullRenderBackend());
        using var input = new DesktopInput();
        input.SetViewportSize(200, 100, 200, 100);
        input.Install();
        var replay = new InputReplay([
            new(0, "click", 30, 40), new(.2, "type", Text: "Hi 🌍"),
            new(.2, "scroll", 30, 40, DeltaY: -2)], 10, 1);
        try
        {
            var first = Frame(0);
            Assert.That(first.Pointer.primaryPressed, Is.True);
            Assert.That(first.Pointer.primaryReleased, Is.False);
            var release = Frame(1);
            Assert.That(release.Pointer.primaryReleased, Is.True);
            var typed = Frame(2);
            Assert.That(typed.Text.characters, Is.EqualTo("Hi 🌍"));
            Assert.That(typed.Pointer.scrollDelta.y, Is.EqualTo(-2));
            var empty = Frame(3);
            Assert.That(empty.Text.characters, Is.Null);
            Assert.That(empty.Pointer.scrollDelta, Is.EqualTo(Vector2.zero));
        }
        finally { input.Dispose(); NowRuntime.Shutdown(); }

        (NowInputSnapshot Pointer, NowTextInputFrame Text) Frame(int frame)
        {
            NowRuntime.BeginFrame();
            replay.Apply(frame, input); input.Drain();
            input.TryGetSnapshot(new NowInputSurface(new Vector2(200, 100)), out var pointer);
            input.TryGetFrame(out var text);
            NowRuntime.EndFrame();
            return (pointer, text);
        }
    }

    [Test]
    public void ScaffoldBuildsAgainstInstalledAssembliesAndNeverOverwritesFiles()
    {
        string project = SceneScaffold.Create(scratch);
        Assert.Throws<ArgumentException>(() => SceneScaffold.Create(scratch));
        // A package upgrade moves the tool directory. The running CLI supplies its current location.
        var xml = System.Xml.Linq.XDocument.Load(project);
        xml.Root!.Element("PropertyGroup")!.Element("NowUIHome")!.Value = Path.Combine(scratch, "old-tool-version");
        xml.Save(project);
        File.AppendAllText(Path.Combine(scratch, "Scene.cs"), """

            public static class ExtensionProbe
            {
                public static System.Type[] Types => new[]
                {
                    typeof(NowUI.Sdf.NowSdfGraph), typeof(NowUI.Docking.NowDockSpace), typeof(NowUI.Markup.NowMarkup)
                };
            }
            """);
        string built = ProjectBuilder.Build(RenderOptions.Parse(["render", project, "--output", Path.Combine(scratch, "frame.png")]));
        using var loaded = LoadedScene.Create(built, "PreviewScene");
        Assert.That(loaded.AssemblyPath, Does.Not.StartWith(Path.GetDirectoryName(built)));
        Assert.That(loaded.SceneType.Name, Is.EqualTo("PreviewScene"));
        var extensionTypes = (Type[])loaded.SceneType.Assembly.GetType("ExtensionProbe")!.GetProperty("Types")!.GetValue(null)!;
        Assert.That(extensionTypes.Select(t => t.Name), Is.EqualTo(new[] { "NowSdfGraph", "NowDockSpace", "NowMarkup" }));
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loaded.SceneType.Assembly);
        Assert.That(extensionTypes.All(t => ReferenceEquals(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(t.Assembly), context)), Is.True,
            "Extensions must load with the collectible scene, not in the CLI default context.");
        using var writable = new FileStream(built, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(writable.Length, Is.GreaterThan(0));
    }

    [Test]
    public void WatchRetainsLoadedSceneAfterBadBuildThenReloadsCodeAndAssets()
    {
        string project = SceneScaffold.Create(scratch);
        string source = Path.Combine(scratch, "Scene.cs");
        WriteScene(1);
        Directory.CreateDirectory(Path.Combine(scratch, "Assets"));
        var options = RenderOptions.Parse(["preview", project, "--unity-project", scratch]);
        string built = ProjectBuilder.Build(options);
        using var original = LoadedScene.Create(built, "PreviewScene");
        NowRuntime.RegisterAssembly(typeof(Now).Assembly);
        NowRuntime.Initialize(new DefaultHostServices(), new NullRenderBackend());
        original.ShutdownRuntime();
        Assert.That(original.SceneType.GetField("ResetCount")!.GetValue(null), Is.EqualTo(1));
        NowRuntime.ResetAll();
        Assert.That(original.SceneType.GetField("ResetCount")!.GetValue(null), Is.EqualTo(1), "The retired scene must be unregistered from later resets.");
        using var watcher = new PreviewWatch(options);
        var previous = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            File.WriteAllText(source, "This is deliberately invalid C#.");
            Wait(() => { Assert.That(watcher.Poll(), Is.Null); return errors.ToString().Contains("keeping the current preview"); });
            Assert.That(Value(original), Is.EqualTo(1));
            WriteScene(2);
            LoadedScene? updated = null;
            Wait(() => (updated = watcher.Poll()) != null);
            using (updated) Assert.That(Value(updated!), Is.EqualTo(2));
            File.WriteAllText(Path.Combine(scratch, "logo.png.meta"), "changed metadata");
            LoadedScene? assetUpdated = null;
            Wait(() => (assetUpdated = watcher.Poll()) != null);
            using (assetUpdated) Assert.That(Value(assetUpdated!), Is.EqualTo(2));
        }
        finally { Console.SetError(previous); }

        int Value(LoadedScene value) => (int)value.SceneType.GetField("Revision")!.GetRawConstantValue()!;
        void WriteScene(int revision) => File.WriteAllText(source,
            "using NowUI; using NowUI.Hosting; using UnityEngine; public sealed class PreviewScene : INowScene { public const int Revision = " + revision +
            "; public static int ResetCount; [RuntimeInitializeOnLoadMethod] static void Reset() { ResetCount++; } public void Draw(NowRect view) {} }");
        void Wait(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed.TotalSeconds > 40) Assert.Fail("Timed out watching reload. " + errors);
                Thread.Sleep(50);
            }
        }
    }

    [TestCase("Assets/icon.png", true)]
    [TestCase("Assets/icon.png.meta", true)]
    [TestCase("Scenes/Panel.cs", true)]
    [TestCase("Scenes/bin/Debug/net9.0/Panel.dll", false)]
    [TestCase("artifacts/preview.png", false)]
    [TestCase("Library/PackageCache/something.cs", false)]
    public void WatchIgnoresBuildAndGeneratedOutputs(string path, bool relevant)
        => Assert.That(PreviewWatch.IsRelevant(path), Is.EqualTo(relevant));
}
