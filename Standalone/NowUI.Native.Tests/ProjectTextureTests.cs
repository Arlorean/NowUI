using System.Globalization;
using System.Text;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

public class ProjectTextureTests
{
    private string scratch = null!;
    private readonly List<UnityEngine.Object> owned = new();

    [SetUp]
    public void SetUp()
    {
        scratch = Path.Combine(Path.GetTempPath(), "nowui-project-textures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var asset in owned.AsEnumerable().Reverse()) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        owned.Clear();
        Directory.Delete(scratch, true);
    }

    [TestCase("png")]
    [TestCase("bmp")]
    [TestCase("tga")]
    public void DecodesSourcePixelsWithCorrectChannelsOrientationAndAlpha(string extension)
    {
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 128, 255, 255, 255, 0];
        string path = Path.Combine(scratch, "colors." + extension);
        File.WriteAllBytes(path, extension == "png" ? NowPng.EncodeRgba(2, 2, rgba) : Bitmap(rgba, extension));
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.That((texture.width, texture.height), Is.EqualTo((2, 2)));
        var actual = texture.GetPixels32().SelectMany(c => new[] { c.r, c.g, c.b, c.a }).ToArray();
        if (extension == "bmp") { rgba[11] = 255; rgba[15] = 255; }
        Assert.That(actual, Is.EqualTo(rgba));
        Assert.That(texture.name, Is.EqualTo("colors"));
        Assert.That(File.Exists(path + ".meta"), Is.False, "The source loader must not create Unity metadata.");
    }

    [Test]
    public void DecodesJpegSourceWithoutAPlatformImageLibrary()
    {
        // A generated 1x1 red JPEG, embedded so the test needs no extra encoder or Windows dependency.
        const string jpeg = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD50ooor8MP9Uz/2Q==";
        string path = Path.Combine(scratch, "red.jpg");
        File.WriteAllBytes(path, Convert.FromBase64String(jpeg));
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.That((texture.width, texture.height), Is.EqualTo((1, 1)));
        var pixel = texture.GetPixels32()[0];
        Assert.That(pixel.r, Is.GreaterThan(250));
        Assert.That(pixel.g, Is.LessThan(3));
        Assert.That(pixel.b, Is.LessThan(3));
        Assert.That(pixel.a, Is.EqualTo(255));
    }

    [Test]
    public void ReadsActualProjectLogoWithoutMetadataOrExport()
    {
        string project = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        string path = Path.Combine(project, "Assets/Mockups/Google/google-logo.png");
        if (!File.Exists(path)) Assert.Ignore("The optional local mockup logo is not present in this checkout.");
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.That(texture.width, Is.GreaterThan(1));
        Assert.That(texture.height, Is.GreaterThan(1));
        Assert.That(texture.GetPixels32().Any(c => c.a != 0), Is.True);
    }

    [Test]
    public void ReadsSamplingColorSpaceMipmapsAndAlphaUsage()
    {
        string path = Png(4, 4, """
          mipmaps: {enableMipMap: 1, sRGBTexture: 0}
          textureSettings: {filterMode: 2, wrapU: 1, wrapV: 2, wrapW: 3, aniso: 4}
          alphaUsage: 0
        """);
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Trilinear));
        Assert.That((texture.wrapModeU, texture.wrapModeV, texture.wrapModeW),
            Is.EqualTo((TextureWrapMode.Clamp, TextureWrapMode.Mirror, TextureWrapMode.MirrorOnce)));
        Assert.That(texture.anisoLevel, Is.EqualTo(4));
        Assert.That(texture.isLinear, Is.True);
        Assert.That(texture.mipmapCount, Is.EqualTo(3));
        Assert.That(texture.GetPixels32().All(c => c.a == 255), Is.True);
    }

    [TestCase(0, 5, 3)]
    [TestCase(1, 4, 4)]
    [TestCase(2, 8, 4)]
    [TestCase(3, 4, 2)]
    public void AppliesNpotSizingWithAnExplicitResizeDiagnostic(int mode, int width, int height)
    {
        string path = Png(5, 3, $"  nPOTScale: {mode}\n");
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        TextWriter previous = Console.Error;
        try
        {
            Console.SetError(log);
            var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
            Assert.That((texture.width, texture.height), Is.EqualTo((width, height)));
        }
        finally { Console.SetError(previous); }
        Assert.That(log.ToString().Contains("resized"), Is.EqualTo(mode != 0));
    }

    [Test]
    public void StandaloneMaxSizeOverrideWinsOverDefaultSettings()
    {
        string path = Png(16, 8, """
          maxTextureSize: 16
          platformSettings:
          - {buildTarget: DefaultTexturePlatform, maxTextureSize: 8, overridden: 0}
          - {buildTarget: Standalone, maxTextureSize: 4, overridden: 1}
        """);
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.That((texture.width, texture.height), Is.EqualTo((4, 2)));
    }

    [Test]
    public void SingleSpriteUsesSourceBorderPivotAndPixelsPerUnit()
    {
        string path = Png(20, 10, """
          textureType: 8
          spriteMode: 1
          spritePixelsToUnits: 32
          alignment: 9
          spritePivot: {x: 0.25, y: 0.75}
          spriteBorder: {x: 2, y: 1, z: 3, w: 4}
          nPOTScale: 1
        """);
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        var sprite = NowUnityTextureAssets.LoadSprite(path, null!, 21300000, texture, owned.Add);
        Assert.That(sprite.texture, Is.SameAs(texture));
        Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, 20, 10)));
        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(5, 7.5f)));
        Assert.That(sprite.border, Is.EqualTo(new Vector4(2, 1, 3, 4)));
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32));
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadSprite(path, "missing", null, texture, owned.Add));
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadSprite(path, null!, 42, texture, owned.Add));
    }

    [TestCase("internalID: 881234", "")]
    [TestCase("internalID: 0", "    nameFileIdTable: {right: 881234}\n")]
    [TestCase("internalID: 0", "  internalIDToNameTable:\n  - first: {213: 881234}\n    second: right\n")]
    [TestCase("internalID: 0", "  fileIDToRecycleName: {881234: right}\n")]
    public void MultipleSpriteResolvesNameAndAllSupportedFileIdTables(string id, string table)
    {
        string path = Png(20, 10, "  spriteMode: 2\n  spritePixelsToUnits: 50\n  spriteSheet:\n    sprites:\n"
            + "    - name: left\n      internalID: 44\n      rect: {x: 0, y: 0, width: 10, height: 10}\n"
            + "    - name: right\n      " + id + "\n      rect: {x: 10, y: 2, width: 10, height: 6}\n"
            + "      alignment: 3\n      border: {x: 1, y: 2, z: 3, w: 1}\n" + table);
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        var byName = NowUnityTextureAssets.LoadSprite(path, "right", null, texture, owned.Add);
        var byId = NowUnityTextureAssets.LoadSprite(path, null!, 881234, texture, owned.Add);
        Assert.That(byId.name, Is.EqualTo("right"));
        Assert.That(byId.rect, Is.EqualTo(byName.rect));
        Assert.That(byId.rect, Is.EqualTo(new Rect(10, 2, 10, 6)));
        Assert.That(byId.pivot, Is.EqualTo(new Vector2(10, 6)));
        Assert.That(byId.border, Is.EqualTo(new Vector4(1, 2, 3, 1)));
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadSprite(path, null!, null, texture, owned.Add));
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadSprite(path, "left", 881234, texture, owned.Add));
    }

    [Test]
    public void MaxSizeSpriteMatchesUnityImportCoordinatesAndWorldScale()
    {
        // Values independently captured from Unity6000.4 TextureImporter, not inferred from the source loader.
        string path = Png(64, 32, """
          textureType: 8
          spriteMode: 1
          maxTextureSize: 32
          spritePixelsToUnits: 32
          alignment: 9
          spritePivot: {x: 0.25, y: 0.75}
          spriteBorder: {x: 8, y: 4, z: 12, w: 8}
        """);
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        var sprite = NowUnityTextureAssets.LoadSprite(path, null!, 21300000, texture, owned.Add);
        Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, 32, 16)));
        Assert.That(sprite.textureRect, Is.EqualTo(sprite.rect));
        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(8, 12)));
        Assert.That(sprite.border, Is.EqualTo(new Vector4(4, 2, 6, 4)));
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(16));
    }

    [TestCase("  textureType: 1\n", "texture")]
    [TestCase("  textureShape: 2\n", "non-2D")]
    [TestCase("  swizzle: 0\n", "swizzling")]
    public void RejectsImportTransformsItCannotReproduce(string settings, string diagnostic)
    {
        string path = Png(2, 2, settings);
        var error = Assert.Throws<NotSupportedException>(() => NowUnityTextureAssets.LoadTexture(path, owned.Add));
        Assert.That(error!.Message, Does.Contain(diagnostic).IgnoreCase);
        Assert.That(owned, Is.Empty);
    }

    [Test]
    public void BadSpriteGeometryAndMalformedMetadataFailClearly()
    {
        string path = Png(2, 2, "  spriteMode: 2\n  spriteSheet:\n    sprites:\n    - name: outside\n      rect: {x: 2, y: 0, width: 2, height: 2}\n");
        var texture = NowUnityTextureAssets.LoadTexture(path, owned.Add);
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadSprite(path, "outside", null, texture, owned.Add));
        Assert.That(owned.Count, Is.EqualTo(1));
        File.WriteAllText(path + ".meta", "TextureImporter: [broken");
        Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadTexture(path, owned.Add));
    }

    [Test]
    public void RefusesHugeImageHeaderBeforeDecodeAllocation()
    {
        byte[] tga = new byte[18];
        tga[2] = 2;
        tga[12] = 255; tga[13] = 255;
        tga[14] = 255; tga[15] = 255;
        tga[16] = 32;
        string path = Path.Combine(scratch, "huge.tga");
        File.WriteAllBytes(path, tga);
        var error = Assert.Throws<InvalidDataException>(() => NowUnityTextureAssets.LoadTexture(path, owned.Add));
        Assert.That(error!.Message, Does.Contain("16384"));
        Assert.That(owned, Is.Empty);
    }

    private string Png(int width, int height, string settings)
    {
        string path = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".png");
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 123; pixels[i + 1] = 45; pixels[i + 2] = 67; pixels[i + 3] = 128; }
        File.WriteAllBytes(path, NowPng.EncodeRgba(width, height, pixels));
        // Raw string indentation is removed by C#; add the importer indentation back when needed.
        if (!settings.StartsWith("  ", StringComparison.Ordinal)) settings = string.Join("\n", settings.Split('\n').Select(line => "  " + line));
        File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\nTextureImporter:\n" + settings + "\n");
        return path;
    }

    private static byte[] Bitmap(byte[] rgba, string format)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        if (format == "bmp")
        {
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(70); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(2); writer.Write(2); writer.Write((short)1); writer.Write((short)24);
            writer.Write(0); writer.Write(16); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++) { int i = (y * 2 + x) * 4; writer.Write(rgba[i + 2]); writer.Write(rgba[i + 1]); writer.Write(rgba[i]); }
                writer.Write((short)0);
            }
        }
        else
        {
            writer.Write(new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 32, 8 });
            for (int i = 0; i < rgba.Length; i += 4) { writer.Write(rgba[i + 2]); writer.Write(rgba[i + 1]); writer.Write(rgba[i]); writer.Write(rgba[i + 3]); }
        }
        return stream.ToArray();
    }
}
