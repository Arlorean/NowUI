using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public class SerializedAssetTests
{
    private string repository = null!;
    private NowFileResources builtIns = null!;
    private NowProjectAssets assets = null!;
    private string scratch = null!;

    [SetUp]
    public void SetUp()
    {
        repository = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        builtIns = new NowFileResources();
        assets = new NowProjectAssets(repository, builtIns);
        scratch = Path.Combine(Path.GetTempPath(), "nowui-serialized-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scratch, "Assets"));
    }

    [TearDown]
    public void TearDown()
    {
        assets.Dispose();
        builtIns.Dispose();
        Directory.Delete(scratch, recursive: true);
    }

    [Test]
    public void ReadsActualBinaryFontBytesAndAuthoredSettings()
    {
        const string path = "Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset";
        var font = assets.LoadAsset<NowFont>(path);
        Assert.That(font, Is.Not.Null);
        Assert.That(font!.name, Is.EqualTo("NotoSans-Regular.ttf"));
        Assert.That(font.TryGetSourceBytes(out byte[] source), Is.True);
        byte[] expected = File.ReadAllBytes(Path.Combine(repository, "Standalone/Tests/Fixtures/NowUI/NotoSans-Regular.ttf"));
        Assert.That(SHA256.HashData(source), Is.EqualTo(SHA256.HashData(expected)));
        Assert.That(font.dynamicAtlasSize, Is.EqualTo(32));
        Assert.That(font.dynamicPixelRange, Is.EqualTo(8));
        Assert.That(font.dynamicPageSize, Is.EqualTo(1024));
        Assert.That(font.dynamicMaxAtlasSize, Is.EqualTo(2048));
        Assert.That(font.dynamicMaxAtlasBytes, Is.EqualTo(16777216));
        Assert.That(font.GetAscender(), Is.GreaterThan(0));
        Assert.That(assets.LoadAsset(path, typeof(NowFontAsset), 11400000), Is.SameAs(font));
    }

    [Test]
    public void ResolvesActualFamilyFacesAndCompleteFallbackGraph()
    {
        var family = assets.LoadAsset<NowFontFamily>("NowUI/NotoSans");
        Assert.That(family, Is.Not.Null);
        Assert.That(family!.regular.HasEmbeddedSource, Is.True);
        Assert.That(family.bold.HasEmbeddedSource, Is.True);
        Assert.That(family.italic.HasEmbeddedSource, Is.True);
        Assert.That(family.boldItalic.HasEmbeddedSource, Is.True);
        Assert.That(family.fallbacks, Has.Count.EqualTo(6));
        Assert.That(family.fallbacks.All(value => value != null), Is.True);
        Assert.That(family.regular, Is.SameAs(assets.LoadAsset<NowFont>("Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset")));
    }

    [TestCase("Default", false, typeof(NowControlRenderer))]
    [TestCase("DefaultDark", true, typeof(NowControlRenderer))]
    [TestCase("Material", false, typeof(NowMaterialControlRenderer))]
    [TestCase("MaterialDark", true, typeof(NowMaterialControlRenderer))]
    [TestCase("UnityEditorDark", true, typeof(NowUnityEditorControlRenderer))]
    public void ReadsActualThemesAndRendererReferences(string name, bool dark, Type renderer)
    {
        var theme = assets.LoadAsset<NowThemeAsset>($"Assets/NowUI/Assets/Themes/{name}.asset");
        Assert.That(theme, Is.Not.Null);
        Assert.That(theme!.name, Is.EqualTo(name));
        Assert.That(theme.isDark, Is.EqualTo(dark));
        Assert.That(theme.controlRenderer, Is.TypeOf(renderer));
        Assert.That(theme.GetSpacing(NowSpacingToken.Md, default).x, Is.GreaterThan(0));
        if (theme.counterpart != null)
        {
            Assert.That(theme.counterpart.isDark, Is.Not.EqualTo(dark));
            Assert.That(theme.counterpart.counterpart, Is.SameAs(theme));
        }
    }

    [Test]
    public void ThemePaletteAndPresetValuesComeFromAsset()
    {
        var theme = assets.LoadAsset<NowThemeAsset>("Assets/NowUI/Assets/Themes/Default.asset");
        Assert.That(theme!.GetSpacing(NowSpacingToken.Panel, default), Is.EqualTo(new Vector4(20, 16, 20, 16)));
        Assert.That(theme.GetRadius(NowRadiusToken.Sm, default), Is.EqualTo(new Vector4(6, 6, 6, 6)));
        Assert.That(theme.GetColor(NowColorToken.SurfaceHover).r, Is.EqualTo(0.957f));
    }

    [Test]
    public void ReadsRawFontWithoutExportOrSidecar()
    {
        string source = Path.Combine(repository, "Standalone/Tests/Fixtures/NowUI/NotoSans-Regular.ttf");
        File.Copy(source, Path.Combine(scratch, "Assets", "MyFont.ttf"));
        using var direct = new NowProjectAssets(scratch, builtIns);
        var font = direct.LoadAsset<NowFont>("Assets/MyFont.ttf");
        Assert.That(font, Is.Not.Null);
        Assert.That(font!.name, Is.EqualTo("MyFont"));
        Assert.That(font.TryGetSourceBytes(out byte[] bytes), Is.True);
        Assert.That(bytes, Is.EqualTo(File.ReadAllBytes(source)));
    }

    [Test]
    public void WrongRequestedTypeReturnsNullWithoutPoisoningCache()
    {
        const string path = "Assets/NowUI/Assets/Themes/Default.asset";
        Assert.That(assets.LoadAsset<NowFont>(path), Is.Null);
        Assert.That(assets.LoadAsset<NowThemeAsset>(path), Is.Not.Null);
    }

    [Test]
    public void UnknownScriptCannotSpoofKnownClassName()
    {
        File.WriteAllText(Path.Combine(scratch, "Assets", "Unknown.asset"), """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &11400000
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 11111111111111111111111111111111, type: 3}
              m_Name: Invalid
              m_EditorClassIdentifier: NowUI.Runtime::NowThemeAsset
            """);
        using var direct = new NowProjectAssets(scratch, builtIns);
        var error = Assert.Throws<InvalidDataException>(() => direct.LoadAsset<NowThemeAsset>("Assets/Unknown.asset"));
        Assert.That(error!.Message, Does.Contain("unsupported script GUID"));
    }

    [Test]
    public void MissingGuidReferenceFailsAndRetryDoesNotReturnHalfLoadedTheme()
    {
        File.WriteAllText(Path.Combine(scratch, "Assets", "Missing.asset"), """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &11400000
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 04c5a0a2a8e8407792a9d8e3844255a0, type: 3}
              m_Name: Missing
              _counterpart: {fileID: 11400000, guid: 11111111111111111111111111111111, type: 2}
            """);
        using var direct = new NowProjectAssets(scratch, builtIns);
        for (int i = 0; i < 2; i++)
        {
            var error = Assert.Throws<InvalidDataException>(() => direct.LoadAsset<NowThemeAsset>("Assets/Missing.asset"));
            Assert.That(error!.Message, Does.Contain("11111111111111111111111111111111"));
        }
    }

    [Test]
    public void ReadsActualEmbeddedAlphaTextureByLocalFileId()
    {
        const string path = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        var texture = (Texture2D)assets.LoadAsset(path, typeof(Texture2D), 28684132378477856);
        Assert.That((texture.width, texture.height), Is.EqualTo((1024, 1024)));
        Assert.That(texture.isLinear, Is.True);
        Assert.That(texture.name, Is.EqualTo("LiberationSans SDF Atlas"));
        var pixels = texture.GetPixels32();
        Assert.That(pixels.All(value => value.r == 255 && value.g == 255 && value.b == 255), Is.True);
        Assert.That(pixels.Any(value => value.a == 0), Is.True);
        Assert.That(pixels.Any(value => value.a > 200), Is.True);
    }

    [TestCase(1, "4080", "ffffff40ffffff80")]
    [TestCase(63, "4080", "400000ff800000ff")]
    [TestCase(3, "010203050607", "010203ff050607ff")]
    [TestCase(4, "0102030405060708", "0102030405060708")]
    [TestCase(5, "0401020308050607", "0102030405060708")]
    [TestCase(14, "0302010407060508", "0102030405060708")]
    public void ConvertsEmbeddedAtlasChannelsWithoutResampling(int format, string source, string expected)
    {
        File.WriteAllText(Path.Combine(scratch, "Assets", "Atlas.asset"), AtlasYaml(format, source));
        using var direct = new NowProjectAssets(scratch, builtIns);
        var texture = (Texture2D)direct.LoadAsset("Assets/Atlas.asset", typeof(Texture2D), 2800001);
        Assert.That(texture.GetRawTextureData(), Is.EqualTo(Convert.FromHexString(expected)));
        Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32));
        Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
        Assert.That(texture.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp));
        Assert.That(texture.isLinear, Is.True);
    }

    [Test]
    public void ResolvesBakedFontPageTextureInsideSameAsset()
    {
        string font = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &11400000
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 10bbcf9941bbd194b90f55d4099e3d9c, type: 3}
              m_Name: Baked
              _bakedCharacters: A
              _bakedAllGlyphs: 0
              _bakedPages:
              - texture: {fileID: 2800001}
                atlasSize: 32
                pixelRange: 8
                distanceRange: 8
                size: 32
                packedSdf16: 1
                metrics: {emSize: 1, lineHeight: 1.4, ascender: 1, descender: -0.4}
                glyphs:
                - unicode: 65
                  advance: 0.5
                  planeBounds: {left: 0, bottom: 0, right: 0.5, top: 1}
                  atlasBounds: {left: 0, bottom: 0, right: 1, top: 2}
            """;
        File.WriteAllText(Path.Combine(scratch, "Assets", "Baked.asset"), font + "\n" + AtlasYaml(4, "0102030405060708", false));
        using var direct = new NowProjectAssets(scratch, builtIns);
        var loaded = direct.LoadAsset<NowFont>("Assets/Baked.asset");
        var texture = (Texture2D)direct.LoadAsset("Assets/Baked.asset", typeof(Texture2D), 2800001);
        Assert.That(loaded!.HasEmbeddedSource, Is.False);
        Assert.That(loaded.bakedPageCount, Is.EqualTo(1));
        Assert.That(loaded.bakedGlyphCount, Is.EqualTo(1));
        Assert.That(loaded.bakedPagesStale, Is.False);
        Assert.That(loaded.bakedCharacters, Is.EqualTo("A"));
        Assert.That(loaded.IsBakedAtlasTexture(texture), Is.True);
        Assert.That(texture.GetPixels32()[1], Is.EqualTo(new Color32(5, 6, 7, 8)));
    }

    [Test]
    public void RejectsCompressedEmbeddedAtlasExplicitly()
    {
        File.WriteAllText(Path.Combine(scratch, "Assets", "Compressed.asset"), AtlasYaml(10, "0102030405060708"));
        using var direct = new NowProjectAssets(scratch, builtIns);
        var error = Assert.Throws<InvalidDataException>(() => direct.LoadAsset("Assets/Compressed.asset", typeof(Texture2D), 2800001));
        Assert.That(error!.Message, Does.Contain("unsupported Unity texture format 10"));
    }

    [Test]
    public void ReadsBinaryFontTextureSubassetAndPreservesMipBytes()
    {
        string path = Path.Combine(scratch, "Assets", "BinaryBaked.asset");
        var manager = new AssetsManager();
        try
        {
            // Start with an actual Unity file, attach a small uncompressed Texture2D through the public writer,
            // and leave the existing NowFont's embedded bytes/type tree intact. No Editor/import/export is used.
            var file = manager.LoadAssetsFile(Path.Combine(repository, "Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset"), false);
            var fontInfo = file.file.GetAssetInfo(11400000);
            var fontData = manager.GetBaseField(file, fontInfo);
            fontData["atlas.m_FileID"].AsInt = 0;
            fontData["atlas.m_PathID"].AsLong = 2800001;
            fontInfo.SetNewData(fontData);
            file.file.Metadata.TypeTreeTypes.Add(TextureTypeTree());
            var textureInfo = AssetFileInfo.Create(file.file, 2800001, 28);
            var textureData = manager.CreateValueBaseField(file, 28);
            textureData["m_Name"].AsString = "Binary Atlas";
            textureData["m_Width"].AsInt = 1;
            textureData["m_Height"].AsInt = 2;
            textureData["m_TextureFormat"].AsInt = 4;
            textureData["m_MipCount"].AsInt = 2;
            textureData["m_ColorSpace"].AsInt = 0;
            textureData["image data"].AsByteArray = Convert.FromHexString("0102030405060708090a0b0c");
            textureInfo.SetNewData(textureData);
            file.file.AssetInfos.Add(textureInfo);
            using var writer = new AssetsFileWriter(path);
            file.file.Write(writer);
        }
        finally { manager.UnloadAll(); }
        using var direct = new NowProjectAssets(scratch, builtIns);
        var font = direct.LoadAsset<NowFont>("Assets/BinaryBaked.asset");
        var texture = (Texture2D)direct.LoadAsset("Assets/BinaryBaked.asset", typeof(Texture2D), 2800001);
        Assert.That(font!.atlas, Is.SameAs(texture));
        Assert.That(texture.name, Is.EqualTo("Binary Atlas"));
        Assert.That(texture.mipmapCount, Is.EqualTo(2));
        Assert.That(texture.GetRawTextureData(), Is.EqualTo(Convert.FromHexString("0102030405060708090a0b0c")));
    }

    private static TypeTreeType TextureTypeTree()
    {
        var type = new TypeTreeType
        {
            TypeId = 28, ScriptTypeIndex = ushort.MaxValue,
            TypeHash = Hash128.NewBlankHash(), ExtTypeHash = Hash128.NewBlankHash(),
            TypeBlobIsDefinition = true, TypeDependencies = [],
            TypeBlob = new TypeTreeBlob { Nodes = [], StringBufferBytes = [] },
        };
        var strings = new StringBuilder();
        uint Text(string value) { uint offset = (uint)strings.Length; strings.Append(value).Append('\0'); return offset; }
        void Field(byte depth, string name, string kind, int size, bool array = false, bool aligned = false)
            => type.Nodes.Add(new TypeTreeNode
            {
                Level = depth, Version = 1, NameStrOffset = Text(name), TypeStrOffset = Text(kind),
                ByteSize = size, Index = (uint)type.Nodes.Count,
                TypeFlags = array ? TypeTreeNodeFlags.Array : 0, MetaFlags = aligned ? 0x4000u : 0u,
            });
        Field(0, "Base", "Texture2D", -1);
        Field(1, "m_Name", "string", -1);
        Field(2, "Array", "Array", -1, true, true);
        Field(3, "size", "int", 4);
        Field(3, "data", "char", 1);
        foreach (string field in new[] { "m_Width", "m_Height", "m_TextureFormat", "m_MipCount", "m_ColorSpace" })
            Field(1, field, "int", 4);
        Field(1, "image data", "TypelessData", -1, true, true);
        Field(2, "size", "int", 4);
        Field(2, "data", "UInt8", 1);
        type.StringBufferBytes = Encoding.UTF8.GetBytes(strings.ToString());
        return type;
    }

    private static string AtlasYaml(int format, string data, bool directives = true)
        => (directives ? "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" : "") + $$"""
            --- !u!28 &2800001
            Texture2D:
              m_Name: Baked Atlas
              m_Width: 1
              m_Height: 2
              m_TextureFormat: {{format}}
              m_MipCount: 1
              m_ImageCount: 1
              m_TextureDimension: 2
              m_TextureSettings:
                m_FilterMode: 0
                m_WrapU: 1
                m_WrapV: 1
                m_WrapW: 1
              m_ColorSpace: 0
              _typelessdata: {{data}}
            """;
}
