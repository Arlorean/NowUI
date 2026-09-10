using System.Diagnostics;
using NowUI.Hosting;
using NUnit.Framework;

namespace NowUI.Native.Tests;

[NonParallelizable, Category("NativeGraphics")]
public sealed class NativeRecordingTests
{
    private string scratch = null!;
    private string standalone = null!;
    private string configuration = null!;
    [SetUp]
    public void SetUp()
    {
        if (Environment.GetEnvironmentVariable("NOWUI_TEST_NATIVE_GRAPHICS") != "1") Assert.Ignore("Set NOWUI_TEST_NATIVE_GRAPHICS=1 for GPU recording checks.");
        standalone = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../.."));
        configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        scratch = Path.Combine(Path.GetTempPath(), "nowui-recording-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }
    [TearDown] public void TearDown() { if (scratch != null && Directory.Exists(scratch)) Directory.Delete(scratch, true); }

    [Test]
    public void AnimationReplaysRealControlsAndProducesIdenticalFrames()
    {
        string input = Path.Combine(scratch, "input.json");
        File.WriteAllText(input, """
            [
              {"time":0.1,"type":"click","x":30,"y":30},
              {"time":0.3,"type":"click","x":30,"y":85},
              {"time":0.5,"type":"type","text":"NowUI"},
              {"time":0.6,"type":"scroll","x":150,"y":130,"deltaY":-2}
            ]
            """);
        string first = Path.Combine(scratch, "first"), second = Path.Combine(scratch, "second");
        var a = Run("animate", "ReplayProbeScene", first, "--duration", "1", "--fps", "10", "--input", input);
        Assert.That(a.Code, Is.Zero, a.Log);
        var b = Run("animate", "ReplayProbeScene", second, "--duration", "1", "--fps", "10", "--input", input);
        Assert.That(b.Code, Is.Zero, b.Log);
        var frames = Directory.GetFiles(first, "*.png").OrderBy(p => p).ToArray();
        Assert.That(frames.Length, Is.EqualTo(10));
        foreach (string frame in frames) Assert.That(File.ReadAllBytes(frame), Is.EqualTo(File.ReadAllBytes(Path.Combine(second, Path.GetFileName(frame)))));
        Assert.That(new NowPngImageDecoder().TryDecode(File.ReadAllBytes(frames[^1]), out int width, out int height, out var pixels, out var error), Is.True, error);
        foreach (int x in new[] { 30, 90, 150 })
        {
            int offset = ((height - 1 - 145) * width + x) * 4;
            Assert.That(pixels.Skip(offset).Take(4), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), $"Control result at {x} is not green.");
        }
        Assert.That(File.Exists(Path.Combine(first, "animation.json")), Is.True);
    }

    [Test]
    public void FailedRecordingPublishesNothingAndPreservesExistingDirectory()
    {
        string output = Path.Combine(scratch, "failed");
        var failed = Run("animate", "ThrowScene", output, "--duration", ".1", "--fps", "10");
        Assert.That(failed.Code, Is.Not.Zero);
        Assert.That(Directory.Exists(output), Is.False);
        Assert.That(Directory.EnumerateDirectories(scratch, ".nowui-*"), Is.Empty);
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "existing output");
        var existing = Run("animate", "ClockScene", output, "--duration", ".1", "--fps", "10");
        Assert.That(existing.Code, Is.EqualTo(2));
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("existing output"));
    }

    private (int Code, string Log) Run(string command, string scene, string output, params string[] extra)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = scratch, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in new[] { Path.Combine(standalone, "NowUI.Cli/bin", configuration, "net9.0/nowui.dll"), command,
            Path.Combine(standalone, "NowUI.Native.Tests/Scenes/Scenes.csproj"), "--scene", scene, "--output", output,
            "--width", "200", "--height", "180", "--configuration", configuration, "--no-build" }.Concat(extra)) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); Assert.Fail("Recording timed out."); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
