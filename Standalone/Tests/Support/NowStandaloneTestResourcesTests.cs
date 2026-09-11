// Self-tests for the standalone resource provider (work unit U25).
//
// These are OUR tests about OUR support layer, not part of the 783-case gate: they check the fixtures and the provider
// before the Unity suite depends on them, so a broken export reads as "the fixture is wrong" rather than as 31 test
// files failing to measure text.
//
// The three acceptance criteria for the unit, one fixture each:
//   * Resources.Load<NowFontAsset>("NowUI/NotoSans") resolves a Regular face whose glyph metrics are non-degenerate.
//   * Two loads of the same path return a reference-equal instance.
//   * Every material template the design lists resolves.
//
// New file of ours; nothing under Assets/NowUITests is touched.
using System.Collections.Generic;
using NowUI;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Standalone.Tests
{
    /// <summary>Checks that the exported fixtures load and behave the way the Unity suite will assume they do.</summary>
    public sealed class NowStandaloneTestResourcesTests
    {
        // 16 px is the size the theme's Body style measures at, so this bakes the tier the gate files actually use.
        private const float k_FontSize = 16f;

        [Test]
        public void FontFamilyResolvesFromResources()
        {
            NowFontAsset asset = Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath);

            Assert.IsNotNull(asset, "The NotoSans family did not resolve. Every text-drawing gate file needs it.");
            Assert.IsInstanceOf<NowFontFamily>(asset);
            Assert.AreEqual("NotoSans", asset.name);
        }

        [Test]
        public void RepeatedLoadsReturnTheSameInstance()
        {
            // NowTextWrapTests compares the family the theme resolved against the one it loaded, and Now.defaultFont is
            // reloaded after every TearDown that nulls it - so "same path, same instance" is a hard contract, not a
            // performance nicety.
            NowFontAsset first = Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath);
            NowFontAsset second = Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath);

            Assert.AreSame(first, second);

            Material firstMaterial = Resources.Load<Material>("NowUI/TxtMaterial");
            Material secondMaterial = Resources.Load<Material>("NowUI/TxtMaterial");

            // NowTextStylingTests.GradientTextDrawsRemainTextAndBatchTogether expects two gradient text draws to share
            // one batch, and batch keys compare materials by instance: a template cloned per load would split them.
            Assert.AreSame(firstMaterial, secondMaterial);
        }

        [Test]
        public void MistypedLoadReturnsNullAsInUnity()
        {
            // Unity's Resources.Load<T> answers null for an asset of another type rather than throwing, and core code
            // probes paths that way.
            Assert.IsNull(Resources.Load<Material>(NowStandaloneTestResources.fontFamilyPath));
            Assert.IsNull(Resources.Load<NowFontAsset>("NowUI/TxtMaterial"));
            Assert.IsNull(Resources.Load<Material>("NowUI/ThisAssetDoesNotExist"));
        }

        [Test]
        public void FamilyResolvesAllFourFaces()
        {
            NowFontFamily family = LoadFamily();

            Assert.IsNotNull(family.regular, "regular");
            Assert.IsNotNull(family.bold, "bold");
            Assert.IsNotNull(family.italic, "italic");
            Assert.IsNotNull(family.boldItalic, "boldItalic");
            Assert.AreEqual("NotoSans-Regular.ttf", family.regular.name);
            Assert.AreEqual("NotoSans-Bold.ttf", family.bold.name);
            Assert.AreEqual("NotoSans-Italic.ttf", family.italic.name);
            Assert.AreEqual("NotoSans-BoldItalic.ttf", family.boldItalic.name);

            NowFont resolved;
            Assert.IsTrue(family.TryResolveFont(NowFontStyle.Regular, out resolved));
            Assert.AreSame(family.regular, resolved);

            Assert.IsTrue(family.TryResolveFont(NowFontStyle.Bold, out resolved));
            Assert.AreSame(family.bold, resolved);

            Assert.IsTrue(family.TryResolveFont(NowFontStyle.Italic, out resolved));
            Assert.AreSame(family.italic, resolved);

            Assert.IsTrue(family.TryResolveFont(NowFontStyle.BoldItalic, out resolved));
            Assert.AreSame(family.boldItalic, resolved);

            // The exporter omits the CJK/Arabic/emoji fallback families; the slot is empty, never null, so traversal
            // sees the same shape it would for a family authored without fallbacks.
            Assert.IsNotNull(family.fallbacks);
            Assert.AreEqual(0, family.fallbacks.Count);
            foreach (NowFont face in new[] { family.regular, family.bold, family.italic, family.boldItalic })
            {
                Assert.IsNotNull(face.fallbacks, face.name);
                Assert.AreEqual(0, face.fallbacks.Count, face.name);
            }
        }

        [Test]
        public void RegularFaceBakesNonDegenerateGlyphMetrics()
        {
            NowFont regular = LoadFamily().regular;

            regular.EnsureGlyphs("AWi ", k_FontSize);

            NowFontAtlasInfo.Glyph a;
            Assert.IsTrue(regular.GetGlyph('A', k_FontSize, out a), "'A' did not bake.");

            Assert.Greater(a.advance, 0f, "'A' has no advance width.");
            Assert.Greater(a.planeBounds.right - a.planeBounds.left, 0f, "'A' has an empty plane box.");
            Assert.Greater(a.planeBounds.top - a.planeBounds.bottom, 0f, "'A' has an empty plane box.");
            Assert.Greater(a.atlasBounds.right - a.atlasBounds.left, 0f, "'A' occupies no atlas area.");
            Assert.Greater(a.atlasBounds.top - a.atlasBounds.bottom, 0f, "'A' occupies no atlas area.");

            // Real metrics, not a synthetic monospace stand-in: NotoSans is proportional, and every hit rectangle the
            // gate files hard-code was measured from these advances.
            NowFontAtlasInfo.Glyph w;
            NowFontAtlasInfo.Glyph i;
            NowFontAtlasInfo.Glyph space;
            Assert.IsTrue(regular.GetGlyph('W', k_FontSize, out w), "'W' did not bake.");
            Assert.IsTrue(regular.GetGlyph('i', k_FontSize, out i), "'i' did not bake.");
            Assert.IsTrue(regular.GetGlyph(' ', k_FontSize, out space), "space did not bake.");

            Assert.Less(i.advance, a.advance, "'i' should be narrower than 'A'.");
            Assert.Less(a.advance, w.advance, "'A' should be narrower than 'W'.");
            Assert.Greater(space.advance, 0f, "space has no advance width.");

            // Vertical metrics come from the same baked table and decide every line box in the suite.
            float lineHeight = regular.GetLineHeight();
            float ascender = regular.GetAscender();

            Assert.Greater(ascender, 0f, "no ascender");
            Assert.Greater(lineHeight, ascender, "line height should exceed the ascender");
        }

        [Test]
        public void MeasuredTextGrowsWithItsContent()
        {
            // The measurement path the controls use, rather than the glyph table directly: if this collapses, every
            // pointer position derived from a label width collapses with it.
            NowFontAsset family = LoadFamily();

            Vector2 empty = family.MeasureText(string.Empty, k_FontSize);
            Vector2 one = family.MeasureText("A", k_FontSize);
            Vector2 two = family.MeasureText("AB", k_FontSize);

            Assert.AreEqual(0f, empty.x, "an empty string should measure zero wide");
            Assert.Greater(one.x, 0f, "\"A\" measured nothing wide");
            Assert.Greater(two.x, one.x, "\"AB\" should be wider than \"A\"");
            Assert.Greater(one.y, 0f, "\"A\" measured nothing tall");
        }

        [Test]
        public void EveryRequiredMaterialTemplateResolves()
        {
            IReadOnlyList<string> paths = NowStandaloneTestResources.requiredMaterialPaths;

            for (int i = 0; i < paths.Count; i++)
            {
                Material material = Resources.Load<Material>(paths[i]);

                Assert.IsNotNull(material, "Material template '" + paths[i] + "' did not resolve.");
                Assert.IsNotNull(material.shader, "Material template '" + paths[i] + "' has no shader.");
            }
        }

        [Test]
        public void TextTemplateDeclaresThePropertiesTheFontGatesOn()
        {
            Material text = Resources.Load<Material>("NowUI/TxtMaterial");

            // HasProperty answers from the shader's declared uniforms, not only from assigned values - that is what
            // Unity does, and NowFont branches on both of these when it bakes a dynamic page.
            Assert.IsTrue(text.HasProperty("_NowUITextSdfEncoding"), "_NowUITextSdfEncoding");
            Assert.IsTrue(text.HasProperty("_NowUITextOutlineOnlyPass"), "_NowUITextOutlineOnlyPass");
            Assert.IsFalse(text.HasProperty("_ThisPropertyIsNotDeclared"));
        }

        [Test]
        public void EveryRequiredShaderResolves()
        {
            IReadOnlyList<string> names = NowStandaloneTestResources.requiredShaderNames;

            for (int i = 0; i < names.Count; i++)
            {
                Shader shader = Shader.Find(names[i]);

                Assert.IsNotNull(shader, "Shader '" + names[i] + "' did not resolve.");
                Assert.AreEqual(names[i], shader.name);
            }

            Assert.IsNull(Shader.Find("NowUI/This Shader Does Not Exist"));
        }

        [Test]
        public void DefaultFontResolvesThroughNow()
        {
            // The path 31 of the 36 gate files reach: a null here logs a Debug.LogError inside LoadRequiredResource,
            // which the parity log policy turns into a failure of whichever test drew text first.
            NowFontAsset previous = Now.defaultFont;

            try
            {
                Now.defaultFont = null;
                Assert.IsNotNull(Now.defaultFont);
                Assert.AreSame(
                    Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath),
                    Now.defaultFont);
            }
            finally
            {
                Now.defaultFont = previous;
            }
        }

        private static NowFontFamily LoadFamily()
        {
            NowFontFamily family =
                Resources.Load<NowFontAsset>(NowStandaloneTestResources.fontFamilyPath) as NowFontFamily;

            Assert.IsNotNull(family, "The NotoSans family did not resolve.");
            return family;
        }
    }
}
