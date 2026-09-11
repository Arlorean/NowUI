using NowUI.Cli;
using NowUI.Hosting;
using NowUI.TrimmingConsumer;
using NUnit.Framework;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace NowUI.Native.Tests;

public sealed class BrowserSceneTrimmingTests
{
    string scratch = null!;

    [SetUp]
    public void SetUp()
    {
        scratch = Path.Combine(Path.GetTempPath(), "nowui-scene-trimming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(scratch, recursive: true);

    static Dictionary<string, string> HostReferences() => new()
    {
        ["NowUI.Engine"] = typeof(NowUI.Engine.NowRuntime).Assembly.Location,
        ["NowUI.Runtime"] = typeof(NowUI.Now).Assembly.Location,
        ["NowUI.Hosting"] = typeof(NowFileResources).Assembly.Location,
        ["NowUI.Browser"] = Path.Combine(AppContext.BaseDirectory, "BrowserKit", "NowUI.Browser.dll"),
        ["AssetsTools.NET"] = typeof(AssetsTools.NET.AssetsFile).Assembly.Location,
        ["StbImageSharp"] = typeof(StbImageSharp.ImageResult).Assembly.Location,
        ["YamlDotNet"] = typeof(YamlDotNet.RepresentationModel.YamlStream).Assembly.Location
    };

    [Test]
    public void ActualNativePreviewCanTrimUnusedScenes()
    {
        string repository = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        string assembly = Path.Combine(repository, "Standalone/Samples/NativePreview/bin", configuration, "net9.0/NativePreview.dll");
        var references = BrowserRunner.Dependencies(assembly, Path.Combine(AppContext.BaseDirectory, "BrowserKit"));
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references,
            new("NowUI.Samples.NativePreview.LottiePreviewScene", "NativePreview", SceneFactoryMode.Direct)), Is.True);
    }

    [Test]
    public void ConfiguredReflectionConsumerKeepsItsEntireAssembly()
    {
        var references = HostReferences();
        references.Add("NowUI.TrimmingConsumer", typeof(ConfiguredConsumer).Assembly.Location);
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references,
            new(typeof(ConfiguredConsumer).FullName!, "NowUI.TrimmingConsumer", SceneFactoryMode.Direct)), Is.False);
    }

    [TestCase((int)SceneFactoryMode.Direct, true)]
    [TestCase((int)SceneFactoryMode.ConstructorAccessor, true)]
    [TestCase((int)SceneFactoryMode.Reflection, false)]
    public void OnlyStaticFactoriesAllowSceneTrimming(int mode, bool expected)
    {
        var references = Fixture("Plain");
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references, new("Example.Scene", "Plain", (SceneFactoryMode)mode)), Is.EqualTo(expected));
    }

    [Test]
    public void RequiredMemberSceneUsesAStaticConstructorAccessorAndRemainsEligible()
    {
        var references = Fixture("Required", (_, type) => type.SetCustomAttribute(
            new CustomAttributeBuilder(typeof(RequiredMemberAttribute).GetConstructor(Type.EmptyTypes)!, [])));
        var loader = new AssemblyLoadContext("Required scene eligibility", isCollectible: true);
        try
        {
            using var stream = File.OpenRead(references["Required"]);
            var assembly = loader.LoadFromStream(stream);
            var options = SceneFactoryOptions.FromType(assembly.GetType("Example.Scene")!);
            Assert.That(options.Mode, Is.EqualTo(SceneFactoryMode.ConstructorAccessor));
            Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references, options), Is.True);
        }
        finally { loader.Unload(); }
    }

    [TestCase("plugin")]
    [TestCase("extension")]
    [TestCase("identity")]
    [TestCase("missing")]
    [TestCase("malformed")]
    [TestCase("scene")]
    public void UncertainDependencyMetadataRetainsTheAssembly(string uncertainty)
    {
        var references = Fixture("Plain");
        string scene = "Example.Scene";
        switch (uncertainty)
        {
            case "plugin": references.Add("Plugin", references["Plain"]); break;
            case "extension": references.Add("NowUI.Extensions.Markup", references["Plain"]); break;
            case "identity": references["NowUI.Engine"] = references["Plain"]; break;
            case "missing": references["Plain"] = Path.Combine(scratch, "missing.dll"); break;
            case "malformed": File.WriteAllText(references["Plain"], "not a managed assembly"); break;
            case "scene": scene = "Example.MissingScene"; break;
        }
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references, new(scene, "Plain", SceneFactoryMode.Direct)), Is.False);
    }

    [TestCase("enum")]
    [TestCase("reset")]
    [TestCase("scriptable")]
    [TestCase("activator")]
    [TestCase("generic-expression")]
    [TestCase("serializer")]
    [TestCase("inspector")]
    [TestCase("implementation-library")]
    [TestCase("interop")]
    public void ReflectionAndLifecycleContractsKeepFullPreservation(string contract)
    {
        var references = Fixture("Contract", (module, type) =>
        {
            if (contract == "enum")
            {
                var choices = module.DefineEnum("Example.Choices", TypeAttributes.Public, typeof(int));
                choices.DefineLiteral("First", 1);
                choices.CreateType();
                return;
            }
            if (contract == "scriptable") { type.SetParent(typeof(UnityEngine.ScriptableObject)); return; }
            var method = type.DefineMethod("Probe", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            var il = method.GetILGenerator();
            if (contract == "reset")
                method.SetCustomAttribute(new CustomAttributeBuilder(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute)
                    .GetConstructor(Type.EmptyTypes)!, []));
            else if (contract == "activator")
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, typeof(Activator).GetMethod("CreateInstance", [typeof(Type)])!);
                il.Emit(OpCodes.Pop);
            }
            else if (contract == "generic-expression")
            {
                // Emits a real constructed-generic MemberReference owner (TypeSpecification).
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, typeof(System.Linq.Expressions.Expression<Func<int>>).GetMethod("Compile", Type.EmptyTypes)!);
                il.Emit(OpCodes.Pop);
            }
            else if (contract == "serializer")
            {
                il.Emit(OpCodes.Newobj, typeof(System.Text.Json.JsonSerializerOptions).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Pop);
            }
            else if (contract == "inspector")
            {
                il.Emit(OpCodes.Ldtoken, typeof(NowUI.NowInspector));
                il.Emit(OpCodes.Pop);
            }
            else if (contract == "implementation-library")
            {
                il.Emit(OpCodes.Ldtoken, typeof(YamlDotNet.RepresentationModel.YamlStream));
                il.Emit(OpCodes.Pop);
            }
            else if (contract == "interop")
            {
                il.Emit(OpCodes.Ldtoken, typeof(System.Runtime.InteropServices.Marshal));
                il.Emit(OpCodes.Pop);
            }
            il.Emit(OpCodes.Ret);
        });
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references, new("Example.Scene", "Contract", SceneFactoryMode.Direct)), Is.False);
    }

    [Test]
    public void PlainApplicationInheritanceRemainsStaticallyReachable()
    {
        var references = Fixture("Inherited", (module, type) =>
        {
            var parent = module.DefineType("Example.BaseScene", TypeAttributes.Public);
            parent.DefineDefaultConstructor(MethodAttributes.Public);
            type.SetParent(parent.CreateType());
        });
        Assert.That(BrowserSceneTrimming.CanTrimSceneAssembly(references,
            new("Example.Scene", "Inherited", SceneFactoryMode.Direct)), Is.True);
    }

    Dictionary<string, string> Fixture(string name, Action<ModuleBuilder, TypeBuilder>? configure = null)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        var module = builder.DefineDynamicModule(name);
        var type = module.DefineType("Example.Scene", TypeAttributes.Public);
        configure?.Invoke(module, type);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();
        string path = Path.Combine(scratch, name + ".dll");
        builder.Save(path);
        var references = HostReferences();
        references.Add(name, path);
        return references;
    }
}
