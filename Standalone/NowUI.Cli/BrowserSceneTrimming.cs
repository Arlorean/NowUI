using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NowUI.Cli;

/// <summary>Opts simple, statically constructed scene assemblies into ordinary linker reachability.</summary>
internal static class BrowserSceneTrimming
{
    static readonly HashSet<string> AuditedDependencies = new(StringComparer.Ordinal)
    {
        "NowUI.Engine", "NowUI.Runtime", "NowUI.Hosting", "NowUI.Browser",
        "AssetsTools.NET", "StbImageSharp", "YamlDotNet"
    };

    internal static bool CanTrimSceneAssembly(IReadOnlyDictionary<string, string> references, SceneFactoryOptions scene)
    {
        if (scene.Mode is not (SceneFactoryMode.Direct or SceneFactoryMode.ConstructorAccessor) ||
            AuditedDependencies.Contains(scene.AssemblyName) || !references.ContainsKey(scene.AssemblyName)) return false;
        try
        {
            foreach (var pair in references)
            {
                if (pair.Key != scene.AssemblyName && !AuditedDependencies.Contains(pair.Key)) return false;
                using var stream = File.OpenRead(pair.Value);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) return false;
                var metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != pair.Key) return false;
                if (pair.Key == scene.AssemblyName && !IsStaticScene(metadata, references, scene.SceneName)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Eligibility is an optimization, never a reason to remove the earlier preservation contract.
            return false;
        }
    }

    static bool IsStaticScene(MetadataReader metadata, IReadOnlyDictionary<string, string> references, string sceneName)
    {
        foreach (var handle in metadata.AssemblyReferences)
        {
            string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (name is "mscorlib" or "netstandard" or "System" || name.StartsWith("System.", StringComparison.Ordinal)) continue;
            // These implementations are audited only when called by the host.
            if (name is "AssetsTools.NET" or "StbImageSharp" or "YamlDotNet") return false;
            if (!AuditedDependencies.Contains(name) || !references.ContainsKey(name)) return false;
        }
        // Inspect type references too: APIs on constructed generic types have a
        // TypeSpecification parent in MemberReferences, rather than a TypeReference.
        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            string ns = metadata.GetString(type.Namespace), name = metadata.GetString(type.Name);
            if (ForbiddenType(ns, name)) return false;
        }
        foreach (var handle in metadata.MemberReferences)
        {
            if (BrowserTrimming.RequiresCoreReflection(metadata, handle)) return false;
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            string owner = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            string name = metadata.GetString(member.Name);
            // typeof(T) is static linker evidence. Other Type APIs and uninitialized
            // construction need a larger preservation contract than this first policy.
            if (owner == "System.Type" && name != "GetTypeFromHandle" ||
                owner == "System.Runtime.CompilerServices.RuntimeHelpers" && name == "GetUninitializedObject" ||
                owner == "NowUI.Engine.NowRuntime" && name is "RegisterAssembly" or "UnregisterAssembly") return false;
        }

        bool foundScene = false;
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (DefinitionName(metadata, handle) == sceneName) foundScene = true;
            // Keep this independent of generated factories/reset calls: application
            // inheritance stays inside this assembly and ends at Object or ValueType.
            // This excludes custom ScriptableObjects, enums and externally discovered
            // framework/NowUI subclasses without another reflection schema walker.
            var visited = new HashSet<TypeDefinitionHandle>();
            var current = handle;
            while (!current.IsNil)
            {
                if (!visited.Add(current)) return false;
                var parent = metadata.GetTypeDefinition(current).BaseType;
                if (parent.IsNil) break;
                if (parent.Kind == HandleKind.TypeDefinition) { current = (TypeDefinitionHandle)parent; continue; }
                if (parent.Kind != HandleKind.TypeReference) return false;
                var baseType = metadata.GetTypeReference((TypeReferenceHandle)parent);
                if (metadata.GetString(baseType.Namespace) != "System" ||
                    metadata.GetString(baseType.Name) is not ("Object" or "ValueType")) return false;
                break;
            }
            foreach (var methodHandle in type.GetMethods())
                if ((metadata.GetMethodDefinition(methodHandle).Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
        }
        return foundScene;
    }

    static bool ForbiddenType(string ns, string name) =>
        ns == "System.Reflection" && !name.EndsWith("Attribute", StringComparison.Ordinal) ||
        ns.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
        ns.StartsWith("System.Linq.Expressions", StringComparison.Ordinal) ||
        ns.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal) ||
        ns.StartsWith("System.Xml.Serialization", StringComparison.Ordinal) ||
        ns.StartsWith("Newtonsoft.Json", StringComparison.Ordinal) ||
        ns.StartsWith("YamlDotNet.Serialization", StringComparison.Ordinal) ||
        ns.StartsWith("Microsoft.CSharp.RuntimeBinder", StringComparison.Ordinal) ||
        ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal) ||
        ns == "System.Text.Json" && name.StartsWith("JsonSerializer", StringComparison.Ordinal) ||
        ns == "System.Text.Json.Serialization" ||
        ns == "System.ComponentModel" && name is "TypeDescriptor" or "ICustomTypeDescriptor" ||
        ns == "System" && name is "Activator" or "AppDomain" ||
        ns == "System.Runtime.Loader" ||
        ns == "System.Runtime.CompilerServices" && (name.StartsWith("CallSite", StringComparison.Ordinal) || name == "DynamicAttribute") ||
        ns == "System.Diagnostics.CodeAnalysis" && name is "RequiresUnreferencedCodeAttribute" or "RequiresDynamicCodeAttribute" ||
        ns == "NowUI" && name == "NowInspector" ||
        ns == "UnityEngine" && name == "RuntimeInitializeOnLoadMethodAttribute";

    static string DefinitionName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var names = new List<string>();
        var visited = new HashSet<TypeDefinitionHandle>();
        while (!handle.IsNil)
        {
            if (!visited.Add(handle)) throw new BadImageFormatException("Cyclic declaring types.");
            var type = metadata.GetTypeDefinition(handle);
            names.Add(metadata.GetString(type.Name));
            handle = type.GetDeclaringType();
            if (!handle.IsNil) continue;
            names.Reverse();
            string ns = metadata.GetString(type.Namespace), name = string.Join("+", names);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        return "";
    }
}
