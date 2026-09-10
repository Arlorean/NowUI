using NowUI.Cli;
using NUnit.Framework;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace NowUI.Native.Tests;

public sealed class BrowserEntrySourceTests
{
    [Test]
    public void NestedAndKeywordSceneNamesProduceDirectConstructorCalls()
    {
        Assert.That(BrowserEntrySource.Create("await Run(/*NOWUI_SCENE_FACTORY*/);", "Example.namespace+class"),
            Is.EqualTo("extern alias nowui_scene;\nawait Run(static () => new nowui_scene::@Example.@namespace.@class());"));
    }

    [TestCase("")]
    [TestCase("/*NOWUI_SCENE_FACTORY*//*NOWUI_SCENE_FACTORY*/")]
    public void MismatchedKitFailsBeforePublishing(string template) =>
        Assert.Throws<InvalidDataException>(() => BrowserEntrySource.Create(template, "Scene"));

    [TestCase("Bad;Name")]
    [TestCase("Generic`1")]
    [TestCase("Bad..Name")]
    public void UnsupportedMetadataNamesFailExplicitly(string name) =>
        Assert.Throws<InvalidDataException>(() => BrowserEntrySource.TypeName(name));

    [TestCase("Bad;Name")]
    [TestCase("Bad.Name")]
    [TestCase("Scene\u200DName")]
    public void UnspellableMetadataNamesUseReflection(string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SceneMetadata"), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("Scenes").DefineType(name, TypeAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var options = SceneFactoryOptions.FromType(builder.CreateType()!);
        // A dot in a top-level full name is a namespace separator and remains representable.
        Assert.That(options.Mode, Is.EqualTo(name == "Bad.Name" ? SceneFactoryMode.Direct : SceneFactoryMode.Reflection));
    }

    [Test]
    public void SelectedFactoriesCompileAndInvokeSupportedSceneConstructors()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "nowui-scene-factory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var loader = new AssemblyLoadContext("Scene factory compatibility", isCollectible: true);
        try
        {
            string fixtures = Path.Combine(scratch, "fixtures");
            Directory.CreateDirectory(fixtures);
            File.WriteAllText(Path.Combine(fixtures, "Fixtures.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>
                """);
            File.WriteAllText(Path.Combine(fixtures, "Scenes.cs"), """
                namespace NowUI.Hosting { public interface INowScene { string Marker { get; } } }
                namespace SceneCases
                {
                    public class Plain : NowUI.Hosting.INowScene { public string Marker => "plain"; }
                    public class Required : NowUI.Hosting.INowScene
                    {
                        public required string Value { get; init; }
                        public string Marker => "required";
                    }
                    public class RequiredSatisfied : Required
                    {
                        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                        public RequiredSatisfied() { Value = "initialized"; }
                    }
                    public class ObsoleteConstructor : NowUI.Hosting.INowScene
                    {
                        [System.Obsolete("Use a factory", true)] public ObsoleteConstructor() { }
                        public string Marker => "constructor";
                    }
                    [System.Obsolete("Legacy scene", true)]
                    public class ObsoleteType : NowUI.Hosting.INowScene { public string Marker => "type"; }
                    [System.Obsolete("Legacy container", true)]
                    public class ObsoleteContainer
                    {
                        public class Nested : NowUI.Hosting.INowScene { public string Marker => "nested"; }
                    }
                }
                """);
            ProjectBuilder.RunDotnet(fixtures, ["build", "Fixtures.csproj", "--configuration", "Release", "--nologo", "--verbosity", "quiet"], false);
            string fixtureDll = Path.Combine(fixtures, "bin", "Release", "net9.0", "Fixtures.dll");
            using var stream = File.OpenRead(fixtureDll);
            var assembly = loader.LoadFromStream(stream);
            string app = Path.Combine(scratch, "app");
            Directory.CreateDirectory(app);
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net9.0"), new XElement("OutputType", "Exe"), new XElement("Nullable", "enable")),
                new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "Fixtures"),
                    new XElement("HintPath", fixtureDll), new XElement("Aliases", "global," + BrowserEntrySource.SceneAssemblyAlias)))))
                .Save(Path.Combine(app, "App.csproj"));
            (string Name, SceneFactoryMode Mode, string Marker)[] cases =
            [
                ("Plain", SceneFactoryMode.Direct, "plain"),
                ("Required", SceneFactoryMode.ConstructorAccessor, "required"),
                ("RequiredSatisfied", SceneFactoryMode.Direct, "required"),
                ("ObsoleteConstructor", SceneFactoryMode.ConstructorAccessor, "constructor"),
                ("ObsoleteType", SceneFactoryMode.Reflection, "type"),
                ("ObsoleteContainer+Nested", SceneFactoryMode.Reflection, "nested")
            ];
            for (int i = 0; i < cases.Length; i++)
            {
                var options = SceneFactoryOptions.FromType(assembly.GetType("SceneCases." + cases[i].Name, throwOnError: true)!);
                Assert.That(options.Mode, Is.EqualTo(cases[i].Mode), cases[i].Name);
                string template = "internal static class Probe" + i + " { internal static string Run() { " +
                    "System.Func<NowUI.Hosting.INowScene> factory = " + BrowserEntrySource.SceneFactoryMarker + "; return factory().Marker; } }";
                File.WriteAllText(Path.Combine(app, "Probe" + i + ".cs"), BrowserEntrySource.Create(template, options));
            }
            File.WriteAllText(Path.Combine(app, "Program.cs"), "System.Console.WriteLine(string.Join(\",\", new[] { " +
                string.Join(",", Enumerable.Range(0, cases.Length).Select(i => "Probe" + i + ".Run()")) + " }));");
            ProjectBuilder.RunDotnet(app, ["build", "App.csproj", "--configuration", "Release", "--nologo", "--verbosity", "quiet"], false);
            string result = ProjectBuilder.RunDotnet(app, [Path.Combine(app, "bin", "Release", "net9.0", "App.dll")], false).Trim();
            Assert.That(result, Is.EqualTo(string.Join(",", cases.Select(item => item.Marker))));
        }
        finally
        {
            loader.Unload();
            Directory.Delete(scratch, recursive: true);
        }
    }
}
