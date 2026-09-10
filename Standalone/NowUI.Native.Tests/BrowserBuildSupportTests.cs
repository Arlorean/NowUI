using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NowUI.Cli;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class BrowserBuildSupportTests
{
    string scratch = null!;
    [SetUp] public void SetUp() { scratch = Path.Combine(Path.GetTempPath(), "nowui-browser-build-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch); }
    [TearDown] public void TearDown() { Directory.Delete(scratch, true); }

    [Test]
    public void BrowserSdkSelectionIgnoresNewerMajorAndPrereleaseInstallations()
    {
        const string installed = "8.0.425 [/sdk]\n9.0.101 [/sdk]\n10.0.200 [/sdk]\n9.0.306 [/other path/sdk]\n9.0.400-preview.1 [/sdk]\n";
        Assert.That(BrowserRunner.SelectNet9Sdk(installed), Is.EqualTo("9.0.306"));
        Assert.Throws<InvalidOperationException>(() => BrowserRunner.SelectNet9Sdk("10.0.200 [/sdk]\n9.0.100-preview.1 [/sdk]"));
    }

    [Test]
    public void GeneratedSdkPinActuallySelectsAnInstalledNet9Sdk()
    {
        BrowserRunner.PinNet9Sdk(scratch);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch, "global.json")));
        var sdk = config.RootElement.GetProperty("sdk");
        Assert.That(sdk.GetProperty("rollForward").GetString(), Is.EqualTo("disable"));
        Assert.That(ProjectBuilder.RunDotnet(scratch, ["--version"], echo: false).Trim(), Is.EqualTo(sdk.GetProperty("version").GetString()));
    }

    [Test]
    public void StaticCompressionRoundTripsAndDoesNotCompressArchivesAgain()
    {
        byte[] source = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("const value = 'A shared C# browser scene';\n", 1000)));
        string file = Path.Combine(scratch, "app.js"); File.WriteAllBytes(file, source);
        File.WriteAllBytes(Path.Combine(scratch, "already.gz"), [1, 2, 3]);
        var report = BrowserCompression.Compress(scratch);
        Assert.That(report.Files, Is.EqualTo(1));
        Assert.That(report.BrotliBytes, Is.LessThan(report.SourceBytes));
        Assert.That(report.GzipBytes, Is.LessThan(report.SourceBytes));
        foreach (string extension in new[] { ".br", ".gz" })
        {
            using var input = File.OpenRead(file + extension);
            using Stream decoder = extension == ".br" ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
            using var decoded = new MemoryStream(); decoder.CopyTo(decoded);
            Assert.That(decoded.ToArray(), Is.EqualTo(source));
        }
        Assert.That(File.ReadAllBytes(file), Is.EqualTo(source));
        Assert.That(BrowserCompression.Compress(scratch), Is.EqualTo(report));
        Assert.That(Directory.GetFiles(scratch).Any(path => path.EndsWith(".gz.br") || path.EndsWith(".br.br") || path.EndsWith(".gz.gz")), Is.False);
    }

    [Test]
    public void CompressionBoundsFailBeforeCreatingVariants()
    {
        File.WriteAllBytes(Path.Combine(scratch, "app.wasm"), new byte[1024]);
        Assert.Throws<InvalidDataException>(() => BrowserCompression.Compress(scratch, maxTotalBytes: 512));
        Assert.That(Directory.GetFiles(scratch).Length, Is.EqualTo(1));
    }

    [Test]
    public void IncompressibleContentKeepsTheOriginalWithoutLargerVariants()
    {
        byte[] bytes = new byte[4096]; new Random(19).NextBytes(bytes);
        string file = Path.Combine(scratch, "data.wasm"); File.WriteAllBytes(file, bytes);
        var report = BrowserCompression.Compress(scratch);
        Assert.That(report.BrotliBytes, Is.EqualTo(report.SourceBytes));
        Assert.That(report.GzipBytes, Is.EqualTo(report.SourceBytes));
        Assert.That(File.Exists(file + ".br") || File.Exists(file + ".gz"), Is.False);
    }
}
