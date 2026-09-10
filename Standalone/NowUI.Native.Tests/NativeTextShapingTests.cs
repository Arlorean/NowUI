using NowUI.Cli;
using NowUI.Engine;
using NowUI.Hosting;
using NowUI.Internal;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class NativeTextShapingTests
{
    NowFileResources builtIns = null!;
    NowProjectAssets assets = null!;
    NowFont latin = null!;

    [SetUp]
    public void SetUp()
    {
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        builtIns = new NowFileResources();
        assets = new NowProjectAssets(root, builtIns);
        NowRuntime.Initialize(new CaptureHost(800, 240, assets, Path.Combine(root, "Assets")), new NullRenderBackend());
        Now.textShaping = true;
        latin = assets.LoadAsset<NowFont>("Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset");
    }

    [TearDown]
    public void TearDown()
    {
        assets?.Dispose();
        builtIns?.Dispose();
        NowRuntime.Shutdown();
        Now.textShaping = true;
    }

    [Test]
    public void DistributedNativePluginAppliesLatinLigaturesAndCombiningClusters()
    {
        Assert.That(latin.TryGetShapedRun("ffi", out var ligature), Is.True, "The distributed native plugin must support shaping.");
        Assert.That(ligature.Length, Is.EqualTo(1), "Noto Sans's ffi ligature must be substituted by HarfBuzz.");
        Assert.That(ligature[0].cluster, Is.Zero);
        Assert.That(ligature[0].glyphIndex, Is.GreaterThan(0));
        Assert.That(NativePluginResolver.LoadedPath("nowui-msdf"),
            Does.Contain(Path.Combine("runtimes", NativePluginResolver.CurrentRid, "native")),
            "Portable hosts resolve the current RID's plugin, even when app-local copies also exist.");
        Assert.That(latin.TryGetShapedRun("e\u0301", out var combining), Is.True);
        Assert.That(combining.Length, Is.EqualTo(1));
        Assert.That(combining[0].cluster, Is.Zero);
    }

    [Test]
    public void DistributedNativePluginKernsAndMeasurementMatchesItsAdvances()
    {
        Assert.That(latin.TryGetShapedRun("AV", out var kerned), Is.True);
        Assert.That(latin.TryGetShapedRun("A", out var a), Is.True);
        Assert.That(latin.TryGetShapedRun("V", out var v), Is.True);
        float advance = kerned.Sum(glyph => glyph.xAdvance);
        Assert.That(advance, Is.LessThan(a[0].xAdvance + v[0].xAdvance));
        Assert.That(latin.MeasureText("AV", 24).x, Is.EqualTo(advance * 24).Within(.001));
    }

    [Test]
    public void ArabicFormsShapeInVisualOrderAndBakeThroughTheActualCSharpRenderer()
    {
        var arabic = assets.LoadAsset<NowFont>("Assets/NowUI/Assets/Fonts/Noto_Sans_Arabic/NotoSansArabic-Regular.ttf.asset");
        Assert.That(arabic, Is.Not.Null);
        const string text = "\u0633\u0644\u0627\u0645"; // salaam: contextual Arabic forms, in right-to-left order.
        Assert.That(arabic.TryGetShapedRun(text, out var run), Is.True, "Arabic shaping must not fall back to isolated codepoints.");
        Assert.That(run.Length, Is.InRange(1, text.Length));
        Assert.That(run[0].cluster, Is.EqualTo(3));
        Assert.That(run[^1].cluster, Is.Zero);
        Assert.That(arabic.TryGetShapedRun("\u0633", out var isolatedSeen), Is.True);
        Assert.That(run[^1].glyphIndex, Is.Not.EqualTo(isolatedSeen[0].glyphIndex),
            "Connected initial seen must differ from the isolated form.");
        Assert.That(run.All(glyph => glyph.glyphIndex != 0), Is.True);
        Assert.That(arabic.EnsureShapedGlyphs(run, 32), Is.True);
        foreach (var glyph in run)
        {
            Assert.That(arabic.TryGetShapedGlyph((int)glyph.glyphIndex, 32, out var baked, out var material), Is.True);
            Assert.That(baked.atlasBounds.right, Is.GreaterThan(baked.atlasBounds.left));
            Assert.That(material, Is.Not.Null);
        }
        using var drawing = new NowDrawList();
        using (drawing.Begin(new Vector2(800, 240)))
            Now.Text(new NowRect(10, 10, 700, 200), arabic).SetFontSize(32).Draw(text);
        Assert.That(drawing.hasGeometry, Is.True);
    }
}
