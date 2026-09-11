using System.Diagnostics;
using NowUI.Hosting;
using NUnit.Framework;

namespace NowUI.Native.Tests;

[Category("NativeGraphics")]
public sealed class RendererParityTests
{
    [TestCase("gamma", 128), TestCase("linear", 188)]
    public void ColorSpaceConvertsColorsTexturesBlendingAndStraightAlpha(string colorSpace, int workingGray)
    {
        var pixels = Render("ColorSpaceScene", 128, 96, colorSpace);
        AssertPixel(pixels, 128, 96, 16, 16, 128, 128, 128, 255, 2);
        AssertPixel(pixels, 128, 96, 48, 16, 128, 128, 128, 255, 1);
        AssertPixel(pixels, 128, 96, 80, 16, workingGray, workingGray, workingGray, 255, 1);
        AssertPixel(pixels, 128, 96, 112, 16, workingGray, workingGray, workingGray, 255, 1);
        AssertPixel(pixels, 128, 96, 16, 80, 128, 64, 191, 128, 3);
        int solidText = 0;
        for (int y = 32; y < 64; y++) for (int x = 0; x < 100; x++)
        {
            int i = ((95 - y) * 128 + x) * 4;
            if (pixels[i] is >= 123 and <= 130) solidText++;
        }
        Assert.That(solidText, Is.GreaterThan(20), "The text must remain in its authored display color.");
    }

    [TestCase("gamma", 128), TestCase("linear", 188)]
    public void GlassBlursEarlierContentAndKeepsLaterContentSharp(string colorSpace, int gray)
    {
        var pixels = Render("GlassParityScene", 128, 96, colorSpace);
        AssertPixel(pixels, 128, 96, 4, 48, 255, 255, 255, 255, 0);
        AssertPixel(pixels, 128, 96, 12, 48, 0, 0, 0, 255, 0);
        AssertPixel(pixels, 128, 96, 48, 32, gray, gray, gray, 255, 12);
        AssertPixel(pixels, 128, 96, 64, 48, 255, 0, 0, 255, 0);
    }

    [TestCase("gamma"), TestCase("linear")]
    public void SdfBooleanOutlineShadowAndMaskRender(string colorSpace)
    {
        var pixels = Render("SdfParityScene", 128, 96, colorSpace);
        AssertPixel(pixels, 128, 96, 40, 30, 255, 0, 0, 255, 2);
        AssertPixel(pixels, 128, 96, 40, 25, 0, 255, 0, 255, 2);
        // The subtracted hole exposes the displaced blue shadow instead of the red shape fill.
        AssertPixel(pixels, 128, 96, 40, 48, 0, 0, 255, 255, 2);
        AssertPixel(pixels, 128, 96, 66, 51, 0, 0, 255, 255, 20);
        AssertPixel(pixels, 128, 96, 100, 48, 0, 255, 255, 255, 2);
        AssertPixel(pixels, 128, 96, 82, 18, 0, 0, 0, 255, 2);
    }

    [TestCase("gamma"), TestCase("linear")]
    public void SdfSpriteSilhouetteBakesThroughAllFiveImagePasses(string colorSpace)
    {
        var pixels = Render("SdfImageParityScene", 96, 96, colorSpace);
        AssertPixel(pixels, 96, 96, 48, 48, 255, 0, 0, 255, 2);
        AssertPixel(pixels, 96, 96, 30, 48, 0, 255, 0, 255, 2);
        AssertPixel(pixels, 96, 96, 18, 18, 0, 0, 0, 255, 0);
    }

    [TestCase("gamma"), TestCase("linear")]
    public void AdvertisedDataFormatsUploadBlitAndSampleWithoutColorDecoding(string colorSpace)
    {
        var pixels = Render("DataTextureParityScene", 224, 64, colorSpace);
        int red = colorSpace == "linear" ? 188 : 128;
        int green = colorSpace == "linear" ? 137 : 64;
        int blue = colorSpace == "linear" ? 225 : 191;
        for (int column = 0; column < 7; column++) for (int row = 0; row < 2; row++)
            AssertPixel(pixels, 224, 64, column * 32 + 16, row * 32 + 16,
                red, column >= 3 ? green : 0, column >= 5 ? blue : 0, 255, 1);
    }

    static byte[] Render(string scene, int width, int height, string colorSpace)
    {
        if (Environment.GetEnvironmentVariable("NOWUI_TEST_NATIVE_GRAPHICS") != "1")
            Assert.Ignore("Set NOWUI_TEST_NATIVE_GRAPHICS=1 in a desktop graphics session.");
        string standalone = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../.."));
        string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        string directory = Path.Combine(Path.GetTempPath(), "nowui-renderer-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        try
        {
            string output = Path.Combine(directory, "frame.png");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { Path.Combine(standalone, "NowUI.Cli/bin", configuration, "net9.0/nowui.dll"),
                "render", Path.Combine(standalone, "NowUI.Native.Tests/Scenes/Scenes.csproj"), "--scene", scene,
                "--output", output, "--width", width.ToString(), "--height", height.ToString(),
                "--color-space", colorSpace, "--unity-project", directory,
                "--configuration", configuration, "--no-build" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000)) { process.Kill(entireProcessTree: true); Assert.Fail("Renderer parity capture exceeded 60 seconds."); }
            Assert.That(process.ExitCode, Is.Zero, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            Assert.That(new NowPngImageDecoder().TryDecode(File.ReadAllBytes(output), out int actualWidth, out int actualHeight,
                out byte[] pixels, out string error), Is.True, error);
            Assert.That((actualWidth, actualHeight), Is.EqualTo((width, height)));
            string? artifacts = Environment.GetEnvironmentVariable("NOWUI_RENDER_ARTIFACT_DIR");
            if (!string.IsNullOrEmpty(artifacts)) { Directory.CreateDirectory(artifacts); File.Copy(output, Path.Combine(artifacts, scene + "-" + colorSpace + ".png"), true); }
            return pixels;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    static void AssertPixel(byte[] pixels, int width, int height, int x, int y, int r, int g, int b, int a, int tolerance)
    {
        int i = ((height - 1 - y) * width + x) * 4;
        int[] expected = [r, g, b, a];
        for (int c = 0; c < 4; c++) Assert.That((int)pixels[i + c], Is.EqualTo(expected[c]).Within(tolerance), $"Pixel ({x},{y}) channel {c}");
    }
}
