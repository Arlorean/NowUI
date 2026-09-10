using System.IO.Compression;
using System.Text;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class LottieProjectAssetTests
{
    internal const string MinimalJson = "{\"v\":\"5.8.1\",\"fr\":30,\"ip\":0,\"op\":60,\"w\":200,\"h\":100,\"layers\":[],\"assets\":[]}";
    string root = null!;
    NowFileResources builtIns = null!;
    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "nowui-lottie-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets/Resources/UI"));
        builtIns = new NowFileResources();
    }
    [TearDown] public void TearDown() { builtIns.Dispose(); Directory.Delete(root, true); }

    [Test]
    public void LoadsRealUnityImporterSourceAndSceneFileIdWithSharedIdentity()
    {
        string repository = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        using var assets = new NowProjectAssets(repository, builtIns);
        const string path = "Assets/NowUI/Assets/AnimatedEmoji/1f600.lottie";
        var first = assets.LoadAsset<NowLottieAsset>(path);
        Assert.That((first.width, first.height, first.frameRate, first.outPoint), Is.EqualTo((1024f, 1024f, 60f, 140f)));
        Assert.That(first.composition, Is.Not.Null);
        Assert.That(assets.ResolveGuid("8e2cbe0cac8263b4c84c2ec9913a4da1", 7868935432812623880, typeof(NowLottieAsset)), Is.SameAs(first));
        Assert.That(assets.LoadAsset<NowLottieAsset>(path + "#animation"), Is.SameAs(first));
        Assert.Throws<InvalidDataException>(() => assets.LoadAsset(path, typeof(NowLottieAsset), 123));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PlainJsonAndDotLottieArchivesUseSharedParserAndResourceAliases(bool archive)
    {
        string extension = archive ? ".lottie" : ".json";
        File.WriteAllBytes(Path.Combine(root, "Assets/Resources/UI/animation" + extension), archive ? Archive(MinimalJson) : Encoding.UTF8.GetBytes(MinimalJson));
        NowLottieAsset asset;
        using (var assets = new NowProjectAssets(root, builtIns))
        {
            asset = assets.LoadAsset<NowLottieAsset>("UI/animation");
            Assert.That((asset.width, asset.height, asset.duration), Is.EqualTo((200f, 100f, 2f)));
            Assert.That(assets.LoadAsset<NowLottieAsset>("Assets/Resources/UI/animation" + extension), Is.SameAs(asset));
            Assert.That(assets.LoadAsset<Texture2D>("UI/animation"), Is.Null);
        }
        Assert.That(asset == null, Is.True);
    }

    [Test]
    public void ReadsKnownSerializedLottieAssetAndRecomputesDerivedMetadata()
    {
        string yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
            "  m_Name: Saved animation\n  m_Script: {fileID: 11500000, guid: 3055cc0dbfa91e549afdc1ff056b42d5, type: 3}\n" +
            "  _json: '" + MinimalJson + "'\n  _width: 999\n";
        File.WriteAllText(Path.Combine(root, "Assets/Resources/UI/saved.asset"), yaml);
        using var assets = new NowProjectAssets(root, builtIns);
        var asset = assets.LoadAsset<NowLottieAsset>("UI/saved");
        Assert.That((asset.name, asset.width, asset.height, asset.frameRate), Is.EqualTo(("Saved animation", 200f, 100f, 30f)));
    }

    [Test]
    public void InvalidDataAndLimitsDoNotPoisonTheAssetCache()
    {
        string path = Path.Combine(root, "Assets/Resources/UI/broken.json");
        File.WriteAllText(path, "not lottie");
        using var assets = new NowProjectAssets(root, builtIns);
        Assert.Throws<InvalidDataException>(() => assets.LoadAsset<NowLottieAsset>("UI/broken"));
        File.WriteAllText(path, MinimalJson);
        Assert.That(assets.LoadAsset<NowLottieAsset>("UI/broken").duration, Is.EqualTo(2));
        long previous = NowLottieAsset.maxJsonBytes;
        try
        {
            NowLottieAsset.maxJsonBytes = 8;
            File.WriteAllText(Path.Combine(root, "Assets/Resources/UI/large.json"), MinimalJson);
            using var limited = new NowProjectAssets(root, builtIns);
            Assert.Throws<InvalidDataException>(() => limited.LoadAsset<NowLottieAsset>("UI/large"));
        }
        finally { NowLottieAsset.maxJsonBytes = previous; }
    }

    internal static byte[] Archive(string json)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("animations/main.json").Open());
            writer.Write(json);
        }
        return bytes.ToArray();
    }
}
