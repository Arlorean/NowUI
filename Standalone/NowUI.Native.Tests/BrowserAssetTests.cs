using System.Text.Json;
using System.Xml.Linq;
using NowUI.Cli;
using NowUI.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class BrowserAssetTests
{
    string scratch = null!;
    [SetUp] public void SetUp() { scratch = Path.Combine(Path.GetTempPath(), "nowui-browser-assets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch); }
    [TearDown] public void TearDown() { Directory.Delete(scratch, true); }

    [Test]
    public void OriginalProjectAssetsLoadThroughTheSameProviderAfterStaging()
    {
        string repository = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        string output = Path.Combine(scratch, "bundle");
        var result = BrowserAssets.Stage(repository, NowFileResources.bundledRoot, output);
        TestContext.WriteLine($"Browser assets: {result.ProjectFiles} project files + {result.HostFiles} host files; {result.Bytes:N0} bytes total.");
        Assert.That(result.HasProject, Is.True);
        Assert.That(result.ProjectFiles, Is.GreaterThan(10));
        Assert.That(result.Bytes, Is.LessThan(256L * 1024 * 1024));
        const string animationPath = "Assets/NowUI/Assets/AnimatedEmoji/1f600.lottie";
        Assert.That(File.ReadAllBytes(Path.Combine(output, "vfs/project", animationPath)), Is.EqualTo(File.ReadAllBytes(Path.Combine(repository, animationPath))));
        const string binaryFont = "Assets/NowUI/Assets/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf.asset";
        Assert.That(File.ReadAllBytes(Path.Combine(output, "vfs/project", binaryFont)), Is.EqualTo(File.ReadAllBytes(Path.Combine(repository, binaryFont))));
        using var builtIns = new NowFileResources(Path.Combine(output, "vfs/host/NowUI/Resources"));
        using var project = new NowProjectAssets(Path.Combine(output, "vfs/project"), builtIns);
        var animation = project.LoadAsset<NowLottieAsset>(animationPath);
        Assert.That(animation.width, Is.EqualTo(1024));
        Assert.That(project.ResolveGuid("8e2cbe0cac8263b4c84c2ec9913a4da1", 7868935432812623880, typeof(NowLottieAsset)), Is.SameAs(animation));
        Assert.That(project.LoadAsset<NowThemeAsset>("Assets/NowUI/Assets/Themes/DefaultDark.asset").counterpart, Is.Not.Null);
        var paths = XDocument.Load(result.PropsPath).Descendants("WasmFilesToIncludeInFileSystem").Select(e => e.Attribute("TargetPath")!.Value).ToArray();
        Assert.That(paths.Any(p => p.EndsWith(".cs") || p.EndsWith(".unity") || p.Contains("/ProjectSettings/") || p.Contains("/Library/")), Is.False);
        using var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath));
        Assert.That(report.RootElement.GetProperty("bytes").GetInt64(), Is.EqualTo(result.Bytes));
        Assert.That(File.ReadAllText(result.ReportPath), Does.Not.Contain(repository));
    }

    [Test]
    public void OnlyRecognizedSourceDataIsProvisionedAndResourceAliasesSurvive()
    {
        string projectRoot = Path.Combine(scratch, "project");
        string resources = Path.Combine(projectRoot, "Assets/Resources/UI"); Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "animation.json"), LottieProjectAssetTests.MinimalJson);
        File.WriteAllText(Path.Combine(resources, "animation.json.meta"), "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n");
        File.WriteAllText(Path.Combine(resources, "private-config.json"), "{\"token\":\"not-public\"}");
        File.WriteAllText(Path.Combine(resources, "Private.cs"), "private source");
        File.WriteAllText(Path.Combine(resources, "unknown.asset"), "%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Script: {guid: unknown}\n  private: value\n");
        Directory.CreateDirectory(Path.Combine(resources, "Hidden~"));
        File.WriteAllText(Path.Combine(resources, "Hidden~/hidden.lottie"), LottieProjectAssetTests.MinimalJson);
        string output = Path.Combine(scratch, "bundle");
        var result = BrowserAssets.Stage(projectRoot, NowFileResources.bundledRoot, output);
        Assert.That(result.ProjectFiles, Is.EqualTo(2));
        using var builtIns = new NowFileResources(Path.Combine(output, "vfs/host/NowUI/Resources"));
        using var provider = new NowProjectAssets(Path.Combine(output, "vfs/project"), builtIns);
        Assert.That(provider.LoadAsset<NowLottieAsset>("UI/animation").duration, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Any(p => p.Contains("private", StringComparison.OrdinalIgnoreCase) || p.Contains("unknown.asset")), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LocalAndRegistryPackagesUseCanonicalPackagePathsWithoutProjectConfiguration(bool registry)
    {
        string project = Path.Combine(scratch, "project"); Directory.CreateDirectory(Path.Combine(project, "Assets"));
        string packages = Path.Combine(project, "Packages"); Directory.CreateDirectory(packages);
        string package = registry ? Path.Combine(project, "Library/PackageCache/com.example.ui@hash") : Path.Combine(scratch, "external-package");
        Directory.CreateDirectory(Path.Combine(package, "Resources"));
        File.WriteAllText(Path.Combine(package, "Resources/loading.lottie"), LottieProjectAssetTests.MinimalJson);
        File.WriteAllText(Path.Combine(package, "package.json"), "{\"name\":\"com.example.ui\",\"version\":\"1.0.0\"}");
        string version = registry ? "1.0.0" : "file:../../external-package";
        File.WriteAllText(Path.Combine(packages, "packages-lock.json"), "{\"dependencies\":{\"com.example.ui\":{\"version\":\"" + version + "\"}}}");
        string output = Path.Combine(scratch, "bundle");
        BrowserAssets.Stage(project, NowFileResources.bundledRoot, output);
        Assert.That(File.Exists(Path.Combine(output, "vfs/project/Packages/com.example.ui/Resources/loading.lottie")), Is.True);
        Assert.That(Directory.GetFiles(output, "packages-lock.json", SearchOption.AllDirectories), Is.Empty);
        using var builtIns = new NowFileResources(Path.Combine(output, "vfs/host/NowUI/Resources"));
        // The marker is materialized by .NET at startup; create its containing empty Assets root for this disk analogue.
        Directory.CreateDirectory(Path.Combine(output, "vfs/project/Assets"));
        using var provider = new NowProjectAssets(Path.Combine(output, "vfs/project"), builtIns);
        Assert.That(provider.LoadAsset<NowLottieAsset>("loading").width, Is.EqualTo(200));
    }

    [TestCase(1, 100000000)]
    [TestCase(4096, 1)]
    public void BoundsAreCheckedBeforeAnyAssetOutputIsCreated(int count, long bytes)
    {
        string output = Path.Combine(scratch, "bundle");
        Assert.Throws<InvalidDataException>(() => BrowserAssets.Stage(null, NowFileResources.bundledRoot, output, maxBytes: bytes, maxFiles: count));
        Assert.That(Directory.Exists(output), Is.False);
    }

    [Test]
    public void ProjectlessSceneOnlyReceivesBundledHostResources()
    {
        var result = BrowserAssets.Stage(null, NowFileResources.bundledRoot, Path.Combine(scratch, "bundle"));
        Assert.That(result.HasProject, Is.False);
        Assert.That(result.ProjectFiles, Is.Zero);
        Assert.That(result.HostFiles, Is.GreaterThanOrEqualTo(11));
        Assert.That(File.ReadAllText(result.PropsPath), Does.Not.Contain("/project/"));
    }

    [Test]
    public void AssemblySelectionKeepsComputedFolderPrefixesAndKnownGuidClosure()
    {
        string repository = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        string assembly = Path.Combine(repository, "Standalone/Samples/NativePreview/bin", configuration, "net9.0/NativePreview.dll");
        var roots = BrowserAssetSelection.FromAssembly(assembly);
        Assert.That(roots, Does.Contain("Assets/NowUI/Assets/AnimatedEmoji/"));
        string output = Path.Combine(scratch, "selected");
        var result = BrowserAssets.Stage(repository, NowFileResources.bundledRoot, output, roots: roots);
        TestContext.WriteLine($"Selected sample assets: {result.ProjectFiles} project files + {result.HostFiles} host files; {result.Bytes:N0} bytes total.");
        Assert.That(result.ProjectFiles, Is.LessThan(100));
        Assert.That(File.Exists(Path.Combine(output, "vfs/project/Assets/NowUI/Assets/AnimatedEmoji/1f600.lottie")), Is.True);
        using var builtIns = new NowFileResources(Path.Combine(output, "vfs/host/NowUI/Resources"));
        using var provider = new NowProjectAssets(Path.Combine(output, "vfs/project"), builtIns);
        var dark = provider.LoadAsset<NowThemeAsset>("Assets/NowUI/Assets/Themes/DefaultDark.asset");
        Assert.That(dark.counterpart, Is.Not.Null, "The light counterpart must be included even when only the dark path is authored.");
    }

    [Test]
    public void MissingGuidDependenciesFailBeforePublishingAnIncompleteSet()
    {
        string project = Path.Combine(scratch, "project"); string assets = Path.Combine(project, "Assets"); Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "Theme.asset"), "%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n" +
            "  m_Script: {fileID: 11500000, guid: 04c5a0a2a8e8407792a9d8e3844255a0, type: 3}\n" +
            "  _counterpart: {fileID: 11400000, guid: 12345678901234567890123456789012, type: 2}\n");
        string output = Path.Combine(scratch, "missing");
        var failure = Assert.Throws<InvalidDataException>(() => BrowserAssets.Stage(project, NowFileResources.bundledRoot, output, roots: ["Assets/Theme.asset"]));
        Assert.That(failure!.Message, Does.Contain("Assets/Theme.asset").And.Contain("12345678901234567890123456789012"));
        Assert.That(Directory.Exists(output), Is.False);
    }

    [Test]
    public void HelperAssemblyStringsAreIncludedAlongsideTheScene()
    {
        const string helperOnlyPath = "Assets/OnlyInHelper/Sentinel.png";
        var roots = BrowserAssetSelection.FromAssemblies([typeof(BrowserAssetTests).Assembly.Location, typeof(Now).Assembly.Location]);
        Assert.That(roots, Does.Contain(helperOnlyPath));
    }

    [Test]
    public void ALocalPackageBelowAssetsRetainsBothPathsAndOneGuidIdentity()
    {
        string project = Path.Combine(scratch, "project");
        string local = Path.Combine(project, "Assets/LocalUi/Resources"); Directory.CreateDirectory(local);
        string packages = Path.Combine(project, "Packages"); Directory.CreateDirectory(packages);
        File.WriteAllText(Path.Combine(local, "spinner.lottie"), LottieProjectAssetTests.MinimalJson);
        File.WriteAllText(Path.Combine(local, "spinner.lottie.meta"), "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n" +
            "ScriptedImporter:\n  script: {fileID: 11500000, guid: 39fc20ac96163174f8a350451c3145a0, type: 3}\n");
        File.WriteAllText(Path.Combine(packages, "packages-lock.json"), "{\"dependencies\":{\"com.example.ui\":{\"version\":\"file:../Assets/LocalUi\"}}}");
        string output = Path.Combine(scratch, "alias-bundle");
        var result = BrowserAssets.Stage(project, NowFileResources.bundledRoot, output);
        Assert.That(result.ProjectFiles, Is.EqualTo(2), "One source and one .meta, even when both project paths resolve.");
        using var builtIns = new NowFileResources(Path.Combine(output, "vfs/host/NowUI/Resources"));
        using var provider = new NowProjectAssets(Path.Combine(output, "vfs/project"), builtIns);
        provider.AddAliases(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(output, "vfs/host/asset-aliases.json")))!);
        var asset = provider.LoadAsset<NowLottieAsset>("Assets/LocalUi/Resources/spinner.lottie");
        Assert.That(provider.LoadAsset<NowLottieAsset>("Packages/com.example.ui/Resources/spinner.lottie"), Is.SameAs(asset));
        Assert.That(provider.LoadAsset<NowLottieAsset>("spinner"), Is.SameAs(asset));
        Assert.That(provider.ResolveGuid("00000000000000000000000000000001", 7868935432812623880, typeof(NowLottieAsset)), Is.SameAs(asset));
    }

    [Test]
    public void CaseInsensitiveProjectReferencesRetainCanonicalVfsCasing()
    {
        string project = Path.Combine(scratch, "project"); string folder = Path.Combine(project, "Assets/Resources/UI"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Animation.json"), LottieProjectAssetTests.MinimalJson);
        string output = Path.Combine(scratch, "case-bundle");
        var result = BrowserAssets.Stage(project, NowFileResources.bundledRoot, output, roots: ["assets/resources/ui/animation.json"]);
        var paths = XDocument.Load(result.PropsPath).Descendants("WasmFilesToIncludeInFileSystem").Select(e => e.Attribute("TargetPath")!.Value);
        Assert.That(paths, Does.Contain("/project/Assets/Resources/UI/Animation.json"));
    }

    [Test]
    public void FilenamesRemainLiteralDuringRealMsBuildEvaluation()
    {
        const string name = "animation;$(Trap)%21.json";
        string project = Path.Combine(scratch, "project"); string folder = Path.Combine(project, "Assets/Resources/UI"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), LottieProjectAssetTests.MinimalJson);
        string output = Path.Combine(scratch, "literal-bundle");
        var result = BrowserAssets.Stage(project, NowFileResources.bundledRoot, output, roots: ["Assets/Resources/UI/" + name]);
        string probe = Path.Combine(scratch, "Evaluate.proj");
        new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("Trap", "EXPANDED")),
            new XElement("Import", new XAttribute("Project", BrowserAssets.MsBuildLiteral(result.PropsPath))))).Save(probe);
        using var evaluated = JsonDocument.Parse(ProjectBuilder.RunDotnet(scratch, ["msbuild", probe, "-nologo", "-getItem:WasmFilesToIncludeInFileSystem"], false));
        var files = evaluated.RootElement.GetProperty("Items").GetProperty("WasmFilesToIncludeInFileSystem").EnumerateArray().ToArray();
        var asset = files.Single(file => file.GetProperty("TargetPath").GetString() == "/project/Assets/Resources/UI/" + name);
        Assert.That(asset.GetProperty("Identity").GetString(), Is.EqualTo(Path.GetFullPath(Path.Combine(output, "vfs/project/Assets/Resources/UI", name))));
        Assert.That(File.Exists(asset.GetProperty("Identity").GetString()), Is.True);
    }

    [Test]
    public void SelectedOversizeSerializedOrJsonAssetsAreNotSilentlyDropped()
    {
        string project = Path.Combine(scratch, "project"); Directory.CreateDirectory(Path.Combine(project, "Assets"));
        using (var file = File.Create(Path.Combine(project, "Assets/large.json"))) file.SetLength(64L * 1024 * 1024 + 1);
        string output = Path.Combine(scratch, "oversize");
        var error = Assert.Throws<InvalidDataException>(() => BrowserAssets.Stage(project, NowFileResources.bundledRoot, output, roots: ["Assets/large.json"]));
        Assert.That(error!.Message, Does.Contain("64 MiB").And.Contain("Assets/large.json"));
        Assert.That(Directory.Exists(output), Is.False);
    }
}
