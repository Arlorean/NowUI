using System;
using System.Collections.Generic;
using NUnit.Framework;
using NowUI;
using UnityEngine;

public class NowTextBaselineSnappingTests
{
    const float FontSize = 14.25f;
    static readonly NowRect TextRect = new NowRect(20.37f, 13.27f, 500f, 180f);
    NowFontAsset fontAsset;
    NowFont font;
    bool previousShaping;
    float previousScale;

    [SetUp]
    public void SetUp()
    {
        fontAsset = Resources.Load<NowFontAsset>("NowUI/NotoSans");
        Assert.NotNull(fontAsset);
        Assert.IsTrue(fontAsset.TryResolveFont(NowFontStyle.Regular, out font));
        previousShaping = Now.textShaping;
        previousScale = Now.uiScale;
        Now.textShaping = false;
    }

    [TearDown]
    public void TearDown()
    {
        Now.textShaping = previousShaping;
        Now.SetUIScale(previousScale);
    }

    [TestCase("string", 1f)]
    [TestCase("span", 1.25f)]
    [TestCase("character", 1.5f)]
    [TestCase("string", 2f)]
    public void EachBaselineUsesPhysicalPixelsWithoutChangingGlyphSpacing(string path, float scale)
    {
        string value = path == "character" ? "H" : "HH\nHH";
        var natural = Capture(false, scale, value, path);
        var snapped = Capture(true, scale, value, path);
        Assert.That(snapped.Count, Is.EqualTo(path == "character" ? 1 : 4));
        Assert.That(snapped.Count, Is.EqualTo(natural.Count));
        Assert.IsTrue(font.GetGlyph('H', FontSize, out var glyph, out _));

        for (int i = 0; i < snapped.Count; i++)
        {
            // Rect UV.y stores the negated bottom of the glyph quad. Undo its
            // font plane offset to recover the actual baseline from geometry.
            float physicalBaseline = (-snapped[i].y + glyph.planeBounds.bottom * FontSize) * scale;
            Assert.That(physicalBaseline, Is.EqualTo(Mathf.Round(physicalBaseline)).Within(0.0001f));
            Assert.That(Mathf.Abs(snapped[i].y - natural[i].y) * scale, Is.LessThanOrEqualTo(0.5001f));
            Assert.That(snapped[i].x, Is.EqualTo(natural[i].x).Within(0.0001f));
            Assert.That(snapped[i].z, Is.EqualTo(natural[i].z).Within(0.0001f));
            Assert.That(snapped[i].w, Is.EqualTo(natural[i].w).Within(0.0001f));
        }
        Assert.That(Mathf.Abs(snapped[0].y - natural[0].y), Is.GreaterThan(0.001f));
    }

    [TestCase(false, 0.5f)]
    [TestCase(false, 2f)]
    [TestCase(true, 0f)]
    public void TransformsAndConfiguredAnimationsKeepFractionalGeometry(bool transform, float time)
    {
        var natural = Capture(false, 1.25f, "HH", "string", transform, !transform, time);
        var snapped = Capture(true, 1.25f, "HH", "string", transform, !transform, time);
        Assert.That(snapped.Count, Is.EqualTo(2));
        Assert.That(snapped, Is.EqualTo(natural), "Snapping must not change animated/transformed glyphs, including settled animations.");
    }

    [Test]
    public void ShapedOffsetsAndAdvancesSurviveBaselineSnap()
    {
        Now.textShaping = true;
        const string value = "a\u0338\u0307 office";
        if (!font.TryGetPreparedShapedRun(value, FontSize, out _))
            Assert.Ignore("HarfBuzz shaping is not available on this host.");
        var natural = Capture(false, 1.25f, value, "string");
        var snapped = Capture(true, 1.25f, value, "string");
        Assert.That(snapped.Count, Is.EqualTo(natural.Count));
        Assert.That(snapped.Count, Is.GreaterThan(2));
        float delta = snapped[0].y - natural[0].y;
        Assert.That(Mathf.Abs(delta), Is.GreaterThan(0.001f));
        for (int i = 0; i < snapped.Count; i++)
        {
            Assert.That(snapped[i].x, Is.EqualTo(natural[i].x).Within(0.0001f));
            Assert.That(snapped[i].y - natural[i].y, Is.EqualTo(delta).Within(0.0001f));
        }
    }

    [Test]
    public void OutlinedGlyphLayersShareTheSnappedBaseline()
    {
        var plain = Capture(true, 1.5f, "H", "string");
        var outlined = Capture(true, 1.5f, "H", "string", outline: true);
        Assert.That(outlined.Count, Is.EqualTo(2));
        Assert.That(outlined[1], Is.EqualTo(plain[0]), "The fill must retain its base glyph geometry after the outline layer.");
    }

    [Test]
    public void MeasurementAndExplicitMasksAreUnchanged()
    {
        var text = Now.Text(TextRect, fontAsset).SetFontSize(FontSize);
        Assert.IsFalse(text.baselineSnap);
        Assert.That(text.SetBaselineSnap().MeasureBounds("HH"), Is.EqualTo(text.MeasureBounds("HH")));
        using var list = new NowDrawList();
        using (list.Begin(new Vector2(640, 240)))
            text.SetBaselineSnap().SetMask(TextRect).Draw("HH");
        var masks = new List<Vector4>();
        list.mesh.GetUVs(6, masks);
        Assert.That(masks.Count, Is.GreaterThan(0));
        Assert.That(masks[0], Is.EqualTo((Vector4)TextRect));
    }

    List<Vector4> Capture(bool snap, float scale, string value, string path,
        bool transform = false, bool animate = false, float time = 0, bool outline = false)
    {
        using var list = new NowDrawList();
        using (list.Begin(new Vector2(640, 240)))
        {
            Now.SetUIScale(scale);
            if (transform)
            {
                using (Now.Transform(1.13f, new Vector2(0.3f, 0.7f))) Draw();
            }
            else Draw();
        }
        var vertices = new List<Vector4>();
        list.mesh.GetUVs(1, vertices);
        var rects = new List<Vector4>();
        for (int i = 0; i < vertices.Count; i += 4) rects.Add(vertices[i]);
        return rects;

        void Draw()
        {
            var text = Now.Text(TextRect, fontAsset).SetFontSize(FontSize).SetBaselineSnap(snap);
            if (animate) text = text.SetAnimation(NowTextAnimations.FadeUp(2f, 1f, 0f)).SetTime(time);
            if (outline) text = text.SetOutlinePixels(1f);
            if (path == "character") text.Draw('H');
            else if (path == "span") text.Draw(value.AsSpan());
            else text.Draw(value);
        }
    }
}
