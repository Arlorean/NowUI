using NUnit.Framework;
using UnityEngine;
using NowUI;
using NowUI.Internal;
using NowUI.Markdown;

/// <summary>
/// The viewport cull in <c>NowMarkdownDocument.DrawResolved</c>, and the things it must not change.
/// </summary>
/// <remarks>
/// <para>WHY THIS FILE EXISTS. Before it, not one markdown test drew inside a mask - so a cull could have
/// disabled text selection, starved every embed of its only measurement, or clipped the document to the wrong
/// rectangle, and the entire suite would have stayed green.</para>
/// <para>The cull is a WHITELIST of three purely visual op kinds. These tests pin the two properties that
/// whitelist exists to protect: what the reader sees inside the viewport does not change, and the document's
/// measured height - which the scroll range is built from - does not depend on how much of it is visible.</para>
/// </remarks>
public class NowMarkdownCullingTests
{
    static readonly Vector2 Surface = new Vector2(400, 4000);

    NowDrawList _drawList;

    /// <summary>Long enough that a viewport-sized mask hides most of it.</summary>
    static string LongDocument()
    {
        var text = new System.Text.StringBuilder("# A long document\n\n");

        for (int i = 0; i < 120; i++)
            text.Append("Paragraph ").Append(i).Append(" with enough prose in it to wrap across the column ")
                .Append("more than once, so the document is many screens tall.\n\n");

        return text.ToString();
    }

    [SetUp]
    public void SetUp()
    {
        _drawList = new NowDrawList();
        NowMarkdown.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        _drawList.Dispose();
    }

    /// <summary>
    /// The measured height is the same whether the document is masked to a sliver or not.
    /// </summary>
    /// <remarks>
    /// This is the property the scroll range depends on. If culling ever reached the layout - or an op whose
    /// measurement feeds back into it, which is exactly what an embed does - the height would shrink to whatever
    /// happened to be visible and the scrollbar would lie.
    /// </remarks>
    [Test]
    public void TheMeasuredHeightDoesNotDependOnHowMuchIsVisible()
    {
        string source = LongDocument();
        var full = new NowRect(0, 0, 360, 4000);

        float unmasked;
        using (_drawList.Begin(Surface))
            unmasked = NowMarkdown.Document(source).SetFontSize(15f).Draw(full).height;

        float masked;
        using (_drawList.Begin(Surface))
        using (Now.Mask(new NowRect(0, 0, 360, 120)))
            masked = NowMarkdown.Document(source).SetFontSize(15f).Draw(full).height;

        Assert.Greater(unmasked, 500f, "the document is not tall enough for this test to mean anything");
        Assert.AreEqual(unmasked, masked, 0.5f,
            "masking the document changed its measured height, so the cull reached the layout - a scroll range " +
            "built on this height would be wrong");
    }

    /// <summary>
    /// A document masked to a sliver submits far less geometry than the same document unmasked.
    /// </summary>
    /// <remarks>
    /// THIS DOES NOT PROVE THE DRAW-LOOP CULL, and saying so matters more than the green tick. It passes with
    /// the cull disabled, because the renderer already rejects a fully-masked text draw before shaping any
    /// glyph (Now.DrawString: "scrolled-out content costs nothing"). What it pins is that masked content does
    /// not reach the vertex buffer, whoever discards it - still worth having, and worth knowing it is not the
    /// thing that would catch a regression in CulledByMask.
    ///
    /// The draw-loop cull saves CPU in front of that rejection - the per-op rect, the style setup, the call -
    /// which no vertex count can see. Its value was measured in the browser instead: a 48 kB document went from
    /// 51.3 ms to 37.8 ms per frame, and the docs viewer from 25.7 to 32.4 FPS.
    /// </remarks>
    [Test]
    public void MaskingADocumentSubmitsFarLessGeometry()
    {
        string source = LongDocument();
        var full = new NowRect(0, 0, 360, 4000);

        using (_drawList.Begin(Surface))
            NowMarkdown.Document(source).SetFontSize(15f).Draw(full);

        long unmasked = TotalVertices();

        using (_drawList.Begin(Surface))
        using (Now.Mask(new NowRect(0, 0, 360, 120)))
            NowMarkdown.Document(source).SetFontSize(15f).Draw(full);

        long masked = TotalVertices();

        TestContext.WriteLine("unmasked " + unmasked + " vertices, masked " + masked);

        Assert.Greater(unmasked, 0L, "nothing was tessellated at all, so this test proves nothing");
        Assert.Less(masked, unmasked / 2,
            "a document masked to a sliver submitted at least half the geometry of the whole document, so it " +
            "is still being drawn in full");
    }

    /// <summary>What IS inside the viewport still draws - a cull that clipped everything would pass the test above.</summary>
    [Test]
    public void TheVisiblePartStillDraws()
    {
        using (_drawList.Begin(Surface))
        using (Now.Mask(new NowRect(0, 0, 360, 120)))
            NowMarkdown.Document(LongDocument()).SetFontSize(15f).Draw(new NowRect(0, 0, 360, 4000));

        Assert.Greater(TotalVertices(), 0L,
            "a document whose first screen is inside the mask drew nothing, so the cull is clipping content it " +
            "should have kept");
    }

    /// <summary>With no mask at all the cull stands aside, and the document overflows its rect as documented.</summary>
    [Test]
    public void WithoutAMaskNothingIsCulled()
    {
        string source = LongDocument();

        using (_drawList.Begin(Surface))
            NowMarkdown.Document(source).SetFontSize(15f).Draw(new NowRect(0, 0, 360, 60));

        Assert.Greater(TotalVertices(), 0L);

        long shortRect = TotalVertices();

        using (_drawList.Begin(Surface))
            NowMarkdown.Document(source).SetFontSize(15f).Draw(new NowRect(0, 0, 360, 4000));

        Assert.AreEqual(TotalVertices(), shortRect,
            "the rect's own height changed how much was drawn, which means something is culling against the " +
            "rect rather than against an ambient mask - a document with no mask must overflow, not clip");
    }

    /// <summary>
    /// Vertices on the canvas mesh. `batchCount` counts BATCHES within a page, not pages - indexing
    /// GetCanvasMesh with it walks off the end of the extra-page list, which is how the first version of this
    /// helper threw instead of measuring.
    /// </summary>
    long TotalVertices()
    {
        return _drawList.mesh != null ? _drawList.mesh.vertexCount : 0L;
    }
}
