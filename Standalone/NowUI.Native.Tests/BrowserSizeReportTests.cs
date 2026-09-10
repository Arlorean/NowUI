using System.Text.Json;
using NowUI.Cli;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class BrowserSizeReportTests
{
    [Test]
    public void TotalsCountUncompressedAssetsAndArchivesWithoutDoubleCountingEncodingsOrTheReport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nowui-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var (name, bytes) in new[] { ("app.wasm", 1000), ("app.wasm.br", 400), ("app.wasm.gz", 500),
                ("image.png", 600), ("archive.gz", 200), (BrowserSizeReport.FileName, 900), (BrowserSizeReport.FileName + ".br", 100) })
                File.WriteAllBytes(Path.Combine(directory, name), new byte[bytes]);
            var report = BrowserSizeReport.Write(directory);
            Assert.Multiple(() =>
            {
                Assert.That(report.OriginalBytes, Is.EqualTo(1800));
                Assert.That(report.BrotliBytes, Is.EqualTo(1200));
                Assert.That(report.GzipBytes, Is.EqualTo(1300));
                Assert.That(report.StoredBytes, Is.EqualTo(2700));
                Assert.That(report.Files.Select(file => file.Path), Is.EquivalentTo(new[] { "app.wasm", "image.png", "archive.gz" }));
            });
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, BrowserSizeReport.FileName)));
            Assert.That(document.RootElement.GetProperty("originalBytes").GetInt64(), Is.EqualTo(1800));
            Assert.That(BrowserSizeReport.Measure(directory).OriginalBytes, Is.EqualTo(report.OriginalBytes));
            Assert.That(BrowserSizeReport.Measure(directory).StoredBytes, Is.EqualTo(report.StoredBytes));
        }
        finally { Directory.Delete(directory, true); }
    }
}
