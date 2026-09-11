using NowUI.Cli;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

public class ProjectAssetProviderTests
{
    private string root = null!;
    private NowFileResources builtIns = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "nowui-project-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets/Resources/UI"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        builtIns = new NowFileResources();
        WriteImage("Assets/Resources/UI/mark.png", 255);
    }

    [TearDown]
    public void TearDown()
    {
        builtIns.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void DiscoversProjectFromNestedScenePathWithoutConfiguration()
    {
        string folder = Path.Combine(root, "Tools/Previews");
        Directory.CreateDirectory(folder);
        Assert.That(NowProjectAssets.FindProjectRoot(Path.Combine(folder, "Demo.csproj")), Is.EqualTo(root));
        var options = RenderOptions.Parse(["render", Path.Combine(folder, "Demo.csproj"), "--output", Path.Combine(root, "out.png")]);
        using var resources = new ProjectResources(options, Path.Combine(folder, "bin/Demo.dll"));
        Assert.That(resources.Provider, Is.TypeOf<NowProjectAssets>());
        Assert.That(resources.DataPath, Is.EqualTo(Path.Combine(root, "Assets")));
    }

    [Test]
    public void AliasesAndProjectPathsShareTextureIdentityAndOwnership()
    {
        Texture2D texture;
        using (var assets = new NowProjectAssets(root, builtIns))
        {
            texture = assets.LoadAsset<Texture2D>("UI/mark");
            Assert.That(texture, Is.Not.Null);
            Assert.That(assets.LoadAsset<Texture2D>("Assets/Resources/UI/mark.png"), Is.SameAs(texture));
            Assert.That(assets.LoadAsset<Texture2D>(Path.Combine(root, "Assets/Resources/UI/mark.png")), Is.SameAs(texture));
            Assert.That(texture.GetPixels32()[0].r, Is.EqualTo(255));
            Assert.That(assets.LoadAsset<NowThemeAsset>("UI/mark"), Is.Null);
            Assert.That(assets.LoadAsset<Texture2D>("UI/missing"), Is.Null);
        }
        Assert.That(texture == null, Is.True, "Provider owns project textures.");
        Assert.That(builtIns.GetMaterial("NowUI/UIMaterial"), Is.Not.Null, "Provider must not own built-in templates.");
    }

    [Test]
    public void ReportsAmbiguousResourcesAliasesInsteadOfPickingOne()
    {
        WriteImage("Assets/Other/Resources/UI/mark.png", 64);
        using var assets = new NowProjectAssets(root, builtIns);
        var error = Assert.Throws<InvalidDataException>(() => assets.LoadAsset<Texture2D>("UI/mark"));
        Assert.That(error!.Message, Does.Contain("Ambiguous").And.Contain("mark.png"));
    }

    [Test]
    public void NewProviderReadsChangedSourceWithoutExport()
    {
        using (var first = new NowProjectAssets(root, builtIns))
            Assert.That(first.LoadAsset<Texture2D>("UI/mark").GetPixels32()[0].r, Is.EqualTo(255));
        WriteImage("Assets/Resources/UI/mark.png", 32);
        using var second = new NowProjectAssets(root, builtIns);
        Assert.That(second.LoadAsset<Texture2D>("UI/mark").GetPixels32()[0].r, Is.EqualTo(32));
    }

    [Test]
    public void NamedSpriteAndSerializedFileIdShareIdentity()
    {
        File.WriteAllText(Path.Combine(root, "Assets/Resources/UI/mark.png.meta"), """
            fileFormatVersion: 2
            guid: 0123456789abcdef0123456789abcdef
            TextureImporter:
              textureType: 8
              spriteMode: 1
              spritePixelsToUnits: 100
              spriteSheet:
                internalID: 21300000
            """);
        using var assets = new NowProjectAssets(root, builtIns);
        var byName = assets.LoadAsset<Sprite>("UI/mark#mark");
        var byId = assets.LoadAsset("Assets/Resources/UI/mark.png", typeof(Sprite), 21300000);
        Assert.That(byId, Is.SameAs(byName));
        Assert.That(assets.LoadAsset<Sprite>("UI/mark"), Is.SameAs(byName));
        Assert.That(byName.texture, Is.SameAs(assets.LoadAsset<Texture2D>("UI/mark")));
    }

    [Test]
    public void MalformedTextureCanBeFixedAndRetriedWithoutPoisoningCache()
    {
        File.WriteAllBytes(Path.Combine(root, "Assets/Resources/UI/mark.png"), [1, 2, 3]);
        using var assets = new NowProjectAssets(root, builtIns);
        Assert.Throws<InvalidDataException>(() => assets.LoadAsset<Texture2D>("UI/mark"));
        WriteImage("Assets/Resources/UI/mark.png", 48);
        Assert.That(assets.LoadAsset<Texture2D>("UI/mark").GetPixels32()[0].r, Is.EqualTo(48));
    }

    [Test]
    public void ExplicitProjectOptionWorksForScenesElsewhereAndRejectsMissingAssets()
    {
        var options = RenderOptions.Parse(["preview", "Elsewhere.csproj", "--unity-project", root]);
        Assert.That(options.UnityProject, Is.EqualTo(root));
        Assert.Throws<ArgumentException>(() => RenderOptions.Parse(["preview", "Elsewhere.csproj", "--unity-project", Path.Combine(root, "missing")]));
    }

    [Test]
    public void ReadsAssetsFromResolvedLocalPackagesWithoutCopying()
    {
        string packages = Path.Combine(root, "Packages");
        Directory.CreateDirectory(packages);
        WriteImage("LocalPackages/ui/Resources/UI/package.png", 73);
        File.WriteAllText(Path.Combine(packages, "packages-lock.json"),
            "{\"dependencies\":{\"com.example.ui\":{\"version\":\"file:../LocalPackages/ui\",\"source\":\"local\"}}}");
        using var assets = new NowProjectAssets(root, builtIns);
        var image = assets.LoadAsset<Texture2D>("UI/package");
        Assert.That(image.GetPixels32()[0].r, Is.EqualTo(73));
        Assert.That(assets.LoadAsset<Texture2D>("Packages/com.example.ui/Resources/UI/package.png"), Is.SameAs(image));
    }

    private void WriteImage(string path, byte red)
    {
        string full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, NowPng.EncodeRgba(1, 1, [red, 20, 30, 255]));
    }
}
