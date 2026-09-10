// Self-tests for the baked font atlas pages the exporter writes beside each face.
//
// WHY THESE EXIST. The browser bundle's first frame spent ~2 s rasterizing printable ASCII in interpreted
// WebAssembly; NowStandaloneAssetExport now bakes those glyphs in Unity and ships the pixels, and
// WebResourceProvider.InstallBakedPages loads them back. Between those two halves sit three things that fail SILENTLY
// - they produce a page rather than an error:
//
//   * ROW ORDER. Texture2D raw data is bottom-up, PNG scanlines are top-down. Get it wrong and every glyph renders
//     upside down, in the right place, at the right width.
//   * THE CHANNEL SWIZZLE. The page is a 16-bit distance packed as high-byte in R/G/A and low-byte in B. It ships as
//     a two-plane greyscale+alpha PNG, and the decoder hands back (hi, hi, hi, lo) where the page wants
//     (hi, hi, lo, hi). Get it wrong and the text is a smear at roughly the right weight.
//   * THE GLYPH RECORDS. atlasBounds travel in PIXELS; NowFont.BuildGlyphCache divides by the atlas size. Normalize
//     them once too often and every glyph samples a sliver of the atlas corner.
//
// None of the three is visible in a byte count or a file size, so the check has to be on pixels. The one below
// compares the shipped page against a page baked fresh from the same TTF, GLYPH BY GLYPH rather than page by page:
// the two bakes need not pack their cells in the same order, and asserting that they do would be asserting something
// nobody promised. What must hold is that the pixels under a given codepoint are the same pixels.
//
// This file is OURS - part of the standalone support layer, like NowStandaloneTestResourcesTests, not part of the
// Unity gate. Nothing under Assets/NowUITests is touched.
//
// The desktop provider (NowStandaloneTestResources) deliberately does NOT load these pages: it keeps baking
// dynamically so the 800-case gate's goldens do not move. That is what makes the "fresh bake" side of the comparison
// available here for free - Now.defaultFont's faces are exactly that.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NowUI;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Standalone.Tests
{
    /// <summary>Checks that a shipped baked page decodes to the same pixels a fresh bake produces.</summary>
    public sealed class NowStandaloneBakedFontPageTests
    {
        private static readonly string[] k_Faces =
        {
            "NotoSans-Regular.font.json",
            "NotoSans-Bold.font.json",
            "NotoSans-Italic.font.json",
            "NotoSans-BoldItalic.font.json",
        };

        private static string FixtureRoot()
        {
            // The csproj copies Fixtures/** next to the assembly.
            return Path.Combine(AppContext.BaseDirectory, "Fixtures");
        }

        private static JsonDocument ReadFace(string fileName)
        {
            return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureRoot(), "NowUI", fileName)));
        }

        [Test]
        [TestCaseSource(nameof(k_Faces))]
        public void EveryFaceShipsExactlyOneBakedAsciiPage(string fileName)
        {
            using (JsonDocument document = ReadFace(fileName))
            {
                JsonElement face = document.RootElement;

                JsonElement pages;
                if (!face.TryGetProperty("bakedPages", out pages))
                {
                    Assert.Ignore(
                        fileName + " carries no baked pages, which is what the export produces TODAY - the bake " +
                        "step was removed once the atlas cell moved to 32/8 and measurement showed the four pages " +
                        "costing more first-frame time (311 ms, mostly managed PNG decode) than the rasterisation " +
                        "they saved (~160 ms), on top of 875 KB of bundle. Re-running the export will not bring " +
                        "them back. This case stays so that re-enabling the bake is checked rather than assumed; " +
                        "see NowStandaloneAssetExport's header.");
                }

                // Printable ASCII fit one 1024 px page per face at the 64/16 cell this was written against, and
                // fits comfortably at today's 32/8. More than one page would still be correct, but it would mean
                // the geometry moved and the size claim with it.
                Assert.AreEqual(1, pages.GetArrayLength(), "baked page count for " + fileName);

                JsonElement page = pages[0];
                Assert.AreEqual(95, page.GetProperty("glyphs").GetArrayLength() -
                                    NegativeKeyedRecords(page.GetProperty("glyphs")),
                                "printable-ASCII codepoint records in " + fileName);

                // The two values NowFont.IsBakedPageCurrent compares against the live font. If these ever drift from
                // the face's own settings the page goes dormant and the whole optimisation silently evaporates.
                Assert.AreEqual(face.GetProperty("dynamicAtlasSize").GetInt32(), page.GetProperty("atlasSize").GetInt32(),
                    "atlasSize must match the face or the page is ignored at runtime");
                Assert.AreEqual(face.GetProperty("dynamicPixelRange").GetInt32(), page.GetProperty("pixelRange").GetInt32(),
                    "pixelRange must match the face or the page is ignored at runtime");

                // The face must still read as dynamic: NowStandaloneTestResources and WebResourceProvider both refuse
                // a prebaked atlas, and a baked page is not one.
                Assert.AreEqual("dynamic", face.GetProperty("kind").GetString());
                Assert.AreEqual(0, face.GetProperty("atlasWidth").GetInt32());
                Assert.AreEqual(0, face.GetProperty("atlasInfo").GetProperty("glyphs").GetArrayLength());

                string file = page.GetProperty("file").GetString();
                string path = Path.Combine(FixtureRoot(), "NowUI", file);
                Assert.IsTrue(File.Exists(path), "the declared page file is missing: " + file);
                Assert.AreEqual(page.GetProperty("pageByteCount").GetInt32(), new FileInfo(path).Length,
                    "pageByteCount for " + file);
            }
        }

        /// <summary>
        /// The pixels under each baked codepoint must equal the pixels a fresh dynamic bake puts there. This is the
        /// test that catches a flipped page or a swapped channel; both would still produce a plausible-looking file.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(k_Faces))]
        public void BakedPagePixelsMatchAFreshDynamicBake(string fileName)
        {
            using (JsonDocument document = ReadFace(fileName))
            {
                JsonElement face = document.RootElement;

                JsonElement pages;
                if (!face.TryGetProperty("bakedPages", out pages) || pages.GetArrayLength() == 0)
                    Assert.Ignore(
                        fileName + " carries no baked pages; nothing to compare. That is today's expected state - " +
                        "see the sibling case for why the export stopped writing them.");

                JsonElement page = pages[0];
                int width = page.GetProperty("width").GetInt32();
                int height = page.GetProperty("height").GetInt32();
                byte[] shipped = DecodeShippedPage(page, out string decodeError);
                Assert.IsNotNull(shipped, "the shipped page did not decode: " + decodeError);

                NowFont fresh = FreshFace(fileName);
                Assert.IsNotNull(fresh, "could not resolve a fresh face for " + fileName);

                int compared = 0;

                foreach (JsonElement record in page.GetProperty("glyphs").EnumerateArray())
                {
                    int unicode = record.GetProperty("unicode").GetInt32();

                    // Negative keys are glyph-INDEX records, which the browser cannot look up (NowTextShaper is
                    // unsupported without HarfBuzz) and which share their cells with the codepoint records anyway.
                    if (unicode < 0x20 || unicode > 0x7E)
                        continue;

                    RectInt shippedRect = PixelRect(record.GetProperty("atlasBounds"));

                    // A blank glyph - space - has an empty box and no pixels to compare.
                    if (shippedRect.width <= 0 || shippedRect.height <= 0)
                        continue;

                    NowFontAtlasInfo.Glyph freshGlyph;
                    Material freshMaterial;
                    Assert.IsTrue(fresh.GetGlyph(unicode, out freshGlyph, out freshMaterial),
                        "the fresh bake has no glyph for U+" + unicode.ToString("X4"));

                    Texture2D freshTexture = freshMaterial.mainTexture as Texture2D;
                    Assert.IsNotNull(freshTexture, "the fresh page has no texture for U+" + unicode.ToString("X4"));

                    // GetGlyph answers from NowFont's lookup cache, whose atlasBounds are NORMALIZED (BuildGlyphCache
                    // divides by the atlas size). The fixture's are in pixels. Undo the division rather than dividing
                    // the fixture's, so a bug in either direction cannot cancel out.
                    RectInt freshRect = new RectInt(
                        (int)Math.Round(freshGlyph.atlasBounds.left * freshTexture.width),
                        (int)Math.Round(freshGlyph.atlasBounds.bottom * freshTexture.height),
                        (int)Math.Round((freshGlyph.atlasBounds.right - freshGlyph.atlasBounds.left) * freshTexture.width),
                        (int)Math.Round((freshGlyph.atlasBounds.top - freshGlyph.atlasBounds.bottom) * freshTexture.height));

                    Assert.AreEqual(shippedRect.width, freshRect.width,
                        "glyph box width for U+" + unicode.ToString("X4") + " in " + fileName);
                    Assert.AreEqual(shippedRect.height, freshRect.height,
                        "glyph box height for U+" + unicode.ToString("X4") + " in " + fileName);

                    byte[] freshPixels = freshTexture.GetRawTextureData();

                    for (int y = 0; y < shippedRect.height; ++y)
                    {
                        for (int x = 0; x < shippedRect.width; ++x)
                        {
                            int a = (((shippedRect.y + y) * width) + shippedRect.x + x) * 4;
                            int b = (((freshRect.y + y) * freshTexture.width) + freshRect.x + x) * 4;

                            if (shipped[a] == freshPixels[b] &&
                                shipped[a + 1] == freshPixels[b + 1] &&
                                shipped[a + 2] == freshPixels[b + 2] &&
                                shipped[a + 3] == freshPixels[b + 3])
                            {
                                continue;
                            }

                            Assert.Fail(
                                "U+" + unicode.ToString("X4") + " in " + fileName + " differs at (" + x + "," + y +
                                ") of its " + shippedRect.width + "x" + shippedRect.height + " box: shipped (" +
                                shipped[a] + "," + shipped[a + 1] + "," + shipped[a + 2] + "," + shipped[a + 3] +
                                ") against fresh (" + freshPixels[b] + "," + freshPixels[b + 1] + "," +
                                freshPixels[b + 2] + "," + freshPixels[b + 3] + "). A whole-page difference is a row-" +
                                "order or channel-swizzle error in NowStandaloneFontPageWriter or in " +
                                "WebResourceProvider.InstallBakedPages; a difference in one glyph is a packing bug.");
                        }
                    }

                    ++compared;
                }

                Assert.Greater(compared, 90, "too few glyphs compared to call this a check of " + fileName);
                Assert.AreEqual(height, page.GetProperty("height").GetInt32());
            }
        }

        /// <summary>
        /// A face whose baked page cannot be read must still build and still measure text. This is the whole safety
        /// argument for shipping pages at all: they are an optimisation over a path that still works.
        /// </summary>
        [Test]
        public void AFaceWhoseBakedPageIsUnreadableStillMeasuresText()
        {
            // The desktop provider never loads pages, so the face behind Now.defaultFont IS the no-pages case - the
            // same object a browser would end up with after every page was skipped.
            NowFont fresh = FreshFace(k_Faces[0]);
            Assert.IsNotNull(fresh);

            NowFontAtlasInfo.Glyph glyph;
            Assert.IsTrue(fresh.GetGlyph('A', out glyph), "a face with no baked pages must still bake on demand");
            Assert.Greater(glyph.advance, 0f);
        }

        /// <summary>
        /// A codepoint outside the baked set must still rasterize. The gallery draws exactly one such line, and the
        /// fallback for it is the ordinary miss path - which the baked pages must not have disturbed.
        /// </summary>
        [Test]
        public void CodepointsOutsideTheBakedSetStillRasterize()
        {
            using (JsonDocument document = ReadFace(k_Faces[0]))
            {
                JsonElement chars;
                if (document.RootElement.TryGetProperty("bakedCharacters", out chars) &&
                    chars.ValueKind == JsonValueKind.String)
                {
                    string baked = chars.GetString();

                    foreach (char c in "éΓД—“…")
                        Assert.IsFalse(baked.IndexOf(c) >= 0, "U+" + ((int)c).ToString("X4") + " should not be baked");
                }
            }

            NowFont fresh = FreshFace(k_Faces[0]);

            // The characters from GalleryAreas.Text.cs's Unicode sample line: Latin-1, Greek, Cyrillic, and
            // typographic punctuation - none of them in printable ASCII.
            foreach (char c in "éßΓД—“…")
            {
                NowFontAtlasInfo.Glyph glyph;
                Assert.IsTrue(fresh.GetGlyph(c, out glyph),
                    "U+" + ((int)c).ToString("X4") + " is outside the bake and must still rasterize on demand");
                Assert.Greater(glyph.advance, 0f, "U+" + ((int)c).ToString("X4") + " advance");
            }
        }

        // ------------------------------------------------------------------------------------------------- helpers

        private static int NegativeKeyedRecords(JsonElement glyphs)
        {
            int count = 0;

            foreach (JsonElement glyph in glyphs.EnumerateArray())
            {
                if (glyph.GetProperty("unicode").GetInt32() < 0)
                    ++count;
            }

            return count;
        }

        /// <summary>Decodes a shipped page exactly the way WebResourceProvider.TryBuildBakedPage does.</summary>
        private static byte[] DecodeShippedPage(JsonElement page, out string error)
        {
            byte[] encoded = File.ReadAllBytes(
                Path.Combine(FixtureRoot(), "NowUI", page.GetProperty("file").GetString()));

            int width, height;
            byte[] rgba;
            if (!NowPngCodec.TryDecode(encoded, out width, out height, out rgba, out error))
                return null;

            if (width != page.GetProperty("width").GetInt32() || height != page.GetProperty("height").GetInt32())
            {
                error = "decoded " + width + "x" + height + ", declared " +
                        page.GetProperty("width").GetInt32() + "x" + page.GetProperty("height").GetInt32();
                return null;
            }

            if (string.Equals(page.GetProperty("pageEncoding").GetString(), "grey8-alpha8", StringComparison.Ordinal))
            {
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    byte low = rgba[i + 3];
                    rgba[i + 3] = rgba[i];
                    rgba[i + 2] = low;
                }
            }

            error = null;
            return rgba;
        }

        private static RectInt PixelRect(JsonElement bounds)
        {
            int left = (int)Math.Round(bounds.GetProperty("left").GetSingle());
            int bottom = (int)Math.Round(bounds.GetProperty("bottom").GetSingle());
            int right = (int)Math.Round(bounds.GetProperty("right").GetSingle());
            int top = (int)Math.Round(bounds.GetProperty("top").GetSingle());
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        /// <summary>The live face for a fixture file name, with no baked pages loaded (the desktop provider's shape).</summary>
        private static NowFont FreshFace(string fileName)
        {
            NowFontFamily family = Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath) as NowFontFamily;
            Assert.IsNotNull(family, "the NotoSans family did not resolve");

            switch (fileName)
            {
                case "NotoSans-Regular.font.json": return family.regular;
                case "NotoSans-Bold.font.json": return family.bold;
                case "NotoSans-Italic.font.json": return family.italic;
                case "NotoSans-BoldItalic.font.json": return family.boldItalic;
                default: throw new ArgumentException("unknown face fixture " + fileName, nameof(fileName));
            }
        }

        private struct RectInt
        {
            public int x;
            public int y;
            public int width;
            public int height;

            public RectInt(int x, int y, int width, int height)
            {
                this.x = x;
                this.y = y;
                this.width = width;
                this.height = height;
            }
        }
    }
}
