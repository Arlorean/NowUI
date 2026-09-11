using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The cross-build contract for <c>Texture2D.LoadImage</c> and mipmaps.
/// </summary>
/// <remarks>
/// <para>WHY THIS FILE EXISTS. NowUI compiles the same runtime sources twice: against Unity, and against the
/// engine-free shim in <c>Standalone/NowUI.Engine</c> that lets it run in a browser. Anywhere the two disagree,
/// a change made in one place stops following to the other - which is the single property the whole arrangement
/// is for.</para>
/// <para><c>NowMarkdownImages</c> creates a 2x2 placeholder texture and lets <c>LoadImage</c> re-create it at the
/// decoded image's size. Whether the CONSTRUCTOR'S mipChain setting survives that re-creation decides whether
/// runtime-fetched pictures can have mipmaps at all, and it was not asserted anywhere: the shim's own comment
/// says Unity "discards whatever the texture was before", and the shim therefore hard-coded no-mipmaps. These
/// tests pin what Unity actually does, so the shim can be held to it rather than guessing.</para>
/// </remarks>
public class NowImageMipmapContractTests
{
    /// <summary>A small PNG with real content, so LoadImage has something to re-create the texture from.</summary>
    private static byte[] EncodedPng(int size)
    {
        var source = new Texture2D(size, size, TextureFormat.RGBA32, false);

        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), 255);

        source.SetPixels32(pixels);
        source.Apply();

        byte[] png = source.EncodeToPNG();
        Object.DestroyImmediate(source);
        return png;
    }

    [Test]
    public void LoadImageKeepsAMipChainTheConstructorAskedFor()
    {
        byte[] png = EncodedPng(64);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

        try
        {
            Assert.IsTrue(texture.LoadImage(png), "the encoded PNG did not decode, so this test proves nothing");
            Assert.AreEqual(64, texture.width);

            Assert.Greater(texture.mipmapCount, 1,
                "Unity's LoadImage discarded the constructor's mipChain, so a fetched image cannot be given " +
                "mipmaps by asking for them at construction - the engine-free shim must not be 'fixed' to " +
                "preserve a setting Unity itself drops");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void LoadImageLeavesAPlainTextureWithoutMips()
    {
        byte[] png = EncodedPng(64);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        try
        {
            Assert.IsTrue(texture.LoadImage(png));
            Assert.AreEqual(1, texture.mipmapCount,
                "a texture built without a mip chain gained one, which would make the memory budget in " +
                "NowMarkdownImages under-count every cached image");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// The cache budget counts mip levels, so a chained texture is charged what it actually costs.
    /// </summary>
    /// <remarks>
    /// Asserted through the public budget rather than by reading a private counter: two 64x64 textures with mip
    /// chains hold 2 x 5461 pixels, so a budget of 8192 admits one and evicts the other, where a base-level-only
    /// count would see 2 x 4096 and wrongly conclude both fit.
    /// </remarks>
    [Test]
    public void TheCacheBudgetChargesForMipLevels()
    {
        int entries = NowUI.Markdown.NowMarkdownImages.maxCacheEntries;
        long pixels = NowUI.Markdown.NowMarkdownImages.maxCachedTexturePixels;

        var first = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        var second = new Texture2D(64, 64, TextureFormat.RGBA32, true);

        try
        {
            Assert.Greater(first.mipmapCount, 1, "the textures under test have no chain, so this proves nothing");

            NowUI.Markdown.NowMarkdownImages.Reset();
            NowUI.Markdown.NowMarkdownImages.maxCacheEntries = 10;
            NowUI.Markdown.NowMarkdownImages.maxCachedTexturePixels = 8192;

            NowUI.Markdown.NowMarkdownImages.SetTexture("local/first", first);
            NowUI.Markdown.NowMarkdownImages.SetTexture("local/second", second);

            Assert.AreEqual(1, NowUI.Markdown.NowMarkdownImages.cachedEntryCount,
                "both 64x64 mip-chained textures were kept under an 8192 pixel budget, so the budget counted " +
                "only base levels and is under-reporting what the cache actually holds");
        }
        finally
        {
            NowUI.Markdown.NowMarkdownImages.Reset();
            NowUI.Markdown.NowMarkdownImages.maxCacheEntries = entries;
            NowUI.Markdown.NowMarkdownImages.maxCachedTexturePixels = pixels;
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

}
