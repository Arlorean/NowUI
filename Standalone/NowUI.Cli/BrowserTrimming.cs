using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NowUI.Cli;

internal static class BrowserTrimming
{
    // The host calls these libraries through statically visible APIs: YAML's
    // representation model, the Unity binary reader, and the image decoder.
    // Keeping their entire assemblies also retains unused writers, serializers,
    // and the framework code those features depend on.
    static readonly HashSet<string> ImplementationLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AssetsTools.NET", "StbImageSharp", "YamlDotNet"
    };

    static readonly HashSet<string> CoreLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "NowUI.Engine", "NowUI.Runtime", "NowUI.Hosting", "NowUI.Browser"
    };

    internal static HashSet<string> PreservedAssemblies(IReadOnlyDictionary<string, string> references, bool trimNowUi = false)
    {
        // Core trimming is enabled only by the publisher that supplies the generated
        // reflection descriptor. Other callers retain the earlier preservation policy.
        var preserved = new HashSet<string>(references.Keys.Where(name => !ImplementationLibraries.Contains(name) &&
            (!trimNowUi || !CoreLibraries.Contains(name))),
            StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in references)
        {
            if (assembly.Key.Equals("NowUI.Hosting", StringComparison.OrdinalIgnoreCase) ||
                ImplementationLibraries.Contains(assembly.Key)) continue;
            // An application or extension may use a library's reflective APIs.
            // Only the host's own audited use is eligible for the smaller root set.
            using var stream = File.OpenRead(assembly.Value);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                string dependency = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                if (ImplementationLibraries.Contains(dependency)) preserved.Add(dependency);
                // Consumer YAML serializers can traverse NowUI values without
                // emitting calls to those values' members. Hosting uses only nodes.
                if (trimNowUi && dependency.Equals("YamlDotNet", StringComparison.OrdinalIgnoreCase))
                    preserved.UnionWith(CoreLibraries.Where(references.ContainsKey));
            }
            // Configured assembly-qualified names produce no AssemblyReference.
            // Consumers that discover types dynamically retain the old full-root
            // behavior, even when the names come from input or downloaded config.
            if (!BrowserAssetSelection.HostAssemblies.Contains(assembly.Key))
            {
                foreach (var handle in metadata.MemberReferences)
                {
                    if (IsDynamicLookup(metadata, handle))
                        preserved.UnionWith(ImplementationLibraries.Where(references.ContainsKey));
                    if (trimNowUi && RequiresCoreReflection(metadata, handle))
                    {
                        preserved.UnionWith(CoreLibraries.Where(references.ContainsKey));
                        preserved.UnionWith(ImplementationLibraries.Where(references.ContainsKey));
                    }
                }
            }
        }
        return preserved;
    }

    internal static bool RequiresCoreReflection(MetadataReader metadata, MemberReferenceHandle handle)
    {
        if (IsDynamicLookup(metadata, handle)) return true;
        var member = metadata.GetMemberReference(handle);
        if (member.GetKind() != MemberReferenceKind.Method || member.Parent.Kind != HandleKind.TypeReference) return false;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        string typeName = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        string method = metadata.GetString(member.Name);
        // A consumer can inspect or serialize a NowUI-defined value without a direct
        // call to its getters, setters, constructors or private serialized fields.
        if (typeName.StartsWith("System.Runtime.Serialization.", StringComparison.Ordinal)) return true;
        return typeName switch
        {
            "NowUI.Now" or "NowUI.NowLayout" => method == "Inspector",
            "NowUI.NowInspector" => true,
            "System.Text.Json.JsonSerializer" or "System.Xml.Serialization.XmlSerializer" or
                "Newtonsoft.Json.JsonSerializer" or "Newtonsoft.Json.JsonConvert" => true,
            "System.ComponentModel.TypeDescriptor" or "System.ComponentModel.ICustomTypeDescriptor" => true,
            "System.Type" or "System.Reflection.TypeInfo" => method.StartsWith("get_Declared", StringComparison.Ordinal) || method is
                "GetField" or "GetFields" or "GetProperty" or "GetProperties" or "GetMember" or "GetMembers" or
                "GetMethod" or "GetMethods" or "GetConstructor" or "GetConstructors" or "GetNestedType" or "GetNestedTypes",
            "System.Reflection.RuntimeReflectionExtensions" => true,
            _ => false
        };
    }

    internal static bool IsDynamicLookup(MetadataReader metadata, MemberReferenceHandle handle)
    {
        var member = metadata.GetMemberReference(handle);
        if (member.GetKind() != MemberReferenceKind.Method || member.Parent.Kind != HandleKind.TypeReference) return false;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        string typeName = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        string method = metadata.GetString(member.Name);
        return typeName switch
        {
            "System.Type" => method is "GetType" or "ReflectionOnlyGetType" or "InvokeMember",
            "System.Reflection.Assembly" => method.StartsWith("Load", StringComparison.Ordinal) ||
                method.StartsWith("ReflectionOnlyLoad", StringComparison.Ordinal) || method is
                    "UnsafeLoadFrom" or "GetType" or "GetTypes" or "GetExportedTypes" or "CreateInstance" or
                    "get_DefinedTypes" or "get_ExportedTypes",
            "System.Reflection.Module" => method is "GetType" or "GetTypes" or "FindTypes" or "ResolveType",
            "System.Activator" => method is "CreateInstanceFrom" || method == "CreateInstance" &&
                member.DecodeMethodSignature(StringParameter.Instance, (object?)null).ParameterTypes.Any(isString => isString),
            "System.Runtime.Loader.AssemblyLoadContext" or "System.Reflection.MetadataLoadContext" =>
                method.StartsWith("Load", StringComparison.Ordinal),
            "System.AppDomain" => method == "Load" || method.StartsWith("CreateInstance", StringComparison.Ordinal) ||
                method == "GetAssemblies",
            // Reflection can also obtain and invoke a loader without emitting its
            // direct member reference. Retain the conservative policy in that case.
            "System.Reflection.MethodBase" or "System.Reflection.MethodInfo" or "System.Reflection.ConstructorInfo" =>
                method is "Invoke" or "CreateDelegate",
            "System.Delegate" => method is "CreateDelegate" or "DynamicInvoke",
            _ => false
        };
    }

    sealed class StringParameter : ISignatureTypeProvider<bool, object?>
    {
        internal static readonly StringParameter Instance = new();
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode == PrimitiveTypeCode.String;
        public bool GetArrayType(bool elementType, ArrayShape shape) => false;
        public bool GetByReferenceType(bool elementType) => elementType;
        public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;
        public bool GetGenericInstantiation(bool genericType, System.Collections.Immutable.ImmutableArray<bool> typeArguments) => false;
        public bool GetGenericMethodParameter(object? genericContext, int index) => false;
        public bool GetGenericTypeParameter(object? genericContext, int index) => false;
        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
        public bool GetPinnedType(bool elementType) => elementType;
        public bool GetPointerType(bool elementType) => false;
        public bool GetSZArrayType(bool elementType) => false;
        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
