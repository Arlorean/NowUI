using System.Xml.Linq;
using System.Reflection;
using NowUI.Cli;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class BrowserReflectionRootsTests
{
    static Dictionary<string, string> References() => new()
    {
        ["NowUI.Engine"] = typeof(NowRuntime).Assembly.Location,
        ["NowUI.Runtime"] = typeof(Now).Assembly.Location
    };
    static XElement Type(XDocument roots, string name) => roots.Descendants("type").Single(type => (string?)type.Attribute("fullname") == name);
    static string[] Fields(XDocument roots, string name) => Type(roots, name).Elements("field").Select(field => (string)field.Attribute("name")!).ToArray();

    [Test]
    public void SerializedAssetRootsIncludePrivateFieldsNestedArraysAndEngineValueTypes()
    {
        var roots = BrowserReflectionRoots.Create(References());
        Assert.That(Fields(roots, "NowUI.NowFont"), Does.Contain("_fontBytes").And.Contain("_bakedPages").And.Contain("atlasInfo"));
        Assert.That(Fields(roots, "NowUI.NowFont/BakedPage"), Does.Contain("glyphs").And.Contain("metrics"));
        Assert.That(Fields(roots, "NowUI.NowFontAtlasInfo/Glyph"), Does.Contain("planeBounds").And.Contain("atlasBounds"));
        Assert.That(Fields(roots, "NowUI.NowFontAtlasInfo/Bounds"), Does.Contain("left").And.Contain("right"));
        Assert.That(Fields(roots, "NowUI.NowFontFamily"), Is.EquivalentTo(new[] { "_regular", "_bold", "_italic", "_boldItalic" }));
        Assert.That(Fields(roots, "NowUI.NowFontAsset"), Is.EquivalentTo(new[] { "_fallbacks" }));
        Assert.That(Fields(roots, "NowUI.NowThemeAsset"), Does.Contain("_palette").And.Contain("_counterpart").And.Contain("_controlRenderer"));
        Assert.That(Fields(roots, "NowUI.NowThemeColorSet"), Does.Contain("_background").And.Not.Contain("_cache"));
        Assert.That(Fields(roots, "UnityEngine.Color"), Is.EquivalentTo(new[] { "r", "g", "b", "a" }));
        Assert.That(Fields(roots, "UnityEngine.Vector4"), Is.EquivalentTo(new[] { "x", "y", "z", "w" }));
        Assert.That(Fields(roots, "NowUI.NowFontStyle"), Does.Contain("Bold").And.Contain("Italic"));
        Assert.That(roots.Descendants("type").Any(type => (string?)type.Attribute("fullname") == "UnityEngine.Material"), Is.False);
        Assert.That(roots.Descendants("type").All(type => (string?)type.Attribute("preserve") == "nothing"), Is.True);
    }

    [Test]
    public void LifecycleAndRuntimeResetMethodsSurviveWithoutWholeTypePreservation()
    {
        var roots = BrowserReflectionRoots.Create(References());
        foreach (string name in new[] { "NowUI.NowFont", "NowUI.NowFontFamily", "NowUI.NowThemeAsset", "NowUI.NowControlRenderer",
            "NowUI.NowMaterialControlRenderer", "NowUI.NowUnityEditorControlRenderer", "NowUI.NowLottieAsset" })
            Assert.That(Type(roots, name).Elements("method").Any(method => (string?)method.Attribute("signature") == "System.Void .ctor()"), Is.True, name);
        Assert.That(Type(roots, "NowUI.NowFont").Elements("method").Select(method => (string?)method.Attribute("name")), Does.Contain("OnDisable").And.Contain("OnDestroy"));
        Assert.That(Type(roots, "NowUI.NowThemeAsset").Elements("method").Select(method => (string?)method.Attribute("name")), Does.Contain("OnEnable").And.Not.Contain("OnValidate"));
        foreach (var (name, methodName) in new[] { ("NowUI.Now", "ResetForRuntimeLoad"), ("NowUI.Now", "ResetGradientRuntime"),
            ("NowUI.NowDrawList", "ResetScopesForRuntimeLoad"), ("NowUI.NowUnityEditorControlRenderer", "ResetEditorCheckTextures"),
            ("NowUI.NowLottieCache", "ResetForRuntimeLoad"), ("NowUI.NowKeyInput", "ResetForRuntimeLoad") })
            Assert.That(Type(roots, name).Elements("method").Any(method => (string?)method.Attribute("name") == methodName), Is.True, name);
    }

    [Test]
    public void ReadsTheReferencedAssemblyAndRejectsMismatchedIdentity()
    {
        var references = References();
        references["NowUI.Runtime"] = references["NowUI.Engine"];
        Assert.Throws<InvalidDataException>(() => BrowserReflectionRoots.Create(references));
    }

    [Test]
    public void EveryWhitelistedAssetHasAConstructorRoot()
    {
        // This test checks the live Hosting whitelist independently of the
        // generator's entry list, so adding an asset kind cannot silently omit it.
        var loader = typeof(NowFileResources).Assembly.GetType("NowUI.Hosting.NowUnitySerializedAssets", true)!;
        var scripts = (Dictionary<string, Type>)loader.GetField("KnownScripts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var roots = BrowserReflectionRoots.Create(References());
        foreach (var asset in scripts.Values)
            Assert.That(Type(roots, asset.FullName!).Elements("method").Any(method =>
                (string?)method.Attribute("signature") == "System.Void .ctor()"), Is.True, asset.FullName);
    }

    [Test]
    public void DescriptorIsDeterministicAndDoesNotContainMachinePaths()
    {
        var references = References();
        string first = BrowserReflectionRoots.Create(references).ToString();
        string second = BrowserReflectionRoots.Create(references.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)).ToString();
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Does.Not.Contain(Path.GetDirectoryName(typeof(Now).Assembly.Location)));
    }
}
