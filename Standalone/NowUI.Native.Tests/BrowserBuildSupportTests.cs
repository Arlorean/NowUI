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

    [TestCase(true)]
    [TestCase(false)]
    public void StaticCompressionRoundTripsAndDoesNotCompressArchivesAgain(bool optimizeSize)
    {
        byte[] source = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("const value = 'A shared C# browser scene';\n", 1000)));
        string file = Path.Combine(scratch, "app.js"); File.WriteAllBytes(file, source);
        File.WriteAllBytes(Path.Combine(scratch, "already.gz"), [1, 2, 3]);
        var report = BrowserCompression.Compress(scratch, optimizeSize: optimizeSize);
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
        Assert.That(BrowserCompression.Compress(scratch, optimizeSize: optimizeSize), Is.EqualTo(report));
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
    public void PerFileBoundsFailBeforeChangingExistingVariants()
    {
        File.WriteAllBytes(Path.Combine(scratch, "app.wasm"), new byte[1024]);
        string previous = Path.Combine(scratch, "app.wasm.br");
        File.WriteAllText(previous, "previous variant");
        Assert.Throws<InvalidDataException>(() => BrowserCompression.Compress(scratch, maxFileBytes: 512));
        Assert.That(File.ReadAllText(previous), Is.EqualTo("previous variant"));
        Assert.That(Directory.GetFiles(scratch).Length, Is.EqualTo(2));
    }

    [Test]
    public void HigherCompressionQualityDoesNotIncreasePatternedStaticPayloads()
    {
        byte[] source = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 2000)
            .Select(i => $"function renderScene{i}(width, height) {{ return drawPanel('Scene {i}', width, height); }}\n")));
        string path = Path.Combine(scratch, "app.js");
        File.WriteAllBytes(path, source);
        long previousBrotli = EncodedLength(source, brotli: true);
        long previousGzip = EncodedLength(source, brotli: false);
        var preview = BrowserCompression.Compress(scratch, optimizeSize: false);
        var publish = BrowserCompression.Compress(scratch);
        Assert.That(publish.BrotliBytes, Is.LessThanOrEqualTo(previousBrotli));
        Assert.That(preview.BrotliBytes, Is.LessThanOrEqualTo(previousBrotli));
        Assert.That(publish.BrotliBytes, Is.LessThanOrEqualTo(preview.BrotliBytes));
        Assert.That(publish.GzipBytes, Is.LessThanOrEqualTo(previousGzip));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
    }

    [Test]
    public void OptionalNativeSymbolMapsReceiveTheSameLosslessCompression()
    {
        byte[] source = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 300)
            .Select(i => $"{i}:NowUI.Browser.Scene.Draw_{i}\n")));
        string path = Path.Combine(scratch, "dotnet.native.js.symbols");
        File.WriteAllBytes(path, source);
        var report = BrowserCompression.Compress(scratch);
        Assert.That(report.Files, Is.EqualTo(1));
        Assert.That(report.BrotliBytes, Is.LessThan(source.Length));
        using var input = File.OpenRead(path + ".br");
        using var decoder = new BrotliStream(input, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        decoder.CopyTo(decoded);
        Assert.That(decoded.ToArray(), Is.EqualTo(source));
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

    [Test]
    public void AReplacedIncompressibleAssetRemovesItsStaleEncodings()
    {
        string file = Path.Combine(scratch, "data.wasm");
        File.WriteAllBytes(file, new byte[4096]);
        BrowserCompression.Compress(scratch);
        Assert.That(File.Exists(file + ".br") && File.Exists(file + ".gz"), Is.True);
        byte[] replacement = new byte[4096]; new Random(37).NextBytes(replacement);
        File.WriteAllBytes(file, replacement);
        BrowserCompression.Compress(scratch);
        Assert.That(File.Exists(file + ".br") || File.Exists(file + ".gz"), Is.False);
        Assert.That(File.ReadAllBytes(file), Is.EqualTo(replacement));
    }

    static long EncodedLength(byte[] source, bool brotli)
    {
        using var destination = new MemoryStream();
        using (Stream encoder = brotli
            ? new BrotliStream(destination, CompressionLevel.Optimal, leaveOpen: true)
            : new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true))
            encoder.Write(source);
        return destination.Length;
    }
}
