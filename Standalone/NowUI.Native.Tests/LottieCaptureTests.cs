using System.Diagnostics;
using NowUI.Hosting;
using NUnit.Framework;

namespace NowUI.Native.Tests;

[NonParallelizable, Category("NativeGraphics")]
public sealed class LottieCaptureTests
{
    string root = null!, scratch = null!, configuration = null!;
    [SetUp]
    public void SetUp()
    {
        if (Environment.GetEnvironmentVariable("NOWUI_TEST_NATIVE_GRAPHICS") != "1") Assert.Ignore("Enable native graphics for Lottie capture validation.");
        root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        scratch = Path.Combine(Path.GetTempPath(), "nowui-lottie-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }
    [TearDown] public void TearDown() { if (scratch != null && Directory.Exists(scratch)) Directory.Delete(scratch, true); }

    [Test]
    public void RemoteCaptureWaitsForRealAnimationAndUsesDeterministicClock()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(root, "Assets/NowUI/Assets/AnimatedEmoji/1f600.lottie"));
        using var server = new LocalAssetServer(_ => new(source, DelayMilliseconds: 150));
        string output = Path.Combine(scratch, "remote.png");
        var result = Run("render", "NowUI.Native.Tests/Scenes/Scenes.csproj", "RemoteLottieScene", output, server.Url + "/emoji.lottie", "--time", "0.5");
        Assert.That(result.Code, Is.Zero, result.Log);
        Assert.That(new NowPngImageDecoder().TryDecode(File.ReadAllBytes(output), out _, out _, out var pixels, out string error), Is.True, error);
        Assert.That(pixels.Where((_, i) => i % 4 < 3).Count(value => value > 100), Is.GreaterThan(1000), "Remote capture contains no rendered animation.");
        string again = Path.Combine(scratch, "again.png");
        var repeated = Run("render", "NowUI.Native.Tests/Scenes/Scenes.csproj", "RemoteLottieScene", again, server.Url + "/emoji.lottie", "--time", "0.5");
        Assert.That(repeated.Code, Is.Zero, repeated.Log);
        Assert.That(File.ReadAllBytes(again), Is.EqualTo(File.ReadAllBytes(output)), "Network timing must not advance the captured animation clock.");
    }

    [Test]
    public void ActualProjectLottieAssetsProduceChangingAnimationFrames()
    {
        string output = Path.Combine(scratch, "frames");
        var result = Run("animate", "Samples/NativePreview/NativePreview.csproj", "LottiePreviewScene", output, null, "--duration", "0.4", "--fps", "10");
        Assert.That(result.Code, Is.Zero, result.Log);
        var files = Directory.GetFiles(output, "*.png").OrderBy(p => p).ToArray();
        Assert.That(files.Length, Is.EqualTo(4));
        Assert.That(File.ReadAllBytes(files[0]), Is.Not.EqualTo(File.ReadAllBytes(files[^1])));
    }

    [Test]
    public void CaptureLoadTimeoutPreservesExistingOutput()
    {
        using var server = new LocalAssetServer(_ => new(System.Text.Encoding.UTF8.GetBytes(LottieProjectAssetTests.MinimalJson), DelayMilliseconds: 2500));
        string output = Path.Combine(scratch, "keep.png");
        File.WriteAllBytes(output, [1, 2, 3]);
        var result = Run("render", "NowUI.Native.Tests/Scenes/Scenes.csproj", "RemoteLottieScene", output,
            server.Url + "/slow.lottie", "--load-timeout", "1");
        Assert.That(result.Code, Is.Not.Zero);
        Assert.That(result.Log, Does.Contain("--load-timeout"));
        Assert.That(File.ReadAllBytes(output), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [TestCase("render")]
    [TestCase("animate")]
    public void NoRemoteWorkPreservesTheTwoWarmupFrameContract(string mode)
    {
        string output = Path.Combine(scratch, mode == "render" ? "frame.png" : "frames");
        string[] extra = mode == "render" ? [] : ["--duration", ".1", "--fps", "10"];
        var result = Run(mode, "NowUI.Native.Tests/Scenes/Scenes.csproj", "FrameCountScene", output, null, extra);
        Assert.That(result.Code, Is.Zero, result.Log);
        string frame = mode == "render" ? output : Path.Combine(output, "frame-000000.png");
        Assert.That(new NowPngImageDecoder().TryDecode(File.ReadAllBytes(frame), out _, out _, out var pixels, out var error), Is.True, error);
        Assert.That(pixels[0], Is.EqualTo(3), "There must be two zero-time warmups, followed by one captured frame.");
    }

    [Test]
    public void ReplayCanStartRemoteLoadingInTheZeroTimeCapturedFrame()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(root, "Assets/NowUI/Assets/AnimatedEmoji/1f600.lottie"));
        using var server = new LocalAssetServer(_ => new(source, DelayMilliseconds: 150));
        string input = Path.Combine(scratch, "press.json");
        File.WriteAllText(input, "[{\"time\":0,\"type\":\"down\",\"x\":30,\"y\":30}]");
        string output = Path.Combine(scratch, "pressed.png");
        var result = Run("render", "NowUI.Native.Tests/Scenes/Scenes.csproj", "OnPressRemoteLottieScene", output,
            server.Url + "/emoji.lottie", "--input", input);
        Assert.That(result.Code, Is.Zero, result.Log);
        Assert.That(new NowPngImageDecoder().TryDecode(File.ReadAllBytes(output), out _, out _, out var pixels, out var error), Is.True, error);
        Assert.That(pixels.Where((_, i) => i % 4 < 3).Count(value => value > 100), Is.GreaterThan(1000));
    }

    (int Code, string Log) Run(string mode, string project, string scene, string output, string? url, params string[] extra)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = scratch, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in new[] { Path.Combine(root, "Standalone/NowUI.Cli/bin", configuration, "net9.0/nowui.dll"), mode,
            Path.Combine(root, "Standalone", project), "--scene", scene, "--output", output, "--width", "960", "--height", "380",
            "--configuration", configuration, "--no-build" }.Concat(extra)) start.ArgumentList.Add(value);
        if (url != null) start.Environment["NOWUI_TEST_LOTTIE_URL"] = url;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); Assert.Fail("Lottie capture timed out."); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
