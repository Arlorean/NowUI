using NUnit.Framework;
using UnityEngine;
using NowUI;
using NowUI.Internal;

public class NowImageRenderingTests
{
    static readonly Vector2 Surface = new Vector2(256, 256);

    NowDrawList _drawList;
    Texture2D _texture;
    Sprite _sprite;
    Material _material;
    Material _canvasMaterial;

    [SetUp]
    public void SetUp()
    {
        _drawList = new NowDrawList();
        _texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        _sprite = Sprite.Create(
            _texture,
            new Rect(0, 0, 32, 32),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(8f, 8f, 8f, 8f));

        var baseMaterial = Resources.Load<Material>("NowUI/UIMaterial");
        if (baseMaterial != null)
            _material = new Material(baseMaterial);

        var baseCanvasMaterial = Resources.Load<Material>("NowUI/UIMaterialUGUI");
        if (baseCanvasMaterial != null)
            _canvasMaterial = new Material(baseCanvasMaterial);
    }

    [TearDown]
    public void TearDown()
    {
        _drawList.Dispose();
        Object.DestroyImmediate(_canvasMaterial);
        Object.DestroyImmediate(_material);
        Object.DestroyImmediate(_sprite);
        Object.DestroyImmediate(_texture);
    }

    [Test]
    public void TexturedRectEmitsGeometry()
    {
        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60)).SetTexture(_texture).Draw();

        Assert.IsTrue(_drawList.hasGeometry);
    }

    [Test]
    public void SlicedSpriteEmitsNineQuads()
    {
        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60)).SetSprite(_sprite, sliced: true).Draw();

        var mesh = _drawList.GetCanvasMesh(0);
        Assert.NotNull(mesh);
        Assert.AreEqual(36, mesh.vertexCount, "a sliced sprite is nine quads");
    }

    [Test]
    public void SlicedSpriteCollapsesCellsWhenRectIsSmallerThanBorders()
    {
        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 12, 12)).SetSprite(_sprite, sliced: true).Draw();

        var mesh = _drawList.GetCanvasMesh(0);
        Assert.NotNull(mesh);
        Assert.GreaterOrEqual(mesh.vertexCount, 4);
        Assert.LessOrEqual(mesh.vertexCount, 36);
    }

    [Test]
    public void SpriteWithoutSlicingIsASingleQuad()
    {
        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60)).SetSprite(_sprite).Draw();

        var mesh = _drawList.GetCanvasMesh(0);
        Assert.NotNull(mesh);
        Assert.AreEqual(4, mesh.vertexCount);
    }

    [Test]
    public void PreserveAspectStillRendersGeometry()
    {
        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(0, 0, 200, 50)).SetTexture(_texture).SetPreserveAspect().Draw();

        Assert.IsTrue(_drawList.hasGeometry);
    }

    [Test]
    public void CustomMaterialRectangleUsesCustomBatch()
    {
        Assert.NotNull(_material);

        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60)).SetMaterial(_material).Draw();

        Assert.IsTrue(_drawList.hasGeometry);
        Assert.AreEqual(1, _drawList.batchCount);
        Assert.AreSame(_material, _drawList.batches[0].material);
        Assert.IsNull(_drawList.batches[0].canvasMaterial);
        Assert.AreEqual(NowMeshKind.CustomRectangle, _drawList.batches[0].kind);
    }

    [Test]
    public void CustomMaterialWithTextureUsesTexturedMaterialInstance()
    {
        Assert.NotNull(_material);

        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60))
                .SetMaterial(_material)
                .SetTexture(_texture)
                .Draw();

        var batch = _drawList.batches[0];
        Assert.AreEqual(NowMeshKind.CustomRectangle, batch.kind);
        Assert.AreNotSame(_material, batch.material);
        Assert.AreSame(_texture, batch.material.mainTexture);
    }

    [Test]
    public void CustomMaterialCanvasOverrideIsCaptured()
    {
        Assert.NotNull(_material);
        Assert.NotNull(_canvasMaterial);

        using (_drawList.Begin(Surface))
            Now.Rectangle(new NowRect(10, 10, 100, 60))
                .SetMaterial(_material, _canvasMaterial)
                .Draw();

        var batch = _drawList.batches[0];
        Assert.AreSame(_material, batch.material);
        Assert.AreSame(_canvasMaterial, batch.canvasMaterial);
        Assert.AreEqual(NowMeshKind.CustomRectangle, batch.kind);
    }

    // ------------------------------------------------------------------ preserveAspect, the contain arithmetic
    //
    // Now.DrawRect's preserveAspect branch (Now.cs:2279-2297) shrinks the drawn QUAD to the source's aspect and
    // centres it. Until these tests it had no coverage at all: PreserveAspectStillRendersGeometry above asserts
    // only that something was emitted, which passes for any shape whatsoever.
    //
    // EVERY ASSERTION HERE COMPARES TWO DRAWN QUADS rather than a quad against a literal. NowUI inflates emitted
    // geometry beyond the shape so the edge can fade across it, and its vertex positions carry their own sign and
    // origin conventions; a test written against absolute numbers would be pinning those conventions instead of
    // the fit, and would fail the next time the antialiasing band changed width.

    /// <summary>The XY extent of the quad the draw list received.</summary>
    static Rect DrawnQuad(NowDrawList list)
    {
        var mesh = list.GetCanvasMesh(0);
        Assert.NotNull(mesh, "nothing was tessellated, so there is no quad to measure");

        Vector3[] vertices = mesh.vertices;
        Assert.Greater(vertices.Length, 0);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vector3 v in vertices)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    Rect DrawAndMeasure(System.Action body)
    {
        using (_drawList.Begin(Surface))
            body();

        return DrawnQuad(_drawList);
    }

    /// <summary>
    /// A texture window less than one pixel tall must still fit at its true aspect.
    /// </summary>
    /// <remarks>
    /// <para>The aspect is computed as <c>uvRect.z * width / Max(uvRect.w * height, 1f)</c>. That floor is meant
    /// as a divide-by-zero guard, but one whole PIXEL is a far coarser number than that needs, and clamping the
    /// denominator upward drags the quotient DOWN - so a sub-pixel window reports an aspect much squarer than it
    /// is and the fitted quad comes out too tall.</para>
    /// <para>The numbers: a 64x64 texture windowed to <c>(0, 0, 1, 0.01)</c> is 64 x 0.64 texels, a true aspect
    /// of 100. Contained in a 1000x1000 box that is 1000x10. With the one-pixel floor the denominator becomes 1
    /// instead of 0.64, the aspect reads 64, and the quad is drawn 1000x15.6 - half again too tall.</para>
    /// <para>1000 px rather than the worked example's 100 because DrawRect snaps quad EDGES to pixels afterwards;
    /// at 100 px the correct and incorrect heights are 1.0 and 1.56 and both survive rounding as the same one or
    /// two pixels, so the test would pass either way and prove nothing.</para>
    /// </remarks>
    [Test]
    public void ASubPixelTextureWindowStillFitsAtItsTrueAspect()
    {
        var wide = new Texture2D(64, 64, TextureFormat.RGBA32, false);

        try
        {
            // 64 x 0.64 texels of source: an aspect of 100, so a 1000-wide box contains it at 10 tall.
            Rect fitted = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 1000, 1000))
                    .SetTexture(wide)
                    .SetUV(new Vector4(0f, 0f, 1f, 0.01f))
                    .SetPreserveAspect()
                    .Draw());

            Rect expected = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 1000, 10)).SetTexture(wide).Draw());

            Assert.AreEqual(expected.width, fitted.width, 1.5f,
                "the contained quad did not use the full width of the box");
            Assert.AreEqual(expected.height, fitted.height, 1.5f,
                "a sub-pixel-tall texture window was fitted at the wrong aspect: the divide-by-zero floor in " +
                "DrawRect's aspect calculation is one whole pixel, so it dominates the real denominator and " +
                "reports the source as far squarer than it is");
        }
        finally
        {
            Object.DestroyImmediate(wide);
        }
    }

    /// <summary>A 2:1 source contained in a square box keeps its aspect by losing height, not by stretching.</summary>
    [Test]
    public void ContainShrinksTheQuadToTheSourceAspect()
    {
        var wide = new Texture2D(64, 32, TextureFormat.RGBA32, false);

        try
        {
            Rect fitted = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 200, 200)).SetTexture(wide).SetPreserveAspect().Draw());

            Rect expected = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 200, 100)).SetTexture(wide).Draw());

            Assert.AreEqual(expected.width, fitted.width, 1.5f, "contain should use the box's full width here");
            Assert.AreEqual(expected.height, fitted.height, 1.5f,
                "a 2:1 source in a square box should be drawn at half the box's height");
        }
        finally
        {
            Object.DestroyImmediate(wide);
        }
    }

    /// <summary>The other branch: a 1:2 source in a square box loses width instead.</summary>
    [Test]
    public void ContainShrinksTheOtherAxisForATallSource()
    {
        var tall = new Texture2D(32, 64, TextureFormat.RGBA32, false);

        try
        {
            Rect fitted = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 200, 200)).SetTexture(tall).SetPreserveAspect().Draw());

            Rect expected = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(0, 0, 100, 200)).SetTexture(tall).Draw());

            Assert.AreEqual(expected.width, fitted.width, 1.5f,
                "a 1:2 source in a square box should be drawn at half the box's width");
            Assert.AreEqual(expected.height, fitted.height, 1.5f, "contain should use the box's full height here");
        }
        finally
        {
            Object.DestroyImmediate(tall);
        }
    }

    /// <summary>
    /// The fitted quad is centred in the box, not parked against an edge.
    /// </summary>
    /// <remarks>
    /// Asserted against the UNFITTED draw of the same box: whatever origin convention the vertices use, a centred
    /// fit shares its centre with the box it was fitted into, so the two centres must agree even though the two
    /// heights must not.
    /// </remarks>
    [Test]
    public void ContainCentresTheFittedQuadInTheBox()
    {
        var wide = new Texture2D(64, 32, TextureFormat.RGBA32, false);

        try
        {
            Rect fitted = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(20, 30, 200, 200)).SetTexture(wide).SetPreserveAspect().Draw());

            Rect box = DrawAndMeasure(() =>
                Now.Rectangle(new NowRect(20, 30, 200, 200)).SetTexture(wide).Draw());

            Assert.AreEqual(box.center.x, fitted.center.x, 1f, "the fitted quad drifted horizontally");
            Assert.AreEqual(box.center.y, fitted.center.y, 1f,
                "the fitted quad was not centred vertically - contain leaves equal space above and below");
            Assert.Less(fitted.height, box.height - 10f,
                "nothing was fitted at all, so the centring assertions above are vacuous");
        }
        finally
        {
            Object.DestroyImmediate(wide);
        }
    }

    /// <summary>
    /// A nine-sliced sprite ignores preserveAspect, because slicing is itself a fit rule.
    /// </summary>
    /// <remarks>
    /// Nine-slice keeps the corners at their pixel size and stretches only the edges and centre, which is a
    /// deliberate distortion of the source's aspect. Shrinking the quad to that aspect first would defeat the
    /// whole mechanism, so DrawRect's fit branch is guarded on <c>!sliced</c>. A square sprite in a 4:1 box is
    /// the discriminator: if the guard ever went away, the quad would collapse to a square.
    /// </remarks>
    [Test]
    public void PreserveAspectIsIgnoredForASlicedSprite()
    {
        Rect sliced = DrawAndMeasure(() =>
            Now.Rectangle(new NowRect(0, 0, 200, 50))
                .SetSprite(_sprite, sliced: true)
                .SetPreserveAspect()
                .Draw());

        Rect box = DrawAndMeasure(() =>
            Now.Rectangle(new NowRect(0, 0, 200, 50)).SetSprite(_sprite, sliced: true).Draw());

        Assert.AreEqual(box.width, sliced.width, 1.5f,
            "preserveAspect narrowed a nine-sliced sprite, which slicing exists to prevent");
        Assert.AreEqual(box.height, sliced.height, 1.5f);
    }

    /// <summary>preserveAspect on an untextured rectangle changes nothing: there is no aspect to preserve.</summary>
    [Test]
    public void PreserveAspectIsIgnoredWithoutATexture()
    {
        Rect fitted = DrawAndMeasure(() =>
            Now.Rectangle(new NowRect(0, 0, 200, 50)).SetPreserveAspect().Draw());

        Rect plain = DrawAndMeasure(() =>
            Now.Rectangle(new NowRect(0, 0, 200, 50)).Draw());

        Assert.AreEqual(plain.width, fitted.width, 1.5f);
        Assert.AreEqual(plain.height, fitted.height, 1.5f);
    }

    /// <summary>
    /// A texture window with no area draws nothing rather than something at an invented aspect.
    /// </summary>
    /// <remarks>
    /// Both degenerate directions have to agree, and before the aspect guard was restructured they did not: a
    /// zero-WIDTH window collapsed the quad through the numerator, while a zero-HEIGHT one divided by the
    /// one-pixel floor and produced a plausible-looking but fabricated aspect. Now each yields an aspect of zero
    /// and the quad collapses either way.
    /// </remarks>
    [Test]
    public void ADegenerateTextureWindowDrawsNothing()
    {
        foreach (Vector4 window in new[] { new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f) })
        {
            using (_drawList.Begin(Surface))
                Now.Rectangle(new NowRect(0, 0, 200, 200))
                    .SetTexture(_texture)
                    .SetUV(window)
                    .SetPreserveAspect()
                    .Draw();

            var mesh = _drawList.GetCanvasMesh(0);

            if (mesh == null || mesh.vertices.Length == 0)
                continue;

            Rect quad = DrawnQuad(_drawList);
            Assert.That(Mathf.Min(quad.width, quad.height), Is.LessThan(6f),
                "a texture window of " + window + " has no area, so the fitted quad should have collapsed " +
                "rather than being drawn at an aspect derived from a clamped denominator");
        }
    }

}
