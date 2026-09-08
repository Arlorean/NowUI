using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NowUI;
using NowUI.Editor;
using Object = UnityEngine.Object;

/// <summary>
/// Baked font pages: an editor bake of the authored characters must serve the
/// runtime exactly like a warmed dynamic cache, without creating a compiler
/// session, and must survive the asset round trip and cache clears.
/// </summary>
public class NowFontBakedPagesTests
{
    const string FontAssetPath = "Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset";
    const string TempFolder = "Assets/NowUITests/Temp";
    const string TempAssetPath = TempFolder + "/NowFontBakedPagesTests.asset";
    const string SampleText = "The quick brown fox jumps over the lazy dog 0123456789";
    const float FontSize = 24f;
    const float Tolerance = 1e-4f;

    byte[] _fontBytes;
    readonly List<Object> _cleanup = new List<Object>();

    [OneTimeSetUp]
    public void LoadFontBytes()
    {
        var font = AssetDatabase.LoadAssetAtPath<NowFont>(FontAssetPath);
        Assert.NotNull(font, $"Test font asset not found at {FontAssetPath}");
        Assert.IsTrue(font.TryGetSourceBytes(out _fontBytes), "Test font asset has no embedded source bytes.");
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = 0; i < _cleanup.Count; ++i)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }

        _cleanup.Clear();

        if (AssetDatabase.LoadMainAssetAtPath(TempAssetPath) != null)
            AssetDatabase.DeleteAsset(TempAssetPath);

        if (AssetDatabase.IsValidFolder(TempFolder))
            AssetDatabase.DeleteAsset(TempFolder);
    }

    NowFont CreateFont()
    {
        var font = ScriptableObject.CreateInstance<NowFont>();
        font.InitializeDynamicSource(_fontBytes, NowFont.DEFAULT_DYNAMIC_ATLAS_SIZE, NowFont.DEFAULT_DYNAMIC_PIXEL_RANGE);
        _cleanup.Add(font);
        return font;
    }

    List<NowFont.BakedPage> Bake(NowFont font, string characters)
    {
        Assert.IsTrue(NowFontBaker.TryBakePages(font, characters, out var pages, out string error), error);
        Assert.IsNotNull(pages);
        Assert.Greater(pages.Count, 0);

        for (int i = 0; i < pages.Count; ++i)
            _cleanup.Add(pages[i].texture);

        return pages;
    }

    static HashSet<int> CollectKeys(List<NowFont.BakedPage> pages)
    {
        var keys = new HashSet<int>();

        for (int i = 0; i < pages.Count; ++i)
        {
            foreach (var glyph in pages[i].glyphs)
                Assert.IsTrue(keys.Add(glyph.unicode), $"Key {glyph.unicode} was baked twice.");
        }

        return keys;
    }

    static List<int> Codepoints(string text)
    {
        var codepoints = new List<int>();

        for (int i = 0; i < text.Length; ++i)
            codepoints.Add(NowFont.ReadCodepoint(text, ref i));

        return codepoints;
    }

    static void AssertSameGlyph(in NowFontAtlasInfo.Glyph expected, in NowFontAtlasInfo.Glyph actual, string label)
    {
        Assert.AreEqual(expected.advance, actual.advance, Tolerance, $"{label}: advance");
        Assert.AreEqual(expected.planeBounds.left, actual.planeBounds.left, Tolerance, $"{label}: plane left");
        Assert.AreEqual(expected.planeBounds.right, actual.planeBounds.right, Tolerance, $"{label}: plane right");
        Assert.AreEqual(expected.planeBounds.top, actual.planeBounds.top, Tolerance, $"{label}: plane top");
        Assert.AreEqual(expected.planeBounds.bottom, actual.planeBounds.bottom, Tolerance, $"{label}: plane bottom");
        Assert.AreEqual(
            expected.atlasBounds.right - expected.atlasBounds.left,
            actual.atlasBounds.right - actual.atlasBounds.left,
            Tolerance,
            $"{label}: cell width");
        Assert.AreEqual(
            expected.atlasBounds.top - expected.atlasBounds.bottom,
            actual.atlasBounds.top - actual.atlasBounds.bottom,
            Tolerance,
            $"{label}: cell height");
    }

    [Test]
    public void BakeCoversEveryAuthoredCodepoint()
    {
        var font = CreateFont();
        var pages = Bake(font, NowFontBaker.ASCII);
        var keys = CollectKeys(pages);
        int maxSide = font.GetDynamicPageSize(0);

        foreach (int codepoint in Codepoints(NowFontBaker.ASCII))
            Assert.IsTrue(keys.Contains(codepoint), $"U+{codepoint:X4} was not baked.");

        for (int i = 0; i < pages.Count; ++i)
        {
            var page = pages[i];
            Assert.IsTrue(page.texture, "Baked page has no texture.");
            Assert.AreEqual(page.texture.width, page.texture.height, "Baked pages are square.");
            Assert.IsTrue(Mathf.IsPowerOfTwo(page.texture.width), "Baked page side is a power of two.");
            Assert.LessOrEqual(page.texture.width, maxSide);
            Assert.AreEqual(font.GetBaseDynamicGlyphSize(), page.atlasSize);
            Assert.AreEqual(font.GetBaseDynamicPixelRange(), page.pixelRange);
            Assert.Greater(page.metrics.lineHeight, 0f);
        }
    }

    [Test]
    public void PrintableAsciiFitsOneDefaultPage()
    {
        var font = CreateFont();
        var pages = Bake(font, NowFontBaker.ASCII);

        Assert.AreEqual(1, pages.Count, "Printable ASCII should fit one page at the default settings.");
        Assert.LessOrEqual(
            pages[0].texture.width,
            NowFont.DEFAULT_DYNAMIC_PAGE_SIZE,
            "Printable ASCII should not need more than the default page size.");
    }

    [Test]
    public void BakeRegistersGlyphIndexKeysForManagedFonts()
    {
        Assert.IsTrue(
            NowUI.NowFontCompiler.DynamicSession.TryCreate(_fontBytes, 64, 16, 256, out var session, out string error),
            error);

        using (session)
        {
            if (!session.supportsGlyphIndexBaking)
                Assert.Ignore("Glyph-index baking needs the managed compiler session.");

            var font = CreateFont();
            var pages = Bake(font, NowFontBaker.ASCII);
            var records = new Dictionary<int, NowFontAtlasInfo.Glyph>();

            for (int i = 0; i < pages.Count; ++i)
            {
                foreach (var glyph in pages[i].glyphs)
                    records[glyph.unicode] = glyph;
            }

            foreach (int codepoint in Codepoints(NowFontBaker.ASCII))
            {
                Assert.IsTrue(session.TryGetGlyphIndex(codepoint, out int glyphIndex));
                int key = NowFont.EncodeGlyphIndexKey(glyphIndex);
                Assert.IsTrue(records.ContainsKey(key), $"U+{codepoint:X4} has no glyph-index record.");
                AssertSameGlyph(records[codepoint], records[key], $"U+{codepoint:X4}");
                Assert.AreEqual(records[codepoint].atlasBounds.left, records[key].atlasBounds.left, "Both keys share one cell.");
                Assert.AreEqual(records[codepoint].atlasBounds.bottom, records[key].atlasBounds.bottom, "Both keys share one cell.");
            }
        }
    }

    [Test]
    public void BakedPagesServeGlyphsWithoutASession()
    {
        var font = CreateFont();
        var pages = Bake(font, NowFontBaker.ASCII);
        font.SetBakedPages(NowFontBaker.ASCII, false, pages.ToArray());

        Assert.IsTrue(font.GetGlyph('A', FontSize, out var glyph));
        Assert.Greater(glyph.advance, 0f);
        Assert.AreEqual(pages.Count, font.GetCachedDynamicPageCount());
        Assert.AreEqual(0, font.dynamicSessionCount, "Baked glyphs must not open a compiler session.");

        Vector2 size = font.MeasureText(SampleText, FontSize);
        Assert.Greater(size.x, 0f);
        Assert.AreEqual(pages.Count, font.GetCachedDynamicPageCount(), "Measuring baked text must not add pages.");
        Assert.AreEqual(0, font.dynamicSessionCount, "Measuring baked text must not open a compiler session.");
        Assert.Greater(font.GetLineHeight(), 0f);
        Assert.AreNotEqual(1f, font.GetLineHeight(), "Baked metrics answer line height before any dynamic bake.");
    }

    [Test]
    public void BakedRecordsMatchDynamicBake()
    {
        var baked = CreateFont();
        baked.SetBakedPages(NowFontBaker.ASCII, false, Bake(baked, NowFontBaker.ASCII).ToArray());

        var dynamic = CreateFont();
        dynamic.EnsureGlyphs(NowFontBaker.ASCII, FontSize);

        foreach (int codepoint in Codepoints(NowFontBaker.ASCII))
        {
            Assert.IsTrue(dynamic.GetGlyph(codepoint, FontSize, out var expected), $"U+{codepoint:X4} dynamic");
            Assert.IsTrue(baked.GetGlyph(codepoint, FontSize, out var actual), $"U+{codepoint:X4} baked");
            AssertSameGlyph(expected, actual, $"U+{codepoint:X4}");
        }

        Vector2 expectedSize = dynamic.MeasureText(SampleText, FontSize);
        Vector2 actualSize = baked.MeasureText(SampleText, FontSize);
        Assert.AreEqual(expectedSize.x, actualSize.x, Tolerance, "Measured width");
        Assert.AreEqual(expectedSize.y, actualSize.y, Tolerance, "Measured height");
        Assert.AreEqual(0, baked.dynamicSessionCount, "Comparing baked text must not open a compiler session.");
    }

    [Test]
    public void StaleBakedPagesStayDormant()
    {
        var font = CreateFont();
        font.SetBakedPages(NowFontBaker.ASCII, false, Bake(font, NowFontBaker.ASCII).ToArray());
        Assert.IsFalse(font.bakedPagesStale);

        font.dynamicAtlasSize = 48;
        font.ClearDynamicCache();

        Assert.IsTrue(font.bakedPagesStale);
        Assert.AreEqual(0, font.GetCachedDynamicPageCount(), "Pages baked at another glyph size must stay dormant.");
        Assert.IsTrue(font.GetGlyph('A', FontSize, out _));
        Assert.AreEqual(1, font.dynamicSessionCount, "A dormant bake falls back to dynamic baking.");
    }

    [Test]
    public void ClearDynamicCacheKeepsBakedTextures()
    {
        var font = CreateFont();
        var pages = Bake(font, NowFontBaker.ASCII);
        font.SetBakedPages(NowFontBaker.ASCII, false, pages.ToArray());

        Assert.IsTrue(font.GetGlyph('A', FontSize, out _));
        font.ClearDynamicCache();

        for (int i = 0; i < pages.Count; ++i)
            Assert.IsTrue(pages[i].texture, "Clearing the cache must not destroy a baked texture.");

        Assert.IsTrue(font.GetGlyph('A', FontSize, out _));
        Assert.AreEqual(pages.Count, font.GetCachedDynamicPageCount());
        Assert.AreEqual(0, font.dynamicSessionCount);
    }

    [Test]
    public void OutlineTierStillBakesDynamically()
    {
        var font = CreateFont();
        font.SetBakedPages(NowFontBaker.ASCII, false, Bake(font, NowFontBaker.ASCII).ToArray());

        Assert.IsTrue(font.GetGlyph('A', FontSize, 0.2f, out _));
        Assert.AreEqual(1, font.dynamicSessionCount, "A wider distance-range tier is not baked and resolves dynamically.");
    }

    [Test]
    public void GlyphsOutsideTheBakeStillBakeDynamically()
    {
        var font = CreateFont();
        font.SetBakedPages(NowFontBaker.ASCII, false, Bake(font, NowFontBaker.ASCII).ToArray());

        Assert.IsTrue(font.GetGlyph('é', FontSize, out var glyph));
        Assert.Greater(glyph.advance, 0f);
        Assert.AreEqual(1, font.dynamicSessionCount);
        Assert.AreEqual(2, font.GetCachedDynamicPageCount(), "A dynamic page joins the baked one.");
    }

    [Test]
    public void AssetRoundTripStoresPagesAsUnreadableSubAssets()
    {
        if (!AssetDatabase.IsValidFolder(TempFolder))
            AssetDatabase.CreateFolder("Assets/NowUITests", "Temp");

        var font = ScriptableObject.CreateInstance<NowFont>();
        font.InitializeDynamicSource(_fontBytes, NowFont.DEFAULT_DYNAMIC_ATLAS_SIZE, NowFont.DEFAULT_DYNAMIC_PIXEL_RANGE);
        AssetDatabase.CreateAsset(font, TempAssetPath);

        Assert.IsTrue(NowFontBaker.TryBake(font, NowFontBaker.ASCII, out string error), error);
        Assert.IsTrue(NowFontBaker.TryBake(font, NowFontBaker.ASCII, out error), error);

        var loaded = AssetDatabase.LoadAssetAtPath<NowFont>(TempAssetPath);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(NowFontBaker.ASCII, loaded.bakedCharacters);
        Assert.Greater(loaded.bakedPageCount, 0);

        var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(TempAssetPath);
        int textures = 0;

        for (int i = 0; i < representations.Length; ++i)
        {
            if (representations[i] is not Texture2D texture)
                continue;

            ++textures;
            Assert.IsTrue(loaded.IsBakedAtlasTexture(texture));
            Assert.IsFalse(texture.isReadable, "Baked textures ship non-readable.");
            Assert.IsFalse(texture.isDataSRGB, "Baked textures stay linear like runtime pages.");
        }

        Assert.AreEqual(loaded.bakedPageCount, textures, "Rebaking must replace the sub-assets, never accumulate them.");

        Assert.IsTrue(loaded.GetGlyph('A', FontSize, out var glyph));
        Assert.Greater(glyph.advance, 0f);
        Assert.AreEqual(0, loaded.dynamicSessionCount);

        Assert.IsFalse(loaded.bakedAllGlyphs);
        Assert.IsTrue(NowFontBaker.TryBakeAll(loaded, out error), error);
        Assert.IsTrue(loaded.bakedAllGlyphs);
        Assert.AreEqual(NowFontBaker.ASCII, loaded.bakedCharacters, "Baking all glyphs keeps the authored characters.");
        Assert.IsTrue(NowFontBaker.BakesAllGlyphs(loaded));
        Assert.Greater(loaded.bakedGlyphCount, NowFontBaker.ASCII.Length * 2 - 2);

        NowFontBaker.Clear(loaded);
        Assert.AreEqual(0, loaded.bakedPageCount);
        Assert.IsFalse(loaded.bakedAllGlyphs);
        Assert.AreEqual(NowFontBaker.ASCII, loaded.bakedCharacters, "Clearing keeps the authored characters.");

        representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(TempAssetPath);

        for (int i = 0; i < representations.Length; ++i)
            Assert.IsFalse(representations[i] is Texture2D, "Clearing removes the texture sub-assets.");
    }

    [Test]
    public void BakeAllCoversTheWholeCmap()
    {
        var font = CreateFont();
        int allGlyphs = NowFontBaker.CountAllGlyphs(font);
        Assert.Greater(allGlyphs, NowFontBaker.ASCII.Length, "The test font maps more than ASCII.");

        Assert.IsTrue(NowFontBaker.TryBakeAllPages(font, out var pages, out string error), error);

        for (int i = 0; i < pages.Count; ++i)
            _cleanup.Add(pages[i].texture);

        var keys = CollectKeys(pages);
        int codepointRecords = 0;

        foreach (int key in keys)
        {
            if (key >= 0)
                ++codepointRecords;
        }

        foreach (int codepoint in Codepoints(NowFontBaker.ASCII))
            Assert.IsTrue(keys.Contains(codepoint), $"U+{codepoint:X4} was not baked.");

        Assert.LessOrEqual(codepointRecords, allGlyphs);
        Assert.Greater(codepointRecords, NowFontBaker.ASCII.Length);

        NowFontBaker.EstimateBake(font, allGlyphs, out int estimatedPages, out _, out long estimatedBytes);
        Assert.GreaterOrEqual(estimatedPages + 1, pages.Count, "The estimate tracks the real page count closely.");
        Assert.Greater(estimatedBytes, 0);
    }

    [Test]
    public void EstimateMatchesTheAsciiBake()
    {
        var font = CreateFont();
        var pages = Bake(font, NowFontBaker.ASCII);

        NowFontBaker.EstimateBake(font, NowFontBaker.ASCII.Length, out int pageCount, out int pageSide, out long bytes);
        Assert.AreEqual(pages.Count, pageCount);
        Assert.AreEqual(pages[0].texture.width, pageSide);
        Assert.AreEqual((long)pageSide * pageSide * 4 * pageCount, bytes);
        Assert.IsTrue(NowFontBaker.BakesAllGlyphs(font), "A font with nothing authored bakes all glyphs by default.");
    }

    [Test]
    public void MergeAppendsOnlyMissingCodepoints()
    {
        Assert.AreEqual("abc", NowFontBaker.Merge("ab", "cab"));
        Assert.AreEqual("ab", NowFontBaker.Merge("ab", ""));
        Assert.AreEqual("xy", NowFontBaker.Merge(null, "xyx"));
        Assert.AreEqual(0x7E - 0x20 + 1, NowFontBaker.ASCII.Length);
    }

    [Test]
    public void EmptyOrMissingInputIsRejected()
    {
        var font = CreateFont();

        Assert.IsFalse(NowFontBaker.TryBakePages(font, "", out _, out string error));
        Assert.IsNotEmpty(error);
        Assert.IsFalse(NowFontBaker.TryBakePages(font, "\n\r", out _, out error));
        Assert.IsNotEmpty(error);
        Assert.IsFalse(NowFontBaker.TryBakePages(null, "a", out _, out error));
        Assert.IsNotEmpty(error);
    }
}
