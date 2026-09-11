using System.Collections.Generic;
using NUnit.Framework;
using NowUI;
using UnityEngine;

public class NowPixelSnappingTests
{
    static readonly Vector2 Surface = new Vector2(256f, 256f);
    NowDrawList _drawList;
    Material _sdfMaterial;
    Texture2D _texture;
    float _previousUiScale;

    [SetUp]
    public void SetUp()
    {
        _previousUiScale = Now.uiScale;
        _drawList = new NowDrawList();
        _sdfMaterial = Resources.Load<Material>("NowUI/SdfMaterial");
        Assert.NotNull(_sdfMaterial);
        _texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
    }

    [TearDown]
    public void TearDown()
    {
        Now.SetUIScale(_previousUiScale);
        _drawList.Dispose();
        Object.DestroyImmediate(_texture);
    }

    [TestCase(1f)]
    [TestCase(1.25f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void OnePhysicalPixelSurvivesPositiveAndNegativeHalfPixelPlacement(float uiScale)
    {
        Now.SetUIScale(uiScale);
        for (int kind = 0; kind < 2; ++kind)
        {
            for (int pixel = -5; pixel < 5; ++pixel)
            {
                float start = (pixel + 0.5f) / uiScale;
                var rect = new NowRect(start, start, 1f / uiScale, 1f / uiScale);
                using (_drawList.Begin(Surface))
                    Draw(rect, kind == 1);

                List<Vector4> rects = ShapeRects();
                Assert.AreEqual(4, rects.Count, $"kind {kind}, pixel {pixel}: one-pixel shape disappeared");
                AssertShape(rects[0], pixel + 1f, pixel + 1f, 1f, 1f, uiScale);
            }
        }
    }

    [TestCase(1f)]
    [TestCase(1.25f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void AdjacentRectanglesShareTheirSnappedBoundary(float uiScale)
    {
        Now.SetUIScale(uiScale);
        float left = 10.2f / uiScale;
        float shared = 17.5f / uiScale;
        float right = 23.2f / uiScale;
        for (int kind = 0; kind < 2; ++kind)
        {
            using (_drawList.Begin(Surface))
            {
                Draw(new NowRect(left, left, shared - left, shared - left), kind == 1);
                Draw(new NowRect(shared, shared, right - shared, right - shared), kind == 1);
            }

            List<Vector4> rects = ShapeRects();
            Assert.AreEqual(8, rects.Count);
            AssertShape(rects[0], 10f, 10f, 8f, 8f, uiScale);
            AssertShape(rects[4], 18f, 18f, 5f, 5f, uiScale);
            Assert.AreEqual(rects[0].x + rects[0].z, rects[4].x, 0.00001f);
            Assert.AreEqual(rects[0].y, rects[4].y + rects[4].w, 0.00001f);
        }
    }

    [TestCase(0.499f, 0f)]
    [TestCase(0.501f, 1f)]
    [TestCase(-0.501f, -1f)]
    [TestCase(-0.499f, 0f)]
    [TestCase(65536.4921875f, 65536f)]
    [TestCase(-65536.5078125f, -65537f)]
    public void CoordinatesOutsideHalfPixelRoundoffKeepNearestEdge(float coordinate, float expected)
    {
        Now.SetUIScale(1f);
        using (_drawList.Begin(Surface))
            Draw(new NowRect(coordinate, coordinate, 1f, 1f), false);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(4, rects.Count);
        AssertShape(rects[0], expected, expected, 1f, 1f, 1f);
    }

    [TestCase(1f)]
    [TestCase(1.25f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void SlicedImageUsesOneSharedPhysicalPixelGrid(float uiScale)
    {
        Now.SetUIScale(uiScale);
        float pixel = 1f / uiScale;
        var rect = new NowRect(0.5f * pixel, 0.5f * pixel, 3f * pixel, 3f * pixel);
        using (_drawList.Begin(Surface))
            DrawSliced(rect, pixel);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(36, rects.Count, "All nine one-pixel cells must survive density scaling.");
        for (int row = 0; row < 3; ++row)
        {
            for (int col = 0; col < 3; ++col)
            {
                int index = (row * 3 + col) * 4;
                AssertShape(rects[index], col + 1f, row + 1f, 1f, 1f, uiScale);
                if (col > 0)
                    Assert.AreEqual(rects[index - 4].x + rects[index - 4].z, rects[index].x, 0.00001f);
                if (row > 0)
                    Assert.AreEqual(rects[index - 12].y, rects[index].y + rects[index].w, 0.00001f);
            }
        }
    }

    [TestCase(1f)]
    [TestCase(1.25f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void CompressedSlicesCoverOnePixelWithoutOverlap(float uiScale)
    {
        Now.SetUIScale(uiScale);
        float pixel = 1f / uiScale;
        using (_drawList.Begin(Surface))
            DrawSliced(new NowRect(0.5f * pixel, 0.5f * pixel, pixel, pixel), 8f);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(4, rects.Count, "Compressed borders must share their rounded meeting point.");
        AssertShape(rects[0], 1f, 1f, 1f, 1f, uiScale);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TransformedShapesRetainFractionalAuthoredBounds(bool sdf)
    {
        Now.SetUIScale(1.5f);
        var rect = new NowRect(0.25f, 0.375f, 0.4f, 0.3f);
        var scale = new Vector2(1.3f, 0.8f);
        var origin = new Vector2(10.25f, 20.75f);
        using (_drawList.Begin(Surface))
        using (Now.Transform(scale, origin))
            Draw(rect, sdf);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(4, rects.Count);
        AssertShape(rects[0],
            origin.x + rect.x * scale.x, origin.y + rect.y * scale.y,
            rect.width * scale.x, rect.height * scale.y, 1f);
    }

    [Test]
    public void TransformedSlicedImageRetainsFractionalGrid()
    {
        Now.SetUIScale(2f);
        var rect = new NowRect(0.25f, 0.375f, 8.4f, 9.3f);
        var scale = new Vector2(1.3f, 0.8f);
        var origin = new Vector2(10.25f, 20.75f);
        using (_drawList.Begin(Surface))
        using (Now.Transform(scale, origin))
            DrawSliced(rect, 1.25f);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(36, rects.Count);
        AssertShape(rects[0],
            origin.x + rect.x * scale.x, origin.y + rect.y * scale.y,
            1.25f * scale.x, 1.25f * scale.y, 1f);
    }

    [Test]
    public void UnsnappedSdfCaptureKeepsSubpixelBounds()
    {
        Now.SetUIScale(2f);
        var rect = new NowRect(0.125f, 0.25f, 0.2f, 0.1f);
        using (_drawList.Begin(Surface))
            Now.DrawSdfUnsnapped(rect, default, _sdfMaterial, Vector4.one);

        List<Vector4> rects = ShapeRects();
        Assert.AreEqual(4, rects.Count);
        AssertShape(rects[0], rect.x, rect.y, rect.width, rect.height, 1f);
    }

    void Draw(NowRect rect, bool sdf)
    {
        if (sdf)
            Now.DrawSdf(rect, default, _sdfMaterial, Vector4.one);
        else
            Now.Rectangle(rect).SetMask(default).Draw();
    }

    void DrawSliced(NowRect rect, float border)
    {
        var style = Now.Rectangle(rect).SetTexture(_texture).SetMask(default);
        style.sliced = true;
        style.spriteBorder = Vector4.one * border;
        style.spritePixelSize = new Vector2(16f, 16f);
        style.Draw();
    }

    List<Vector4> ShapeRects()
    {
        var result = new List<Vector4>();
        _drawList.mesh.GetUVs(1, result);
        return result;
    }

    static void AssertShape(Vector4 actual, float x, float y, float width, float height, float uiScale)
    {
        // Rectangle shader bounds exclude the outer quad's AA padding. Reading
        // this stream tests visible geometry without depending on that padding.
        Assert.AreEqual(x, actual.x * uiScale, 0.00001f, "left physical edge");
        Assert.AreEqual(y, -(actual.y + actual.w) * uiScale, 0.00001f, "top physical edge");
        Assert.AreEqual(width, actual.z * uiScale, 0.00001f, "physical width");
        Assert.AreEqual(height, actual.w * uiScale, 0.00001f, "physical height");
    }
}
