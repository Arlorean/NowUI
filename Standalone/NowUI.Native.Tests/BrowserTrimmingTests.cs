using NowUI.Cli;
using NowUI.Hosting;
using NowUI.TrimmingConsumer;
using NUnit.Framework;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace NowUI.Native.Tests;

public sealed class BrowserTrimmingTests
{
    static Dictionary<string, string> HostReferences() => new()
    {
        ["NowUI.Hosting"] = typeof(NowFileResources).Assembly.Location,
        ["AssetsTools.NET"] = typeof(AssetsTools.NET.AssetsFile).Assembly.Location,
        ["StbImageSharp"] = typeof(StbImageSharp.ImageResult).Assembly.Location,
        ["YamlDotNet"] = typeof(YamlDotNet.RepresentationModel.YamlStream).Assembly.Location
    };

    [Test]
    public void HostOnlyInfrastructureRetainsOnlyReachableMethods()
    {
        var references = HostReferences();
        var preserved = BrowserTrimming.PreservedAssemblies(references);
        Assert.That(preserved, Is.EquivalentTo(new[] { "NowUI.Hosting" }));
    }

    [Test]
    public void ApplicationUseRetainsLibraryReflectionContracts()
    {
        var references = HostReferences();
        references.Add("NowUI.Native.Tests", typeof(BrowserTrimmingTests).Assembly.Location);
        // This actual application assembly references the same libraries as the
        // host. Its usage must retain the previous conservative reflection policy.
        var preserved = BrowserTrimming.PreservedAssemblies(references);
        Assert.That(preserved, Is.EquivalentTo(references.Keys));
    }

    [Test]
    public void ConfiguredTypeLookupWithoutLibraryReferenceRetainsPriorBehavior()
    {
        var references = HostReferences();
        string consumer = typeof(ConfiguredConsumer).Assembly.Location;
        references.Add("NowUI.TrimmingConsumer", consumer);
        using var stream = File.OpenRead(consumer);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        Assert.That(metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)),
            Does.Not.Contain("YamlDotNet"));
        Assert.That(metadata.MemberReferences.Count(handle => BrowserTrimming.IsDynamicLookup(metadata, handle)), Is.EqualTo(1));
        Assert.That(ConfiguredConsumer.Find("YamlDotNet.Serialization.SerializerBuilder, YamlDotNet"), Is.Not.Null);
        Assert.That(BrowserTrimming.PreservedAssemblies(references), Is.EquivalentTo(references.Keys));
    }

    [Test]
    public void ShippedExtensionClassificationOnlyExemptsDynamicDiscovery()
    {
        var references = HostReferences();
        const string extension = "NowUI.Extensions.Markup";
        // Exercise the same host classification with independently compiled PE
        // contents: runtime discovery is audited, direct library use is not.
        references.Add(extension, typeof(ConfiguredConsumer).Assembly.Location);
        Assert.That(BrowserTrimming.PreservedAssemblies(references),
            Is.EquivalentTo(new[] { "NowUI.Hosting", extension }));
        references[extension] = typeof(BrowserTrimmingTests).Assembly.Location;
        Assert.That(BrowserTrimming.PreservedAssemblies(references), Is.EquivalentTo(references.Keys));
    }

    static Dictionary<string, string> CoreReferences()
    {
        var references = HostReferences();
        references.Add("NowUI.Engine", typeof(NowUI.Engine.NowRuntime).Assembly.Location);
        references.Add("NowUI.Runtime", typeof(NowUI.Now).Assembly.Location);
        references.Add("NowUI.Browser", Path.Combine(AppContext.BaseDirectory, "BrowserKit", "NowUI.Browser.dll"));
        return references;
    }

    [Test]
    public void CoreTrimmingIsExplicitForDescriptorAwareCallers()
    {
        var references = CoreReferences();
        Assert.That(BrowserTrimming.PreservedAssemblies(references, trimNowUi: true), Is.Empty);
        Assert.That(BrowserTrimming.PreservedAssemblies(references), Does.Contain("NowUI.Runtime"));
    }

    [Test]
    public void ConfiguredConsumerTypesRestoreCoreReflectionMembers()
    {
        var references = CoreReferences();
        references.Add("NowUI.TrimmingConsumer", typeof(ConfiguredConsumer).Assembly.Location);
        Assert.That(BrowserTrimming.PreservedAssemblies(references, trimNowUi: true), Is.EquivalentTo(references.Keys));
    }

    [TestCase("NowUI.NowLayout", "Inspector")]
    [TestCase("System.Type", "GetFields")]
    [TestCase("System.Text.Json.JsonSerializer", "Serialize")]
    [TestCase("System.Runtime.Serialization.DataContractSerializer", ".ctor")]
    [TestCase("System.ComponentModel.TypeDescriptor", "GetProperties")]
    public void ConsumerInspectionAndSerializationKeepCoreMembers(string typeName, string methodName)
    {
        using var stream = File.OpenRead(typeof(BrowserTrimmingTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var matches = FindMembers(metadata, typeName, methodName).ToArray();
        Assert.That(matches, Is.Not.Empty);
        Assert.That(matches.All(handle => BrowserTrimming.RequiresCoreReflection(metadata, handle)), Is.True);
    }

    static void CoreInspectionApis(Type type, object value)
    {
        _ = NowUI.NowLayout.Inspector();
        _ = type.GetFields();
        _ = System.Text.Json.JsonSerializer.Serialize(value);
        _ = new System.Runtime.Serialization.DataContractSerializer(type);
        _ = System.ComponentModel.TypeDescriptor.GetProperties(value);
    }

    [TestCase("System.Reflection.Assembly", "GetType")]
    [TestCase("System.Reflection.Assembly", "Load")]
    [TestCase("System.Reflection.Assembly", "LoadFrom")]
    [TestCase("System.Reflection.Assembly", "GetTypes")]
    [TestCase("System.Reflection.Module", "GetType")]
    [TestCase("System.Activator", "CreateInstanceFrom")]
    [TestCase("System.Runtime.Loader.AssemblyLoadContext", "LoadFromAssemblyName")]
    [TestCase("System.Runtime.Loader.AssemblyLoadContext", "LoadFromAssemblyPath")]
    [TestCase("System.Runtime.Loader.AssemblyLoadContext", "LoadFromStream")]
    [TestCase("System.Reflection.MethodBase", "Invoke")]
    public void DynamicLookupApiReferencesKeepReflectionRoots(string typeName, string methodName)
    {
        using var stream = File.OpenRead(typeof(BrowserTrimmingTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var matches = FindMembers(metadata, typeName, methodName).ToArray();
        Assert.That(matches, Is.Not.Empty, "The compiled test must contain the real lookup API reference.");
        Assert.That(matches.All(handle => BrowserTrimming.IsDynamicLookup(metadata, handle)), Is.True);
    }

    [Test]
    public void ActivatorOnlyStringOverloadsDisableInfrastructureTrimming()
    {
        using var stream = File.OpenRead(typeof(BrowserTrimmingTests).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var matches = FindMembers(metadata, "System.Activator", "CreateInstance").ToArray();
        Assert.That(matches.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(matches.Any(handle => BrowserTrimming.IsDynamicLookup(metadata, handle)), Is.True);
        Assert.That(matches.Any(handle => !BrowserTrimming.IsDynamicLookup(metadata, handle)), Is.True);
    }

    static IEnumerable<MemberReferenceHandle> FindMembers(MetadataReader metadata, string typeName, string methodName)
    {
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name) == typeName &&
                metadata.GetString(member.Name) == methodName) yield return handle;
        }
    }

    // These methods are inspected as PE metadata, never executed. The compiler
    // emits the real declaring types and overloaded signatures used by consumers.
    static object? LookupApis(string configuredName, Assembly assembly, Module module, AssemblyLoadContext context, Stream stream)
    {
        _ = assembly.GetType(configuredName);
        _ = Assembly.Load(configuredName);
        _ = Assembly.LoadFrom(configuredName);
        _ = assembly.GetTypes();
        _ = module.GetType(configuredName);
        _ = Activator.CreateInstance(configuredName, configuredName);
        _ = Activator.CreateInstanceFrom(configuredName, configuredName);
        _ = context.LoadFromAssemblyName(new AssemblyName(configuredName));
        _ = context.LoadFromAssemblyPath(configuredName);
        _ = context.LoadFromStream(stream);
        _ = typeof(string).GetMethod(configuredName)!.Invoke(null, null);
        return Activator.CreateInstance(typeof(object));
    }
}
